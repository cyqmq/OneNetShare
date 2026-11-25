package monitor

import (
	"bytes"
	"encoding/binary"
	"testing"
)

func TestParseClientHello(t *testing.T) {
	data := buildClientHello(t, 0x0303, "example.com", false)
	hello, ok := ParseClientHello(data)
	if !ok {
		t.Fatal("ParseClientHello failed")
	}
	if hello.SNI != "example.com" {
		t.Fatalf("SNI = %q", hello.SNI)
	}
	if hello.JA3 != "38eaca597c62da4c9db8cfad482f14ad" {
		t.Fatalf("JA3 = %s", hello.JA3)
	}
}

func TestParseClientHelloIgnoresGREASE(t *testing.T) {
	data := buildClientHello(t, 0x0303, "example.com", true)
	hello, ok := ParseClientHello(data)
	if !ok {
		t.Fatal("ParseClientHello failed")
	}
	for _, value := range hello.CipherSuites {
		if isGREASE(value) {
			t.Fatalf("GREASE cipher retained: %#x", value)
		}
	}
	for _, value := range hello.Extensions {
		if isGREASE(value) {
			t.Fatalf("GREASE extension retained: %#x", value)
		}
	}
	if hello.JA3 != "38eaca597c62da4c9db8cfad482f14ad" {
		t.Fatalf("JA3 = %s", hello.JA3)
	}
}

func buildClientHello(t *testing.T, version uint16, sni string, grease bool) []byte {
	t.Helper()
	var body bytes.Buffer
	binary.Write(&body, binary.BigEndian, version)
	body.Write(make([]byte, 32))
	body.WriteByte(0)

	ciphers := []uint16{0x1301, 0x1302}
	if grease {
		ciphers = append([]uint16{0x0a0a}, ciphers...)
	}
	binary.Write(&body, binary.BigEndian, uint16(len(ciphers)*2))
	for _, cipher := range ciphers {
		binary.Write(&body, binary.BigEndian, cipher)
	}
	body.WriteByte(1)
	body.WriteByte(0)

	var exts bytes.Buffer
	writeExt := func(extType uint16, payload []byte) {
		binary.Write(&exts, binary.BigEndian, extType)
		binary.Write(&exts, binary.BigEndian, uint16(len(payload)))
		exts.Write(payload)
	}
	var serverName bytes.Buffer
	binary.Write(&serverName, binary.BigEndian, uint16(len(sni)+3))
	serverName.WriteByte(0)
	binary.Write(&serverName, binary.BigEndian, uint16(len(sni)))
	serverName.WriteString(sni)
	writeExt(0, serverName.Bytes())

	var groups bytes.Buffer
	binary.Write(&groups, binary.BigEndian, uint16(4))
	binary.Write(&groups, binary.BigEndian, uint16(0x001d))
	binary.Write(&groups, binary.BigEndian, uint16(0x0017))
	writeExt(10, groups.Bytes())

	writeExt(11, []byte{1, 0})
	if grease {
		writeExt(0x1a1a, nil)
	}
	binary.Write(&body, binary.BigEndian, uint16(exts.Len()))
	body.Write(exts.Bytes())

	var handshake bytes.Buffer
	handshake.WriteByte(0x01)
	length := body.Len()
	handshake.Write([]byte{byte(length >> 16), byte(length >> 8), byte(length)})
	handshake.Write(body.Bytes())

	var record bytes.Buffer
	record.WriteByte(0x16)
	record.Write([]byte{0x03, 0x01})
	binary.Write(&record, binary.BigEndian, uint16(handshake.Len()))
	record.Write(handshake.Bytes())
	return record.Bytes()
}
