package netutil

import (
	"net"
	"sync"
	"time"
)

var egress struct {
	mu      sync.RWMutex
	name    string
	index   int
	localIP net.IP
}

func Configure(name string, index int, localIP net.IP) {
	egress.mu.Lock()
	defer egress.mu.Unlock()
	egress.name = name
	egress.index = index
	egress.localIP = append(net.IP(nil), localIP...)
}

func Reset() {
	egress.mu.Lock()
	defer egress.mu.Unlock()
	egress.name = ""
	egress.index = 0
	egress.localIP = nil
}

func Interface() (string, int, net.IP) {
	egress.mu.RLock()
	defer egress.mu.RUnlock()
	return egress.name, egress.index, append(net.IP(nil), egress.localIP...)
}

func DialTimeout(network, address string, timeout time.Duration) (net.Conn, error) {
	if isLoopbackAddress(address) {
		return (&net.Dialer{Timeout: timeout}).Dial(network, address)
	}
	d := &net.Dialer{Timeout: timeout, Control: control}
	_, index, ip := Interface()
	if index > 0 && ip != nil {
		d.LocalAddr = &net.TCPAddr{IP: ip}
	}
	return d.Dial(network, address)
}

func isLoopbackAddress(address string) bool {
	host, _, err := net.SplitHostPort(address)
	if err != nil {
		host = address
	}
	ip := net.ParseIP(host)
	return ip != nil && ip.IsLoopback()
}

func DialUDP(raddr *net.UDPAddr, laddr *net.UDPAddr) (*net.UDPConn, error) {
	if raddr != nil && raddr.IP.IsLoopback() && laddr == nil {
		return net.DialUDP("udp4", nil, raddr)
	}
	d := &net.Dialer{Control: control}
	if laddr != nil {
		d.LocalAddr = laddr
	} else if _, index, ip := Interface(); index > 0 && ip != nil {
		d.LocalAddr = &net.UDPAddr{IP: ip}
	}
	conn, err := d.Dial("udp4", raddr.String())
	if err != nil {
		return nil, err
	}
	udp, ok := conn.(*net.UDPConn)
	if !ok {
		conn.Close()
		return nil, &net.OpError{Op: "dial", Net: "udp4", Err: net.ErrClosed}
	}
	return udp, nil
}
