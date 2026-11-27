package outboundguard

import (
	"encoding/binary"
	"log"
	"strings"
	"sync"
	"sync/atomic"

	"hotspot-proxy/internal/config"
)

type Stats struct {
	Enabled        bool
	UDPPorts       []int
	TCPPorts       []int
	BlockBroadcast bool
	BlockMulticast bool
	Dropped        uint64
	UDP            uint64
	TCP            uint64
	Broadcast      uint64
	Multicast      uint64
}

type Guard struct {
	mu      sync.RWMutex
	cfg     config.OutboundGuardConfig
	logger  *log.Logger
	dropped atomic.Uint64
	udp     atomic.Uint64
	tcp     atomic.Uint64
	bcast   atomic.Uint64
	mcast   atomic.Uint64
	logged  atomic.Uint64
}

func New(cfg config.OutboundGuardConfig, logger *log.Logger) *Guard {
	return &Guard{cfg: cfg, logger: logger}
}

func (g *Guard) Reload(cfg config.OutboundGuardConfig) {
	g.mu.Lock()
	g.cfg = cfg
	g.mu.Unlock()
}

func (g *Guard) Enabled() bool {
	g.mu.RLock()
	defer g.mu.RUnlock()
	return g.cfg.IsEnabled()
}

func (g *Guard) DropIPv4(pkt []byte) bool {
	g.mu.RLock()
	cfg := g.cfg
	g.mu.RUnlock()
	if !cfg.IsEnabled() || len(pkt) < 20 || pkt[0]>>4 != 4 {
		return false
	}
	ihl := int(pkt[0]&0x0f) * 4
	if ihl < 20 || len(pkt) < ihl+8 {
		return false
	}
	dst := pkt[16:20]
	reason := ""
	port := 0

	if cfg.BlockBroadcast && dst[0] == 255 && dst[1] == 255 && dst[2] == 255 && dst[3] == 255 {
		reason = "broadcast"
	} else if cfg.BlockMulticast && dst[0] >= 224 && dst[0] <= 239 {
		reason = "multicast"
	} else {
		payload := pkt[ihl:]
		switch pkt[9] {
		case 17:
			if len(payload) >= 4 {
				port = int(binary.BigEndian.Uint16(payload[2:4]))
				if hasPort(cfg.UDPPorts, port) {
					reason = "udp"
				}
			}
		case 6:
			if len(payload) >= 4 {
				port = int(binary.BigEndian.Uint16(payload[2:4]))
				if hasPort(cfg.TCPPorts, port) {
					reason = "tcp"
				}
			}
		}
	}
	if reason == "" {
		return false
	}

	g.dropped.Add(1)
	switch reason {
	case "udp":
		g.udp.Add(1)
	case "tcp":
		g.tcp.Add(1)
	case "broadcast":
		g.bcast.Add(1)
	case "multicast":
		g.mcast.Add(1)
	}
	count := g.dropped.Load()
	if g.logger != nil && (count == 1 || count%100 == 0) {
		g.logger.Printf("[probe][出站封堵] 丢弃 dst=%d.%d.%d.%d reason=%s port=%d total=%d",
			dst[0], dst[1], dst[2], dst[3], reason, port, count)
	}
	return true
}

func (g *Guard) Stats() Stats {
	g.mu.RLock()
	cfg := g.cfg
	g.mu.RUnlock()
	return Stats{
		Enabled:        cfg.IsEnabled(),
		UDPPorts:       append([]int(nil), cfg.UDPPorts...),
		TCPPorts:       append([]int(nil), cfg.TCPPorts...),
		BlockBroadcast: cfg.BlockBroadcast,
		BlockMulticast: cfg.BlockMulticast,
		Dropped:        g.dropped.Load(),
		UDP:            g.udp.Load(),
		TCP:            g.tcp.Load(),
		Broadcast:      g.bcast.Load(),
		Multicast:      g.mcast.Load(),
	}
}

func (s Stats) Format() string {
	return "enabled=" + boolText(s.Enabled) +
		" udp=[" + joinPorts(s.UDPPorts) + "]" +
		" tcp=[" + joinPorts(s.TCPPorts) + "]" +
		" broadcast=" + boolText(s.BlockBroadcast) +
		" multicast=" + boolText(s.BlockMulticast) +
		" dropped=" + uintText(s.Dropped) +
		" udp_dropped=" + uintText(s.UDP) +
		" tcp_dropped=" + uintText(s.TCP) +
		" broadcast_dropped=" + uintText(s.Broadcast) +
		" multicast_dropped=" + uintText(s.Multicast)
}

func hasPort(ports []int, port int) bool {
	for _, value := range ports {
		if value == port {
			return true
		}
	}
	return false
}

func joinPorts(ports []int) string {
	var parts []string
	for _, port := range ports {
		parts = append(parts, uintText(uint64(port)))
	}
	return strings.Join(parts, ",")
}

func boolText(value bool) string {
	if value {
		return "true"
	}
	return "false"
}

func uintText(value uint64) string {
	if value == 0 {
		return "0"
	}
	var digits []byte
	for value > 0 {
		digits = append(digits, byte('0'+value%10))
		value /= 10
	}
	for i, j := 0, len(digits)-1; i < j; i, j = i+1, j-1 {
		digits[i], digits[j] = digits[j], digits[i]
	}
	return string(digits)
}
