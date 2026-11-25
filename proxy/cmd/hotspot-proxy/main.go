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
	"syscall"
	"time"

	"hotspot-proxy/internal/config"
	"hotspot-proxy/internal/deviceforge"
	"hotspot-proxy/internal/headers"
	"hotspot-proxy/internal/monitor"
	"hotspot-proxy/internal/proxy"
	"hotspot-proxy/internal/tun"
	"hotspot-proxy/internal/version"
)

func main() {
	showVersion := flag.Bool("version", false, "显示版本")
	configPath := flag.String("config", "", "配置文件路径")
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

	var forge *deviceforge.Manager
	loader, err := config.NewLoader(path, func(c *config.Config) {
		logger.Printf("配置已加载: mode=%s enabled=%v rules=%d", c.Mode, c.Enabled, len(c.Rules))
		if forge != nil {
			forge.Reload(c)
		}
	})
	if err != nil {
		logger.Fatalf("配置错误: %v", err)
	}
	forge = deviceforge.NewManager(loader, logger)
	tracker := monitor.New(logger, func() bool { return loader.Get().Monitor.IsEnabled() })

	cfg := loader.Get()

	if cfg.TUN.Enabled {
		tunDev, err := tun.New(cfg.TUN.Name, cfg.TUN.IP, logger)
		if err != nil {
			logger.Fatalf("TUN 初始化失败: %v", err)
		}
		tunDev.SetTTL(cfg.TUN.TTL)
		tunDev.SetHTTPEngine(headers.NewEngine(loader, tracker))
		tunDev.SetMonitor(tracker)
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
		tunDev.StartCleanup()
		if cfg.DeviceForge.Enabled {
			tunDev.SetUDPHandler(func(srcIP net.IP, srcPort uint16, dstIP net.IP, dstPort uint16, payload []byte) {
				forge.HandleUDP(srcIP, srcPort, dstIP, dstPort, payload, tunDev)
			})
			forge.Reload(cfg)
		}

		proxyServer := proxy.NewServer(loader, logger, tracker)
		go func() {
			if err := proxyServer.Run(cfg.ListenHTTP); err != nil {
				logger.Printf("HTTP 代理启动失败: %v", err)
			}
		}()

		stopCh := make(chan struct{})
		var stopOnce sync.Once
		stop := func() { stopOnce.Do(func() { close(stopCh) }) }
		go startStatusServer(loader, logger, stop, tracker)
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
				cleanup()
				tunDev.Close()
				logger.Printf("全局 TUN 模式已退出，路由已恢复")
				return
			case sig := <-sigCh:
				if sig == syscall.SIGHUP {
					if err := loader.Reload(); err != nil {
						logger.Printf("reload 失败: %v", err)
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

	if cfg.Mode == "forward" && cfg.ListenHTTPS == "" {
		cfg.ListenHTTPS = "127.0.0.1:18443"
	}

	if cfg.Mode != "forward" {
		if err := srv.ListenHTTPS(cfg.ListenHTTPS); err != nil {
			logger.Fatal(err)
		}
	}
	go func() {
		if err := srv.Run(cfg.ListenHTTP); err != nil {
			logger.Fatal(err)
		}
	}()

	go startReloadWatcher(loader, logger)

	stopCh := make(chan struct{})
	var stopOnce sync.Once
	stop := func() { stopOnce.Do(func() { close(stopCh) }) }
	go startStatusServer(loader, logger, stop, tracker)

	logger.Printf("%s 启动完成 (http=%s https=%s mode=%s)",
		version.String(), cfg.ListenHTTP, cfg.ListenHTTPS, cfg.Mode)

	sigCh := make(chan os.Signal, 1)
	signal.Notify(sigCh, syscall.SIGINT, syscall.SIGTERM, syscall.SIGHUP)
	for {
		select {
		case <-stopCh:
			srv.Close()
			return
		case sig := <-sigCh:
			if sig == syscall.SIGHUP {
				if err := loader.Reload(); err != nil {
					logger.Printf("reload 失败: %v", err)
				}
				continue
			}
			logger.Printf("收到 %v，退出", sig)
			stop()
		}
	}
}

func startReloadWatcher(loader *config.Loader, logger *log.Logger) {
	ticker := time.NewTicker(2 * time.Second)
	defer ticker.Stop()
	for range ticker.C {
		changed, err := loader.ReloadIfChanged()
		if err != nil {
			continue
		}
		if changed {
			logger.Println("检测到配置文件变更，已热重载")
		}
	}
}

func startStatusServer(loader *config.Loader, logger *log.Logger, stop func(), tracker *monitor.Tracker) {
	mux := http.NewServeMux()
	mux.HandleFunc("/status", func(w http.ResponseWriter, r *http.Request) {
		cfg := loader.Get()
		exe, _ := os.Executable()
		fmt.Fprintf(w, "version=%s revision=%s pid=%d exe=%s\n", version.String(), version.Revision, os.Getpid(), exe)
		fmt.Fprintf(w, "generation=%s\n", cfg.GUIGeneration)
		fmt.Fprintf(w, "mode=%s enabled=%v profile=%s ua_preset=%s user_agent_override=%s rules=%d tun=%v\n",
			cfg.Mode, cfg.Enabled, cfg.DefaultProfile, cfg.UAPreset, cfg.UserAgentOverride, len(cfg.Rules), cfg.TUN.Enabled)
		fmt.Fprintf(w, "deviceforge=%v target=%s udp=%v dns=%v ntp=%v mdns=%v dhcp=%v captive=%v/%s headers=%v\n",
			cfg.DeviceForge.Enabled, cfg.DeviceForge.TargetOS, cfg.DeviceForge.UDP.Enabled,
			cfg.DeviceForge.DNS.Enabled, cfg.DeviceForge.NTP.Enabled,
			cfg.DeviceForge.MDNS.Enabled, cfg.DeviceForge.DHCP.Enabled,
			cfg.DeviceForge.Captive.Enabled, cfg.DeviceForge.Captive.Mode, cfg.DeviceForge.Headers)
		stats := tracker.Stats()
		fmt.Fprintf(w, "monitor=%v/%v/%v/%v ua_phone=%d ua_desktop=%d tls=%d alerts=%d\n",
			cfg.Monitor.IsEnabled(), cfg.Monitor.IsTTL(), cfg.Monitor.IsUA(), cfg.Monitor.IsTLS(),
			stats.Phone, stats.Desktop, stats.TLS, stats.Alerts)
	})
	mux.HandleFunc("/reload", func(w http.ResponseWriter, r *http.Request) {
		if err := loader.Reload(); err != nil {
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
