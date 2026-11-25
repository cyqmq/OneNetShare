//go:build linux

// Package tun implements a userspace TUN-based transparent proxy.
// It uses a simplified TCP state machine: outbound connections are
// created by the gateway kernel via net.Dial, so all TCP/IP layer
// parameters (IPID, ISN, window, MSS, timestamps) are gateway-native.
package tun

import (
	"encoding/binary"
	"fmt"
	"hash/fnv"
	"log"
	"net"
	"os/exec"
	"sync"
	"sync/atomic"
	"time"

	"github.com/songgao/water"
	"golang.org/x/sys/unix"
)

const (
	ipv4         = 0x45
	tcpProtocol  = 6
	udpProtocol  = 17
	outboundMark = 2
	finFlag      = 0x01
	synFlag      = 0x02
	rstFlag      = 0x04
	ackFlag      = 0x10
	pshFlag      = 0x08
)

type connection struct {
	key        string
	clientIP   net.IP
	clientPort uint16
	dstIP      net.IP
	dstPort    uint16
	seq        uint32
	ack        uint32
	upstream   net.Conn
	lastSeen   time.Time
	closed     bool
}

type UDPHandler func(srcIP net.IP, srcPort uint16, dstIP net.IP, dstPort uint16, payload []byte)

type Device struct {
	iface          *water.Interface
	tunIP          net.IP
	logger         *log.Logger
	conns          sync.Map
	recentOutbound sync.Map
	udpHandler     UDPHandler
	ipid           uint16
	mu             sync.Mutex
	wg             sync.WaitGroup
	quit           chan struct{}
}

func New(tunName string, tunIP string, logger *log.Logger) (*Device, error) {
	cfg := water.Config{DeviceType: water.TUN}
	cfg.Name = tunName
	iface, err := water.New(cfg)
	if err != nil {
		return nil, fmt.Errorf("创建 TUN 设备失败: %w", err)
	}

	ip := net.ParseIP(tunIP)
	if ip == nil {
		return nil, fmt.Errorf("无效 TUN IP: %s", tunIP)
	}

	d := &Device{
		iface:  iface,
		tunIP:  ip.To4(),
		logger: logger,
		quit:   make(chan struct{}),
	}
	return d, nil
}

func (d *Device) Name() string { return d.iface.Name() }

func (d *Device) LocalIP() net.IP { return d.tunIP }

func (d *Device) SetUDPHandler(h UDPHandler) { d.udpHandler = h }

func (d *Device) Close() {
	close(d.quit)
	d.iface.Close()
	d.wg.Wait()
}

func connKey(srcIP net.IP, srcPort, dstPort uint16, dstIP net.IP) string {
	return fmt.Sprintf("%s:%d->%s:%d", srcIP, srcPort, dstIP, dstPort)
}

func (d *Device) nextIPID() uint16 {
	d.mu.Lock()
	defer d.mu.Unlock()
	d.ipid++
	return d.ipid
}

