package main

import (
	"fmt"
	"os"
	"runtime"
	"strings"
	"time"
)

type texts struct {
	session, week, context, loading, fast, stale string
	minSuffix                                    string
	days                                         [7]string
	today, tomorrow                              string
	effort                                       map[string]string
	clock                                        func(time.Time) string

	tagline, okCopied, okSavedOld, okBackup, okConfigured           string
	okRemoved, okRestored, okDeleted, notInstalled                  string
	errCopy, errRead, errInvalid, errWrite, nothingChanged          string
	previewTitle, exampleTitle, legend, done, howToRemove           string
	doneUninstall                                                   string
	pressEnter, unknownCmd                                          string
	hintNoLogin, hintExpired, hintRateLimited, hintNetwork, hintAny string
	helpText                                                        string
}

func (t *texts) help(v string) string {
	return fmt.Sprintf(t.helpText, appName, v, appName, appName, appName, appName, appName)
}

func (t *texts) fetchHint(err string) string {
	switch {
	case strings.Contains(err, "no credentials"):
		return t.hintNoLogin
	case strings.Contains(err, "HTTP 401"), strings.Contains(err, "HTTP 403"):
		return t.hintExpired
	case strings.Contains(err, "HTTP 429"):
		return t.hintRateLimited
	case strings.Contains(err, "network"):
		return t.hintNetwork
	}
	return fmt.Sprintf(t.hintAny, err)
}

var textsPT = &texts{
	session: "sessão", week: "semana", context: "contexto", loading: "carregando uso…",
	fast: "rápido", stale: "⚠ sem atualizar há %d min", minSuffix: "min",
	days:  [7]string{"dom", "seg", "ter", "qua", "qui", "sex", "sáb"},
	today: "hoje", tomorrow: "amanhã",
	effort: map[string]string{"low": "baixo", "medium": "médio", "high": "alto", "xhigh": "extra", "max": "máximo"},
	clock: func(d time.Time) string {
		if d.Minute() == 0 {
			return fmt.Sprintf("%dh", d.Hour())
		}
		return fmt.Sprintf("%dh%02d", d.Hour(), d.Minute())
	},

	tagline:         "barra de uso para o Claude Code",
	okCopied:        "Programa instalado em %s",
	okSavedOld:      "Sua barra antiga foi guardada (o uninstall traz ela de volta)",
	okBackup:        "Cópia de segurança das suas configurações: %s",
	okConfigured:    "Barra ligada em %s",
	okRemoved:       "Barra desligada em %s",
	okRestored:      "Sua barra anterior voltou",
	okDeleted:       "Pasta do programa apagada: %s",
	notInstalled:    "A barra não estava ligada em %s",
	errCopy:         "Não consegui copiar o programa para %s: %v",
	errRead:         "Não consegui ler %s: %v",
	errInvalid:      "O arquivo %s tem um erro de JSON. Conserte ou apague a linha com problema e rode de novo.",
	errWrite:        "Não consegui gravar %s: %v",
	nothingChanged:  "Nada foi alterado nas suas configurações.",
	previewTitle:    "Assim vai ficar (com o seu uso de agora):",
	exampleTitle:    "Assim vai ficar (exemplo):",
	legend:          "A marquinha branca na barra é o ritmo: quanto da janela já passou. Se a cor passou dela, você está gastando mais rápido do que dá pra durar até renovar.",
	done:            "Pronto! Abra (ou reinicie) o Claude Code para ver a barra lá embaixo.",
	howToRemove:     "Para desinstalar: \"%s\" uninstall",
	doneUninstall:   "Pronto! Reinicie o Claude Code.",
	pressEnter:      "Aperte Enter para fechar…",
	unknownCmd:      "Comando desconhecido: %s",
	hintNoLogin:     "Ainda não achei o login do Claude Code. Abra o Claude Code e entre na conta; a barra aparece assim que ele mandar os primeiros números.",
	hintExpired:     "O login do Claude Code expirou. É só abrir o Claude Code que ele renova sozinho.",
	hintRateLimited: "O servidor pediu para esperar um pouco. A barra tenta de novo sozinha em 5 minutos.",
	hintNetwork:     "Sem internet agora? A barra tenta de novo sozinha a cada minuto.",
	hintAny:         "Não consegui buscar o uso agora (%s). A barra tenta de novo sozinha.",
	helpText: `%s %s — barra de uso para o Claude Code

Como usar:
  %s              instala (com dois cliques ou no terminal)
  %s install      instala ou atualiza
  %s uninstall    desliga a barra e traz a anterior de volta
  %s preview      mostra a barra com o seu uso de agora
  %s version      mostra a versão

Opções (servem para o install e ficam gravadas no comando da barra):
  --lang pt|en       idioma (padrão: o do sistema)
  --no-fetch         não consulta o uso da conta; usa só o que o Claude Code envia
  --bar-width N      largura da barra em células (padrão 10)
  --plain            sem cores
  --hide LISTA       esconde partes: session,week,context,model,effort
  --refresh N        atualiza a barra a cada N segundos (padrão 30)
`,
}

