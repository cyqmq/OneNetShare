package deviceforge

import (
	"bytes"
	"encoding/binary"
	"io"
	"log"
	"net"
	"sync/atomic"
	"testing"

	"hotspot-proxy/internal/config"
)

func dnsHeader(flags uint16, qd, an, ns, ar uint16) []byte {
	h := make([]byte, 12)
	binary.BigEndian.PutUint16(h[2:4], flags)
	binary.BigEndian.PutUint16(h[4:6], qd)
	binary.BigEndian.PutUint16(h[6:8], an)
	binary.BigEndian.PutUint16(h[8:10], ns)
	binary.BigEndian.PutUint16(h[10:12], ar)
	return h
}

func TestDNSQueryStripEDNS(t *testing.T) {
	q := dnsHeader(0x0100, 1, 0, 0, 1)
	q = append(q, encodeDNSName("example.com")...)
	q = append(q, 0, 1, 0, 1)
	q = append(q, 0, 41, 0x10, 0, 0, 0, 0, 0, 0)

	out, forward := NormalizeDNSQuery(q, config.DNSForgeConfig{StripEDNS: true})
	if !forward {
		t.Fatal("expected query to be forwarded")
	}
	if got := binary.BigEndian.Uint16(out[10:12]); got != 0 {
		t.Fatalf("arcount=%d", got)
	}
	if !bytes.Contains(out, encodeDNSName("example.com")) {
		t.Fatal("question missing")
	}
}

func TestDNSQueryForceTCP(t *testing.T) {
	q := dnsHeader(0x0100, 1, 0, 0, 0)
	q = append(q, encodeDNSName("example.com")...)
	q = append(q, 0, 1, 0, 1)
	out, forward := NormalizeDNSQuery(q, config.DNSForgeConfig{ForceTCP: true})
	if forward {
		t.Fatal("expected forced TCP retry")
	}
	flags := binary.BigEndian.Uint16(out[2:4])
	if flags&dnsFlagQR == 0 || flags&dnsFlagTC == 0 {
		t.Fatalf("flags=%04x", flags)
	}
}

func TestDNSResponseTTLClamp(t *testing.T) {
	resp := dnsHeader(0x8180, 0, 1, 0, 0)
	resp = append(resp, 0, 0, 1, 0, 1, 0, 0, 0, 1, 0, 4, 1, 2, 3, 4)
	out := NormalizeDNSResponse(resp, config.DNSForgeConfig{MinTTL: 60, MaxTTL: 100})
	if got := binary.BigEndian.Uint32(out[17:21]); got != 60 {
		t.Fatalf("ttl=%d", got)
	}
}

func TestDNSCacheKeyIgnoresID(t *testing.T) {
	q := dnsHeader(0x0100, 1, 0, 0, 0)
	q = append(q, encodeDNSName("example.com")...)
	q = append(q, 0, 1, 0, 1)
	other := append([]byte(nil), q...)
	other[0], other[1] = 0x12, 0x34
	if dnsCacheKey(q) != dnsCacheKey(other) {
		t.Fatal("cache key should ignore DNS transaction ID")
	}
}

func TestAgeDNSResponse(t *testing.T) {
	resp := dnsHeader(0x8180, 1, 1, 0, 0)
	resp = append(resp, encodeDNSName("example.com")...)
	resp = append(resp, 0, 1, 0, 1)
	resp = append(resp, 0xc0, 0x0c, 0, 1, 0, 1, 0, 0, 0, 120, 0, 4, 1, 2, 3, 4)
	out := ageDNSResponse(resp, 30)
	if got := binary.BigEndian.Uint32(out[len(out)-10 : len(out)-6]); got != 90 {
		t.Fatalf("ttl=%d", got)
	}
	if got := binary.BigEndian.Uint32(ageDNSResponse(resp, 240)[len(resp)-10 : len(resp)-6]); got != 1 {
		t.Fatalf("expired ttl=%d", got)
	}
}