func markTCPConn(conn net.Conn) error {
	tc, ok := conn.(*net.TCPConn)
	if !ok {
		return nil
	}
	rc, err := tc.SyscallConn()
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

func checksum(data []byte) uint16 {
	var sum uint32
	for i := 0; i < len(data)-1; i += 2 {
		sum += uint32(binary.BigEndian.Uint16(data[i : i+2]))
	}
	if len(data)%2 == 1 {
		sum += uint32(data[len(data)-1]) << 8
	}
	for sum>>16 != 0 {
		sum = (sum & 0xffff) + (sum >> 16)
	}
	return ^uint16(sum)
}

func (d *Device) sendPacket(dstIP net.IP, dstPort uint16, srcIP net.IP, srcPort uint16,
	seq, ack uint32, flags byte, payload []byte) error {

	tcpLen := 20 + len(payload)
	totalLen := 20 + tcpLen

	pkt := make([]byte, totalLen)
	// IPv4 header
	pkt[0] = ipv4
	pkt[1] = 0
	binary.BigEndian.PutUint16(pkt[2:4], uint16(totalLen))
	binary.BigEndian.PutUint16(pkt[4:6], d.nextIPID())
	binary.BigEndian.PutUint16(pkt[6:8], 0x4000) // Don't Fragment
	pkt[8] = 64                                  // TTL
	pkt[9] = tcpProtocol
	copy(pkt[12:16], srcIP.To4())
	copy(pkt[16:20], dstIP.To4())
	binary.BigEndian.PutUint16(pkt[10:12], checksum(pkt[:20]))

	// TCP header
	t := pkt[20:]
	binary.BigEndian.PutUint16(t[0:2], srcPort)
	binary.BigEndian.PutUint16(t[2:4], dstPort)
	binary.BigEndian.PutUint32(t[4:8], seq)
	binary.BigEndian.PutUint32(t[8:12], ack)
	t[12] = 5 << 4
	t[13] = flags
	binary.BigEndian.PutUint16(t[14:16], 8192) // window
	binary.BigEndian.PutUint16(t[16:18], 0)    // checksum (computed below)
	binary.BigEndian.PutUint16(t[18:20], 0)    // urgent
	copy(t[20:], payload)

	// TCP checksum with pseudo-header
	ph := make([]byte, 12+tcpLen)
	copy(ph[0:4], srcIP.To4())
	copy(ph[4:8], dstIP.To4())
	ph[8] = 0
	ph[9] = tcpProtocol
	binary.BigEndian.PutUint16(ph[10:12], uint16(tcpLen))
	copy(ph[12:], t)
	binary.BigEndian.PutUint16(t[16:18], checksum(ph))

	_, err := d.iface.Write(pkt)
	return err
}

func (d *Device) SendUDP(srcIP net.IP, srcPort uint16, dstIP net.IP, dstPort uint16, payload []byte) error {
	udpLen := 8 + len(payload)
	totalLen := 20 + udpLen
	pkt := make([]byte, totalLen)
	pkt[0] = ipv4
	binary.BigEndian.PutUint16(pkt[2:4], uint16(totalLen))
	binary.BigEndian.PutUint16(pkt[4:6], d.nextIPID())
	binary.BigEndian.PutUint16(pkt[6:8], 0x4000)
	pkt[8] = 64
	pkt[9] = udpProtocol
	copy(pkt[12:16], srcIP.To4())
	copy(pkt[16:20], dstIP.To4())
	binary.BigEndian.PutUint16(pkt[10:12], checksum(pkt[:20]))

	u := pkt[20:]
	binary.BigEndian.PutUint16(u[0:2], srcPort)
	binary.BigEndian.PutUint16(u[2:4], dstPort)
	binary.BigEndian.PutUint16(u[4:6], uint16(udpLen))
	binary.BigEndian.PutUint16(u[6:8], 0)
	copy(u[8:], payload)

	ph := make([]byte, 12+udpLen)
	copy(ph[0:4], srcIP.To4())
	copy(ph[4:8], dstIP.To4())
	ph[8] = 0
	ph[9] = udpProtocol
	binary.BigEndian.PutUint16(ph[10:12], uint16(udpLen))
	copy(ph[12:], u)
	binary.BigEndian.PutUint16(u[6:8], checksum(ph))

	d.recentOutbound.Store(hashPacket(pkt), time.Now().UnixNano())
	_, err := d.iface.Write(pkt)
	return err
}

func hashPacket(pkt []byte) uint64 {
	h := fnv.New64a()
	h.Write(pkt)
	return h.Sum64()
}

func (d *Device) isRecentOutbound(pkt []byte) bool {
	key := hashPacket(pkt)
	v, ok := d.recentOutbound.Load(key)
	if !ok {
		return false
	}
	ts, ok := v.(int64)
	if ok && time.Since(time.Unix(0, ts)) < 10*time.Second {
		return true
	}
	d.recentOutbound.Delete(key)
	return false
}

func (d *Device) Run() error {
	buf := make([]byte, 65535)
	d.logger.Printf("TUN 设备 %s 已启动 (IP=%s)", d.iface.Name(), d.tunIP)

	for {
		select {
		case <-d.quit:
			return nil
		default:
		}
		n, err := d.iface.Read(buf)
		if err != nil {
			select {
			case <-d.quit:
				return nil
			default:
			}
			d.logger.Printf("TUN 读取错误: %v", err)
			continue
		}
		go d.handlePacket(buf[:n])
	}
}

func (d *Device) handlePacket(pkt []byte) {
	if len(pkt) < 20 || pkt[0] != ipv4 || d.isRecentOutbound(pkt) {
		return
	}
	ihl := int(pkt[0]&0x0f) * 4
	if len(pkt) < ihl+8 {
		return
	}

	srcIP := net.IP(append([]byte(nil), pkt[12:16]...))
	dstIP := net.IP(append([]byte(nil), pkt[16:20]...))
	t := pkt[ihl:]

	if pkt[9] == udpProtocol {
		if len(t) < 8 {
			return
		}
		udpLen := int(binary.BigEndian.Uint16(t[4:6]))
		if udpLen < 8 || udpLen > len(t) {
			return
		}
		if d.udpHandler != nil {
			d.udpHandler(srcIP, binary.BigEndian.Uint16(t[0:2]), dstIP, binary.BigEndian.Uint16(t[2:4]), t[8:udpLen])
		}
		return
	}
	if pkt[9] != tcpProtocol || len(t) < 20 {
		return
	}

	srcPort := binary.BigEndian.Uint16(t[0:2])
	dstPort := binary.BigEndian.Uint16(t[2:4])
	seq := binary.BigEndian.Uint32(t[4:8])
	ack := binary.BigEndian.Uint32(t[8:12])
	dataOff := int((t[12]>>4)&0x0f) * 4
	if dataOff < 20 || dataOff > len(t) {
		return
	}
	flags := t[13]
	payload := t[dataOff:]

	key := connKey(srcIP, srcPort, dstPort, dstIP)

	if flags&synFlag != 0 && flags&ackFlag == 0 {
		d.handleSYN(key, srcIP, srcPort, dstIP, dstPort, seq, ack)
		return
	}

	val, ok := d.conns.Load(key)
	if !ok {
		// RST for unknown connection
		if flags&rstFlag == 0 {
			d.sendPacket(srcIP, srcPort, dstIP, dstPort, ack, seq+uint32(len(payload)), rstFlag|ackFlag, nil)
		}
		return
	}
	c := val.(*connection)
	c.lastSeen = time.Now()

	if flags&rstFlag != 0 || flags&finFlag != 0 {
		d.closeConnection(c, key)
		return
	}

	if len(payload) > 0 {
		if c.upstream == nil {
			return
		}
		if _, err := c.upstream.Write(payload); err != nil {
			d.logger.Printf("[tun] upstream write: %v", err)
			d.closeConnection(c, key)
			return
		}
		c.ack = seq + uint32(len(payload))
		d.sendPacket(srcIP, srcPort, dstIP, dstPort, c.seq, c.ack, ackFlag, nil)
	}
}

func (d *Device) handleSYN(key string, srcIP net.IP, srcPort uint16, dstIP net.IP, dstPort uint16, seq, ack uint32) {
	if _, exists := d.conns.Load(key); exists {
		return
	}

	c := &connection{
		key: key, clientIP: srcIP, clientPort: srcPort,
		dstIP: dstIP, dstPort: dstPort,
		seq: atomic.AddUint32(&globalISN, 1), ack: seq + 1,
		lastSeen: time.Now(),
	}
	d.conns.Store(key, c)

	// SYNC-ACK back to client (generated by our userspace stack)
	d.sendPacket(dstIP, dstPort, srcIP, srcPort, c.seq, c.ack, synFlag|ackFlag, nil)

	// Establish real outbound connection from gateway kernel
	dst := net.JoinHostPort(dstIP.String(), fmt.Sprintf("%d", dstPort))
	upstream, err := net.DialTimeout("tcp", dst, 10*time.Second)
	if err != nil {
		d.logger.Printf("[tun] dial %s: %v", dst, err)
		d.conns.Delete(key)
		return
	}
	if err := markTCPConn(upstream); err != nil {
		d.logger.Printf("[tun] mark upstream: %v", err)
	}
	c.upstream = upstream
	d.wg.Add(1)
	go d.pumpUpstream(c)
}

var globalISN uint32 = 0x1a2b3c4d

func (d *Device) pumpUpstream(c *connection) {
	defer d.wg.Done()
	buf := make([]byte, 65535)
	for {
		n, err := c.upstream.Read(buf)
		if n > 0 {
			payload := append([]byte(nil), buf[:n]...)
			d.sendPacket(c.dstIP, c.dstPort, c.clientIP, c.clientPort,
				c.seq, c.ack, pshFlag|ackFlag, payload)
			c.seq += uint32(n)
		}
		if err != nil {
			break
		}
	}
	d.closeConnection(c, c.key)
}

func (d *Device) closeConnection(c *connection, key string) {
	if c.closed {
		return
	}
	c.closed = true
	d.conns.Delete(key)
	if c.upstream != nil {
		c.upstream.Close()
	}
	d.sendPacket(c.dstIP, c.dstPort, c.clientIP, c.clientPort,
		c.seq, c.ack+1, finFlag|ackFlag, nil)
}

func (d *Device) StartCleanup() {
	go func() {
		ticker := time.NewTicker(30 * time.Second)
		defer ticker.Stop()
		for {
			select {
			case <-d.quit:
				return
			case <-ticker.C:
				now := time.Now()
				d.conns.Range(func(k, v interface{}) bool {
					c := v.(*connection)
					if now.Sub(c.lastSeen) > 5*time.Minute {
						d.closeConnection(c, c.key)
					}
					return true
				})
				d.recentOutbound.Range(func(k, v interface{}) bool {
					ts, ok := v.(int64)
					if !ok || time.Since(time.Unix(0, ts)) > 30*time.Second {
						d.recentOutbound.Delete(k)
					}
					return true
				})
			}
		}
	}()
}

func SetupTUN(name, ip, cidr string) error {
	// ip link set <name> up
	if err := runCommand("ip", "link", "set", name, "up"); err != nil {
		return err
	}
	// ip addr add <ip>/24 dev <name>
	if err := runCommand("ip", "addr", "add", ip+"/24", "dev", name); err != nil {
		return err
	}
	return nil
}

func runCommand(name string, args ...string) error {
	cmd := exec.Command(name, args...)
	out, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("%s %v: %w: %s", name, args, err, string(out))
	}
	return nil
}
