package main

import (
	"fmt"
	"github.com/jinghaihan/fee-save-editor/internal/app"
	"os"
)

func main() {
	path := ""
	if len(os.Args) > 1 {
		path = os.Args[1]
	}
	if err := app.Run(path); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
