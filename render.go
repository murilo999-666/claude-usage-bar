package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"math"
	"os"
	"strconv"
	"strings"
	"time"
)

// ---------- o que o Claude Code manda no stdin ----------

type Input struct {
	SessionID string `json:"session_id"`
	Model     *struct {
		ID          string `json:"id"`
		DisplayName string `json:"display_name"`
	} `json:"model"`
	Effort        json.RawMessage `json:"effort"` // {"level":"high"} ou "high"
	FastMode      bool            `json:"fast_mode"`
	ContextWindow *struct {
		UsedPercentage *float64 `json:"used_percentage"`
	} `json:"context_window"`
	RateLimits *struct {
		FiveHour *stdinWindow `json:"five_hour"`
		SevenDay *stdinWindow `json:"seven_day"`
	} `json:"rate_limits"`
}

type stdinWindow struct {
	UsedPercentage *float64 `json:"used_percentage"`
	ResetsAt       flexTime `json:"resets_at"`
}

func (in *Input) effortLevel() string {
	if in == nil || len(in.Effort) == 0 {
		return ""
	}
	var s string
	if json.Unmarshal(in.Effort, &s) == nil {
		return s
	}
	var o struct {
		Level string `json:"level"`
	}
	if json.Unmarshal(in.Effort, &o) == nil {
		return o.Level
	}
	return ""
}

// flexTime aceita segundos, milissegundos ou texto ISO 8601. Guarda segundos Unix (0 = sem data).
type flexTime float64

func (t *flexTime) UnmarshalJSON(b []byte) error {
	*t = 0
	b = bytes.TrimSpace(b)
	if len(b) == 0 || string(b) == "null" {
		return nil
	}
	if b[0] == '"' {
		var s string
		if err := json.Unmarshal(b, &s); err != nil {
			return nil
		}
		if s == "" {
			return nil
		}
		if p, err := time.Parse(time.RFC3339Nano, s); err == nil {
			*t = flexTime(float64(p.UnixNano()) / 1e9)
			return nil
		}
		if f, err := strconv.ParseFloat(s, 64); err == nil {
			*t = flexTime(normEpoch(f))
		}
		return nil
	}
	var f float64
	if json.Unmarshal(b, &f) == nil {
		*t = flexTime(normEpoch(f))
	}
	return nil
}

func normEpoch(f float64) float64 {
	if f > 1e12 { // milissegundos
		return f / 1000
	}
	return f
}

func (w *stdinWindow) toWindow() *Window {
	if w == nil || w.UsedPercentage == nil {
		return nil
	}
	return &Window{Pct: *w.UsedPercentage, ResetsAt: float64(w.ResetsAt)}
}

// ---------- cores ----------

type rgb [3]int

var (
	colTrack   = rgb{55, 58, 66}    // fundo da parte vazia da barra
	colTick    = rgb{235, 235, 240} // marquinha do ritmo
	colLabel   = rgb{150, 156, 170}
	colMuted   = rgb{105, 110, 122}
	colWarn    = rgb{250, 204, 21}
	colModel   = rgb{200, 205, 215}
	colEfforts = []rgb{{125, 211, 252}, {96, 165, 250}, {129, 140, 248}, {167, 139, 250}, {232, 121, 249}}
)

// Verde -> amarelo -> laranja -> vermelho, mudando aos poucos.
var colorStops = []struct {
	at float64
	c  rgb
}{
	{0, rgb{74, 222, 128}},
	{40, rgb{74, 222, 128}},
	{60, rgb{250, 204, 21}},
	{80, rgb{251, 146, 60}},
	{92, rgb{248, 113, 113}},
	{100, rgb{239, 68, 68}},
}

func colorFor(pct float64) rgb {
	pct = clamp(pct, 0, 100)
	for i := 1; i < len(colorStops); i++ {
		p0, c0 := colorStops[i-1].at, colorStops[i-1].c
		p1, c1 := colorStops[i].at, colorStops[i].c
		if pct <= p1 {
			t := (pct - p0) / (p1 - p0)
			var out rgb
			for k := 0; k < 3; k++ {
				out[k] = int(math.Round(float64(c0[k]) + float64(c1[k]-c0[k])*t))
			}
			return out
		}
	}
	return colorStops[len(colorStops)-1].c
}

const (
	colorNone = iota
	color256
	colorTrue
)

type painter struct{ mode int }

func detectPainter(plain bool) painter {
	if plain || os.Getenv("NO_COLOR") != "" {
		return painter{colorNone}
	}
	ct := strings.ToLower(os.Getenv("COLORTERM"))
	if ct == "truecolor" || ct == "24bit" {
		return painter{colorTrue}
	}
	// O Terminal.app do macOS não entende cor de 24 bits.
	if os.Getenv("TERM_PROGRAM") == "Apple_Terminal" {
		return painter{color256}
	}
	return painter{colorTrue}
}

