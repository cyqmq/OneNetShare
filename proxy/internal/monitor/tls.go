package monitor

import (
	"crypto/md5"
	"encoding/binary"
	"encoding/hex"
	"fmt"
	"strconv"
	"strings"
)

type ClientHello struct {
	SNI          string
	JA3          string
	Version      uint16
	CipherSuites []uint16
	Extensions   []uint16
	Curves       []uint16
	PointFormats []byte
}

func ParseClientHello(data []byte) (*ClientHello, bool) {
	if len(data) < 5 || data[0] != 0x16 {
		return nil, false
	}
	recordLen := int(binary.BigEndian.Uint16(data[3:5]))
	if recordLen < 4 || 5+recordLen > len(data) {
		return nil, false
	}
	handshake := data[5 : 5+recordLen]
	if len(handshake) < 4 || handshake[0] != 0x01 {
		return nil, false
	}
	handshakeLen := int(handshake[1])<<16 | int(handshake[2])<<8 | int(handshake[3])
	if handshakeLen < 35 || 4+handshakeLen > len(handshake) {
		return nil, false
	}
	body := handshake[4 : 4+handshakeLen]

	version := binary.BigEndian.Uint16(body[0:2])
	pos := 2 + 32
	if pos >= len(body) {
		return nil, false
	}
	sessionLen := int(body[pos])
	pos++
	if pos+sessionLen > len(body) {
		return nil, false
	}
	pos += sessionLen

	if pos+2 > len(body) {
		return nil, false
	}
	cipherLen := int(binary.BigEndian.Uint16(body[pos : pos+2]))
	pos += 2
	if pos+cipherLen > len(body) || cipherLen%2 != 0 {
		return nil, false
	}
	ciphers := make([]uint16, 0, cipherLen/2)
	for i := 0; i < cipherLen; i += 2 {
		value := binary.BigEndian.Uint16(body[pos+i : pos+i+2])
		if !isGREASE(value) {
			ciphers = append(ciphers, value)
		}
	}
	pos += cipherLen

	if pos >= len(body) {
		return nil, false
	}
	compressionLen := int(body[pos])
	pos++
	if pos+compressionLen > len(body) {
		return nil, false
	}
	pos += compressionLen

	if pos+2 > len(body) {
		return nil, false
	}
	extTotal := int(binary.BigEndian.Uint16(body[pos : pos+2]))
	pos += 2
	if pos+extTotal > len(body) {
		return nil, false
	}
	end := pos + extTotal
	hello := &ClientHello{Version: version, CipherSuites: ciphers}
	for pos+4 <= end {
		extType := binary.BigEndian.Uint16(body[pos : pos+2])
		extLen := int(binary.BigEndian.Uint16(body[pos+2 : pos+4]))
		pos += 4
		if extLen < 0 || pos+extLen > end {
			return nil, false
		}
		data := body[pos : pos+extLen]
		pos += extLen
		if isGREASE(extType) {
			continue
		}
		hello.Extensions = append(hello.Extensions, extType)
		switch extType {
		case 0:
			hello.SNI = parseServerName(data)
		case 10:
			hello.Curves = parseCurves(data)
		case 11:
			hello.PointFormats = parsePointFormats(data)
		}
	}
	if len(hello.CipherSuites) == 0 && len(hello.Extensions) == 0 {
		return nil, false
	}
	hello.JA3 = buildJA3(hello)
	return hello, true
}

func parseServerName(data []byte) string {
	if len(data) < 2 {
		return ""
	}
	listLen := int(binary.BigEndian.Uint16(data[0:2]))
	if listLen < 2 || 2+listLen > len(data) {
		return ""
	}
	pos := 2
	end := 2 + listLen
	for pos+3 <= end {
		nameType := data[pos]
		nameLen := int(binary.BigEndian.Uint16(data[pos+1 : pos+3]))
		pos += 3
		if nameLen == 0 || pos+nameLen > end {
			return ""
		}
		if nameType == 0 {
			return string(data[pos : pos+nameLen])
		}
		pos += nameLen
	}
	return ""
}

func parseCurves(data []byte) []uint16 {
	if len(data) < 2 {
		return nil
	}
	listLen := int(binary.BigEndian.Uint16(data[0:2]))
	if listLen%2 != 0 || 2+listLen > len(data) {
		return nil
	}
	var result []uint16
	for i := 0; i < listLen; i += 2 {
		value := binary.BigEndian.Uint16(data[2+i : 4+i])
		if !isGREASE(value) {
			result = append(result, value)
		}
	}
	return result
}

func parsePointFormats(data []byte) []byte {
	if len(data) < 1 {
		return nil
	}
	count := int(data[0])
	if count <= 0 || count > len(data)-1 {
		return nil
	}
	return append([]byte(nil), data[1:1+count]...)
}

func buildJA3(hello *ClientHello) string {
	raw := fmt.Sprintf("%d,%s,%s,%s,%s",
		hello.Version,
		joinUint16(hello.CipherSuites),
		joinUint16(hello.Extensions),
		joinUint16(hello.Curves),
		joinBytes(hello.PointFormats),
	)
	sum := md5.Sum([]byte(raw))
	return hex.EncodeToString(sum[:])
}

func joinUint16(values []uint16) string {
	parts := make([]string, 0, len(values))
	for _, value := range values {
		parts = append(parts, strconv.FormatUint(uint64(value), 10))
	}
	return strings.Join(parts, "-")
}

func joinBytes(values []byte) string {
	parts := make([]string, 0, len(values))
	for _, value := range values {
		parts = append(parts, strconv.Itoa(int(value)))
	}
	return strings.Join(parts, "-")
}

func isGREASE(value uint16) bool {
	high := value >> 8
	low := value & 0xff
	return high == low && low&0x0f == 0x0a
}
