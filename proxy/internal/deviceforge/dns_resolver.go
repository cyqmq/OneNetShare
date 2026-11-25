package deviceforge

import (
	"crypto/sha256"
	"encoding/binary"
	"encoding/hex"
	"fmt"
	"log"
	"net"
	"strconv"
	"sync"
	"sync/atomic"
	"time"

	"hotspot-proxy/internal/config"
	"hotspot-proxy/internal/netutil"
)

type DNSStats struct {
	Queries     uint64
	Hits        uint64
	Misses      uint64
	UpstreamOK  uint64
	UpstreamErr uint64
}

type dnsCacheEntry struct {
	response []byte
	stored   time.Time
	expires  time.Time
}

type dnsPending struct {
	done chan struct{}
}

type dnsResolver struct {
	logger  *log.Logger
	mu      sync.Mutex
	cache   map[string]dnsCacheEntry
	pending map[string]*dnsPending
	limit   int
	next    uint32
	stats   DNSStats
}

func newDNSResolver(logger *log.Logger, limit int) *dnsResolver {
	if limit <= 0 {
		limit = 4096
	}
	return &dnsResolver{
		logger:  logger,
		cache:   make(map[string]dnsCacheEntry, limit),
		pending: make(map[string]*dnsPending),
		limit:   limit,
	}
}

func (r *dnsResolver) setLimit(limit int) {
	if limit <= 0 {
		limit = 4096
	}
	r.mu.Lock()
	r.limit = limit
	for len(r.cache) > r.limit {
		for key := range r.cache {
			delete(r.cache, key)
			break
		}
	}
	r.mu.Unlock()
}

func (r *dnsResolver) Stats() DNSStats {
	r.mu.Lock()
	defer r.mu.Unlock()
	return r.stats
}

func (r *dnsResolver) resolve(query []byte, cfg config.DNSForgeConfig) ([]byte, bool, error) {
	key := dnsCacheKey(query)
	now := time.Now()

	r.mu.Lock()
	r.stats.Queries++
	if response, ok := r.cachedLocked(key, query, now); ok {
		r.stats.Hits++
		r.mu.Unlock()
		return response, true, nil
	}
	if pending, ok := r.pending[key]; ok {
		r.mu.Unlock()
		select {
		case <-pending.done:
			r.mu.Lock()
			response, hit := r.cachedLocked(key, query, time.Now())
			r.mu.Unlock()
			if hit {
				return response, true, nil
			}
			return nil, false, fmt.Errorf("DNS 上游查询未完成")
		case <-time.After(5 * time.Second):
			return nil, false, fmt.Errorf("DNS 上游查询超时")
		}
	}
	pending := &dnsPending{done: make(chan struct{})}
	r.pending[key] = pending
	r.stats.Misses++
	r.mu.Unlock()

	transport := "udp"
	if cfg.ForceTCP {
		transport = "tcp"
	}
	name := DNSQueryName(query)
	response, server, err := queryDNSUpstream(query, cfg)
	if r.logger != nil {
		result := "ok"
		if err != nil {
			result = "err=" + err.Error()
		}
		r.logger.Printf("[probe][DNS监控] 上游查询 name=%s server=%s transport=%s result=%s", name, server, transport, result)
	}
	if err == nil {
		response = NormalizeDNSResponse(response, cfg)
		if len(response) < 12 {
			err = fmt.Errorf("无效 DNS 响应")
		}
	}

	r.mu.Lock()
	if err != nil {
		r.stats.UpstreamErr++
	} else {
		r.stats.UpstreamOK++
		ttl := dnsResponseTTL(response, cfg)
		r.cache[key] = dnsCacheEntry{
			response: append([]byte(nil), response...),
			stored:   now,
			expires:  now.Add(time.Duration(ttl) * time.Second),
		}
		if len(r.cache) > r.limit {
			for oldKey := range r.cache {
				delete(r.cache, oldKey)
				break
			}
		}
	}
	delete(r.pending, key)
	close(pending.done)
	r.mu.Unlock()

	if err != nil {
		return nil, false, err
	}
	return responseForQuery(response, query), false, nil
}

func (r *dnsResolver) cachedLocked(key string, query []byte, now time.Time) ([]byte, bool) {
	entry, ok := r.cache[key]
	if !ok {
		return nil, false
	}
	if !now.Before(entry.expires) {
		delete(r.cache, key)
		return nil, false
	}
	age := uint32(now.Sub(entry.stored) / time.Second)
	return ageDNSResponse(responseForQuery(entry.response, query), age), true
}

func queryDNSUpstream(query []byte, cfg config.DNSForgeConfig) ([]byte, string, error) {
	servers := cfg.UpstreamServers
	if len(servers) == 0 {
		servers = []string{"1.1.1.1"}
	}
	index := int(atomic.AddUint32(&dnsServerCursor, 1)-1) % len(servers)
	server := servers[index]
	host, port, err := net.SplitHostPort(server)
	if err != nil {
		host, port = server, "53"
	}
	ip := net.ParseIP(host)
	if ip == nil {
		return nil, server, fmt.Errorf("无效上游 DNS %q", server)
	}
	if v4 := ip.To4(); v4 != nil {
		ip = v4
	}
	address := net.JoinHostPort(ip.String(), port)

	if cfg.ForceTCP {
		conn, err := netutil.DialTimeout("tcp", address, 4*time.Second)
		if err != nil {
			return nil, server, err
		}
		defer conn.Close()
		conn.SetDeadline(time.Now().Add(5 * time.Second))
		prefix := []byte{byte(len(query) >> 8), byte(len(query))}
		if _, err := conn.Write(append(prefix, query...)); err != nil {
			return nil, server, err
		}
		var size [2]byte
		if _, err := readFull(conn, size[:]); err != nil {
			return nil, server, err
		}
		length := int(binary.BigEndian.Uint16(size[:]))
		response := make([]byte, length)
		if _, err := readFull(conn, response); err != nil {
			return nil, server, err
		}
		return response, server, nil
	}

	portNumber := 53
	if parsed, convErr := strconv.Atoi(port); convErr == nil {
		portNumber = parsed
	}
	conn, err := netutil.DialUDP(&net.UDPAddr{IP: ip, Port: portNumber}, nil)
	if err != nil {
		return nil, server, err
	}
	defer conn.Close()
	conn.SetDeadline(time.Now().Add(4 * time.Second))
	if _, err := conn.Write(query); err != nil {
		return nil, server, err
	}
	buffer := make([]byte, 65535)
	n, err := conn.Read(buffer)
	if err != nil {
		return nil, server, err
	}
	if n == 0 {
		return nil, server, fmt.Errorf("上游 DNS 返回空响应")
	}
	return append([]byte(nil), buffer[:n]...), server, nil
}

