package monitor

import "testing"

func TestClassifyUserAgent(t *testing.T) {
	tests := map[string]uaKind{
		"Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 Chrome/120 Mobile Safari/537.36": kindPhone,
		"Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148":   kindPhone,
		"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36":       kindDesktop,
		"curl/8.5.0": kindUnknown,
	}
	for ua, want := range tests {
		if got := classifyUserAgent(ua); got != want {
			t.Fatalf("classifyUserAgent(%q) = %s, want %s", ua, got, want)
		}
	}
}
