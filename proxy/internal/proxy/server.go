package proxy

import (
	"bufio"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"strings"
	"sync"
	"time"

	"hotspot-proxy/internal/config"
	"hotspot-proxy/internal/headers"
	"hotspot-proxy/internal/netutil"
	"hotspot-proxy/internal/redirect"
)

type Server struct {
	loader   *config.Loader
	engine   *headers.Engine
	logger   *log.Logger
	listener net.Listener
	wg       sync.WaitGroup
	quit     chan struct{}
}

func NewServer(loader *config.Loader, logger *log.Logger) *Server {
	return &Server{
		loader: loader,
		engine: headers.NewEngine(loader),
		logger: logger,
		quit:   make(chan struct{}),
	}
}

func (s *Server) handleCaptive(client net.Conn, req *headers.Request) bool {
	path := req.Path
	if idx := strings.Index(path, "://"); idx >= 0 {
		rest := path[idx+3:]
		if part := strings.IndexAny(rest, "/?"); part >= 0 {
			path = rest[part:]
		} else {
			path = "/"
		}
	}
	cfg := s.loader.Get()
	c := cfg.DeviceForge.Captive
	if !cfg.DeviceForge.Enabled || !config.CaptiveHTTPMatch(c, req.Host, path) {
		return false
	}
	if c.Mode == "drop" {
		return true
	}

	code := c.HTTPCode
	if code <= 0 {
		code = http.StatusNoContent
	}
	body := c.HTTPBody
	fmt.Fprintf(client, "HTTP/1.1 %d %s\r\nContent-Length: %d\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n%s",
		code, http.StatusText(code), len(body), body)
	return true
}

func (s *Server) listen(addr string) (net.Listener, error) {
	if s.loader.Get().Mode == "tproxy" {
		return redirect.ListenTransparent(addr)
	}
	return net.Listen("tcp", addr)
}

func (s *Server) ListenHTTPS(addr string) error {
	ln, err := s.listen(addr)
	if err != nil {
		return fmt.Errorf("监听 HTTPS 端口 %s 失败: %w", addr, err)
	}
	go s.acceptLoop(ln, "https")
	return nil
}

func (s *Server) Run(addr string) error {
	ln, err := s.listen(addr)
	if err != nil {
		return fmt.Errorf("监听 HTTP 端口 %s 失败: %w", addr, err)
	}
	s.listener = ln
	s.acceptLoop(ln, "http")
	return nil
}

func (s *Server) Close() {
	close(s.quit)
	if s.listener != nil {
		s.listener.Close()
	}
	s.wg.Wait()
}

func (s *Server) acceptLoop(ln net.Listener, kind string) {
	s.logger.Printf("监听 %s 代理: %s", kind, ln.Addr())
	for {
		conn, err := ln.Accept()
		if err != nil {
			select {
			case <-s.quit:
				return
			default:
				s.logger.Printf("accept 错误: %v", err)
				time.Sleep(100 * time.Millisecond)
				continue
			}
		}
		s.wg.Add(1)
		go func() {
			defer s.wg.Done()
			defer conn.Close()
			s.handle(conn.(*net.TCPConn), kind)
		}()
	}
}

func (s *Server) handle(client *net.TCPConn, kind string) {
	client.SetNoDelay(true)
	cfg := s.loader.Get()

	var dst string
	if cfg.Mode == "forward" {
		// forward mode determines destination from HTTP request / CONNECT
		dst = ""
	} else {
		var err error
		dst, err = redirect.OriginalDst(client)
		if err != nil {
			s.logger.Printf("获取原始目标失败: %v", err)
			return
		}
	}

	if kind == "https" {
		if cfg.Mode == "forward" {
			s.handleForwardConnect(client)
		} else {
			s.handleTunnel(client, dst)
		}
		return
	}

	if cfg.Mode == "forward" {
		s.handleForwardHTTP(client)
		return
	}
	s.handleTransparentHTTP(client, dst)
}

func (s *Server) dial(dst string) (net.Conn, error) {
	conn, err := netutil.DialTimeout("tcp", dst, 15*time.Second)
	if err != nil {
		return nil, fmt.Errorf("连接目标 %s 失败: %w", dst, err)
	}
	if tc, ok := conn.(*net.TCPConn); ok {
		tc.SetNoDelay(true)
	}
	return conn, nil
}

func isLocalTarget(dst string) bool {
	host, _, err := net.SplitHostPort(dst)
	if err != nil {
		return false
	}
	ip := net.ParseIP(host)
	if ip == nil {
		return false
	}
	if ip.IsLoopback() {
		return true
	}
	addrs, err := net.InterfaceAddrs()
	if err != nil {
		return false
	}
	for _, addr := range addrs {
		if ipnet, ok := addr.(*net.IPNet); ok && ipnet.IP.Equal(ip) {
			return true
		}
	}
	return false
}

func (s *Server) handleTunnel(client *net.TCPConn, dst string) {
	if isLocalTarget(dst) {
		return
	}
	target, err := s.dial(dst)
	if err != nil {
		s.logger.Printf("[tunnel] %v", err)
		return
	}
	defer target.Close()

	var wg sync.WaitGroup
	wg.Add(2)
	go func() {
		defer wg.Done()
		io.Copy(target, client)
		if c, ok := target.(*net.TCPConn); ok {
			c.CloseWrite()
		}
	}()
	go func() { defer wg.Done(); io.Copy(client, target); client.CloseWrite() }()
	wg.Wait()
}

