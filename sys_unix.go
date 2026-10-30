//go:build !windows

package main

import (
	"os"
	"os/exec"
	"syscall"
)

// detach: a busca roda numa sessão própria, sem segurar o Claude Code.
func detach(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{Setsid: true}
}

func consoleProcessCount() int { return 0 }

func osLocale() string { return "" }

func enableVT() {}

func shortPath(p string) string { return p }

func removeAppDirLater(dir string) error { return os.RemoveAll(dir) }
