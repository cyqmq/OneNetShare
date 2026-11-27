package headers

import (
	"bufio"
	"fmt"
	"io"
	"net"
	"net/textproto"
	"strings"
	"sync"

	"hotspot-proxy/internal/config"
	"hotspot-proxy/internal/monitor"
)

type Header struct {
	Key   string
	Value string
}

type Request struct {
	Method      string
	URI         string
	Proto       string
	Host        string
	Path        string
	Headers     []Header
	RawHeader   string
	RawLines    []string
	RequestLine string
}

func ParseRequest(reader *bufio.Reader) (*Request, error) {
	line, err := reader.ReadString('\n')
	if err != nil {
		return nil, fmt.Errorf("读取请求行失败: %w", err)
	}
	line = strings.TrimRight(line, "\r\n")
	parts := strings.Split(line, " ")
	if len(parts) < 3 {
		return nil, fmt.Errorf("无效请求行: %s", line)
	}

	req := &Request{
		Method:      parts[0],
		URI:         parts[1],
		Proto:       parts[2],
		RequestLine: line,
	}

	var rawLines []string
	for {
		h, err := reader.ReadString('\n')
		if err != nil {
			return nil, fmt.Errorf("读取 Header 失败: %w", err)
		}
		h = strings.TrimRight(h, "\r\n")
		if h == "" {
			break
		}
		rawLines = append(rawLines, h)
		if idx := strings.IndexByte(h, ':'); idx > 0 {
			key := strings.TrimSpace(h[:idx])
			val := strings.TrimSpace(h[idx+1:])
			req.Headers = append(req.Headers, Header{Key: key, Value: val})
			if strings.EqualFold(key, "Host") {
				req.Host = val
			}
		}
	}

	req.RawHeader = strings.Join(rawLines, "\r\n")
	req.RawLines = rawLines
	req.Path = req.URI
	if req.Host == "" && strings.HasPrefix(req.URI, "http://") {
		rest := req.URI[len("http://"):]
		if idx := strings.IndexAny(rest, "/?"); idx > 0 {
			req.Host = rest[:idx]
			req.Path = rest[idx:]
		} else {
			req.Host = rest
			req.Path = "/"
		}
	} else if req.Host == "" && strings.HasPrefix(req.URI, "https://") {
		rest := req.URI[len("https://"):]
		if idx := strings.IndexAny(rest, "/?"); idx > 0 {
			req.Host = rest[:idx]
			req.Path = rest[idx:]
		} else {
			req.Host = rest
			req.Path = "/"
		}
	}

	// strip port from host for matching
	if h, _, err := splitHostPort(req.Host); err == nil {
		req.Host = h
	}
	return req, nil
}

func splitHostPort(hostport string) (string, string, error) {
	hostport = strings.TrimSpace(hostport)
	if strings.HasPrefix(hostport, "[") {
		if i := strings.Index(hostport, "]:"); i > 0 {
			return hostport[1:i], hostport[i+2:], nil
		}
		return strings.Trim(hostport, "[]"), "", nil
	}
	if i := strings.LastIndex(hostport, ":"); i > 0 {
		return hostport[:i], hostport[i+1:], nil
	}
	return hostport, "", nil
}

type Stats struct {
	Local        uint64
	Forward      uint64
	CaptiveScrub uint64
	LeakScrub    uint64
}

func (s Stats) Format() string {
	return fmt.Sprintf("local=%d forward=%d captive_scrub=%d leak_scrub=%d",
		s.Local, s.Forward, s.CaptiveScrub, s.LeakScrub)
}

type Engine struct {
	loader  *config.Loader
	monitor *monitor.Tracker
	mu      sync.Mutex
	stats   Stats
}

func NewEngine(loader *config.Loader, trackers ...*monitor.Tracker) *Engine {
	engine := &Engine{loader: loader}
	if len(trackers) > 0 {
		engine.monitor = trackers[0]
	}
	return engine
}

func (e *Engine) SetMonitor(tracker *monitor.Tracker) { e.monitor = tracker }

func (e *Engine) Stats() Stats {
	e.mu.Lock()
	defer e.mu.Unlock()
	return e.stats
}

func planeFromClient(client string) string {
	host := strings.TrimSpace(client)
	if parsed, _, err := net.SplitHostPort(host); err == nil {
		host = parsed
	}
	host = strings.Trim(host, "[]")
	if host == "" || host == "::1" || strings.HasPrefix(host, "127.") {
		return "local"
	}
	return "forward"
}

