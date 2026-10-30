// claude-usage-bar: barra de status do Claude Code com o uso da sessão (5 h)
// e da semana (7 dias), o ritmo de gasto, o contexto e o modelo.
//
// O mesmo executável faz tudo:
//   - chamado pelo Claude Code (JSON no stdin): desenha a barra;
//   - aberto com dois cliques ou no terminal: se instala;
//   - "--fetch" (uso interno): atualiza o cache de uso em segundo plano.
package main

import (
	"flag"
	"fmt"
	"io"
	"os"
	"strconv"
	"strings"
)

var version = "dev"

const (
	appName = "claude-usage-bar"
	repoURL = "https://github.com/murilo999-666/claude-usage-bar"
)

type options struct {
	lang     string
	noFetch  bool
	barWidth int
	plain    bool
	hide     map[string]bool
	refresh  int
	fetch    bool

	// flags de desenho que o usuário passou, para repetir no comando instalado
	renderArgs []string
}

func parseFlags(args []string, out io.Writer) (options, error) {
	var o options
	var hide string
	fs := flag.NewFlagSet(appName, flag.ContinueOnError)
	fs.SetOutput(out)
	fs.StringVar(&o.lang, "lang", "", "")
	fs.BoolVar(&o.noFetch, "no-fetch", false, "")
	fs.IntVar(&o.barWidth, "bar-width", 10, "")
	fs.BoolVar(&o.plain, "plain", false, "")
	fs.StringVar(&hide, "hide", "", "")
	fs.IntVar(&o.refresh, "refresh", 30, "")
	fs.BoolVar(&o.fetch, "fetch", false, "")
	fs.Usage = func() {}
	if err := fs.Parse(args); err != nil {
		return o, err
	}
	if o.barWidth < 3 {
		o.barWidth = 3
	}
	if o.barWidth > 40 {
		o.barWidth = 40
	}
	if o.refresh < 0 {
		o.refresh = 0
	}
	o.hide = map[string]bool{}
	for _, h := range strings.Split(hide, ",") {
		if h = strings.TrimSpace(strings.ToLower(h)); h != "" {
			o.hide[h] = true
		}
	}
	fs.Visit(func(f *flag.Flag) {
		switch f.Name {
		case "lang", "hide":
			o.renderArgs = append(o.renderArgs, "--"+f.Name, f.Value.String())
		case "bar-width":
			o.renderArgs = append(o.renderArgs, "--bar-width", strconv.Itoa(o.barWidth))
		case "no-fetch", "plain":
			if f.Value.String() == "true" {
				o.renderArgs = append(o.renderArgs, "--"+f.Name)
			}
		}
	})
	return o, nil
}

func main() {
	args := os.Args[1:]
	cmd := ""
	if len(args) > 0 && !strings.HasPrefix(args[0], "-") {
		cmd = strings.ToLower(args[0])
		args = args[1:]
	}

	opts, err := parseFlags(args, io.Discard)
	tr := pickTexts(opts.lang)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		fmt.Fprint(os.Stderr, tr.help(version))
		os.Exit(2)
	}

	switch cmd {
	case "":
		if opts.fetch {
			fetchUsage(fetchTimeout)
			return
		}
		if isTerminal(os.Stdin) {
			os.Exit(runInstall(opts, tr))
		}
		runStatusLine(opts, tr)
	case "install", "update":
		os.Exit(runInstall(opts, tr))
	case "uninstall", "remove":
		os.Exit(runUninstall(opts, tr))
	case "preview":
		runPreview(opts, tr)
	case "demo":
		runDemo(opts)
	case "version":
		fmt.Println(appName, version)
	case "help":
		fmt.Print(tr.help(version))
	default:
		fmt.Fprintf(os.Stderr, tr.unknownCmd+"\n\n", cmd)
		fmt.Fprint(os.Stderr, tr.help(version))
		os.Exit(2)
	}
}
