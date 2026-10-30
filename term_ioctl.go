//go:build linux || darwin

package main

import (
	"os"
	"syscall"
	"unsafe"
)

func isTerminal(f *os.File) bool {
	var t syscall.Termios
	_, _, errno := syscall.Syscall6(syscall.SYS_IOCTL, f.Fd(), ioctlReadTermios, uintptr(unsafe.Pointer(&t)), 0, 0, 0)
	return errno == 0
}