func (e *Engine) recordStats(plane string, captiveScrub, leakScrub int) {
	e.mu.Lock()
	defer e.mu.Unlock()
	if plane == "local" {
		e.stats.Local++
	} else {
		e.stats.Forward++
	}
	if captiveScrub > 0 {
		e.stats.CaptiveScrub += uint64(captiveScrub)
	}
	if leakScrub > 0 {
		e.stats.LeakScrub += uint64(leakScrub)
	}
}

func originalRequest(req *Request) string {
	var sb strings.Builder
	sb.WriteString(req.Method + " " + req.URI + " " + req.Proto + "\r\n")
	for _, h := range req.Headers {
		sb.WriteString(h.Key + ": " + h.Value + "\r\n")
	}
	sb.WriteString("\r\n")
	return sb.String()
}

func (e *Engine) Rewrite(req *Request, originalDst string) (string, string, error) {
	return e.RewriteWithClient(req, originalDst, "")
}

func (e *Engine) RewriteWithClient(req *Request, originalDst, client string) (string, string, error) {
	cfg := e.loader.Get()
	plane := planeFromClient(client)
	originalUA, _ := headerValueFromHeaders(req.Headers, "User-Agent")
	recordUA := func(effective string) {
		if e.monitor != nil {
			e.monitor.RecordUA(client, req.Host, originalUA, effective)
		}
	}
	if !cfg.Enabled || !cfg.DeviceForge.Enabled || !cfg.DeviceForge.Headers {
		e.recordStats(plane, 0, 0)
		recordUA(originalUA)
		return req.Proto, originalRequest(req), nil
	}

	headers := make([]Header, len(req.Headers))
	copy(headers, req.Headers)

	applied := false
	uaSet := false
	for _, rule := range cfg.Rules {
		if !rule.Enabled {
			continue
		}
		if !matchRule(rule, req.Host, req.Path) {
			continue
		}

		if p, ok := cfg.Profiles[rule.Profile]; ok {
			if _, ok := headerValue(p.Headers, "User-Agent"); ok {
				uaSet = true
			}
			headers = setHeaders(headers, p.Headers)
			applied = true
		}
		if len(rule.SetHeaders) > 0 {
			if _, ok := headerValue(rule.SetHeaders, "User-Agent"); ok {
				uaSet = true
			}
			headers = setHeaders(headers, rule.SetHeaders)
			applied = true
		}
		if len(rule.RemoveHeaders) > 0 {
			headers = removeHeaders(headers, rule.RemoveHeaders)
			applied = true
		}
		if len(rule.AddHeaders) > 0 {
			headers = addHeaders(headers, rule.AddHeaders)
			applied = true
		}
	}

	if !applied {
		if p, ok := cfg.Profiles[cfg.DefaultProfile]; ok {
			if _, ok := headerValue(p.Headers, "User-Agent"); ok {
				uaSet = true
			}
			headers = setHeaders(headers, p.Headers)
		} else if p, ok := config.BuiltinProfiles()["chrome-win11"]; ok {
			uaSet = true
			headers = setHeaders(headers, p.Headers)
		}
	}
	if !uaSet {
		ua, ok := headerValue(cfg.Profiles[cfg.DefaultProfile].Headers, "User-Agent")
		if !ok {
			ua, ok = headerValue(config.BuiltinProfiles()["chrome-win11"].Headers, "User-Agent")
		}
		if ok {
			headers = setHeaders(headers, map[string]string{"User-Agent": ua})
		}
	}

	// Normalize fingerprint headers together with UA profile.
	effectiveProfile := cfg.DefaultProfile
	for _, rule := range cfg.Rules {
		if rule.Enabled && matchRule(rule, req.Host, req.Path) && rule.Profile != "" {
			effectiveProfile = rule.Profile
		}
	}
	if p, ok := cfg.Profiles[effectiveProfile]; ok {
		headers = normalizeFingerprintHeaders(headers, p.Headers)
	} else if p, ok := config.BuiltinProfiles()["chrome-win11"]; ok {
		headers = normalizeFingerprintHeaders(headers, p.Headers)
	}
	captiveScrub := 0
	if cfg.DeviceForge.Captive.Enabled && isCaptiveProbe(req, cfg) {
		before := len(headers)
		names := make([]string, 0)
		for _, header := range headers {
			if isCaptiveLeakHeader(header.Key) {
				names = append(names, header.Key)
			}
		}
		headers = removeHeaders(headers, names)
		captiveScrub = before - len(headers)
	}

	// Always strip proxy-leak headers unless explicitly re-added by rule
	before := len(headers)
	headers = removeHeaders(headers, []string{
		"X-Forwarded-For", "X-Real-IP", "X-Forwarded-Proto",
		"Forwarded", "Via", "Proxy-Connection", "Proxy-Authorization",
	})
	e.recordStats(plane, captiveScrub, before-len(headers))

	uri := req.URI
	if strings.HasPrefix(uri, "http://") && cfg.Mode == "forward" {
		if idx := strings.Index(uri[len("http://"):], "/"); idx > 0 {
			uri = uri[len("http://")+idx:]
		}
	}

	var sb strings.Builder
	sb.WriteString(req.Method + " " + uri + " " + req.Proto + "\r\n")
	for _, h := range headers {
		sb.WriteString(h.Key + ": " + h.Value + "\r\n")
	}
	sb.WriteString("\r\n")
	effectiveUA, _ := headerValueFromHeaders(headers, "User-Agent")
	recordUA(effectiveUA)
	return req.Proto, sb.String(), nil
}

