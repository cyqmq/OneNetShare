package monitor

import (
	"bufio"
	"log"
	"net"
	"sync"
	"time"
)

const (
	uaWindow         = 90 * time.Second
	closeWindow      = 20 * time.Second
	evidenceCooldown = 60 * time.Second
)

type uaEvent struct {
	At     time.Time
	Client string
	Host   string
	Kind   uaKind
	UA     string
}

type closeEvent struct {
	At     time.Time
	Client string
}

type Stats struct {
	Phone   int
	Desktop int
	Unknown int
	TLS     int
	Alerts  int
	Events  int
}

type Tracker struct {
	mu        sync.Mutex
	logger    *log.Logger
	enabled   func() bool
	uaEvents  []uaEvent
	closes    []closeEvent
	stats     Stats
	lastAlert time.Time
}

func New(logger *log.Logger, enabled func() bool) *Tracker {
	return &Tracker{logger: logger, enabled: enabled}
}

func (t *Tracker) active() bool {
	return t != nil && t.logger != nil && (t.enabled == nil || t.enabled())
}

func (t *Tracker) RecordUA(client, host, originalUA, effectiveUA string) {
	if !t.active() {
		return
	}
	kind := classifyUserAgent(originalUA)
	if kind == kindUnknown {
		kind = classifyUserAgent(effectiveUA)
	}
	t.logger.Printf("[probe][监控] UA client=%s host=%s kind=%s original=%q effective=%q",
		client, host, kind, originalUA, effectiveUA)

	t.mu.Lock()
	defer t.mu.Unlock()
	t.uaEvents = append(t.uaEvents, uaEvent{At: time.Now(), Client: client, Host: host, Kind: kind, UA: originalUA})
	t.pruneLocked(time.Now())
	switch kind {
	case kindPhone:
		t.stats.Phone++
	case kindDesktop:
		t.stats.Desktop++
	default:
		t.stats.Unknown++
	}
}

func (t *Tracker) RecordTLS(client, dst, sni, ja3 string) {
	if !t.active() {
		return
	}
	t.logger.Printf("[probe][监控] TLS client=%s dst=%s sni=%q ja3=%s", client, dst, sni, ja3)
	t.mu.Lock()
	defer t.mu.Unlock()
	t.stats.TLS++
}

func (t *Tracker) RecordClose(client string) {
	if !t.active() {
		return
	}
	t.mu.Lock()
	defer t.mu.Unlock()
	now := time.Now()
	t.closes = append(t.closes, closeEvent{At: now, Client: client})
	t.pruneLocked(now)
	t.analyzeLocked(now, client)
}

func (t *Tracker) InspectTLS(conn net.Conn, reader *bufio.Reader, client, dst string) {
	if !t.active() || reader == nil {
		return
	}
	if tc, ok := conn.(*net.TCPConn); ok {
		_ = tc.SetReadDeadline(time.Now().Add(400 * time.Millisecond))
		defer tc.SetReadDeadline(time.Time{})
	}
	head, _ := reader.Peek(5)
	if len(head) < 5 || head[0] != 0x16 {
		return
	}
	recordLen := int(head[3])<<8 | int(head[4])
	if recordLen <= 0 || recordLen > 16384 {
		return
	}
	total := 5 + recordLen
	data, _ := reader.Peek(total)
	if len(data) < total {
		return
	}
	hello, ok := ParseClientHello(data[:total])
	if !ok {
		return
	}
	t.RecordTLS(client, dst, hello.SNI, hello.JA3)
}

func (t *Tracker) Stats() Stats {
	t.mu.Lock()
	defer t.mu.Unlock()
	result := t.stats
	result.Events = len(t.uaEvents)
	return result
}

func (t *Tracker) pruneLocked(now time.Time) {
	uaCutoff := now.Add(-5 * time.Minute)
	keptUA := t.uaEvents[:0]
	for _, event := range t.uaEvents {
		if event.At.After(uaCutoff) {
			keptUA = append(keptUA, event)
		}
	}
	t.uaEvents = keptUA
	if len(t.uaEvents) > 4096 {
		t.uaEvents = append([]uaEvent(nil), t.uaEvents[len(t.uaEvents)-4096:]...)
	}

	closeCutoff := now.Add(-2 * time.Minute)
	keptCloses := t.closes[:0]
	for _, event := range t.closes {
		if event.At.After(closeCutoff) {
			keptCloses = append(keptCloses, event)
		}
	}
	t.closes = keptCloses
	if len(t.closes) > 512 {
		t.closes = append([]closeEvent(nil), t.closes[len(t.closes)-512:]...)
	}
}

func (t *Tracker) analyzeLocked(now time.Time, client string) {
	if !now.After(t.lastAlert.Add(evidenceCooldown)) {
		return
	}
	cutoff := now.Add(-uaWindow)
	var phones, desktops int
	var lastUA time.Time
	for _, event := range t.uaEvents {
		if !event.At.After(cutoff) {
			continue
		}
		switch event.Kind {
		case kindPhone:
			phones++
		case kindDesktop:
			desktops++
		}
		if event.At.After(lastUA) {
			lastUA = event.At
		}
	}
	if phones < 3 || desktops < 1 || lastUA.IsZero() {
		return
	}
	closed := false
	for _, event := range t.closes {
		if !event.At.After(cutoff) || event.At.Before(lastUA) {
			continue
		}
		if event.At.Sub(lastUA) <= closeWindow {
			closed = true
			if event.Client != "" {
				client = event.Client
			}
			break
		}
	}
	if !closed {
		return
	}
	t.lastAlert = now
	t.stats.Alerts++
	t.logger.Printf("[probe][监控证据] %s 内手机UA=%d、电脑UA=%d，客户端 %s 随后断开；判定=疑似网关按应用层UA/指纹匹配断网（需结合 probe.log 复核）",
		uaWindow, phones, desktops, client)
}
