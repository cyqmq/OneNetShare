package deviceforge

import (
	"encoding/binary"
	"fmt"
	"net"
	"strings"

	"hotspot-proxy/internal/config"
)

const (
	dnsTypeA   = 1
	dnsTypePTR = 12
	dnsTypeTXT = 16
	dnsTypeSRV = 33
	dnsClassIN = 1
)

func BuildMDNSResponse(query []byte, cfg config.MDNSForgeConfig, localIP net.IP) []byte {
	if len(query) < 12 {
		return nil
	}
	qd := int(binary.BigEndian.Uint16(query[4:6]))
	if qd == 0 {
		return nil
	}
	questions, _, err := dnsQuestions(query, qd)
	if err != nil {
		return nil
	}

	service := normalizeMDNSName(cfg.Service)
	host := strings.TrimSuffix(cfg.Hostname, ".local")
	if host == "" {
		host = "gateway"
	}
	instance := host + "." + service
	target := host + ".local"

	resp := make([]byte, 12)
	copy(resp[0:2], query[0:2])
	binary.BigEndian.PutUint16(resp[2:4], 0x8400)
	binary.BigEndian.PutUint16(resp[4:6], uint16(qd))
	resp = append(resp, questions...)

	answers := []byte{}
	answers = append(answers, mdnsRR("_services._dns-sd._udp.local", dnsTypePTR, dnsClassIN, 4500, encodeDNSName(service))...)
	answers = append(answers, mdnsRR(service, dnsTypePTR, dnsClassIN, 4500, encodeDNSName(instance))...)
	srv := make([]byte, 6)
	binary.BigEndian.PutUint16(srv[4:6], 5353)
	srv = append(srv, encodeDNSName(target)...)
	answers = append(answers, mdnsRR(instance, dnsTypeSRV, dnsClassIN, 120, srv)...)
	answers = append(answers, mdnsRR(instance, dnsTypeTXT, dnsClassIN, 120, []byte{0})...)
	answers = append(answers, mdnsRR(target, dnsTypeA, dnsClassIN, 120, mdnsIPv4(localIP))...)
	resp = append(resp, answers...)
	binary.BigEndian.PutUint16(resp[6:8], 5)
	return resp
}

func normalizeMDNSName(name string) string {
	if name == "" {
		name = "_workstation._tcp.local"
	}
	if !strings.HasSuffix(strings.ToLower(name), ".local") {
		name += ".local"
	}
	return name
}

func mdnsIPv4(ip net.IP) []byte {
	v4 := ip.To4()
	if v4 == nil {
		return []byte{127, 0, 0, 1}
	}
	return append([]byte(nil), v4...)
}

func mdnsRR(name string, rrType uint16, class uint16, ttl uint32, rdata []byte) []byte {
	out := encodeDNSName(name)
	tail := make([]byte, 10)
	binary.BigEndian.PutUint16(tail[0:2], rrType)
	binary.BigEndian.PutUint16(tail[2:4], class)
	binary.BigEndian.PutUint32(tail[4:8], ttl)
	binary.BigEndian.PutUint16(tail[8:10], uint16(len(rdata)))
	out = append(out, tail...)
	out = append(out, rdata...)
	return out
}

func encodeDNSName(name string) []byte {
	name = strings.TrimSuffix(strings.TrimSpace(name), ".")
	if name == "" {
		return []byte{0}
	}
	out := []byte{}
	for _, label := range strings.Split(name, ".") {
		if len(label) > 63 {
			label = label[:63]
		}
		out = append(out, byte(len(label)))
		out = append(out, []byte(label)...)
	}
	return append(out, 0)
}

func decodeDNSName(msg []byte, off int) (string, int, error) {
	var labels []string
	jumps := 0
	seen := map[int]bool{}
	for {
		if off >= len(msg) {
			return "", 0, fmt.Errorf("truncated name")
		}
		if seen[off] {
			return "", 0, fmt.Errorf("name loop")
		}
		seen[off] = true
		l := int(msg[off])
		if l&0xc0 == 0xc0 {
			if off+1 >= len(msg) {
				return "", 0, fmt.Errorf("truncated pointer")
			}
			ptr := ((l & 0x3f) << 8) | int(msg[off+1])
			off += 2
			if ptr >= len(msg) {
				return "", 0, fmt.Errorf("bad pointer")
			}
			part, _, err := decodeDNSName(msg, ptr)
			if err != nil {
				return "", 0, err
			}
			labels = append(labels, part)
			break
		}
		if l&0xc0 != 0 {
			return "", 0, fmt.Errorf("invalid label")
		}
		off++
		if l == 0 {
			break
		}
		if off+l > len(msg) {
			return "", 0, fmt.Errorf("truncated label")
		}
		labels = append(labels, string(msg[off:off+l]))
		off += l
		jumps++
		if jumps > 128 {
			return "", 0, fmt.Errorf("name too long")
		}
	}
	return strings.Join(labels, "."), off, nil
}
