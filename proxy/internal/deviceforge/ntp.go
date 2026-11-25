package deviceforge

import (
	"encoding/binary"
	"time"

	"hotspot-proxy/internal/config"
)

const ntpEpochOffset = 2208988800

func NormalizeNTPRequest(payload []byte, cfg config.NTPForgeConfig) []byte {
	if len(payload) < 48 {
		return payload
	}
	out := append([]byte(nil), payload[:48]...)
	out[0] = 0x24
	out[1] = 0
	out[2] = 6
	out[3] = 0xfa
	clear := make([]byte, 20)
	copy(out[24:44], clear)
	now := uint64(time.Now().Unix() + ntpEpochOffset)
	binary.BigEndian.PutUint64(out[40:48], now<<32)
	return out
}

func NormalizeNTPResponse(payload []byte, cfg config.NTPForgeConfig) []byte {
	if len(payload) < 48 {
		return payload
	}
	out := append([]byte(nil), payload[:48]...)
	out[0] = (out[0] & 0xc0) | 0x24
	out[2] = 6
	out[3] = 0xfa
	if cfg.RewriteRefID {
		out[1] = 2
		binary.BigEndian.PutUint32(out[4:8], 0x00000200)
		binary.BigEndian.PutUint32(out[8:12], 0x00000400)
		copy(out[12:16], []byte("GPS\x00"))
	}
	return out
}