func (p painter) fg(c rgb) string {
	switch p.mode {
	case colorTrue:
		return fmt.Sprintf("\x1b[38;2;%d;%d;%dm", c[0], c[1], c[2])
	case color256:
		return fmt.Sprintf("\x1b[38;5;%dm", to256(c))
	}
	return ""
}

func (p painter) bg(c rgb) string {
	switch p.mode {
	case colorTrue:
		return fmt.Sprintf("\x1b[48;2;%d;%d;%dm", c[0], c[1], c[2])
	case color256:
		return fmt.Sprintf("\x1b[48;5;%dm", to256(c))
	}
	return ""
}

func (p painter) bold() string {
	if p.mode == colorNone {
		return ""
	}
	return "\x1b[1m"
}

func (p painter) reset() string {
	if p.mode == colorNone {
		return ""
	}
	return "\x1b[0m"
}

// to256 acha a cor mais próxima na paleta xterm de 256 cores.
func to256(c rgb) int {
	levels := []int{0, 95, 135, 175, 215, 255}
	idx := func(v int) int {
		best, bd := 0, 1<<30
		for i, l := range levels {
			if d := (v - l) * (v - l); d < bd {
				best, bd = i, d
			}
		}
		return best
	}
	ri, gi, bi := idx(c[0]), idx(c[1]), idx(c[2])
	cube := 16 + 36*ri + 6*gi + bi
	cd := sq(c[0]-levels[ri]) + sq(c[1]-levels[gi]) + sq(c[2]-levels[bi])
	avg := (c[0] + c[1] + c[2]) / 3
	gray := 0
	if avg > 238 {
		gray = 23
	} else if avg > 8 {
		gray = (avg - 8) / 10
	}
	gv := 8 + gray*10
	gd := sq(c[0]-gv) + sq(c[1]-gv) + sq(c[2]-gv)
	if gd < cd {
		return 232 + gray
	}
	return cube
}

func sq(v int) int { return v * v }

func clamp(v, lo, hi float64) float64 {
	return math.Max(lo, math.Min(hi, v))
}

// ---------- desenho ----------

var partials = []string{"", "▏", "▎", "▍", "▌", "▋", "▊", "▉"}

// fmtBar desenha a barra com 8 subdivisões por célula. elapsed < 0 = sem marquinha.
func fmtBar(p painter, pct, elapsed float64, width int) string {
	color := colorFor(pct)
	units := int(math.Round(clamp(pct, 0, 100) / 100 * float64(width) * 8))
	tick := -1
	if elapsed >= 0 {
		tick = int(math.Floor(clamp(elapsed, 0, 1) * float64(width)))
		if tick > width-1 {
			tick = width - 1
		}
	}
	var s strings.Builder
	for i := 0; i < width; i++ {
		u := units - i*8
		if u > 8 {
			u = 8
		}
		if u < 0 {
			u = 0
		}
		if p.mode == colorNone {
			switch {
			case u == 8:
				s.WriteString("█")
			case u == 0 && i == tick:
				s.WriteString("│")
			case u == 0:
				s.WriteString("░")
			default:
				s.WriteString(partials[u])
			}
			continue
		}
		switch {
		case u == 8:
			s.WriteString(p.bg(color))
			if i == tick {
				s.WriteString(p.fg(colTrack) + "▏")
			} else {
				s.WriteString(" ")
			}
		case u == 0:
			s.WriteString(p.bg(colTrack))
			if i == tick {
				s.WriteString(p.fg(colTick) + "▏")
			} else {
				s.WriteString(" ")
			}
		default:
			s.WriteString(p.bg(colTrack) + p.fg(color) + partials[u])
		}
	}
	s.WriteString(p.reset())
	return s.String()
}

// fmtReset: sessão "2h05"; semana "qui 13h" (ou "hoje 13h"/"amanhã 13h").
func fmtReset(resetsAt float64, long bool, now time.Time, tr *texts) string {
	if resetsAt <= 0 {
		return ""
	}
	diff := math.Max(0, resetsAt-unixF(now))
	if !long || diff < 6*3600 {
		h := int(diff / 3600)
		m := int(math.Mod(diff, 3600) / 60)
		if h > 0 {
			return fmt.Sprintf("%dh%02d", h, m)
		}
		return fmt.Sprintf("%d%s", m, tr.minSuffix)
	}
	d := time.Unix(int64(math.Round(resetsAt)), 0).In(now.Location())
	tomorrow := now.AddDate(0, 0, 1)
	day := tr.days[d.Weekday()]
	if sameDay(d, now) {
		day = tr.today
	} else if sameDay(d, tomorrow) {
		day = tr.tomorrow
	}
	return day + " " + tr.clock(d)
}

