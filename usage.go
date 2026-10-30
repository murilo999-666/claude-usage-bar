package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"time"
)

// O mesmo endpoint que o /usage do Claude Code usa. Soma o uso da conta
// inteira: site, app, celular e todos os terminais.
const usageURL = "https://api.anthropic.com/api/oauth/usage"

const (
	fetchEveryS      = 60  // intervalo mínimo entre buscas (somando todas as janelas)
	retryAfterErrorS = 60  // depois de erro comum (sem rede, timeout)
	retryAfter429S   = 300 // depois de "muitas requisições"
	lockMaxS         = 30
	fetchTimeout     = 15 * time.Second
)

type Window struct {
	Pct      float64 `json:"pct"`
	ResetsAt float64 `json:"resetsAt,omitempty"` // segundos Unix; 0 = sem data
}

type Cache struct {
	FiveHour    *Window `json:"five_hour,omitempty"`
	SevenDay    *Window `json:"seven_day,omitempty"`
	FetchedAt   float64 `json:"fetchedAt,omitempty"`
	AttemptedAt float64 `json:"attemptedAt,omitempty"`
	Error       string  `json:"error,omitempty"`
	ErrorAt     float64 `json:"errorAt,omitempty"`
}

func configDir() string {
	if d := os.Getenv("CLAUDE_CONFIG_DIR"); d != "" {
		if strings.HasPrefix(d, "~/") || strings.HasPrefix(d, `~\`) {
			if home, err := os.UserHomeDir(); err == nil {
				d = filepath.Join(home, d[2:])
			}
		}
		return d
	}
	home, err := os.UserHomeDir()
	if err != nil {
		home = "."
	}
	return filepath.Join(home, ".claude")
}

func appDir() string    { return filepath.Join(configDir(), appName) }
func cachePath() string { return filepath.Join(appDir(), "usage.json") }
func lockPath() string  { return filepath.Join(appDir(), "usage.lock") }

func readCache() *Cache {
	c := &Cache{}
	if b, err := os.ReadFile(cachePath()); err == nil {
		_ = json.Unmarshal(b, c)
	}
	return c
}

func writeFileAtomic(path string, data []byte, perm os.FileMode) error {
	tmp := fmt.Sprintf("%s.%d.tmp", path, os.Getpid())
	if err := os.WriteFile(tmp, data, perm); err != nil {
		return err
	}
	if err := os.Rename(tmp, path); err != nil {
		os.Remove(tmp)
		return err
	}
	return nil
}

func writeCache(c *Cache) {
	b, err := json.Marshal(c)
	if err != nil {
		return
	}
	_ = os.MkdirAll(appDir(), 0o755)
	_ = writeFileAtomic(cachePath(), b, 0o600)
}

// ---------- token do login do Claude Code ----------

func tokenFrom(b []byte) string {
	var v struct {
		ClaudeAiOauth struct {
			AccessToken string `json:"accessToken"`
		} `json:"claudeAiOauth"`
	}
	if json.Unmarshal(b, &v) != nil {
		return ""
	}
	return v.ClaudeAiOauth.AccessToken
}

// readToken pega o token que o próprio Claude Code salvou ao fazer login.
// Ele só é enviado para api.anthropic.com.
func readToken() (string, error) {
	if b, err := os.ReadFile(filepath.Join(configDir(), ".credentials.json")); err == nil {
		if t := tokenFrom(b); t != "" {
			return t, nil
		}
	}
	if runtime.GOOS == "darwin" { // no macOS o login fica no Keychain
		out, err := exec.Command("security", "find-generic-password", "-s", "Claude Code-credentials", "-w").Output()
		if err == nil {
			if t := tokenFrom(bytes.TrimSpace(out)); t != "" {
				return t, nil
			}
		}
	}
	if t := os.Getenv("CLAUDE_CODE_OAUTH_TOKEN"); t != "" {
		return t, nil
	}
	return "", errors.New("no credentials")
}

// ---------- busca ----------

type apiWindow struct {
	Utilization *float64 `json:"utilization"`
	ResetsAt    flexTime `json:"resets_at"`
}

func (w *apiWindow) toWindow() *Window {
	if w == nil || w.Utilization == nil {
		return nil
	}
	return &Window{Pct: *w.Utilization, ResetsAt: float64(w.ResetsAt)}
}

func requestUsage(timeout time.Duration) (*Window, *Window, error) {
	token, err := readToken()
	if err != nil {
		return nil, nil, err
	}
	req, err := http.NewRequest("GET", usageURL, nil)
	if err != nil {
		return nil, nil, err
	}
	req.Header.Set("Authorization", "Bearer "+token)
	req.Header.Set("anthropic-beta", "oauth-2025-04-20")
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("User-Agent", appName+"/"+version+" (+"+repoURL+")")
	resp, err := (&http.Client{Timeout: timeout}).Do(req)
	if err != nil {
		return nil, nil, errors.New("network error")
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return nil, nil, fmt.Errorf("HTTP %d", resp.StatusCode)
	}
	var body struct {
		FiveHour *apiWindow `json:"five_hour"`
		SevenDay *apiWindow `json:"seven_day"`
	}
	if err := json.NewDecoder(io.LimitReader(resp.Body, 1<<20)).Decode(&body); err != nil {
		return nil, nil, errors.New("bad response")
	}
	return body.FiveHour.toWindow(), body.SevenDay.toWindow(), nil
}

// fetchUsage busca o uso agora e atualiza o cache. Em caso de erro mantém o último número bom.
func fetchUsage(timeout time.Duration) *Cache {
	defer os.Remove(lockPath())
	c := readCache()
	t := unixF(time.Now())
	c.AttemptedAt = t
	five, seven, err := requestUsage(timeout)
	if err != nil {
		c.Error = err.Error()
		c.ErrorAt = t
	} else {
		c.FiveHour, c.SevenDay = five, seven
		c.FetchedAt = t
		c.Error, c.ErrorAt = "", 0
	}
	writeCache(c)
	return c
}

// shouldFetch decide se já está na hora de buscar de novo.
func shouldFetch(c *Cache, t float64) bool {
	if t-c.FetchedAt < fetchEveryS {
		return false
	}
	if c.Error != "" {
		wait := float64(retryAfterErrorS)
		if strings.Contains(c.Error, "429") {
			wait = retryAfter429S
		}
		if t-c.ErrorAt < wait {
			return false
		}
	}
	return true
}

// takeLock garante que só uma janela do Claude Code busca por vez.
func takeLock(t float64) bool {
	_ = os.MkdirAll(appDir(), 0o755)
	for i := 0; i < 2; i++ {
		f, err := os.OpenFile(lockPath(), os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0o600)
		if err == nil {
			fmt.Fprint(f, os.Getpid())
			f.Close()
			return true
		}
		st, serr := os.Stat(lockPath())
		if serr != nil || t-unixF(st.ModTime()) < lockMaxS {
			return false
		}
		os.Remove(lockPath()) // trava velha de uma busca que morreu
	}
	return false
}

// maybeStartFetch dispara a busca em segundo plano, sem atrasar a barra.
func maybeStartFetch(c *Cache, now time.Time) {
	t := unixF(now)
	if !shouldFetch(c, t) || !takeLock(t) {
		return
	}
	self, err := os.Executable()
	if err != nil {
		os.Remove(lockPath())
		return
	}
	cmd := exec.Command(self, "--fetch")
	detach(cmd)
	if err := cmd.Start(); err != nil {
		os.Remove(lockPath())
		return
	}
	_ = cmd.Process.Release()
}
