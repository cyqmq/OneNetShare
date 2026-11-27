package main

import (
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"os"
	"os/signal"
	"sync"
	"sync/atomic"
	"syscall"
	"time"

	"hotspot-proxy/internal/config"
	"hotspot-proxy/internal/deviceforge"
	"hotspot-proxy/internal/headers"
	"hotspot-proxy/internal/monitor"
	"hotspot-proxy/internal/outboundguard"
	"hotspot-proxy/internal/proxy"
	"hotspot-proxy/internal/routes"
	"hotspot-proxy/internal/tun"
	"hotspot-proxy/internal/version"
)

var processStartedAt = time.Now()
var configReloads atomic.Uint64
var lastConfigReload atomic.Int64

func main() {
	showVersion := flag.Bool("version", false, "显示版本")
	configPath := flag.String("config", "", "配置文件路径")
	managedRoutesPath := flag.String("managed-routes", "", "管理路由配置文件路径")
	dumpConfig := flag.Bool("dump-config", false, "输出默认配置模板到 stdout")
	flag.Parse()

	if *showVersion {
		fmt.Println(version.String())
		return
	}
	if *dumpConfig {
		fmt.Print(config.DefaultJSON())
		return
	}

	path := config.ResolveConfigPath(*configPath)
	var logger *log.Logger
	cfg0, err := config.LoadRaw(path)
	if err == nil {
		if p := os.Getenv("HOTSPOT_PROXY_LOG_FILE"); p != "" {
			cfg0.Log.File = p
		}
	}
	if err == nil && cfg0.Log.File != "" {
		f, ferr := os.OpenFile(cfg0.Log.File, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0644)
		if ferr == nil {
			defer f.Close()
			logger = log.New(f, "", log.LstdFlags)
		}
	}
	if logger == nil {
		logger = log.New(os.Stderr, "", log.LstdFlags)
	}

	routesPath := *managedRoutesPath
	if routesPath == "" {
		routesPath = routes.DefaultPath(path)
	}
	routeManager := routes.New(routesPath, logger)
	reloadManagedRoutes := func() error {
		_, err := routeManager.ReloadIfChanged()
		return err
	}

	var forge *deviceforge.Manager
	var outbound *outboundguard.Guard
	var headerEngine *headers.Engine
	loader, err := config.NewLoader(path, func(c *config.Config) {
		configReloads.Add(1)
		lastConfigReload.Store(time.Now().UnixNano())
		logger.Printf("配置已加载: mode=%s enabled=%v rules=%d", c.Mode, c.Enabled, len(c.Rules))
		if forge != nil {
			forge.Reload(c)
		}
		if outbound != nil {
			outbound.Reload(c.OutboundGuard)
			logger.Printf("出站封堵: %s", outbound.Stats().Format())
		}
		identity := config.CheckIdentity(c)
		for i := range identity.Issues {
			logger.Printf("[probe][画像一致性] %s", identity.IssueLine(i))
		}
	})
	if err != nil {
		logger.Fatalf("配置错误: %v", err)
	}
	forge = deviceforge.NewManager(loader, logger)
	outbound = outboundguard.New(loader.Get().OutboundGuard, logger)
	tracker := monitor.New(logger, func() bool { return loader.Get().Monitor.IsEnabled() })
	headerEngine = headers.NewEngine(loader, tracker)

	cfg := loader.Get()

	if cfg.TUN.Enabled {
		tunDev, err := tun.New(cfg.TUN.Name, cfg.TUN.IP, logger)
		if err != nil {
			logger.Fatalf("TUN 初始化失败: %v", err)
		}
		tunDev.SetTTL(cfg.TUN.TTL)
		tunDev.SetHTTPEngine(headerEngine)
		tunDev.SetMonitor(tracker)
		tunDev.SetOutboundGuard(outbound)
		if cfg.Network.MtuClamp {
			tunDev.SetMSS(cfg.Network.MssClamp)
		}
		route := true
		if cfg.TUN.Route != nil {
			route = *cfg.TUN.Route
		}
		cleanup, err := tun.SetupTUN(tun.Options{
			Name: cfg.TUN.Name, IP: cfg.TUN.IP, GatewayIP: cfg.TUN.GatewayIP,
			UpstreamInterface: cfg.TUN.UpstreamInterface, PrefixLen: cfg.TUN.PrefixLen, Route: route,
			DNSServers: cfg.DeviceForge.DNS.UpstreamServers,
		}, logger)
		if err != nil {
			tunDev.Close()
			logger.Fatalf("TUN 路由配置失败: %v", err)
		}
		if err := routeManager.Start(); err != nil {
			cleanup()
			tunDev.Close()
			logger.Fatalf("管理路由配置失败: %v", err)
		}
		tunDev.StartCleanup()
		if cfg.DeviceForge.Enabled {
			tunDev.SetUDPHandler(func(srcIP net.IP, srcPort uint16, dstIP net.IP, dstPort uint16, payload []byte) {
				forge.HandleUDP(srcIP, srcPort, dstIP, dstPort, payload, tunDev)
			})
			forge.Reload(cfg)
		}

		proxyServer := proxy.NewServer(loader, logger, tracker)
		proxyServer.SetHeaderEngine(headerEngine)
		go func() {
			if err := proxyServer.Run(cfg.ListenHTTP); err != nil {
				logger.Printf("HTTP 代理启动失败: %v", err)
			}
		}()

		stopCh := make(chan struct{})
		var stopOnce sync.Once
		stop := func() { stopOnce.Do(func() { close(stopCh) }) }
		go startStatusServer(loader, logger, stop, tracker, forge, routeManager, outbound, headerEngine)
		go func() {
			if err := tunDev.Run(); err != nil {
				logger.Printf("TUN 运行错误: %v", err)
				stop()
			}
		}()
		logger.Printf("全局 TUN 模式运行: %s (%s)", tunDev.Name(), cfg.TUN.IP)

		sigCh := make(chan os.Signal, 1)
		signal.Notify(sigCh, syscall.SIGINT, syscall.SIGTERM, syscall.SIGHUP)
		for {
			select {
			case <-stopCh:
				proxyServer.Close()
				if err := routeManager.Cleanup(); err != nil {
					logger.Printf("管理路由清理失败: %v", err)
				}
				cleanup()
				tunDev.Close()
				logger.Printf("全局 TUN 模式已退出，路由已恢复")
				return
			case sig := <-sigCh:
				if sig == syscall.SIGHUP {
					if err := loader.Reload(); err != nil {
						logger.Printf("reload 失败: %v", err)
					}
					if err := reloadManagedRoutes(); err != nil {
						logger.Printf("managed routes reload 失败: %v", err)
					}
					continue
				}
				logger.Printf("收到 %v，退出", sig)
				stop()
			}
		}
	}

	if cfg.DeviceForge.Enabled && cfg.DeviceForge.UDP.Enabled {
		if err := forge.StartUDP(cfg.Mode); err != nil {
			logger.Printf("DeviceForge UDP 启动失败: %v", err)
		}
	}

	srv := proxy.NewServer(loader, logger, tracker)
	srv.SetHeaderEngine(headerEngine)

	if cfg.Mode == "forward" && cfg.ListenHTTPS == "" {
		cfg.ListenHTTPS = "127.0.0.1:18443"
	}

	if cfg.Mode != "forward" {
		if err := srv.ListenHTTPS(cfg.ListenHTTPS); err != nil {
			logger.Fatal(err)
		}
	}
	if err := routeManager.Start(); err != nil {
		logger.Fatalf("管理路由配置失败: %v", err)
	}
	go func() {
		if err := srv.Run(cfg.ListenHTTP); err != nil {
			logger.Fatal(err)
		}
	}()

	stopCh := make(chan struct{})
	var stopOnce sync.Once
	stop := func() { stopOnce.Do(func() { close(stopCh) }) }
	go startReloadWatcher(loader, logger, routeManager, stopCh)
	go startStatusServer(loader, logger, stop, tracker, forge, routeManager, outbound, headerEngine)

	logger.Printf("%s 启动完成 (http=%s https=%s mode=%s)",
		version.String(), cfg.ListenHTTP, cfg.ListenHTTPS, cfg.Mode)

	sigCh := make(chan os.Signal, 1)
	signal.Notify(sigCh, syscall.SIGINT, syscall.SIGTERM, syscall.SIGHUP)
	for {
		select {
		case <-stopCh:
			srv.Close()
			if err := routeManager.Cleanup(); err != nil {
				logger.Printf("管理路由清理失败: %v", err)
			}
			return
		case sig := <-sigCh:
			if sig == syscall.SIGHUP {
				if err := loader.Reload(); err != nil {
					logger.Printf("reload 失败: %v", err)
				}
				if err := reloadManagedRoutes(); err != nil {
					logger.Printf("managed routes reload 失败: %v", err)
				}
				continue
			}
			logger.Printf("收到 %v，退出", sig)
			stop()
		}
	}
}