func sameDay(a, b time.Time) bool {
	ay, am, ad := a.Date()
	by, bm, bd := b.Date()
	return ay == by && am == bm && ad == bd
}

func unixF(t time.Time) float64 { return float64(t.UnixNano()) / 1e9 }

// alive: janela que já resetou vira 0%.
func alive(w *Window, now float64) *Window {
	if w == nil {
		return nil
	}
	if w.ResetsAt > 0 && w.ResetsAt <= now {
		return &Window{Pct: 0}
	}
	return w
}

// merge junta o número da conta (cache) com o desta sessão (stdin).
// Na mesma janela de tempo vale o maior, porque o uso só sobe até resetar.
func merge(a, b *Window, now float64) *Window {
	a, b = alive(a, now), alive(b, now)
	if a == nil {
		return b
	}
	if b == nil {
		return a
	}
	if a.ResetsAt > 0 && b.ResetsAt > 0 && math.Abs(a.ResetsAt-b.ResetsAt) > 600 {
		if a.ResetsAt > b.ResetsAt { // janelas diferentes: fica a mais nova
			return a
		}
		return b
	}
	if a.Pct >= b.Pct {
		return a
	}
	return b
}

func windowPart(p painter, label string, w *Window, windowS float64, long bool, now time.Time, tr *texts, width int) string {
	if w == nil {
		return ""
	}
	pct := math.Round(w.Pct)
	elapsed := -1.0
	if w.ResetsAt > 0 {
		elapsed = 1 - math.Max(0, w.ResetsAt-unixF(now))/windowS
	}
	s := p.fg(colLabel) + label + p.reset() + " " + fmtBar(p, pct, elapsed, width) + " " +
		p.bold() + p.fg(colorFor(pct)) + fmt.Sprintf("%2d%%", int(pct)) + p.reset()
	if r := fmtReset(w.ResetsAt, long, now, tr); r != "" {
		if !long {
			r = padRight(r, 5)
		}
		s += " " + p.fg(colMuted) + "↻ " + r + p.reset()
	}
	return s
}

func padRight(s string, n int) string {
	if l := len([]rune(s)); l < n {
		return s + strings.Repeat(" ", n-l)
	}
	return s
}

var effortDots = map[string]int{"low": 1, "medium": 2, "high": 3, "xhigh": 4, "max": 5}

// modelPart: "Opus 5.5  alto ●●●○○" (+ "⚡ rápido" com o /fast ligado).
func modelPart(p painter, in *Input, tr *texts, hide map[string]bool) string {
	if in == nil || in.Model == nil || in.Model.DisplayName == "" {
		return ""
	}
	s := p.fg(colModel) + in.Model.DisplayName + p.reset()
	if lvl := in.effortLevel(); lvl != "" && !hide["effort"] {
		n := effortDots[lvl]
		label := tr.effort[lvl]
		if label == "" {
			label = lvl
		}
		color := colMuted
		if n > 0 {
			color = colEfforts[n-1]
		}
		s += "  " + p.fg(color) + label
		if n > 0 {
			if p.mode == colorNone {
				s += " " + strings.Repeat("●", n) + strings.Repeat("○", 5-n)
			} else {
				s += " " + p.fg(color) + strings.Repeat("●", n) + p.fg(colTrack) + strings.Repeat("●", 5-n)
			}
		}
		s += p.reset()
	}
	if in.FastMode {
		s += "  " + p.fg(colWarn) + "⚡ " + tr.fast + p.reset()
	}
	return s
}

const staleAfterS = 300 // a partir daqui avisa que o número pode estar velho

