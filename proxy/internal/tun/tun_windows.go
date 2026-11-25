//go:build windows

package tun

import (
	"errors"
	"fmt"
	"log"
	"net"
	"os/exec"
	"regexp"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"syscall"
	"time"
	"unsafe"

	"hotspot-proxy/internal/netutil"
)

const (
	wintunRingCapacity = 0x40000
	wintunMaxPacket    = 0xffff

	wintunErrorHandleEOF    = 38
	wintunErrorNoMoreItems  = 259
	wintunErrorBufferFull   = 111
	wintunWaitObject0       = 0
	wintunWaitTimeout       = 258
	wintunReadWaitMS        = 1000
	wintunSendRetryInterval = time.Millisecond
)

var defaultRoutePattern = regexp.MustCompile(`(?m)^\s*0\.0\.0\.0\s+0\.0\.0\.0\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)\s+([0-9]+)`)

var moveMemory = syscall.NewLazyDLL("kernel32.dll").NewProc("RtlMoveMemory")

type wintunAPI struct {
	createAdapter  *syscall.LazyProc
	openAdapter    *syscall.LazyProc
	closeAdapter   *syscall.LazyProc
	startSession   *syscall.LazyProc
	endSession     *syscall.LazyProc
	readWaitEvent  *syscall.LazyProc
	receivePacket  *syscall.LazyProc
	releasePacket  *syscall.LazyProc
	allocatePacket *syscall.LazyProc
	sendPacket     *syscall.LazyProc
}

func loadWintun() (*wintunAPI, error) {
	dll := syscall.NewLazyDLL("wintun.dll")
	if err := dll.Load(); err != nil {
		return nil, fmt.Errorf("加载 wintun.dll 失败: %w；请将官方 x86 wintun.dll 放到程序目录", err)
	}
	return &wintunAPI{
		createAdapter:  dll.NewProc("WintunCreateAdapter"),
		openAdapter:    dll.NewProc("WintunOpenAdapter"),
		closeAdapter:   dll.NewProc("WintunCloseAdapter"),
		startSession:   dll.NewProc("WintunStartSession"),
		endSession:     dll.NewProc("WintunEndSession"),
		readWaitEvent:  dll.NewProc("WintunGetReadWaitEvent"),
		receivePacket:  dll.NewProc("WintunReceivePacket"),
		releasePacket:  dll.NewProc("WintunReleaseReceivePacket"),
		allocatePacket: dll.NewProc("WintunAllocateSendPacket"),
		sendPacket:     dll.NewProc("WintunSendPacket"),
	}, nil
}

func callError(r1, r2 uintptr, callErr error) error {
	if callErr != nil {
		return callErr
	}
	if r1 != 0 {
		return nil
	}
	if r2 != 0 {
		return syscall.Errno(r2)
	}
	return errors.New("Wintun 调用失败")
}

func errnoCode(err error) uintptr {
	if e, ok := err.(syscall.Errno); ok {
		return uintptr(e)
	}
	return 0
}

type wintunAdapter struct {
	api       *wintunAPI
	name      string
	adapter   uintptr
	session   uintptr
	readEvent uintptr
	closed    atomic.Bool
	closeOnce sync.Once
}

func newAdapter(name, ip string, logger *log.Logger) (packetAdapter, error) {
	api, err := loadWintun()
	if err != nil {
		return nil, err
	}
	namePtr, err := syscall.UTF16PtrFromString(name)
	if err != nil {
		return nil, fmt.Errorf("Wintun 适配器名称无效: %w", err)
	}
	tunnelPtr, err := syscall.UTF16PtrFromString("OneNetShare")
	if err != nil {
		return nil, fmt.Errorf("Wintun 隧道类型无效: %w", err)
	}

	adapter, r2, callErr := api.openAdapter.Call(uintptr(unsafe.Pointer(namePtr)))
	if adapter == 0 {
		adapter, r2, callErr = api.createAdapter.Call(uintptr(unsafe.Pointer(namePtr)), uintptr(unsafe.Pointer(tunnelPtr)), 0)
	}
	if adapter == 0 {
		return nil, fmt.Errorf("创建或打开 Wintun 适配器 %s 失败: %w (r2=%d)", name, callError(adapter, r2, callErr), r2)
	}

	session, r2, callErr := api.startSession.Call(adapter, wintunRingCapacity)
	if session == 0 {
		api.closeAdapter.Call(adapter)
		return nil, fmt.Errorf("启动 Wintun 会话失败: %w (r2=%d)", callError(session, r2, callErr), r2)
	}

	event, _, _ := api.readWaitEvent.Call(session)
	w := &wintunAdapter{
		api:       api,
		name:      name,
		adapter:   adapter,
		session:   session,
		readEvent: event,
	}
	logger.Printf("Wintun 适配器 %s 已创建，隧道地址 %s", name, ip)
	return w, nil
}

