//go:build windows

// O app "Claude Code" (abas) que vem junto no Windows x64: o Claude Code numa janela própria,
// com uma aba por sessão, rodapé com modelo e esforço e a barra de uso embaixo.
// O app é o winapp/ (C#, compilado pelo winapp/build.cmd) e roda no Windows Terminal
// portátil oficial, que o instalador baixa do GitHub da Microsoft e confere pelo SHA-256.
package main

import (
	"archive/zip"
	"crypto/sha256"
	_ "embed"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime"
	"strings"
	"syscall"
	"time"
)

//go:embed winapp/bin/ClaudeCode.exe
var launcherExe []byte

//go:embed winapp/claude.ico
var launcherIcon []byte

//go:embed winapp/claude-fundo.hlsl
var launcherShader []byte

//go:embed winapp/wt-settings.json
var wtSettingsTemplate string

const (
	wtVersion = "1.25.2733.0"
	wtURL     = "https://github.com/microsoft/terminal/releases/download/v" + wtVersion +
		"/Microsoft.WindowsTerminal_" + wtVersion + "_x64.zip"
	wtSHA256     = "bf3ef2012f6c44d8340a4c58125acc9498d19b580f9890dc043cdf831852e796"
	launcherName = "Claude Code.exe"
)

func appSupported() bool { return runtime.GOARCH == "amd64" }

func launcherDir() string  { return filepath.Join(appDir(), "app") }
func launcherPath() string { return filepath.Join(launcherDir(), launcherName) }
func terminalDir() string  { return filepath.Join(launcherDir(), "terminal") }

// Pasta onde o app guarda o estado de cada janela; a barra grava ali o modelo e o esforço.
func launcherStateDir() string {
	base := os.Getenv("LOCALAPPDATA")
	if base == "" {
		return ""
	}
	return filepath.Join(base, "claude-launcher")
}

// runLauncher roda o app com um comando (--setup, --unsetup, --quit) e espera.
func runLauncher(arg string) error {
	cmd := exec.Command(launcherPath(), arg)
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	return cmd.Run()
}

func installApp(tr *texts, out io.Writer) error {
	dir := launcherDir()
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return err
	}
	if err := replaceFile(launcherPath(), launcherExe); err != nil {
		return err
	}
	// fecha o app que estiver aberto (as sessões continuam; o novo adota as janelas)
	_ = runLauncher("--quit")
	if err := replaceFile(filepath.Join(dir, "claude.ico"), launcherIcon); err != nil {
		return err
	}
	if err := replaceFile(filepath.Join(dir, "claude-fundo.hlsl"), launcherShader); err != nil {
		return err
	}
	fmt.Fprintf(out, "  ✓ "+tr.appCopied+"\n", launcherPath())

	if err := installTerminal(tr, out); err != nil {
		return err
	}
	if err := writeTerminalSettings(); err != nil {
		return err
	}
	if st := launcherStateDir(); st != "" {
		_ = os.MkdirAll(filepath.Join(st, "estado"), 0o755)
	}
	if err := runLauncher("--setup"); err == nil {
		fmt.Fprintln(out, "  ✓ "+tr.appShortcuts)
	}
	if _, err := exec.LookPath("claude"); err != nil {
		fmt.Fprintln(out, "  ! "+tr.appNoClaude)
		return nil
	}
	cmd := exec.Command(launcherPath())
	cmd.Dir = dir
	if err := cmd.Start(); err == nil {
		cmd.Process.Release()
		fmt.Fprintln(out, "  ✓ "+tr.appStarted)
	}
	return nil
}