func renderLine(in *Input, cache *Cache, now time.Time, opts options, tr *texts, p painter) string {
	if cache == nil {
		cache = &Cache{}
	}
	t := unixF(now)
	var s5, s7 *Window
	if in != nil && in.RateLimits != nil {
		s5 = in.RateLimits.FiveHour.toWindow()
		s7 = in.RateLimits.SevenDay.toWindow()
	}
	session := merge(cache.FiveHour, s5, t)
	week := merge(cache.SevenDay, s7, t)

	var parts []string
	add := func(s string) {
		if s != "" {
			parts = append(parts, s)
		}
	}
	if !opts.hide["session"] {
		add(windowPart(p, tr.session, session, 5*3600, false, now, tr, opts.barWidth))
	}
	if !opts.hide["week"] {
		add(windowPart(p, tr.week, week, 7*86400, true, now, tr, opts.barWidth))
	}
	if !opts.hide["context"] && in != nil && in.ContextWindow != nil && in.ContextWindow.UsedPercentage != nil {
		add(p.fg(colMuted) + fmt.Sprintf("%s %d%%", tr.context, int(math.Round(*in.ContextWindow.UsedPercentage))) + p.reset())
	}
	if !opts.hide["model"] {
		add(modelPart(p, in, tr, opts.hide))
	}
	if cache.FetchedAt > 0 && t-cache.FetchedAt > staleAfterS && len(parts) > 0 && !opts.noFetch {
		min := int(math.Round((t - cache.FetchedAt) / 60))
		add(p.fg(colWarn) + fmt.Sprintf(tr.stale, min) + p.reset())
	}
	if len(parts) == 0 {
		return p.fg(colMuted) + tr.loading + p.reset()
	}
	return strings.Join(parts, "  "+p.fg(colTrack)+"│"+p.reset()+"  ")
}

// ---------- modos ----------

// readStdin lê o JSON do Claude Code sem nunca travar a barra.
func readStdin(r io.Reader, timeout time.Duration) []byte {
	ch := make(chan []byte, 1)
	go func() {
		b, _ := io.ReadAll(io.LimitReader(r, 4<<20))
		ch <- b
	}()
	select {
	case b := <-ch:
		return b
	case <-time.After(timeout):
		return nil
	}
}

func runStatusLine(opts options, tr *texts) {
	defer func() {
		if recover() != nil {
			fmt.Print(appName)
		}
	}()
	var in *Input
	if raw := readStdin(os.Stdin, 2*time.Second); len(raw) > 0 {
		var v Input
		if json.Unmarshal(raw, &v) == nil {
			in = &v
		}
	}
	saveLauncherState(in)
	cache := &Cache{}
	if !opts.noFetch {
		cache = readCache()
		maybeStartFetch(cache, time.Now())
	}
	fmt.Print(renderLine(in, cache, time.Now(), opts, tr, detectPainter(opts.plain)))
}

// runPreview mostra a barra com o uso de agora, buscando na hora.
func runPreview(opts options, tr *texts) {
	enableVT()
	cache := readCache()
	if !opts.noFetch {
		cache = fetchUsage(fetchTimeout)
	}
	fmt.Println(renderLine(nil, cache, time.Now(), opts, tr, detectPainter(opts.plain)))
	if cache.Error != "" && !opts.noFetch {
		fmt.Println(tr.fetchHint(cache.Error))
	}
}

// exampleLine é uma barra de exemplo, para quando ainda não há números.
func exampleLine(opts options, tr *texts, p painter) string {
	now := time.Now()
	t := unixF(now)
	c := &Cache{FiveHour: &Window{23, t + 3*3600}, SevenDay: &Window{41, t + 2.6*86400}, FetchedAt: t}
	return renderLine(nil, c, now, opts, tr, p)
}

// runDemo imprime exemplos fixos (para prints e testes).
func runDemo(opts options) {
	enableVT()
	now := time.Now()
	t := unixF(now)
	f := func(v float64) *float64 { return &v }
	p := detectPainter(opts.plain)
	scenarios := []struct {
		lang   string
		s5, s7 Window
		ctx    float64
		effort string
		fast   bool
	}{
		{"pt", Window{23, t + 3*3600}, Window{41, t + 2.6*86400}, 34, "xhigh", false},
		{"pt", Window{78, t + 2.5*3600}, Window{64, t + 1.2*86400}, 61, "high", true},
		{"en", Window{23, t + 3*3600}, Window{41, t + 2.6*86400}, 34, "xhigh", false},
		{"en", Window{96, t + 40*60}, Window{88, t + 5*3600}, 12, "medium", false},
	}
	for _, sc := range scenarios {
		if opts.lang != "" && !strings.HasPrefix(strings.ToLower(opts.lang), sc.lang) {
			continue
		}
		tr := pickTexts(sc.lang)
		in := &Input{}
		in.Model = &struct {
			ID          string `json:"id"`
			DisplayName string `json:"display_name"`
		}{"", "Opus 5.5"}
		in.Effort, _ = json.Marshal(sc.effort)
		in.FastMode = sc.fast
		in.ContextWindow = &struct {
			UsedPercentage *float64 `json:"used_percentage"`
		}{f(sc.ctx)}
		c := &Cache{FiveHour: &Window{sc.s5.Pct, sc.s5.ResetsAt}, SevenDay: &Window{sc.s7.Pct, sc.s7.ResetsAt}, FetchedAt: t}
		fmt.Println(renderLine(in, c, now, opts, tr, p))
	}
}