func TestEmbeddedDNSCache(t *testing.T) {
	query := dnsHeader(0x0100, 1, 0, 0, 0)
	query = append(query, encodeDNSName("example.com")...)
	query = append(query, 0, 1, 0, 1)
	binary.BigEndian.PutUint16(query[0:2], 0x0100)

	server, err := net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1), Port: 0})
	if err != nil {
		t.Fatal(err)
	}
	defer server.Close()
	var upstreamQueries int32
	go func() {
		buffer := make([]byte, 65535)
		for {
			n, addr, err := server.ReadFromUDP(buffer)
			if err != nil {
				return
			}
			atomic.AddInt32(&upstreamQueries, 1)
			request := append([]byte(nil), buffer[:n]...)
			questions, _, err := dnsQuestions(request, 1)
			if err != nil {
				continue
			}
			resp := dnsHeader(0x8180, 1, 1, 0, 0)
			copy(resp[0:2], request[0:2])
			resp = append(resp, questions...)
			resp = append(resp, 0xc0, 0x0c, 0, 1, 0, 1, 0, 0, 0, 120, 0, 4, 1, 2, 3, 4)
			server.WriteToUDP(resp, addr)
		}
	}()

	cfg := config.DNSForgeConfig{
		Enabled:         true,
		MinTTL:          60,
		MaxTTL:          86400,
		UpstreamServers: []string{server.LocalAddr().String()},
	}
	resolver := newDNSResolver(log.New(io.Discard, "", 0), 16)
	first, hit, err := resolver.resolve(query, cfg)
	if err != nil || hit {
		t.Fatalf("first err=%v hit=%v", err, hit)
	}
	if binary.BigEndian.Uint16(first[0:2]) != 0x0100 {
		t.Fatalf("response id mismatch id=%04x", binary.BigEndian.Uint16(first[0:2]))
	}
	second := append([]byte(nil), query...)
	second[0], second[1] = 0x12, 0x34
	cached, hit, err := resolver.resolve(second, cfg)
	if err != nil || !hit {
		t.Fatalf("second err=%v hit=%v", err, hit)
	}
	if binary.BigEndian.Uint16(cached[0:2]) != 0x1234 {
		t.Fatal("cached response id not adapted")
	}
	if got := atomic.LoadInt32(&upstreamQueries); got != 1 {
		t.Fatalf("upstream queries=%d", got)
	}
}

func TestNTPRequestNormalization(t *testing.T) {
	out := NormalizeNTPRequest(make([]byte, 48), config.NTPForgeConfig{})
	if out[0] != 0x24 {
		t.Fatalf("li/vn/mode=%02x", out[0])
	}
	if out[2] != 6 {
		t.Fatalf("poll=%d", out[2])
	}
}

func TestDHCPRequestNormalization(t *testing.T) {
	msg := make([]byte, 240)
	copy(msg[236:240], []byte{99, 130, 83, 99})
	msg = append(msg, 55, 1, 1, 60, 3, 'o', 'l', 'd', 255)
	out := NormalizeDHCP(msg, config.DHCPForgeConfig{VendorClass: "MSFT 5.0"}, "windows-11", true)
	if !bytes.Contains(out, []byte("MSFT 5.0")) {
		t.Fatal("vendor class missing")
	}
}

func TestCaptiveDNSReply(t *testing.T) {
	q := dnsHeader(0x0100, 1, 0, 0, 0)
	q = append(q, encodeDNSName("captive.apple.com")...)
	q = append(q, 0, 1, 0, 1)
	cfg := config.CaptiveConfig{
		Enabled: true, Mode: "reply", TTL: 60,
		DNSDomains: []string{"captive.apple.com"},
	}
	out, handled := CaptiveDNSReply(q, cfg, net.IPv4(192, 168, 1, 1))
	if !handled {
		t.Fatal("expected captive handling")
	}
	if got := binary.BigEndian.Uint16(out[6:8]); got != 1 {
		t.Fatalf("answers=%d", got)
	}
	if !bytes.Contains(out, []byte{192, 168, 1, 1}) {
		t.Fatal("reply ip missing")
	}
}

func TestCaptiveDNSDrop(t *testing.T) {
	q := dnsHeader(0x0100, 1, 0, 0, 0)
	q = append(q, encodeDNSName("www.msftconnecttest.com")...)
	q = append(q, 0, 1, 0, 1)
	cfg := config.CaptiveConfig{Enabled: true, Mode: "drop", DNSDomains: []string{"www.msftconnecttest.com"}}
	out, handled := CaptiveDNSReply(q, cfg, net.IPv4(192, 168, 1, 1))
	if !handled || len(out) != 0 {
		t.Fatalf("handled=%v len=%d", handled, len(out))
	}
}

func TestCaptiveHTTPMatch(t *testing.T) {
	cfg := config.CaptiveConfig{
		Enabled: true, Mode: "reply",
		Domains: []string{"captive.apple.com"},
		Paths:   []string{"/hotspot-detect.html"},
	}
	if !config.CaptiveHTTPMatch(cfg, "captive.apple.com", "/hotspot-detect.html?x=1") {
		t.Fatal("expected captive http match")
	}
	if config.CaptiveHTTPMatch(cfg, "example.com", "/hotspot-detect.html") {
		t.Fatal("unexpected captive http match")
	}
}

func TestMDNSResponse(t *testing.T) {
	q := dnsHeader(0x0000, 1, 0, 0, 0)
	q = append(q, encodeDNSName("_workstation._tcp.local")...)
	q = append(q, 0, 12, 0, 1)
	out := BuildMDNSResponse(q, config.MDNSForgeConfig{Hostname: "gateway", Service: "_workstation._tcp.local"}, net.IPv4(192, 168, 1, 1))
	if len(out) == 0 {
		t.Fatal("empty response")
	}
	if got := binary.BigEndian.Uint16(out[6:8]); got != 5 {
		t.Fatalf("answers=%d", got)
	}
	if !bytes.Contains(out, encodeDNSName("gateway.local")) {
		t.Fatal("A record name missing")
	}
}
