package tun

import (
	"bufio"
	"bytes"
	"strconv"
	"strings"

	"hotspot-proxy/internal/headers"
)

type httpStreamRewriter struct {
	engine   *headers.Engine
	clientIP string
	buf      []byte
}

func (r *httpStreamRewriter) Rewrite(p []byte) ([]byte, error) {
	if len(p) == 0 {
		return nil, nil
	}
	r.buf = append(r.buf, p...)
	var out bytes.Buffer
	for len(r.buf) > 0 {
		if !looksLikeHTTPRequest(r.buf) {
			out.Write(r.buf)
			r.buf = nil
			break
		}
		idx := bytes.Index(r.buf, []byte("\r\n\r\n"))
		if idx < 0 {
			if len(r.buf) > 64*1024 {
				out.Write(r.buf)
				r.buf = nil
			}
			break
		}
		header := r.buf[:idx+4]
		total := idx + 4 + contentLength(header)
		if len(r.buf) < total {
			if total > 2*1024*1024 {
				out.Write(r.buf)
				r.buf = nil
			}
			break
		}
		if bytes.Contains(bytes.ToUpper(header), []byte("TRANSFER-ENCODING: CHUNKED")) {
			out.Write(header)
		} else if rewritten, err := r.rewriteHeader(header); err == nil && len(rewritten) > 0 {
			out.Write(rewritten)
		} else {
			out.Write(header)
		}
		out.Write(r.buf[idx+4 : total])
		r.buf = append([]byte(nil), r.buf[total:]...)
	}
	return out.Bytes(), nil
}

func (r *httpStreamRewriter) rewriteHeader(header []byte) ([]byte, error) {
	req, err := headers.ParseRequest(bufio.NewReader(bytes.NewReader(header)))
	if err != nil {
		return nil, err
	}
	_, rewritten, err := r.engine.RewriteWithClient(req, "", r.clientIP)
	if err != nil {
		return nil, err
	}
	return []byte(rewritten), nil
}

func looksLikeHTTPRequest(b []byte) bool {
	if len(b) == 0 || b[0] < 'A' || b[0] > 'Z' {
		return false
	}
	for i := 0; i < len(b) && i < 16; i++ {
		if b[i] == ' ' {
			return i >= 2 && i <= 10
		}
		if b[i] < 'A' || b[i] > 'Z' {
			return false
		}
	}
	return true
}

func contentLength(header []byte) int {
	for _, line := range strings.Split(string(header), "\r\n") {
		idx := strings.IndexByte(line, ':')
		if idx <= 0 || !strings.EqualFold(strings.TrimSpace(line[:idx]), "Content-Length") {
			continue
		}
		n, err := strconv.Atoi(strings.TrimSpace(line[idx+1:]))
		if err == nil && n >= 0 {
			return n
		}
	}
	return 0
}
