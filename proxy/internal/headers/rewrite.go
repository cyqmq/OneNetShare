package headers

import (
	"bufio"
	"fmt"
	"io"
	"net/textproto"
	"strings"

	"hotspot-proxy/internal/config"
)

type Header struct {
	Key   string
	Value string
}

type Request struct {
	Method    string
	URI       string
	Proto     string
	Host      string
	Path      string
	Headers   []Header
	RawHeader string
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
		Method: parts[0],
		URI:    parts[1],
		Proto:  parts[2],
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

type Engine struct {
	loader *config.Loader
}

func NewEngine(loader *config.Loader) *Engine { return &Engine{loader: loader} }

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
	cfg := e.loader.Get()
	if !cfg.Enabled || !cfg.DeviceForge.Enabled || !cfg.DeviceForge.Headers {
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
	if cfg.DeviceForge.Captive.Enabled && isCaptiveProbe(req, cfg) {
		headers = removeHeaders(headers, []string{"Referer", "Origin"})
	}

	// Always strip proxy-leak headers unless explicitly re-added by rule
	headers = removeHeaders(headers, []string{
		"X-Forwarded-For", "X-Real-IP", "X-Forwarded-Proto",
		"Forwarded", "Via", "Proxy-Connection", "Proxy-Authorization",
	})

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
	return req.Proto, sb.String(), nil
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
	for _, name := range []string{"Accept-Language", "Accept-Encoding"} {
		if val, ok := headerValue(profileHeaders, name); ok {
			headers = setHeaders(headers, map[string]string{name: val})
		}
	}
	return headers
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