func readFull(conn net.Conn, buffer []byte) (int, error) {
	total := 0
	for total < len(buffer) {
		n, err := conn.Read(buffer[total:])
		total += n
		if err != nil {
			return total, err
		}
	}
	return total, nil
}

var dnsServerCursor uint32

func dnsCacheKey(query []byte) string {
	key := append([]byte(nil), query...)
	if len(key) >= 2 {
		key[0], key[1] = 0, 0
	}
	sum := sha256.Sum256(key)
	return hex.EncodeToString(sum[:])
}

func responseForQuery(response, query []byte) []byte {
	if len(response) < 12 || len(query) < 12 {
		return append([]byte(nil), response...)
	}
	queryCount := int(binary.BigEndian.Uint16(query[4:6]))
	questions, _, err := dnsQuestions(query, queryCount)
	if err != nil {
		out := append([]byte(nil), response...)
		copy(out[0:2], query[0:2])
		return out
	}
	cachedCount := int(binary.BigEndian.Uint16(response[4:6]))
	_, off, err := dnsQuestions(response, cachedCount)
	if err != nil {
		out := append([]byte(nil), response...)
		copy(out[0:2], query[0:2])
		return out
	}
	out := append([]byte(nil), response[:12]...)
	out = append(out, questions...)
	out = append(out, response[off:]...)
	copy(out[0:2], query[0:2])
	return out
}

func ageDNSResponse(response []byte, age uint32) []byte {
	if age == 0 || len(response) < 12 {
		return append([]byte(nil), response...)
	}
	out := append([]byte(nil), response...)
	qd := int(binary.BigEndian.Uint16(out[4:6]))
	an := int(binary.BigEndian.Uint16(out[6:8]))
	ns := int(binary.BigEndian.Uint16(out[8:10]))
	ar := int(binary.BigEndian.Uint16(out[10:12]))
	_, off, err := dnsQuestions(out, qd)
	if err != nil {
		return out
	}
	off = ageDNSSection(out, off, an, age)
	off = ageDNSSection(out, off, ns, age)
	ageDNSSection(out, off, ar, age)
	return out
}

func ageDNSSection(msg []byte, off int, count int, age uint32) int {
	for i := 0; i < count; i++ {
		start := off
		var err error
		off, err = skipDNSName(msg, off)
		if err != nil {
			return start
		}
		if off+10 > len(msg) {
			return start
		}
		rrType := binary.BigEndian.Uint16(msg[off : off+2])
		ttlPos := off + 4
		rdLen := int(binary.BigEndian.Uint16(msg[off+8 : off+10]))
		off += 10
		end := off + rdLen
		if end > len(msg) {
			return start
		}
		if rrType != dnsTypeOPT {
			ttl := binary.BigEndian.Uint32(msg[ttlPos : ttlPos+4])
			if ttl > age {
				ttl -= age
			} else {
				ttl = 1
			}
			binary.BigEndian.PutUint32(msg[ttlPos:ttlPos+4], ttl)
		}
		off = end
	}
	return off
}

func dnsResponseTTL(response []byte, cfg config.DNSForgeConfig) uint32 {
	min := cfg.MinTTL
	if min == 0 {
		min = 60
	}
	if len(response) < 12 {
		return min
	}
	qd := int(binary.BigEndian.Uint16(response[4:6]))
	an := int(binary.BigEndian.Uint16(response[6:8]))
	ns := int(binary.BigEndian.Uint16(response[8:10]))
	ar := int(binary.BigEndian.Uint16(response[10:12]))
	_, off, err := dnsQuestions(response, qd)
	if err != nil {
		return min
	}
	ttl := uint32(0)
	off = scanDNSSectionTTL(response, off, an, &ttl)
	off = scanDNSSectionTTL(response, off, ns, &ttl)
	scanDNSSectionTTL(response, off, ar, &ttl)
	if ttl == 0 {
		return min
	}
	return clampDNSTTL(ttl, cfg)
}

func scanDNSSectionTTL(msg []byte, off int, count int, min *uint32) int {
	for i := 0; i < count; i++ {
		start := off
		var err error
		off, err = skipDNSName(msg, off)
		if err != nil {
			return start
		}
		if off+10 > len(msg) {
			return start
		}
		rrType := binary.BigEndian.Uint16(msg[off : off+2])
		ttl := binary.BigEndian.Uint32(msg[off+4 : off+8])
		rdLen := int(binary.BigEndian.Uint16(msg[off+8 : off+10]))
		off += 10
		end := off + rdLen
		if end > len(msg) {
			return start
		}
		if rrType != dnsTypeOPT && ttl > 0 && (*min == 0 || ttl < *min) {
			*min = ttl
		}
		off = end
	}
	return off
}
