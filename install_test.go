package main

import (
	"io"
	"os"
	"path/filepath"
	"testing"
)

// Instala e desinstala numa pasta de teste: o settings.json tem que voltar idêntico.
func TestInstallUninstallRoundTrip(t *testing.T) {
	dir := t.TempDir()
	t.Setenv("CLAUDE_CONFIG_DIR", dir)
	settings := filepath.Join(dir, "settings.json")
	orig := "{\n  \"model\": \"opus\",\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": \"echo hi\"\n  },\n  \"env\": {}\n}\n"
	if err := os.WriteFile(settings, []byte(orig), 0o644); err != nil {
		t.Fatal(err)
	}
	opts, _ := parseFlags([]string{"--no-fetch", "--lang", "en"}, io.Discard)

	for i := 0; i < 2; i++ { // a segunda instalação não pode guardar a si mesma como "barra antiga"
		if err := install(opts, textsEN, io.Discard); err != nil {
			t.Fatal(err)
		}
	}
	b, _ := os.ReadFile(settings)
	raw, _ := getMember(b, "statusLine")
	if !isOurs(raw) {
		t.Fatalf("statusLine not installed:\n%s", b)
	}
	if _, err := os.Stat(filepath.Join(dir, appName, binName())); err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(settings + ".bak-" + appName); err != nil {
		t.Fatal("no backup:", err)
	}

	if err := uninstall(textsEN, io.Discard); err != nil {
		t.Fatal(err)
	}
	b, _ = os.ReadFile(settings)
	if string(b) != orig {
		t.Fatalf("settings not restored:\n%s", b)
	}
	if _, err := os.Stat(filepath.Join(dir, appName)); !os.IsNotExist(err) {
		t.Fatal("app folder still there")
	}
}

func TestInstallWithoutSettingsFile(t *testing.T) {
	dir := t.TempDir()
	t.Setenv("CLAUDE_CONFIG_DIR", dir)
	opts, _ := parseFlags([]string{"--no-fetch"}, io.Discard)
	if err := install(opts, textsEN, io.Discard); err != nil {
		t.Fatal(err)
	}
	if err := uninstall(textsEN, io.Discard); err != nil {
		t.Fatal(err)
	}
	b, _ := os.ReadFile(filepath.Join(dir, "settings.json"))
	if string(b) != "{}\n" {
		t.Fatalf("got %q", b)
	}
}

func TestInstallRefusesBrokenSettings(t *testing.T) {
	dir := t.TempDir()
	t.Setenv("CLAUDE_CONFIG_DIR", dir)
	broken := "{\n  \"model\": \"opus\",\n}\n"
	settings := filepath.Join(dir, "settings.json")
	os.WriteFile(settings, []byte(broken), 0o644)
	opts, _ := parseFlags(nil, io.Discard)
	if err := install(opts, textsEN, io.Discard); err == nil {
		t.Fatal("expected an error")
	}
	if b, _ := os.ReadFile(settings); string(b) != broken {
		t.Fatal("broken settings were modified")
	}
}
