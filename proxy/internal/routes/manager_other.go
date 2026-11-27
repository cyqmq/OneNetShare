//go:build !windows

package routes

import "log"

func applyRoutes(file *File, routes []normalizedRoute, logger *log.Logger) ([]InstalledRoute, error) {
	if len(routes) > 0 {
		logger.Printf("当前平台不管理 Windows 路由，已跳过 %d 条 managed routes", len(routes))
	}
	return nil, nil
}

func cleanupRoutes(routes []InstalledRoute, logger *log.Logger) error {
	return nil
}