// installTerminal baixa o Windows Terminal portátil (só se ainda não tiver esta versão).
func installTerminal(tr *texts, out io.Writer) error {
	term := terminalDir()
	if v, err := os.ReadFile(filepath.Join(term, "version.txt")); err == nil && strings.TrimSpace(string(v)) == wtVersion {
		if _, err := os.Stat(filepath.Join(term, "wt.exe")); err == nil {
			fmt.Fprintf(out, "  ✓ "+tr.appTerminalOk+"\n", wtVersion)
			return nil
		}
	}
	if err := checkAppNotRunning(tr); err != nil {
		return err
	}
	fmt.Fprintf(out, "  … "+tr.appDownloading+"\n", wtVersion)
	zipPath := filepath.Join(launcherDir(), "terminal-download.zip")
	defer os.Remove(zipPath)
	if err := download(wtURL, zipPath, wtSHA256); err != nil {
		return err
	}
	fresh := term + ".new"
	os.RemoveAll(fresh)
	if err := unzipStrip(zipPath, fresh); err != nil {
		os.RemoveAll(fresh)
		return err
	}
	// .portable = o Terminal guarda as configurações em terminal\settings, longe do Terminal do sistema
	_ = os.WriteFile(filepath.Join(fresh, ".portable"), nil, 0o644)
	_ = os.WriteFile(filepath.Join(fresh, "version.txt"), []byte(wtVersion+"\n"), 0o644)
	if _, err := os.Stat(term); err == nil {
		old := term + ".old"
		os.RemoveAll(old)
		if err := os.Rename(term, old); err != nil {
			os.RemoveAll(fresh)
			return errors.New(tr.appInUse)
		}
		// as configurações do usuário vão junto para a versão nova
		os.Rename(filepath.Join(old, "settings"), filepath.Join(fresh, "settings"))
		defer os.RemoveAll(old)
	}
	if err := os.Rename(fresh, term); err != nil {
		return err
	}
	fmt.Fprintf(out, "  ✓ "+tr.appTerminalOk+"\n", wtVersion)
	return nil
}

func download(url, dest, wantSHA string) error {
	client := &http.Client{Timeout: 10 * time.Minute}
	resp, err := client.Get(url)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("%s: HTTP %d", url, resp.StatusCode)
	}
	f, err := os.Create(dest)
	if err != nil {
		return err
	}
	h := sha256.New()
	_, err = io.Copy(io.MultiWriter(f, h), resp.Body)
	if cerr := f.Close(); err == nil {
		err = cerr
	}
	if err != nil {
		return err
	}
	if got := hex.EncodeToString(h.Sum(nil)); got != wantSHA {
		return fmt.Errorf("SHA-256 mismatch for %s (got %s)", url, got)
	}
	return nil
}

