//go:build windows

package main

import (
	"archive/zip"
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// O zip do Terminal vem com uma pasta "terminal-x.y.z\" na raiz: ela some na extração.
func TestUnzipStrip(t *testing.T) {
	dir := t.TempDir()
	zp := filepath.Join(dir, "t.zip")
	f, _ := os.Create(zp)
	w := zip.NewWriter(f)
	for name, body := range map[string]string{"terminal-1.0/wt.exe": "x", "terminal-1.0/sub/a.txt": "y"} {
		fw, _ := w.Create(name)
		fw.Write([]byte(body))
	}
	w.Close()
	f.Close()
	out := filepath.Join(dir, "out")
	if err := unzipStrip(zp, out); err != nil {
		t.Fatal(err)
	}
	for _, p := range []string{"wt.exe", filepath.Join("sub", "a.txt")} {
		if _, err := os.Stat(filepath.Join(out, p)); err != nil {
			t.Fatal(err)
		}
	}
}

func TestUnzipRejectsEscape(t *testing.T) {
	dir := t.TempDir()
	zp := filepath.Join(dir, "t.zip")
	f, _ := os.Create(zp)
	w := zip.NewWriter(f)
	fw, _ := w.Create("terminal-1.0/../../evil.txt")
	fw.Write([]byte("x"))
	w.Close()
	f.Close()
	if err := unzipStrip(zp, filepath.Join(dir, "out")); err == nil {
		t.Fatal("zip with ../ was extracted")
	}
}

// As configurações do Terminal saem como JSON válido, com o caminho do app (que tem \) escapado.
func TestTerminalSettings(t *testing.T) {
	dir := t.TempDir()
	t.Setenv("CLAUDE_CONFIG_DIR", dir)
	if err := writeTerminalSettings(); err != nil {
		t.Fatal(err)
	}
	b, err := os.ReadFile(filepath.Join(terminalDir(), "settings", "settings.json"))
	if err != nil {
		t.Fatal(err)
	}
	var v struct {
		Profiles struct {
			List []struct {
				Name   string `json:"name"`
				Shader string `json:"experimental.pixelShaderPath"`
				Font   struct {
					Face string `json:"face"`
				} `json:"font"`
			} `json:"list"`
		} `json:"profiles"`
	}
	if err := json.Unmarshal(b, &v); err != nil {
		t.Fatalf("invalid JSON: %v", err)
	}
	p := v.Profiles.List[0]
	if p.Name != "Claude" || p.Shader != filepath.Join(launcherDir(), "claude-fundo.hlsl") || p.Font.Face == "" || strings.Contains(string(b), "__") {
		t.Fatalf("unexpected profile: %+v", p)
	}
}

// A barra só grava o estado da sessão se o app já criou a pasta dele.
func TestSaveLauncherState(t *testing.T) {
	dir := t.TempDir()
	t.Setenv("LOCALAPPDATA", dir)
	var in Input
	json.Unmarshal([]byte(`{"session_id":"ab/c-1","model":{"id":"claude-opus-5-5","display_name":"Opus 5.5"},"effort":{"level":"high"}}`), &in)
	saveLauncherState(&in)
	if _, err := os.Stat(filepath.Join(dir, "claude-launcher")); !os.IsNotExist(err) {
		t.Fatal("state written without the app folder")
	}
	os.MkdirAll(filepath.Join(dir, "claude-launcher", "estado"), 0o755)
	saveLauncherState(&in)
	b, err := os.ReadFile(filepath.Join(dir, "claude-launcher", "estado", "abc-1.json"))
	if err != nil {
		t.Fatal(err)
	}
	var st map[string]any
	json.Unmarshal(b, &st)
	if st["modelo"] != "Opus 5.5" || st["esforco"] != "high" {
		t.Fatalf("unexpected state: %s", b)
	}
}
