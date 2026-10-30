package main

import (
	"encoding/json"
	"regexp"
	"strings"
	"testing"
	"time"
)

var ansi = regexp.MustCompile(`\x1b\[[0-9;]*m`)

func strip(s string) string { return ansi.ReplaceAllString(s, "") }

func defaultOpts() options { return options{barWidth: 10, hide: map[string]bool{}} }

func TestRenderFromStdin(t *testing.T) {
	now := time.Date(2026, 10, 29, 14, 0, 0, 0, time.Local)
	ts := unixF(now)
	raw := `{
	  "model": {"display_name": "Opus 5.5"},
	  "effort": {"level": "high"},
	  "context_window": {"used_percentage": 33.6},
	  "rate_limits": {
	    "five_hour": {"used_percentage": 42.4, "resets_at": ` + jsonNum(ts+2*3600+5*60) + `},
	    "seven_day": {"used_percentage": 7, "resets_at": ` + jsonNum(ts+3*86400) + `}
	  }
	}`
	var in Input
	if err := json.Unmarshal([]byte(raw), &in); err != nil {
		t.Fatal(err)
	}
	got := strip(renderLine(&in, nil, now, defaultOpts(), textsPT, painter{colorTrue}))
	for _, want := range []string{"sessão", "42%", "↻ 2h05", "semana", " 7%", "↻ dom 14h", "contexto 34%", "Opus 5.5", "alto ●●●●●"} {
		if !strings.Contains(got, want) {
			t.Errorf("missing %q in %q", want, got)
		}
	}
}

func jsonNum(f float64) string { b, _ := json.Marshal(f); return string(b) }

func TestMergeKeepsHigherAndNewerWindow(t *testing.T) {
	now := 1_000_000.0
	a := &Window{Pct: 30, ResetsAt: now + 3600}
	b := &Window{Pct: 45, ResetsAt: now + 3700}
	if m := merge(a, b, now); m.Pct != 45 {
		t.Fatalf("same window should keep the higher pct, got %v", m.Pct)
	}
	old := &Window{Pct: 90, ResetsAt: now + 100}
	fresh := &Window{Pct: 5, ResetsAt: now + 5*3600}
	if m := merge(old, fresh, now); m.Pct != 5 {
		t.Fatalf("different windows should keep the newer one, got %v", m.Pct)
	}
	expired := &Window{Pct: 99, ResetsAt: now - 1}
	if m := merge(expired, nil, now); m.Pct != 0 {
		t.Fatalf("expired window should be 0, got %v", m.Pct)
	}
}

func TestBarWidthAndTick(t *testing.T) {
	for _, pct := range []float64{0, 1, 37, 50, 99, 100, 140} {
		s := strip(fmtBar(painter{colorTrue}, pct, 0.5, 10))
		if n := len([]rune(s)); n != 10 {
			t.Fatalf("pct %v: bar has %d cells: %q", pct, n, s)
		}
	}
	plain := fmtBar(painter{colorNone}, 20, 0.75, 10)
	if plain != "██░░░░░│░░" {
		t.Fatalf("plain bar = %q", plain)
	}
}

func TestResetFormats(t *testing.T) {
	now := time.Date(2026, 10, 29, 9, 0, 0, 0, time.Local) // quinta
	ts := unixF(now)
	cases := []struct {
		at   float64
		long bool
		tr   *texts
		want string
	}{
		{ts + 45*60, false, textsPT, "45min"},
		{ts + 45*60, false, textsEN, "45m"},
		{ts + 4*3600 + 30, false, textsPT, "4h00"},
		{ts + 3*3600, true, textsPT, "3h00"},
		{ts + 10*3600, true, textsPT, "hoje 19h"},
		{ts + 24*3600 + 30*60, true, textsPT, "amanhã 9h30"},
		{ts + 2*86400 + 4*3600, true, textsEN, "Sat 1pm"},
		{ts + 15*3600, true, textsEN, "tomorrow 12am"},
	}
	for _, c := range cases {
		if got := fmtReset(c.at, c.long, now, c.tr); got != c.want {
			t.Errorf("fmtReset(+%vs, %v) = %q, want %q", c.at-ts, c.long, got, c.want)
		}
	}
}

func TestStaleWarningAndLoading(t *testing.T) {
	now := time.Now()
	c := &Cache{FiveHour: &Window{Pct: 10, ResetsAt: unixF(now) + 3600}, FetchedAt: unixF(now) - 12*60}
	got := strip(renderLine(nil, c, now, defaultOpts(), textsEN, painter{colorTrue}))
	if !strings.Contains(got, "not updated for 12 min") {
		t.Fatalf("no stale warning: %q", got)
	}
	if got := strip(renderLine(nil, nil, now, defaultOpts(), textsEN, painter{colorTrue})); got != "loading usage…" {
		t.Fatalf("got %q", got)
	}
}

func TestFlexTime(t *testing.T) {
	var v struct{ A, B, C, D flexTime }
	_ = json.Unmarshal([]byte(`{"A": 1767225600, "B": 1767225600000, "C": "2026-01-01T00:00:00Z", "D": null}`), &v)
	if v.A != 1767225600 || v.B != 1767225600 || v.C != 1767225600 || v.D != 0 {
		t.Fatalf("%+v", v)
	}
}

func TestEffortAsString(t *testing.T) {
	in := Input{Effort: json.RawMessage(`"xhigh"`)}
	if in.effortLevel() != "xhigh" {
		t.Fatal(in.effortLevel())
	}
}

func TestHideAndNoColor(t *testing.T) {
	o := defaultOpts()
	o.hide = map[string]bool{"session": true, "model": true}
	now := time.Now()
	c := &Cache{FiveHour: &Window{Pct: 10}, SevenDay: &Window{Pct: 20}, FetchedAt: unixF(now)}
	got := renderLine(nil, c, now, o, textsEN, painter{colorNone})
	if strings.Contains(got, "\x1b") || strings.Contains(got, "session") || !strings.Contains(got, "week") {
		t.Fatalf("got %q", got)
	}
}

func TestParseFlagsRenderArgs(t *testing.T) {
	o, err := parseFlags([]string{"--lang", "pt", "--refresh", "10", "--no-fetch", "--bar-width", "99"}, nil)
	if err != nil {
		t.Fatal(err)
	}
	got := strings.Join(o.renderArgs, " ")
	if got != "--bar-width 40 --lang pt --no-fetch" || o.refresh != 10 {
		t.Fatalf("renderArgs = %q refresh = %d", got, o.refresh)
	}
}