func (w *wintunAdapter) Name() string { return w.name }

func (w *wintunAdapter) Read(buf []byte) (int, error) {
	for {
		if w.closed.Load() {
			return 0, ioEOF()
		}
		var size uint32
		packet, r2, callErr := w.api.receivePacket.Call(w.session, uintptr(unsafe.Pointer(&size)))
		if packet != 0 {
			n := int(size)
			if n > len(buf) {
				n = len(buf)
			}
			copyFromAddr(buf[:n], packet)
			w.api.releasePacket.Call(w.session, packet)
			return n, nil
		}
		err := callError(packet, r2, callErr)
		code := errnoCode(err)
		if code == wintunErrorNoMoreItems {
			if w.readEvent != 0 {
				wait, waitErr := syscall.WaitForSingleObject(syscall.Handle(w.readEvent), wintunReadWaitMS)
				if waitErr != nil || wait == wintunWaitTimeout {
					continue
				}
				_ = wintunWaitObject0
			}
			continue
		}
		if code == wintunErrorHandleEOF {
			return 0, ioEOF()
		}
		return 0, fmt.Errorf("WintunReceivePacket: %w", err)
	}
}

func (w *wintunAdapter) Write(pkt []byte) (int, error) {
	if len(pkt) == 0 {
		return 0, nil
	}
	if len(pkt) > wintunMaxPacket {
		return 0, fmt.Errorf("Wintun 数据包过大: %d", len(pkt))
	}
	for attempt := 0; attempt < 100; attempt++ {
		if w.closed.Load() {
			return 0, ioEOF()
		}
		packet, r2, callErr := w.api.allocatePacket.Call(w.session, uintptr(len(pkt)))
		if packet != 0 {
			copyToAddr(packet, pkt)
			w.api.sendPacket.Call(w.session, packet)
			return len(pkt), nil
		}
		err := callError(packet, r2, callErr)
		if errnoCode(err) != wintunErrorBufferFull {
			return 0, fmt.Errorf("WintunAllocateSendPacket: %w", err)
		}
		time.Sleep(wintunSendRetryInterval)
	}
	return 0, fmt.Errorf("Wintun 发送缓冲区持续已满")
}

func (w *wintunAdapter) Close() error {
	w.closeOnce.Do(func() {
		w.closed.Store(true)
		if w.session != 0 {
			w.api.endSession.Call(w.session)
			w.session = 0
		}
		if w.adapter != 0 {
			w.api.closeAdapter.Call(w.adapter)
			w.adapter = 0
		}
	})
	return nil
}

func ioEOF() error { return errors.New("Wintun 适配器已关闭") }

func copyFromAddr(dst []byte, addr uintptr) {
	if len(dst) == 0 || addr == 0 {
		return
	}
	moveMemory.Call(uintptr(unsafe.Pointer(&dst[0])), addr, uintptr(len(dst)))
}

func copyToAddr(addr uintptr, src []byte) {
	if len(src) == 0 || addr == 0 {
		return
	}
	moveMemory.Call(addr, uintptr(unsafe.Pointer(&src[0])), uintptr(len(src)))
}

type upstreamInfo struct {
	Name    string
	Index   int
	LocalIP net.IP
	Gateway net.IP
	Metric  int
}

