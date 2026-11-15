package fee

import (
	_ "embed"
	"strings"
)

//go:embed VERSION
var version string

// Version returns the version recorded in the source tree when it was built.
func Version() string { return strings.TrimSpace(version) }
