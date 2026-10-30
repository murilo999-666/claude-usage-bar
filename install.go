package main

import (
	"bufio"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"regexp"
	"runtime"
	"strings"
	"time"
)

func binName() string {
	if runtime.GOOS == "windows" {
		return appName + ".exe"
	}
	return appName
}

func settingsPath() string { return filepath.Join(configDir(), "settings.json") }
func previousPath() string { return filepath.Join(appDir(), "previous-statusline.json") }

type statusLineCfg struct {
	Type            string `json:"type"`
	Command         string `json:"command"`
	RefreshInterval int    `json:"refreshInterval,omitempty"`
}

// isOurs diz se o statusLine atual já é esta barra.
func isOurs(raw []byte) bool {
	var v struct {
		Command string `json:"command"`
	}
	return raw != nil && json.Unmarshal(raw, &v) == nil && strings.Contains(v.Command, appName)
}

var safeUnixPath = regexp.MustCompile(`^[A-Za-z0-9_./+:@%-]+$`)

// commandFor monta o comando que vai no settings.json.
// Sem aspas quando dá: assim funciona em bash, cmd e PowerShell.
func commandFor(exe string, args []string) string {
	var cmd string
	if runtime.GOOS == "windows" {
		const special = " &()^%!'\"`;,="
		p := filepath.ToSlash(exe)
		if strings.ContainsAny(p, special) {
			// Encurta só a pasta (8.3, sem espaços); o nome do arquivo fica
			// inteiro para o instalador continuar reconhecendo a barra.
			sp := filepath.ToSlash(shortPath(filepath.Dir(exe))) + "/" + filepath.Base(exe)
			if !strings.ContainsAny(sp, special) {
				p = sp
			} else {
				p = `"` + p + `"`
			}
		}
		cmd = p
	} else {
		cmd = shellQuote(exe)
	}
	for _, a := range args {
		cmd += " " + shellQuote(a)
	}
	return cmd
}

func shellQuote(s string) string {
	if safeUnixPath.MatchString(s) {
		return s
	}
	if runtime.GOOS == "windows" {
		return `"` + s + `"`
	}
	return "'" + strings.ReplaceAll(s, "'", `'\''`) + "'"
}

// copySelf põe este executável na pasta do app. No Windows não dá para
// sobrescrever um .exe em uso, mas dá para renomear: o velho vira .old.
func copySelf(target string) error {
	self, err := os.Executable()
	if err != nil {
		return err
	}
	if s, err := filepath.EvalSymlinks(self); err == nil {
		self = s
	}
	if samePath(self, target) {
		return nil
	}
	src, err := os.Open(self)
	if err != nil {
		return err
	}
	defer src.Close()
	tmp := target + ".new"
	dst, err := os.OpenFile(tmp, os.O_CREATE|os.O_TRUNC|os.O_WRONLY, 0o755)
	if err != nil {
		return err
	}
	if _, err := io.Copy(dst, src); err != nil {
		dst.Close()
		os.Remove(tmp)
		return err
	}
	if err := dst.Close(); err != nil {
		os.Remove(tmp)
		return err
	}
	old := target + ".old"
	os.Remove(old)
	if _, err := os.Stat(target); err == nil && runtime.GOOS == "windows" {
		if err := os.Rename(target, old); err != nil {
			os.Remove(tmp)
			return err
		}
	}
	if err := os.Rename(tmp, target); err != nil {
		os.Rename(old, target)
		return err
	}
	os.Remove(old) // se o velho ainda estiver rodando, fica para a próxima
	return nil
}

func samePath(a, b string) bool {
	a, _ = filepath.Abs(a)
	b, _ = filepath.Abs(b)
	if runtime.GOOS == "windows" {
		return strings.EqualFold(a, b)
	}
	return a == b
}

// writeSettings grava no arquivo de verdade (se o settings.json for um link, segue o link).
func writeSettings(path string, data []byte) error {
	if real, err := filepath.EvalSymlinks(path); err == nil {
		path = real
	}
	perm := os.FileMode(0o644)
	if st, err := os.Stat(path); err == nil {
		perm = st.Mode().Perm()
	}
	return writeFileAtomic(path, data, perm)
}

func backupSettings(orig []byte) string {
	if len(orig) == 0 {
		return ""
	}
	bak := settingsPath() + ".bak-" + appName
	if _, err := os.Stat(bak); err == nil {
		return "" // já existe o backup de antes da primeira instalação
	}
	if os.WriteFile(bak, orig, 0o600) != nil {
		return ""
	}
	return bak
}

func readSettings() ([]byte, error) {
	b, err := os.ReadFile(settingsPath())
	if errors.Is(err, os.ErrNotExist) {
		return nil, nil
	}
	return b, err
}

