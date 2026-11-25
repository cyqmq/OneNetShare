//go:build !linux

package redirect

import (
	"fmt"
	"net"
)

func OriginalDst(conn *net.TCPConn) (string, error) {
	return "", fmt.Errorf("透明代理仅支持 Linux；当前平台请使用 forward 模式")
}

func ListenTransparent(addr string) (net.Listener, error) {
	return nil, fmt.Errorf("TPROXY 仅支持 Linux；当前平台请使用 forward 模式")
}