func (e *Engine) RewriteWithClientExact(req *Request, originalDst, client string) (string, string, error) {
	proto, rewritten, err := e.RewriteWithClient(req, originalDst, client)
	if err != nil {
		return proto, rewritten, err
	}
	if len(req.RawLines) == 0 || len(req.RawLines) != len(req.Headers) {
		return proto, rewritten, nil
	}
	parsed, err := ParseRequest(bufio.NewReader(strings.NewReader(rewritten)))
	if err != nil {
		return proto, rewritten, nil
	}
	desired := make(map[string][]string)
	for _, header := range parsed.Headers {
		key := strings.ToLower(header.Key)
		desired[key] = append(desired[key], header.Value)
	}
	matched := make(map[string]int)
	lines := make([]string, len(req.RawLines))
	spare := make([]int, 0)
	for i, raw := range req.RawLines {
		key, _, ok := rawHeaderParts(raw)
		if !ok {
			lines[i] = raw
			continue
		}
		lowerKey := strings.ToLower(key)
		index := matched[lowerKey]
		if index < len(desired[lowerKey]) {
			value := desired[lowerKey][index]
			if lowerKey == "user-agent" && len(value) > rawHeaderValueCapacity(raw) {
				value = fitUserAgent(rawHeaderValueCapacity(raw))
			}
			lines[i] = setRawHeaderValue(raw, value)
			matched[lowerKey] = index + 1
		} else {
			lines[i] = exactLine("X-Padding:", len(raw))
			spare = append(spare, i)
		}
	}
	seen := make(map[string]int)
	extra := make([]Header, 0)
	for _, header := range parsed.Headers {
		key := strings.ToLower(header.Key)
		if seen[key] < matched[key] {
			seen[key]++
			continue
		}
		seen[key]++
		extra = append(extra, header)
	}
	for _, header := range extra {
		best := -1
		bestCapacity := 0
		for _, index := range spare {
			candidate := header.Key + ": " + header.Value
			if len(candidate) <= len(req.RawLines[index]) && len(req.RawLines[index]) > bestCapacity {
				best = index
				bestCapacity = len(req.RawLines[index])
			}
		}
		if best < 0 {
			continue
		}
		lines[best] = exactLine(header.Key+": "+header.Value, len(req.RawLines[best]))
		for i, index := range spare {
			if index == best {
				spare = append(spare[:i], spare[i+1:]...)
				break
			}
		}
	}
	requestLine := req.RequestLine
	if requestLine == "" {
		requestLine = parsed.RequestLine
	}
	var sb strings.Builder
	sb.WriteString(requestLine + "\r\n")
	for _, line := range lines {
		sb.WriteString(line + "\r\n")
	}
	sb.WriteString("\r\n")
	return proto, sb.String(), nil
}

func rawHeaderParts(raw string) (string, string, bool) {
	idx := strings.IndexByte(raw, ':')
	if idx <= 0 {
		return "", "", false
	}
	return strings.TrimSpace(raw[:idx]), raw[idx+1:], true
}

func rawHeaderValueCapacity(raw string) int {
	idx := strings.IndexByte(raw, ':')
	if idx <= 0 {
		return 0
	}
	return len(raw) - idx - 1
}

func setRawHeaderValue(raw, value string) string {
	idx := strings.IndexByte(raw, ':')
	if idx <= 0 {
		return raw
	}
	capacity := len(raw) - idx - 1
	if len(value) > capacity {
		return raw
	}
	return raw[:idx+1] + value + strings.Repeat(" ", capacity-len(value))
}

func fitUserAgent(capacity int) string {
	candidates := []string{
		"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/158.0.0.0 Safari/537.36",
		"Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
		"Mozilla/5.0 (Windows NT 10.0)",
		"Mozilla/5.0 (Windows)",
		"Mozilla/5.0",
	}
	for _, candidate := range candidates {
		if len(candidate) <= capacity {
			return candidate
		}
	}
	return ""
}

func exactLine(value string, capacity int) string {
	if capacity <= 0 {
		return ""
	}
	if len(value) >= capacity {
		return value[:capacity]
	}
	return value + strings.Repeat(" ", capacity-len(value))
}

