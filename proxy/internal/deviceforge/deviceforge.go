package deviceforge

import (
	"bytes"
	"fmt"
	"log"
	"net"
	"sync"
	"sync/atomic"
	"time"

	"hotspot-proxy/internal/config"
	"hotspot-proxy/internal/netutil"
)

type Sender interface {
	SendUDP(srcIP net.IP, srcPort uint16, dstIP net.IP, dstPort uint16, payload []byte) error
	LocalIP() net.IP
}

type Manager struct {
	loader   *config.Loader
	logger   *log.Logger
	sessions sync.Map
}

type udpSession struct {
	key      string
	srcIP    net.IP
	srcPort  uint16
	dstIP    net.IP
	dstPort  uint16
	conn     *net.UDPConn
	sender   Sender
	lastSeen int64
}

func NewManager(loader *config.Loader, logger *log.Logger) *Manager {
	m := &Manager{loader: loader, logger: logger}
	go m.cleanup()
	return m
}

func (m *Manager) StartUDP(mode string) error { return startUDPListeners(m, mode) }

func (m *Manager) Reload(c *config.Config) {
	m.logger.Printf("DeviceForge: enabled=%v target=%s udp=%v dns=%v ntp=%v mdns=%v dhcp=%v captive=%v/%s headers=%v",
		c.DeviceForge.Enabled, c.DeviceForge.TargetOS, c.DeviceForge.UDP.Enabled,
		c.DeviceForge.DNS.Enabled, c.DeviceForge.NTP.Enabled, c.DeviceForge.MDNS.Enabled,
		c.DeviceForge.DHCP.Enabled, c.DeviceForge.Captive.Enabled, c.DeviceForge.Captive.Mode,
		c.DeviceForge.Headers)
}

func (m *Manager) HandleUDP(srcIP net.IP, srcPort uint16, dstIP net.IP, dstPort uint16, payload []byte, sender Sender) {
	cfg := m.loader.Get().DeviceForge
	if !cfg.Enabled || !cfg.UDP.Enabled {
		return
	}

	if cfg.MDNS.Enabled && dstPort == 5353 {
		resp := BuildMDNSResponse(payload, cfg.MDNS, sender.LocalIP())
		if len(resp) > 0 {
			if err := sender.SendUDP(dstIP, dstPort, srcIP, srcPort, resp); err != nil {
				m.logger.Printf("[deviceforge] mdns reply: %v", err)
			}
		}
		return
	}

	if cfg.DHCP.Enabled && (dstPort == 67 || dstPort == 68) {
		out := NormalizeDHCP(payload, cfg.DHCP, cfg.TargetOS, srcPort == 68)
		if err := sender.SendUDP(srcIP, srcPort, dstIP, dstPort, out); err != nil {
			m.logger.Printf("[deviceforge] dhcp rewrite: %v", err)
		}
		return
	}

	if cfg.Captive.Enabled && dstPort == 53 {
		resp, handled := CaptiveDNSReply(payload, cfg.Captive, dstIP)
		if handled {
			label := CaptiveProbeLabel(CaptiveDNSName(payload))
			if cfg.Captive.Mode == "drop" {
				m.logger.Printf("[probe] 拦截 %s -> 丢弃", label)
			} else if len(resp) > 0 {
				m.logger.Printf("[probe] 拦截 %s -> 伪造返回成功", label)
			}
			if len(resp) > 0 {
				if err := sender.SendUDP(dstIP, dstPort, srcIP, srcPort, resp); err != nil {
					m.logger.Printf("[deviceforge] captive dns: %v", err)
				}
			}
			return
		}
	}

	if !m.allowed(cfg, dstPort) {
		m.logger.Printf("[probe] 阻止未知 UDP 转发 %s:%d -> %s:%d", srcIP, srcPort, dstIP, dstPort)
		return
	}

	if cfg.DNS.Enabled && dstPort == 53 {
		name := DNSQueryName(payload)
		q, forward := NormalizeDNSQuery(payload, cfg.DNS)
		if !forward {
			m.logger.Printf("[probe] DNS 查询 %s -> 强制 TCP 重试", name)
			if err := sender.SendUDP(dstIP, dstPort, srcIP, srcPort, q); err != nil {
				m.logger.Printf("[deviceforge] dns tcpretry: %v", err)
			}
			return
		}
		if !bytes.Equal(q, payload) {
			m.logger.Printf("[probe] DNS 查询 %s -> 去 EDNS/清 AD/CD", name)
		}
		payload = q
	}

	if cfg.NTP.Enabled && dstPort == 123 {
		payload = NormalizeNTPRequest(payload, cfg.NTP)
	}

	targetIP, targetPort := dstIP, dstPort
	if cfg.NTP.Enabled && dstPort == 123 && cfg.NTP.Server != "" {
		if addr, err := net.ResolveUDPAddr("udp4", cfg.NTP.Server); err == nil {
			if addr.Port == 0 {
				addr.Port = 123
			}
			if v4 := addr.IP.To4(); v4 != nil {
				targetIP, targetPort = v4, uint16(addr.Port)
			}
		}
	}

	key := fmt.Sprintf("%s:%d->%s:%d@%s:%d", srcIP, srcPort, dstIP, dstPort, targetIP, targetPort)
	session, err := m.getOrCreateSession(key, srcIP, srcPort, dstIP, dstPort, targetIP, targetPort, sender)
	if err != nil {
		m.logger.Printf("[deviceforge] session: %v", err)
		return
	}
	if _, err := session.conn.Write(payload); err != nil {
		m.logger.Printf("[deviceforge] udp write: %v", err)
		m.dropSession(session)
	}
}