var textsEN = &texts{
	session: "session", week: "week", context: "context", loading: "loading usage…",
	fast: "fast", stale: "⚠ not updated for %d min", minSuffix: "m",
	days:  [7]string{"Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"},
	today: "today", tomorrow: "tomorrow",
	effort: map[string]string{"low": "low", "medium": "medium", "high": "high", "xhigh": "xhigh", "max": "max"},
	clock: func(d time.Time) string {
		h := d.Hour() % 12
		if h == 0 {
			h = 12
		}
		ampm := "am"
		if d.Hour() >= 12 {
			ampm = "pm"
		}
		if d.Minute() == 0 {
			return fmt.Sprintf("%d%s", h, ampm)
		}
		return fmt.Sprintf("%d:%02d%s", h, d.Minute(), ampm)
	},

	tagline:         "usage bar for Claude Code",
	okCopied:        "Installed to %s",
	okSavedOld:      "Your old status line was saved (uninstall brings it back)",
	okBackup:        "Backup of your settings: %s",
	okConfigured:    "Status line turned on in %s",
	okRemoved:       "Status line turned off in %s",
	okRestored:      "Your previous status line is back",
	okDeleted:       "Program folder deleted: %s",
	notInstalled:    "The bar was not turned on in %s",
	errCopy:         "Could not copy the program to %s: %v",
	errRead:         "Could not read %s: %v",
	errInvalid:      "%s has a JSON error. Fix or delete the broken line and run this again.",
	errWrite:        "Could not write %s: %v",
	nothingChanged:  "Nothing was changed in your settings.",
	previewTitle:    "This is how it will look (with your usage right now):",
	exampleTitle:    "This is how it will look (example):",
	legend:          "The thin white mark is the pace: how much of the window has passed. If the color goes past it, you are spending faster than the window lasts.",
	done:            "Done! Open (or restart) Claude Code to see the bar at the bottom.",
	howToRemove:     "To uninstall: \"%s\" uninstall",
	doneUninstall:   "Done! Restart Claude Code.",
	pressEnter:      "Press Enter to close…",
	unknownCmd:      "Unknown command: %s",
	hintNoLogin:     "I could not find your Claude Code login yet. Open Claude Code and sign in; the bar shows up as soon as it sends the first numbers.",
	hintExpired:     "Your Claude Code login expired. Just open Claude Code and it renews it by itself.",
	hintRateLimited: "The server asked to wait a bit. The bar tries again by itself in 5 minutes.",
	hintNetwork:     "No internet right now? The bar tries again by itself every minute.",
	hintAny:         "Could not fetch usage right now (%s). The bar tries again by itself.",
	helpText: `%s %s — usage bar for Claude Code

Usage:
  %s              install (double-click it or run it in a terminal)
  %s install      install or update
  %s uninstall    turn the bar off and bring your previous one back
  %s preview      show the bar with your current usage
  %s version      print the version

Options (for install; they are saved in the status line command):
  --lang en|pt       language (default: system language)
  --no-fetch         don't query account usage; only use what Claude Code sends
  --bar-width N      bar width in cells (default 10)
  --plain            no colors
  --hide LIST        hide parts: session,week,context,model,effort
  --refresh N        refresh the bar every N seconds (default 30)
`,
}

// pickTexts escolhe o idioma: --lang, CLAUDE_USAGE_BAR_LANG, o do sistema, ou inglês.
func pickTexts(flagLang string) *texts {
	candidates := []string{flagLang, os.Getenv("CLAUDE_USAGE_BAR_LANG")}
	if runtime.GOOS == "windows" {
		candidates = append(candidates, osLocale())
	}
	candidates = append(candidates, os.Getenv("LC_ALL"), os.Getenv("LC_MESSAGES"), os.Getenv("LANG"))
	for _, c := range candidates {
		c = strings.ToLower(strings.TrimSpace(c))
		if c == "" || c == "c" || c == "posix" || strings.HasPrefix(c, "c.") {
			continue
		}
		if strings.HasPrefix(c, "pt") {
			return textsPT
		}
		return textsEN
	}
	return textsEN
}
