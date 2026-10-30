//go:build windows

package main

import (
	"os"
	"os/exec"
	"syscall"
	"unsafe"
)

var (
	kernel32                     = syscall.NewLazyDLL("kernel32.dll")
	procGetConsoleProcessList    = kernel32.NewProc("GetConsoleProcessList")
	procGetUserDefaultLocaleName = kernel32.NewProc("GetUserDefaultLocaleName")
	procGetConsoleMode           = kernel32.NewProc("GetConsoleMode")
	procSetConsoleMode           = kernel32.NewProc("SetConsoleMode")
)

const (
	createNewProcessGroup = 0x00000200
	createNoWindow        = 0x08000000
	enableVTProcessing    = 0x0004
)

// detach: a busca roda sem janela e sem segurar o Claude Code.
func detach(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{CreationFlags: createNoWindow | createNewProcessGroup, HideWindow: true}
}

func consoleMode(h syscall.Handle) (uint32, bool) {
	var mode uint32
	r, _, _ := procGetConsoleMode.Call(uintptr(h), uintptr(unsafe.Pointer(&mode)))
	return mode, r != 0
}

func isTerminal(f *os.File) bool {
	_, ok := consoleMode(syscall.Handle(f.Fd()))
	return ok
}

// consoleProcessCount = 1 quando o programa foi aberto com dois cliques
// (a janela é só dele); no terminal tem pelo menos o shell junto.
func consoleProcessCount() int {
	var pids [8]uint32
	n, _, _ := procGetConsoleProcessList.Call(uintptr(unsafe.Pointer(&pids[0])), uintptr(len(pids)))
	return int(n)
}

func osLocale() string {
	buf := make([]uint16, 85)
	r, _, _ := procGetUserDefaultLocaleName.Call(uintptr(unsafe.Pointer(&buf[0])), uintptr(len(buf)))
	if r == 0 {
		return ""
	}
	return syscall.UTF16ToString(buf)
}

// enableVT liga as cores ANSI no console antigo do Windows.
func enableVT() {
	h := syscall.Handle(os.Stdout.Fd())
	if mode, ok := consoleMode(h); ok {
		procSetConsoleMode.Call(uintptr(h), uintptr(mode|enableVTProcessing))
	}
}

// shortPath devolve o nome curto 8.3 (sem espaços), se o disco tiver.
func shortPath(p string) string {
	u, err := syscall.UTF16PtrFromString(p)
	if err != nil {
		return p
	}
	buf := make([]uint16, 1024)
	n, err := syscall.GetShortPathName(u, &buf[0], uint32(len(buf)))
	if err != nil || n == 0 || int(n) > len(buf) {
		return p
	}
	return syscall.UTF16ToString(buf[:n])
}

// removeAppDirLater apaga a pasta. Se o próprio .exe estiver rodando lá
// dentro, pede para o cmd apagar dois segundos depois que ele fechar.
func removeAppDirLater(dir string) error {
	if err := os.RemoveAll(dir); err == nil {
		return nil
	}
	cmd := exec.Command("cmd.exe")
	cmd.SysProcAttr = &syscall.SysProcAttr{
		CmdLine:       `cmd.exe /c ping -n 3 127.0.0.1 >nul & rmdir /s /q "` + dir + `"`,
		CreationFlags: createNoWindow | createNewProcessGroup,
		HideWindow:    true,
	}
	if err := cmd.Start(); err != nil {
		return err
	}
	return cmd.Process.Release()
}
