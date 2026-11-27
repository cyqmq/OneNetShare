package routes

import (
	"fmt"
	"log"
	"os/exec"
	"strconv"
	"strings"
)

type existingRoute struct {
	Prefix  string
	Metric  int
	Index   int
	Gateway string
}

func applyRoutes(file *File, routes []normalizedRoute, logger *log.Logger) ([]InstalledRoute, error) {
	installed := make([]InstalledRoute, 0, len(routes))
	for _, route := range routes {
		existing, err := showRoutes(route.Prefix)
		if err != nil {
			return installed, err
		}
		for _, item := range existing {
			if !sameRoute(item, route) {
				return installed, fmt.Errorf("路由 %s 已存在其他归属: prefix=%s idx=%d gateway=%s；未修改任何路由", route.ID, item.Prefix, item.Index, item.Gateway)
			}
		}
		if len(existing) > 0 {
			logger.Printf("管理路由复用已有条目: %s %s", route.ID, route.Prefix)
			continue
		}
		args := []string{
			"interface", "ipv4", "add", "route",
			"prefix=" + route.Prefix,
			"interface=\"" + route.Interface + "\"",
			"nexthop=" + route.Gateway,
			fmt.Sprintf("metric=%d", route.Metric),
			"store=active",
		}
		if _, err := runNetsh(args...); err != nil {
			return installed, fmt.Errorf("添加路由 %s %s 失败: %w", route.ID, route.Prefix, err)
		}
		logger.Printf("管理路由已添加: %s %s via %s %s metric=%d", route.ID, route.Prefix, route.Interface, route.Gateway, route.Metric)
		installed = append(installed, toInstalled(route))
	}
	return installed, nil
}

func cleanupRoutes(routes []InstalledRoute, logger *log.Logger) error {
	for _, route := range routes {
		existing, err := showRoutes(route.Prefix)
		if err != nil {
			logger.Printf("检查管理路由 %s 失败: %v", route.ID, err)
			continue
		}
		matched := false
		for _, item := range existing {
			if sameInstalledRoute(item, route) {
				matched = true
				break
			}
		}
		if !matched {
			continue
		}
		args := []string{
			"interface", "ipv4", "delete", "route",
			"prefix=" + route.Prefix,
			"interface=\"" + route.Interface + "\"",
			"nexthop=" + route.Gateway,
			"store=active",
		}
		if _, err := runNetsh(args...); err != nil {
			logger.Printf("删除管理路由 %s 失败: %v", route.ID, err)
			continue
		}
		logger.Printf("管理路由已删除: %s %s", route.ID, route.Prefix)
	}
	return nil
}

func showRoutes(prefix string) ([]existingRoute, error) {
	out, err := runNetsh("interface", "ipv4", "show", "routes")
	if err != nil {
		return nil, err
	}
	var result []existingRoute
	for _, line := range strings.Split(out, "\n") {
		fields := strings.Fields(line)
		for i, field := range fields {
			if !strings.Contains(field, "/") {
				continue
			}
			if _, _, parseErr := parseDestination(field); parseErr != nil || field != prefix {
				continue
			}
			item := existingRoute{Prefix: field}
			if i > 0 {
				item.Metric, _ = strconv.Atoi(fields[i-1])
			}
			if i+1 < len(fields) {
				item.Index, _ = strconv.Atoi(fields[i+1])
			}
			if i+2 < len(fields) {
				item.Gateway = fields[i+2]
			}
			result = append(result, item)
			break
		}
	}
	return result, nil
}

func sameRoute(item existingRoute, route normalizedRoute) bool {
	if item.Metric > 0 && item.Metric != route.Metric {
		return false
	}
	if item.Gateway == route.Gateway || strings.EqualFold(item.Gateway, route.Interface) {
		return true
	}
	return route.Index > 0 && item.Index == route.Index && item.Gateway == "0.0.0.0"
}

func sameInstalledRoute(item existingRoute, route InstalledRoute) bool {
	if item.Metric > 0 && item.Metric != route.Metric {
		return false
	}
	if item.Gateway == route.Gateway || strings.EqualFold(item.Gateway, route.Interface) {
		return true
	}
	return route.Index > 0 && item.Index == route.Index && item.Gateway == "0.0.0.0"
}

func runNetsh(args ...string) (string, error) {
	cmd := exec.Command("netsh", args...)
	out, err := cmd.CombinedOutput()
	if err != nil {
		return string(out), fmt.Errorf("netsh %s: %w: %s", strings.Join(args, " "), err, strings.TrimSpace(string(out)))
	}
	return string(out), nil
}