func (m *Manager) allowed(cfg config.DeviceForgeConfig, port uint16) bool {
	if cfg.UDP.AllPorts {
		return true
	}
	for _, p := range cfg.UDP.TargetPorts {
		if uint16(p) == port {
			return true
		}
	}
	if cfg.DNS.Enabled && port == 53 {
		return true
	}
	if cfg.NTP.Enabled && port == 123 {
		return true
	}
	if cfg.MDNS.Enabled && port == 5353 {
		return true
	}
	if cfg.DHCP.Enabled && (port == 67 || port == 68) {
		return true
	}
	return false
}

func (m *Manager) getOrCreateSession(key string, srcIP net.IP, srcPort uint16, dstIP net.IP, dstPort uint16, targetIP net.IP, targetPort uint16, sender Sender) (*udpSession, error) {
	if v, ok := m.sessions.Load(key); ok {
		s := v.(*udpSession)
		atomic.StoreInt64(&s.lastSeen, time.Now().UnixNano())
		return s, nil
	}

	conn, err := netutil.DialUDP(&net.UDPAddr{IP: targetIP.To4(), Port: int(targetPort)}, nil)
	if err != nil {
		return nil, err
	}
	s := &udpSession{
		key: key, srcIP: append(net.IP(nil), srcIP...), srcPort: srcPort,
		dstIP: append(net.IP(nil), dstIP...), dstPort: dstPort,
		conn: conn, sender: sender, lastSeen: time.Now().UnixNano(),
	}
	m.sessions.Store(key, s)
	go m.pumpSession(s)
	return s, nil
}

func (m *Manager) pumpSession(s *udpSession) {
	buf := make([]byte, 65535)
	for {
		n, err := s.conn.Read(buf)
		if n > 0 {
			atomic.StoreInt64(&s.lastSeen, time.Now().UnixNano())
			resp := append([]byte(nil), buf[:n]...)
			cfg := m.loader.Get().DeviceForge
			if cfg.DNS.Enabled && s.dstPort == 53 {
				resp = NormalizeDNSResponse(resp, cfg.DNS)
			}
			if cfg.NTP.Enabled && s.dstPort == 123 {
				resp = NormalizeNTPResponse(resp, cfg.NTP)
			}
			if err := s.sender.SendUDP(s.dstIP, s.dstPort, s.srcIP, s.srcPort, resp); err != nil {
				m.logger.Printf("[deviceforge] udp reply: %v", err)
			}
		}
		if err != nil {
			break
		}
	}
	m.dropSession(s)
}

func (m *Manager) dropSession(s *udpSession) {
	if _, ok := m.sessions.Load(s.key); ok {
		m.sessions.Delete(s.key)
	}
	if s.conn != nil {
		s.conn.Close()
	}
}

func (m *Manager) cleanup() {
	ticker := time.NewTicker(30 * time.Second)
	defer ticker.Stop()
	for range ticker.C {
		now := time.Now().UnixNano()
		m.sessions.Range(func(k, v interface{}) bool {
			s := v.(*udpSession)
			if now-atomic.LoadInt64(&s.lastSeen) > int64(5*time.Minute) {
				m.dropSession(s)
			}
			return true
		})
	}
}