func startReloadWatcher(loader *config.Loader, logger *log.Logger, routeManager *routes.Manager, stopCh <-chan struct{}) {
	ticker := time.NewTicker(2 * time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-stopCh:
			return
		case <-ticker.C:
			changed, err := loader.ReloadIfChanged()
			if err != nil {
				continue
			}
			if changed {
				logger.Println("检测到配置文件变更，已热重载")
			}
			if _, err := routeManager.ReloadIfChanged(); err != nil {
				logger.Printf("managed routes 热重载失败: %v", err)
			}
		}
	}
}

func startStatusServer(loader *config.Loader, logger *log.Logger, stop func(), tracker *monitor.Tracker, forge *deviceforge.Manager, routeManager *routes.Manager, outbound *outboundguard.Guard, headerEngine *headers.Engine) {
	mux := http.NewServeMux()
	mux.HandleFunc("/status", func(w http.ResponseWriter, r *http.Request) {
		cfg := loader.Get()
		exe, _ := os.Executable()
		fmt.Fprintf(w, "version=%s revision=%s pid=%d exe=%s\n", version.String(), version.Revision, os.Getpid(), exe)
		lastReloadAge := int64(-1)
		if last := lastConfigReload.Load(); last > 0 {
			lastReloadAge = int64(time.Since(time.Unix(0, last)).Seconds())
		}
		fmt.Fprintf(w, "runtime=uptime_seconds=%d start_time=%s config_reloads=%d last_reload_age_seconds=%d\n",
			int64(time.Since(processStartedAt).Seconds()), processStartedAt.Format(time.RFC3339), configReloads.Load(), lastReloadAge)
		fmt.Fprintf(w, "generation=%s\n", cfg.GUIGeneration)
		identity := config.CheckIdentity(cfg)
		fmt.Fprintf(w, "identity=%s ua_browser=%s ua_platform=%s ua_target=%s tun_ttl=%d identity_issues=%d\n",
			identity.Status, identity.Browser, identity.Platform, identity.TargetOS, identity.TTL, len(identity.Issues))
		for i := range identity.Issues {
			fmt.Fprintf(w, "identity_issue=%s\n", identity.IssueLine(i))
		}
		fmt.Fprintf(w, "mode=%s enabled=%v profile=%s ua_preset=%s user_agent_override=%s rules=%d tun=%v\n",
			cfg.Mode, cfg.Enabled, cfg.DefaultProfile, cfg.UAPreset, cfg.UserAgentOverride, len(cfg.Rules), cfg.TUN.Enabled)
		fmt.Fprintf(w, "deviceforge=%v target=%s udp=%v dns=%v ntp=%v mdns=%v dhcp=%v captive=%v/%s headers=%v\n",
			cfg.DeviceForge.Enabled, cfg.DeviceForge.TargetOS, cfg.DeviceForge.UDP.Enabled,
			cfg.DeviceForge.DNS.Enabled, cfg.DeviceForge.NTP.Enabled,
			cfg.DeviceForge.MDNS.Enabled, cfg.DeviceForge.DHCP.Enabled,
			cfg.DeviceForge.Captive.Enabled, cfg.DeviceForge.Captive.Mode, cfg.DeviceForge.Headers)
		if headerEngine != nil {
			fmt.Fprintf(w, "header_plane=%s\n", headerEngine.Stats().Format())
		}
		stats := tracker.Stats()
		fmt.Fprintf(w, "monitor=%v/%v/%v/%v ua_phone=%d ua_desktop=%d tls=%d alerts=%d\n",
			cfg.Monitor.IsEnabled(), cfg.Monitor.IsTTL(), cfg.Monitor.IsUA(), cfg.Monitor.IsTLS(),
			stats.Phone, stats.Desktop, stats.TLS, stats.Alerts)
		if outbound != nil {
			fmt.Fprintf(w, "outbound_guard=%s\n", outbound.Stats().Format())
		}
		dnsStats := forge.DNSStats()
		fmt.Fprintf(w, "dns_embedded=%v dns_cache=%d dns_queries=%d dns_hits=%d dns_misses=%d dns_upstream_ok=%d dns_upstream_err=%d\n",
			cfg.DeviceForge.DNS.IsEmbedded(), cfg.DeviceForge.DNS.CacheSize,
			dnsStats.Queries, dnsStats.Hits, dnsStats.Misses, dnsStats.UpstreamOK, dnsStats.UpstreamErr)
		fmt.Fprintln(w, routeManager.Status())
	})
	mux.HandleFunc("/reload", func(w http.ResponseWriter, r *http.Request) {
		if err := loader.Reload(); err != nil {
			http.Error(w, err.Error(), 500)
			return
		}
		if _, err := routeManager.ReloadIfChanged(); err != nil {
			http.Error(w, err.Error(), 500)
			return
		}
		io.WriteString(w, "ok\n")
	})
	mux.HandleFunc("/shutdown", func(w http.ResponseWriter, r *http.Request) {
		stop()
		io.WriteString(w, "ok\n")
	})
	srv := &http.Server{Addr: "127.0.0.1:18089", Handler: mux, ReadHeaderTimeout: 5 * time.Second}
	if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
		logger.Printf("状态服务: %v", err)
	}
}