func (s *Server) handleTransparentHTTP(client *net.TCPConn, dst string) {
	reader := bufio.NewReader(client)
	req, err := headers.ParseRequest(reader)
	if err != nil {
		s.logger.Printf("[http] %v", err)
		return
	}
	if s.handleCaptive(client, req) {
		return
	}

	_, rewritten, err := s.engine.Rewrite(req, dst)
	if err != nil {
		s.logger.Printf("[http] rewrite: %v", err)
		return
	}

	target, err := s.dial(dst)
	if err != nil {
		s.logger.Printf("[http] %v", err)
		return
	}
	defer target.Close()

	if _, err := io.WriteString(target, rewritten); err != nil {
		s.logger.Printf("[http] 写请求失败: %v", err)
		return
	}
	if err := headers.CopyRequestBody(target, reader, req.Headers); err != nil {
		s.logger.Printf("[http] 写 Body 失败: %v", err)
		return
	}

	respReader := bufio.NewReader(target)
	if _, err := s.relayResponse(client, respReader); err != nil {
		s.logger.Printf("[http] 响应转发失败: %v", err)
	}
}

func (s *Server) relayResponse(client net.Conn, resp *bufio.Reader) (int64, error) {
	statusLine, err := resp.ReadString('\n')
	if err != nil {
		return 0, err
	}
	if _, err := io.WriteString(client, statusLine); err != nil {
		return 0, err
	}

	var respHeaders []headers.Header
	for {
		line, err := resp.ReadString('\n')
		if err != nil {
			return 0, err
		}
		line = strings.TrimRight(line, "\r\n")
		if _, err := io.WriteString(client, line+"\r\n"); err != nil {
			return 0, err
		}
		if line == "" {
			break
		}
		if idx := strings.IndexByte(line, ':'); idx > 0 {
			respHeaders = append(respHeaders, headers.Header{Key: line[:idx], Value: strings.TrimSpace(line[idx+1:])})
		}
	}
	if err := headers.CopyResponseBody(client, resp, respHeaders); err != nil {
		return 0, err
	}
	return 0, nil
}

func (s *Server) handleForwardHTTP(client *net.TCPConn) {
	reader := bufio.NewReader(client)
	req, err := headers.ParseRequest(reader)
	if err != nil {
		s.logger.Printf("[forward] %v", err)
		return
	}

	if strings.EqualFold(req.Method, "CONNECT") {
		s.relayConnect(client, reader, req.URI)
		return
	}
	if s.handleCaptive(client, req) {
		return
	}

	host, port, err := extractForwardDst(req)
	if err != nil {
		s.logger.Printf("[forward] %v", err)
		return
	}
	dst := net.JoinHostPort(host, port)

	_, rewritten, err := s.engine.Rewrite(req, dst)
	if err != nil {
		s.logger.Printf("[forward] rewrite: %v", err)
		return
	}

	target, err := s.dial(dst)
	if err != nil {
		s.logger.Printf("[forward] %v", err)
		return
	}
	defer target.Close()

	if _, err := io.WriteString(target, rewritten); err != nil {
		return
	}
	if err := headers.CopyRequestBody(target, reader, req.Headers); err != nil {
		return
	}

	resp := bufio.NewReader(target)
	if _, err := s.relayResponse(client, resp); err != nil {
		s.logger.Printf("[forward] 响应失败: %v", err)
	}
}

func extractForwardDst(req *headers.Request) (string, string, error) {
	if strings.HasPrefix(req.URI, "http://") {
		rest := req.URI[len("http://"):]
		idx := strings.IndexAny(rest, "/?")
		hostport := rest
		if idx > 0 {
			hostport = rest[:idx]
		}
		host, port, err := net.SplitHostPort(hostport)
		if err != nil {
			return hostport, "80", nil
		}
		return host, port, nil
	}
	if req.Host != "" {
		host, port, err := net.SplitHostPort(req.Host)
		if err != nil {
			return req.Host, "80", nil
		}
		return host, port, nil
	}
	return "", "", fmt.Errorf("无法确定目标主机")
}

func (s *Server) handleForwardConnect(client *net.TCPConn) {
	reader := bufio.NewReader(client)
	line, err := reader.ReadString('\n')
	if err != nil {
		return
	}
	parts := strings.Fields(line)
	if len(parts) < 2 || !strings.EqualFold(parts[0], "CONNECT") {
		io.WriteString(client, "HTTP/1.1 400 Bad Request\r\n\r\n")
		return
	}
	for {
		h, err := reader.ReadString('\n')
		if err != nil {
			return
		}
		if strings.TrimRight(h, "\r\n") == "" {
			break
		}
	}
	s.relayConnect(client, reader, parts[1])
}

func (s *Server) relayConnect(client *net.TCPConn, reader *bufio.Reader, dst string) {
	if _, _, err := net.SplitHostPort(dst); err != nil {
		dst = net.JoinHostPort(dst, "443")
	}
	host, _, _ := net.SplitHostPort(dst)
	cfg := s.loader.Get()
	if cfg.DeviceForge.Enabled && config.CaptiveHostMatch(cfg.DeviceForge.Captive, host) {
		return
	}
	target, err := s.dial(dst)
	if err != nil {
		io.WriteString(client, "HTTP/1.1 502 Bad Gateway\r\n\r\n")
		return
	}
	defer target.Close()
	io.WriteString(client, "HTTP/1.1 200 Connection Established\r\n\r\n")

	var wg sync.WaitGroup
	wg.Add(2)
	go func() { defer wg.Done(); io.Copy(target, reader) }()
	go func() { defer wg.Done(); io.Copy(client, target) }()
	wg.Wait()
}