// unzipStrip extrai o zip tirando a primeira pasta (o zip do Terminal vem com "terminal-x.y.z\").
func unzipStrip(zipPath, dest string) error {
	r, err := zip.OpenReader(zipPath)
	if err != nil {
		return err
	}
	defer r.Close()
	root, _ := filepath.Abs(dest)
	for _, f := range r.File {
		name := strings.ReplaceAll(f.Name, `\`, "/")
		if i := strings.Index(name, "/"); i >= 0 {
			name = name[i+1:]
		} else {
			continue
		}
		if name == "" {
			continue
		}
		target := filepath.Join(root, filepath.FromSlash(name))
		if !strings.HasPrefix(target, root+string(os.PathSeparator)) {
			return fmt.Errorf("bad path in zip: %s", f.Name)
		}
		if f.FileInfo().IsDir() {
			if err := os.MkdirAll(target, 0o755); err != nil {
				return err
			}
			continue
		}
		if err := os.MkdirAll(filepath.Dir(target), 0o755); err != nil {
			return err
		}
		src, err := f.Open()
		if err != nil {
			return err
		}
		dst, err := os.Create(target)
		if err != nil {
			src.Close()
			return err
		}
		_, err = io.Copy(dst, src)
		src.Close()
		if cerr := dst.Close(); err == nil {
			err = cerr
		}
		if err != nil {
			return err
		}
	}
	return nil
}

// writeTerminalSettings grava as configurações do Terminal na primeira vez (depois são do usuário).
func writeTerminalSettings() error {
	path := filepath.Join(terminalDir(), "settings", "settings.json")
	if _, err := os.Stat(path); err == nil {
		return nil
	}
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	esc := func(s string) string { b, _ := json.Marshal(s); return string(b[1 : len(b)-1]) }
	s := strings.ReplaceAll(wtSettingsTemplate, "__APP__", esc(launcherDir()))
	s = strings.ReplaceAll(s, "__FONT__", esc(pickFont()))
	return os.WriteFile(path, []byte(s), 0o644)
}

// pickFont: JetBrains Mono se estiver instalada; senão Cascadia Mono, que vem com o Terminal.
func pickFont() string {
	dirs := []string{filepath.Join(os.Getenv("WINDIR"), "Fonts")}
	if l := os.Getenv("LOCALAPPDATA"); l != "" {
		dirs = append(dirs, filepath.Join(l, "Microsoft", "Windows", "Fonts"))
	}
	for _, d := range dirs {
		if m, _ := filepath.Glob(filepath.Join(d, "JetBrainsMono*")); len(m) > 0 {
			return "JetBrains Mono"
		}
	}
	return "Cascadia Mono"
}

// replaceFile grava um arquivo mesmo que o antigo seja um .exe aberto (renomeia o velho para .old).
func replaceFile(path string, data []byte) error {
	tmp := path + ".new"
	if err := os.WriteFile(tmp, data, 0o755); err != nil {
		return err
	}
	old := path + ".old"
	os.Remove(old)
	if _, err := os.Stat(path); err == nil {
		if err := os.Rename(path, old); err != nil {
			os.Remove(tmp)
			return err
		}
	}
	if err := os.Rename(tmp, path); err != nil {
		os.Rename(old, path)
		return err
	}
	os.Remove(old)
	return nil
}

// checkAppNotRunning: um .exe rodando não abre para escrita (renomear a pasta não serve:
// o Windows deixa renomear mesmo com o programa aberto).
func checkAppNotRunning(tr *texts) error {
	for _, exe := range []string{
		launcherPath(),
		filepath.Join(terminalDir(), "WindowsTerminal.exe"),
		filepath.Join(terminalDir(), "OpenConsole.exe"),
	} {
		if _, err := os.Stat(exe); err != nil {
			continue
		}
		f, err := os.OpenFile(exe, os.O_WRONLY, 0) // sem O_TRUNC: não muda nada no arquivo
		if err != nil {
			return errors.New(tr.appInUse)
		}
		f.Close()
	}
	return nil
}

func uninstallApp(tr *texts, out io.Writer) error {
	if _, err := os.Stat(launcherPath()); err != nil {
		return nil
	}
	if err := checkAppNotRunning(tr); err != nil {
		return err
	}
	// sem --quit aqui: com as janelas do app fechadas, o gerenciador dele já saiu sozinho
	// (e o --quit fecharia qualquer gerenciador aberto, até de outra cópia)
	_ = runLauncher("--unsetup")
	if st := launcherStateDir(); st != "" {
		os.RemoveAll(st)
	}
	fmt.Fprintln(out, "  ✓ "+tr.appRemoved)
	return nil
}

var unsafeID = regexp.MustCompile(`[^\w-]`)

// saveLauncherState grava o modelo e o esforço desta sessão para o rodapé do app.
// Só grava se o app já criou a pasta dele (quem não usa o app não ganha arquivo nenhum).
func saveLauncherState(in *Input) {
	st := launcherStateDir()
	if in == nil || in.Model == nil || in.SessionID == "" || st == "" {
		return
	}
	dir := filepath.Join(st, "estado")
	if _, err := os.Stat(dir); err != nil {
		return
	}
	// nomes em português: é o formato que o app lê
	b, err := json.Marshal(map[string]any{
		"modelo":    in.Model.DisplayName,
		"modelo_id": in.Model.ID,
		"esforco":   in.effortLevel(),
		"rapido":    in.FastMode,
		"t":         time.Now().UnixMilli(),
	})
	if err != nil {
		return
	}
	_ = writeFileAtomic(filepath.Join(dir, unsafeID.ReplaceAllString(in.SessionID, "")+".json"), b, 0o644)
}
