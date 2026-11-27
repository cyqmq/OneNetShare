package outboundguard

import (
	"encoding/binary"
	"testing"

	"hotspot-proxy/internal/config"
)

func ipv4Packet(proto byte, dst [4]byte, dstPort uint16) []byte {
	pkt := make([]byte, 28)
	pkt[0] = 0x45
	pkt[9] = proto
	copy(pkt[16:20], dst[:])
	binary.BigEndian.PutUint16(pkt[22:24], dstPort)
	return pkt
}

func TestDropIPv4MatchesConfiguredRules(t *testing.T) {
	guard := New(config.OutboundGuardConfig{
		Enabled:        true,
		UDPPorts:       []int{443},
		TCPPorts:       []int{445},
		BlockBroadcast: true,
		BlockMulticast: true,
	}, nil)

	cases := []struct {
		name string
		pkt  []byte
		want bool
	}{
		{"udp443", ipv4Packet(17, [4]byte{1, 1, 1, 1}, 443), true},
		{"tcp445", ipv4Packet(6, [4]byte{1, 1, 1, 1}, 445), true},
		{"broadcast", ipv4Packet(17, [4]byte{255, 255, 255, 255}, 137), true},
		{"multicast", ipv4Packet(17, [4]byte{224, 0, 0, 251}, 5353), true},
		{"unmatched", ipv4Packet(6, [4]byte{1, 1, 1, 1}, 80), false},
	}
	for _, tc := range cases {
		if got := guard.DropIPv4(tc.pkt); got != tc.want {
			t.Fatalf("%s: got %v want %v", tc.name, got, tc.want)
		}
	}
	stats := guard.Stats()
	if stats.Dropped != 4 || stats.UDP != 1 || stats.TCP != 1 || stats.Broadcast != 1 || stats.Multicast != 1 {
		t.Fatalf("统计错误: %+v", stats)
	}
}

func TestDropIPv4Disabled(t *testing.T) {
	guard := New(config.OutboundGuardConfig{Enabled: false, UDPPorts: []int{443}}, nil)
	if guard.DropIPv4(ipv4Packet(17, [4]byte{1, 1, 1, 1}, 443)) {
		t.Fatal("关闭时不应丢包")
	}
}
