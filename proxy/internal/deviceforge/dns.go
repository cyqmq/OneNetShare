package deviceforge

import (
	"encoding/binary"
	"fmt"

	"hotspot-proxy/internal/config"
)

const (
	dnsFlagQR  = 0x8000
	dnsFlagTC  = 0x0200
	dnsFlagRD  = 0x0100
	dnsFlagAD  = 0x0020
	dnsFlagCD  = 0x0010
	dnsTypeOPT = 41
)

func NormalizeDNSQuery(msg []byte, cfg config.DNSForgeConfig) ([]byte, bool) {
	if len(msg) < 12 {
		return append([]byte(nil), msg...), true
	}
	flags := binary.BigEndian.Uint16(msg[2:4])
	if flags&dnsFlagQR != 0 {
		return append([]byte(nil), msg...), true
	}
	flags &^= dnsFlagAD | dnsFlagCD

	qd := int(binary.BigEndian.Uint16(msg[4:6]))
	questions, _, err := dnsQuestions(msg, qd)
	if err != nil {
		return append([]byte(nil), msg...), true
	}

	if cfg.ForceTCP {
		out := make([]byte, 12+len(questions))
		binary.BigEndian.PutUint16(out[0:2], binary.BigEndian.Uint16(msg[0:2]))
		replyFlags := uint16(dnsFlagQR | dnsFlagTC)
		if flags&dnsFlagRD != 0 {
			replyFlags |= dnsFlagRD
		}
		binary.BigEndian.PutUint16(out[2:4], replyFlags)
		binary.BigEndian.PutUint16(out[4:6], uint16(qd))
		copy(out[12:], questions)
		return out, false
	}

	out := append([]byte(nil), msg...)
	binary.BigEndian.PutUint16(out[2:4], flags)
	if cfg.StripEDNS {
		out = append([]byte(nil), msg[:12]...)
		out = append(out, questions...)
		binary.BigEndian.PutUint16(out[2:4], flags)
		binary.BigEndian.PutUint16(out[8:10], 0)
		binary.BigEndian.PutUint16(out[10:12], 0)
	}
	return out, true
}

func NormalizeDNSResponse(msg []byte, cfg config.DNSForgeConfig) []byte {
	if len(msg) < 12 {
		return append([]byte(nil), msg...)
	}
	flags := binary.BigEndian.Uint16(msg[2:4])
	if flags&dnsFlagQR == 0 {
		return append([]byte(nil), msg...)
	}
	out := make([]byte, 12)
	copy(out, msg[:12])
	binary.BigEndian.PutUint16(out[2:4], flags&^(dnsFlagAD|dnsFlagCD))

	qd := int(binary.BigEndian.Uint16(msg[4:6]))
	questions, off, err := dnsQuestions(msg, qd)
	if err != nil {
		return append([]byte(nil), msg...)
	}
	out = append(out, questions...)

	an, anCount := int(binary.BigEndian.Uint16(msg[6:8])), 0
	ns, nsCount := int(binary.BigEndian.Uint16(msg[8:10])), 0
	ar, arCount := int(binary.BigEndian.Uint16(msg[10:12])), 0

	answerBytes, n, err := rewriteDNSSection(msg, off, an, true, false, cfg)
	if err != nil {
		return append([]byte(nil), msg...)
	}
	anCount = n
	off += consumedDNSSection(msg, off, an)
	out = append(out, answerBytes...)

	authorityBytes, n, err := rewriteDNSSection(msg, off, ns, true, false, cfg)
	if err != nil {
		return append([]byte(nil), msg...)
	}
	nsCount = n
	off += consumedDNSSection(msg, off, ns)
	out = append(out, authorityBytes...)

	additionalBytes, n, err := rewriteDNSSection(msg, off, ar, false, cfg.StripEDNS, cfg)
	if err != nil {
		return append([]byte(nil), msg...)
	}
	arCount = n
	out = append(out, additionalBytes...)

	binary.BigEndian.PutUint16(out[4:6], uint16(qd))
	binary.BigEndian.PutUint16(out[6:8], uint16(anCount))
	binary.BigEndian.PutUint16(out[8:10], uint16(nsCount))
	binary.BigEndian.PutUint16(out[10:12], uint16(arCount))
	return out
}