func detectUpstream(preferred string) (upstreamInfo, error) {
	out, err := exec.Command("route", "print", "0.0.0.0").CombinedOutput()
	if err != nil {
		return upstreamInfo{}, fmt.Errorf("读取默认路由失败: %w: %s", err, string(out))
	}
	ifaces, err := net.Interfaces()
	if err != nil {
		return upstreamInfo{}, fmt.Errorf("读取网卡列表失败: %w", err)
	}

	var best upstreamInfo
	found := false
	for _, line := range strings.Split(string(out), "\n") {
		matches := defaultRoutePattern.FindStringSubmatch(line)
		if len(matches) != 4 {
			continue
		}
		gateway := net.ParseIP(matches[1]).To4()
		localIP := net.ParseIP(matches[2]).To4()
		metric, _ := strconv.Atoi(matches[3])
		if gateway == nil || localIP == nil || gateway.Equal(net.IPv4zero) {
			continue
		}
		ifi, ok := interfaceByIP(ifaces, localIP)
		if !ok {
			continue
		}
		if preferred != "" && !strings.EqualFold(ifi.Name, preferred) {
			continue
		}
		if found && metric >= best.Metric {
			continue
		}
		best = upstreamInfo{
			Name: ifi.Name, Index: ifi.Index, LocalIP: localIP,
			Gateway: gateway, Metric: metric,
		}
		found = true
	}
	if !found {
		if preferred != "" {
			return upstreamInfo{}, fmt.Errorf("未在网卡 %s 上找到 IPv4 默认路由", preferred)
		}
		return upstreamInfo{}, fmt.Errorf("未找到可用的 IPv4 默认路由")
	}
	return best, nil
}

func interfaceByIP(ifaces []net.Interface, ip net.IP) (net.Interface, bool) {
	for _, ifi := range ifaces {
		addrs, err := ifi.Addrs()
		if err != nil {
			continue
		}
		for _, addr := range addrs {
			ipNet, ok := addr.(*net.IPNet)
			if !ok || ipNet.IP == nil {
				continue
			}
			if ipNet.IP.To4() != nil && ipNet.IP.Equal(ip) {
				return ifi, true
			}
		}
	}
	return net.Interface{}, false
}

func nameArg(name string) string { return "name=" + name }

func runNetsh(args ...string) error {
	cmd := exec.Command("netsh", args...)
	out, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("netsh %s: %w: %s", strings.Join(args, " "), err, strings.TrimSpace(string(out)))
	}
	return nil
}

func tryNetsh(logger *log.Logger, format string, args ...string) {
	if err := runNetsh(args...); err != nil {
		logger.Printf(format, err)
	}
}

func SetupTUN(opts Options, logger *log.Logger) (func(), error) {
	if opts.Name == "" {
		opts.Name = "OneNetShare Tunnel"
	}
	if opts.IP == "" {
		opts.IP = "10.66.0.1"
	}
	if opts.GatewayIP == "" {
		opts.GatewayIP = "10.66.0.2"
	}
	prefix := opts.PrefixLen
	if prefix <= 0 || prefix > 32 {
		prefix = 24
	}
	mask := net.IP(net.CIDRMask(prefix, 32)).String()

	upstream, err := detectUpstream(opts.UpstreamInterface)
	if err != nil {
		return func() {}, err
	}
	netutil.Configure(upstream.Name, upstream.Index, upstream.LocalIP)
	logger.Printf("上游出口: %s (index=%d ip=%s gateway=%s metric=%d)",
		upstream.Name, upstream.Index, upstream.LocalIP, upstream.Gateway, upstream.Metric)

	if err := runNetsh("interface", "ipv4", "set", "address", nameArg(opts.Name), "static", opts.IP, mask); err != nil {
		netutil.Reset()
		return func() {}, err
	}
	tryNetsh(logger, "设置 Wintun MTU 失败: %v", "interface", "ipv4", "set", "subinterface", nameArg(opts.Name), "mtu=1420", "store=persistent")
	tryNetsh(logger, "设置 Wintun 接口跃点数失败: %v", "interface", "ipv4", "set", "interface", nameArg(opts.Name), "metric=1")

	if opts.Route {
		tryNetsh(logger, "清理旧默认路由失败: %v", "interface", "ipv4", "delete", "route", "0.0.0.0/0", nameArg(opts.Name))
		if err := runNetsh("interface", "ipv4", "add", "route", "0.0.0.0/0", nameArg(opts.Name), opts.GatewayIP, "metric=1"); err != nil {
			netutil.Reset()
			return func() {}, err
		}
		tryNetsh(logger, "设置 Wintun DNS 失败: %v", "interface", "ipv4", "set", "dnsservers", nameArg(opts.Name), "static", "1.1.1.1", "primary")
	}

	cleanup := func() {
		if opts.Route {
			tryNetsh(logger, "删除 Wintun 默认路由失败: %v", "interface", "ipv4", "delete", "route", "0.0.0.0/0", nameArg(opts.Name))
			tryNetsh(logger, "恢复 Wintun DNS 失败: %v", "interface", "ipv4", "set", "dnsservers", nameArg(opts.Name), "dhcp")
		}
		netutil.Reset()
	}
	return cleanup, nil
}
