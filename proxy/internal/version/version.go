package version

import "fmt"

const (
	Major = 1
	Minor = 0
	Patch = 0
)

func String() string {
	return fmt.Sprintf("hotspot-proxy v%d.%d.%d", Major, Minor, Patch)
}