func install(opts options, tr *texts, out io.Writer) error {
	orig, err := readSettings()
	if err != nil {
		return fmt.Errorf(tr.errRead, settingsPath(), err)
	}
	current, err := getMember(orig, "statusLine")
	if err != nil {
		return fmt.Errorf(tr.errInvalid, settingsPath())
	}

	dir := appDir()
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return err
	}
	target := filepath.Join(dir, binName())
	if err := copySelf(target); err != nil {
		return fmt.Errorf(tr.errCopy, target, err)
	}
	fmt.Fprintf(out, "  ✓ "+tr.okCopied+"\n", target)

	if !isOurs(current) {
		prev := []byte("null")
		if current != nil {
			prev = current
		}
		_ = os.WriteFile(previousPath(), prev, 0o600)
		if current != nil {
			fmt.Fprintln(out, "  ✓ "+tr.okSavedOld)
		}
	}

	cfg := statusLineCfg{Type: "command", Command: commandFor(target, opts.renderArgs), RefreshInterval: opts.refresh}
	val, err := marshalNoEscape(cfg)
	if err != nil {
		return err
	}
	updated, err := setMember(orig, "statusLine", val)
	if err != nil {
		return fmt.Errorf(tr.errInvalid, settingsPath())
	}
	if bak := backupSettings(orig); bak != "" {
		fmt.Fprintf(out, "  ✓ "+tr.okBackup+"\n", bak)
	}
	if err := writeSettings(settingsPath(), updated); err != nil {
		return fmt.Errorf(tr.errWrite, settingsPath(), err)
	}
	fmt.Fprintf(out, "  ✓ "+tr.okConfigured+"\n", settingsPath())
	return nil
}

func uninstall(tr *texts, out io.Writer) error {
	orig, err := readSettings()
	if err != nil {
		return fmt.Errorf(tr.errRead, settingsPath(), err)
	}
	current, err := getMember(orig, "statusLine")
	if err != nil {
		return fmt.Errorf(tr.errInvalid, settingsPath())
	}
	if isOurs(current) {
		var updated []byte
		restored := false
		prev, perr := os.ReadFile(previousPath())
		if perr == nil && json.Valid(prev) && strings.TrimSpace(string(prev)) != "null" {
			updated, err = setMember(orig, "statusLine", prev)
			restored = true
		} else {
			updated, err = removeMember(orig, "statusLine")
		}
		if err != nil {
			return err
		}
		if err := writeSettings(settingsPath(), updated); err != nil {
			return fmt.Errorf(tr.errWrite, settingsPath(), err)
		}
		fmt.Fprintf(out, "  ✓ "+tr.okRemoved+"\n", settingsPath())
		if restored {
			fmt.Fprintln(out, "  ✓ "+tr.okRestored)
		}
	} else {
		fmt.Fprintf(out, "  · "+tr.notInstalled+"\n", settingsPath())
	}
	if _, err := os.Stat(appDir()); err == nil {
		if err := removeAppDirLater(appDir()); err == nil {
			fmt.Fprintf(out, "  ✓ "+tr.okDeleted+"\n", appDir())
		}
	}
	return nil
}

func header(tr *texts) {
	p := detectPainter(false)
	fmt.Println()
	fmt.Println("  " + p.bold() + appName + p.reset() + " " + version + p.fg(colMuted) + "  ·  " + tr.tagline + p.reset())
	fmt.Println()
}

// pauseIfDoubleClicked segura a janela aberta quando o programa foi aberto com dois cliques.
func pauseIfDoubleClicked(tr *texts) {
	if consoleProcessCount() == 1 {
		fmt.Print("\n  " + tr.pressEnter)
		bufio.NewReader(os.Stdin).ReadString('\n')
	}
}

func runInstall(opts options, tr *texts) int {
	enableVT()
	header(tr)
	defer pauseIfDoubleClicked(tr)
	if err := install(opts, tr, os.Stdout); err != nil {
		fmt.Println("\n  ✗ " + err.Error())
		fmt.Println("  " + tr.nothingChanged)
		return 1
	}

	p := detectPainter(opts.plain)
	cache := readCache()
	if !opts.noFetch {
		cache = fetchUsage(10 * time.Second)
	}
	fmt.Println()
	if cache.FiveHour != nil || cache.SevenDay != nil {
		fmt.Println("  " + tr.previewTitle)
		fmt.Println()
		fmt.Println("    " + renderLine(nil, cache, time.Now(), opts, tr, p))
	} else {
		fmt.Println("  " + tr.exampleTitle)
		fmt.Println()
		fmt.Println("    " + exampleLine(opts, tr, p))
	}
	fmt.Println()
	if cache.Error != "" && !opts.noFetch {
		fmt.Println("  " + tr.fetchHint(cache.Error))
		fmt.Println()
	}
	fmt.Println("  " + p.fg(colMuted) + tr.legend + p.reset())
	fmt.Println()
	fmt.Println("  " + p.bold() + tr.done + p.reset())
	fmt.Println("  " + p.fg(colMuted) + fmt.Sprintf(tr.howToRemove, filepath.Join(appDir(), binName())) + p.reset())
	return 0
}

func runUninstall(_ options, tr *texts) int {
	enableVT()
	header(tr)
	defer pauseIfDoubleClicked(tr)
	if err := uninstall(tr, os.Stdout); err != nil {
		fmt.Println("\n  ✗ " + err.Error())
		return 1
	}
	fmt.Println()
	fmt.Println("  " + tr.doneUninstall)
	return 0
}