func matchRule(rule config.Rule, host, path string) bool {
	matched := false
	for _, pattern := range rule.Domains {
		if config.MatchDomain(pattern, host) {
			matched = true
			break
		}
	}
	if !matched {
		return false
	}
	if rule.PathPrefix != "" && !strings.HasPrefix(path, rule.PathPrefix) {
		return false
	}
	return true
}

func normalizeFingerprintHeaders(headers []Header, profileHeaders map[string]string) []Header {
	for _, name := range []string{"Accept", "Accept-Language", "Accept-Encoding"} {
		if val, ok := headerValue(profileHeaders, name); ok {
			headers = setHeaders(headers, map[string]string{name: val})
		}
	}
	return headers
}

func isCaptiveLeakHeader(name string) bool {
	lower := strings.ToLower(strings.TrimSpace(name))
	switch lower {
	case "referer", "origin", "accept", "priority", "upgrade-insecure-requests":
		return true
	}
	return strings.HasPrefix(lower, "accept-") || strings.HasPrefix(lower, "sec-fetch-")
}

func isCaptiveProbe(req *Request, cfg *config.Config) bool {
	host := strings.ToLower(req.Host)
	path := strings.ToLower(req.Path)
	for _, domain := range cfg.DeviceForge.Captive.Domains {
		if config.MatchDomain(strings.ToLower(domain), host) {
			return true
		}
	}
	for _, domain := range cfg.DeviceForge.Captive.DNSDomains {
		if config.MatchDomain(strings.ToLower(domain), host) {
			return true
		}
	}
	for _, probePath := range cfg.DeviceForge.Captive.Paths {
		if probePath != "" && strings.HasPrefix(path, strings.ToLower(probePath)) {
			return true
		}
	}
	return false
}

func headerValueFromHeaders(headers []Header, name string) (string, bool) {
	for _, header := range headers {
		if strings.EqualFold(header.Key, name) && header.Value != "" {
			return header.Value, true
		}
	}
	return "", false
}

func headerValue(values map[string]string, name string) (string, bool) {
	for key, value := range values {
		if strings.EqualFold(key, name) && value != "" {
			return value, true
		}
	}
	return "", false
}

func setHeaders(headers []Header, values map[string]string) []Header {
	for key, val := range values {
		found := false
		for i := range headers {
			if strings.EqualFold(headers[i].Key, key) {
				headers[i].Value = val
				found = true
			}
		}
		if !found {
			headers = append(headers, Header{Key: key, Value: val})
		}
	}
	return headers
}

func removeHeaders(headers []Header, names []string) []Header {
	out := headers[:0]
	for _, h := range headers {
		remove := false
		for _, name := range names {
			if strings.EqualFold(h.Key, name) {
				remove = true
				break
			}
		}
		if !remove {
			out = append(out, h)
		}
	}
	return out
}

func addHeaders(headers []Header, values map[string]string) []Header {
	for key, val := range values {
		found := false
		for i := range headers {
			if strings.EqualFold(headers[i].Key, key) {
				headers[i].Value = val
				found = true
				break
			}
		}
		if !found {
			headers = append(headers, Header{Key: key, Value: val})
		}
	}
	return headers
}

// ContentLength returns -1 when body length is unknown (chunked or absent).
func ContentLength(headers []Header) int64 {
	for _, h := range headers {
		if strings.EqualFold(h.Key, "Content-Length") {
			var n int64
			if _, err := fmt.Sscan(h.Value, &n); err == nil {
				return n
			}
		}
	}
	return -1
}

func IsChunked(headers []Header) bool {
	for _, h := range headers {
		if strings.EqualFold(h.Key, "Transfer-Encoding") && strings.Contains(strings.ToLower(h.Value), "chunked") {
			return true
		}
	}
	return false
}

func CopyRequestBody(dst io.Writer, src io.Reader, headers []Header) error {
	if IsChunked(headers) {
		_, err := io.Copy(dst, src)
		return err
	}
	n := ContentLength(headers)
	if n <= 0 {
		return nil
	}
	_, err := io.CopyN(dst, src, n)
	return err
}

func CopyResponseBody(dst io.Writer, src io.Reader, headers []Header) error {
	if IsChunked(headers) {
		_, err := io.Copy(dst, src)
		return err
	}
	n := ContentLength(headers)
	if n < 0 {
		_, err := io.Copy(dst, src)
		return err
	}
	if n == 0 {
		return nil
	}
	_, err := io.CopyN(dst, src, n)
	return err
}

func CanonicalHeaderKey(s string) string { return textproto.CanonicalMIMEHeaderKey(s) }
