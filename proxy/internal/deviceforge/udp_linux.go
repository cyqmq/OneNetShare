//go:build linux

package deviceforge

import (
	"fmt"
	"net"
	"strconv"
	"unsafe"

	"golang.org/x/sys/unix"
)

const (
	solIP             = unix.SOL_IP
	ipTransparent     = 19
	ipRecvOrigDstAddr = 20
	ipPktInfo         = 8
	outboundMark      = 2
)

func markOutboundConn(conn *net.UDPConn) error {
	rc, err := conn.SyscallConn()
	if err != nil {
		return err
	}
	var opErr error
	err = rc.Control(func(fd uintptr) {
		opErr = unix.SetsockoptInt(int(fd), unix.SOL_SOCKET, unix.SO_MARK, outboundMark)
	})
	if err != nil {
		return err
	}
	return opErr
}

type sockaddrIn struct {
	Family uint16
	Port   uint16
	Addr   [4]byte
	Zero   [8]byte
}

type ipPktinfo struct {
	Ifindex int32
	SpecDst [4]byte
	Addr    [4]byte
}

type udpSender struct {
	conn     *net.UDPConn
	client   *net.UDPAddr
	localIP  net.IP
	useSrcIP bool
}

func (s *udpSender) LocalIP() net.IP { return s.localIP }

func (s *udpSender) SendUDP(srcIP net.IP, srcPort uint16, dstIP net.IP, dstPort uint16, payload []byte) error {
	if s.useSrcIP {
		oob := pktinfoCmsg(srcIP)
		_, _, err := s.conn.WriteMsgUDP(payload, oob, s.client)
		return err
	}
	_, err := s.conn.WriteToUDP(payload, s.client)
	return err
}

func startUDPListeners(m *Manager, mode string) error {
	cfg := m.loader.Get().DeviceForge
	if !cfg.Enabled || !cfg.UDP.Enabled {
		return nil
	}
	if mode == "forward" {
		return fmt.Errorf("DeviceForge UDP 需 redirect/tproxy/tun 模式")
	}

	if mode == "tproxy" && cfg.UDP.AllPorts {
		return listenUDP(m, cfg.UDP.Listen, mode, 0)
	}

	ports := map[int]int{}
	for _, p := range cfg.UDP.TargetPorts {
		if p > 0 && p < 65536 {
			ports[p] = defaultUDPListenPort(p)
		}
	}
	for key, listenPort := range cfg.UDP.ListenPorts {
		p, err := strconv.Atoi(key)
		if err != nil || p <= 0 || p >= 65536 || listenPort <= 0 || listenPort >= 65536 {
			continue
		}
		ports[p] = listenPort
	}
	if cfg.DNS.Enabled {
		ports[53] = listenPortFor(ports, 53)
	}
	if cfg.NTP.Enabled {
		ports[123] = listenPortFor(ports, 123)
	}
	if cfg.MDNS.Enabled {
		ports[5353] = listenPortFor(ports, 5353)
	}
	if cfg.DHCP.Enabled {
		ports[67] = listenPortFor(ports, 67)
		ports[68] = listenPortFor(ports, 68)
	}
	if len(ports) == 0 {
		return nil
	}

	for origPort, listenPort := range ports {
		addr := net.JoinHostPort("0.0.0.0", strconv.Itoa(listenPort))
		if err := listenUDP(m, addr, mode, origPort); err != nil {
			return err
		}
	}
	if cfg.UDP.AllPorts && mode != "tproxy" {
		m.logger.Printf("DeviceForge: redirect 模式不支持全端口 UDP；已仅接管显式端口，建议改用 tun 或 tproxy")
	}
	return nil
}

func listenPortFor(m map[int]int, port int) int {
	if p, ok := m[port]; ok {
		return p
	}
	return defaultUDPListenPort(port)
}

func defaultUDPListenPort(port int) int {
	if port > 0 && port < 18000 {
		return 18000 + port
	}
	return 20000 + (port % 20000)
}