func dnsQuestions(msg []byte, count int) ([]byte, int, error) {
	start := 12
	off := start
	for i := 0; i < count; i++ {
		var err error
		off, err = skipDNSName(msg, off)
		if err != nil {
			return nil, 0, err
		}
		if off+4 > len(msg) {
			return nil, 0, fmt.Errorf("truncated question")
		}
		off += 4
	}
	return append([]byte(nil), msg[start:off]...), off, nil
}

func rewriteDNSSection(msg []byte, off int, count int, mutateTTL bool, dropOPT bool, cfg config.DNSForgeConfig) ([]byte, int, error) {
	var out []byte
	written := 0
	for i := 0; i < count; i++ {
		start := off
		var err error
		off, err = skipDNSName(msg, off)
		if err != nil {
			return nil, 0, err
		}
		if off+10 > len(msg) {
			return nil, 0, fmt.Errorf("truncated rr")
		}
		rrType := binary.BigEndian.Uint16(msg[off : off+2])
		ttlPos := off + 4
		rdLen := int(binary.BigEndian.Uint16(msg[off+8 : off+10]))
		off += 10
		end := off + rdLen
		if end > len(msg) {
			return nil, 0, fmt.Errorf("truncated rdata")
		}
		if dropOPT && rrType == dnsTypeOPT {
			off = end
			continue
		}

		rr := append([]byte(nil), msg[start:end]...)
		if mutateTTL {
			ttl := binary.BigEndian.Uint32(msg[ttlPos : ttlPos+4])
			binary.BigEndian.PutUint32(rr[ttlPos-start:ttlPos-start+4], clampDNSTTL(ttl, cfg))
		}
		out = append(out, rr...)
		written++
		off = end
	}
	return out, written, nil
}

func consumedDNSSection(msg []byte, off int, count int) int {
	start := off
	for i := 0; i < count; i++ {
		var err error
		off, err = skipDNSName(msg, off)
		if err != nil {
			return 0
		}
		if off+10 > len(msg) {
			return 0
		}
		rdLen := int(binary.BigEndian.Uint16(msg[off+8 : off+10]))
		off += 10 + rdLen
		if off > len(msg) {
			return 0
		}
	}
	return off - start
}

func skipDNSName(msg []byte, off int) (int, error) {
	jumps := 0
	seen := map[int]bool{}
	for {
		if off >= len(msg) {
			return 0, fmt.Errorf("truncated name")
		}
		if seen[off] {
			return 0, fmt.Errorf("dns name loop")
		}
		seen[off] = true
		labelLen := int(msg[off])
		if labelLen&0xc0 == 0xc0 {
			if off+1 >= len(msg) {
				return 0, fmt.Errorf("truncated pointer")
			}
			return off + 2, nil
		}
		if labelLen&0xc0 != 0 {
			return 0, fmt.Errorf("invalid label")
		}
		off++
		if labelLen == 0 {
			return off, nil
		}
		if off+labelLen > len(msg) {
			return 0, fmt.Errorf("truncated label")
		}
		off += labelLen
		jumps++
		if jumps > 128 {
			return 0, fmt.Errorf("name too long")
		}
	}
}

func clampDNSTTL(ttl uint32, cfg config.DNSForgeConfig) uint32 {
	min, max := cfg.MinTTL, cfg.MaxTTL
	if min == 0 {
		min = 60
	}
	if max == 0 {
		max = 86400
	}
	if min > max {
		min, max = max, min
	}
	if ttl < min {
		return min
	}
	if ttl > max {
		return max
	}
	return ttl
}