func listenUDP(m *Manager, addr string, mode string, origPort int) error {
	ua, err := net.ResolveUDPAddr("udp4", addr)
	if err != nil {
		return err
	}
	conn, err := net.ListenUDP("udp4", ua)
	if err != nil {
		return fmt.Errorf("监听 UDP %s 失败: %w", addr, err)
	}

	localIP := detectLocalIP()
	if ua.IP != nil && !ua.IP.IsUnspecified() {
		localIP = ua.IP.To4()
	}

	if err := configureUDPSocket(conn, mode == "tproxy"); err != nil {
		conn.Close()
		return err
	}
	m.logger.Printf("DeviceForge UDP 监听: %s mode=%s origPort=%d", conn.LocalAddr(), mode, origPort)
	go readUDPLoop(m, conn, localIP, mode == "tproxy", origPort)
	return nil
}

func configureUDPSocket(conn *net.UDPConn, transparent bool) error {
	rc, err := conn.SyscallConn()
	if err != nil {
		return err
	}
	var opErr error
	err = rc.Control(func(fd uintptr) {
		if transparent {
			if e := unix.SetsockoptInt(int(fd), solIP, ipTransparent, 1); e != nil {
				opErr = e
				return
			}
			if e := unix.SetsockoptInt(int(fd), solIP, ipPktInfo, 1); e != nil {
				opErr = e
				return
			}
		}
		if e := unix.SetsockoptInt(int(fd), solIP, ipRecvOrigDstAddr, 1); e != nil {
			opErr = e
		}
	})
	if err != nil {
		return err
	}
	return opErr
}

func readUDPLoop(m *Manager, conn *net.UDPConn, localIP net.IP, transparent bool, origPort int) {
	buf := make([]byte, 65535)
	oob := make([]byte, 512)
	for {
		n, oobn, _, client, err := conn.ReadMsgUDP(buf, oob)
		if err != nil {
			m.logger.Printf("[deviceforge] udp read: %v", err)
			return
		}
		if n == 0 || client == nil {
			continue
		}

		dstIP, dstPort, ok := originalDstFromOOB(oob[:oobn])
		if origPort > 0 {
			if !ok {
				dstIP = localIP
			}
			dstPort = uint16(origPort)
		} else if !ok {
			continue
		}
		srcIP := client.IP.To4()
		if srcIP == nil {
			continue
		}
		clientCopy := &net.UDPAddr{IP: append(net.IP(nil), client.IP...), Port: client.Port, Zone: client.Zone}
		sender := &udpSender{conn: conn, client: clientCopy, localIP: localIP, useSrcIP: transparent}
		m.HandleUDP(srcIP, uint16(client.Port), dstIP, dstPort, append([]byte(nil), buf[:n]...), sender)
	}
}

func originalDstFromOOB(oob []byte) (net.IP, uint16, bool) {
	if len(oob) == 0 {
		return nil, 0, false
	}
	msgs, err := unix.ParseSocketControlMessage(oob)
	if err != nil {
		return nil, 0, false
	}
	for _, msg := range msgs {
		if msg.Header.Level != solIP || msg.Header.Type != ipRecvOrigDstAddr {
			continue
		}
		if len(msg.Data) < int(unsafe.Sizeof(sockaddrIn{})) {
			continue
		}
		var sa sockaddrIn
		copy((*[unsafe.Sizeof(sockaddrIn{})]byte)(unsafe.Pointer(&sa))[:], msg.Data)
		port := sa.Port<<8 | sa.Port>>8
		return net.IP(append([]byte(nil), sa.Addr[:]...)), port, true
	}
	return nil, 0, false
}

func pktinfoCmsg(srcIP net.IP) []byte {
	v4 := srcIP.To4()
	if v4 == nil {
		v4 = net.IPv4zero.To4()
	}
	oob := make([]byte, unix.CmsgSpace(12))
	hdr := (*unix.Cmsghdr)(unsafe.Pointer(&oob[0]))
	hdr.Level = solIP
	hdr.Type = ipPktInfo
	hdr.SetLen(12)
	info := (*ipPktinfo)(unsafe.Pointer(uintptr(unsafe.Pointer(&oob[0])) + uintptr(unix.CmsgLen(0))))
	copy(info.SpecDst[:], v4)
	copy(info.Addr[:], v4)
	return oob
}

func detectLocalIP() net.IP {
	conn, err := net.Dial("udp4", "8.8.8.8:80")
	if err == nil {
		defer conn.Close()
		if addr, ok := conn.LocalAddr().(*net.UDPAddr); ok && addr.IP != nil {
			return addr.IP.To4()
		}
	}
	return net.IPv4(127, 0, 0, 1).To4()
}
