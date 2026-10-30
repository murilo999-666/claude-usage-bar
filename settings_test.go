package main

import (
	"encoding/json"
	"strings"
	"testing"
)

const ourVal = `{"type":"command","command":"/home/a/.claude/claude-usage-bar/claude-usage-bar","refreshInterval":30}`

func mustSet(t *testing.T, doc, val string) string {
	t.Helper()
	out, err := setMember([]byte(doc), "statusLine", []byte(val))
	if err != nil {
		t.Fatalf("setMember: %v", err)
	}
	if !json.Valid(splitBOMs(out)) {
		t.Fatalf("result is not valid JSON:\n%s", out)
	}
	return string(out)
}

func splitBOMs(b []byte) []byte { r, _ := splitBOM(b); return r }

func TestSetInsertKeepsOrderAndStyle(t *testing.T) {
	doc := "{\n  \"model\": \"opus\",\n  \"hooks\": {\"a\": [1, 2]},\n  \"z\": \"}{,\\\"\"\n}\n"
	got := mustSet(t, doc, ourVal)
	want := "{\n  \"model\": \"opus\",\n  \"hooks\": {\"a\": [1, 2]},\n  \"z\": \"}{,\\\"\",\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": \"/home/a/.claude/claude-usage-bar/claude-usage-bar\",\n    \"refreshInterval\": 30\n  }\n}\n"
	if got != want {
		t.Fatalf("got:\n%s\nwant:\n%s", got, want)
	}
}

func TestSetReplacesExisting(t *testing.T) {
	doc := "{\n    \"statusLine\": {\"type\": \"command\", \"command\": \"node x.js\"},\n    \"b\": true\n}"
	got := mustSet(t, doc, ourVal)
	if !strings.HasPrefix(got, "{\n    \"statusLine\": {\n        \"type\": \"command\",") {
		t.Fatalf("unexpected:\n%s", got)
	}
	if !strings.HasSuffix(got, "},\n    \"b\": true\n}") {
		t.Fatalf("tail changed:\n%s", got)
	}
	if strings.Contains(got, "node x.js") {
		t.Fatal("old value still there")
	}
}

func TestSetEmptyAndMissing(t *testing.T) {
	for _, doc := range []string{"", "  \n", "{}", "{\n}\n"} {
		got := mustSet(t, doc, ourVal)
		raw, _ := getMember([]byte(got), "statusLine")
		if !isOurs(raw) {
			t.Fatalf("doc %q -> %q", doc, got)
		}
	}
}

func TestSetMinifiedStaysMinified(t *testing.T) {
	got := mustSet(t, `{"a":1,"b":[{"c":"d"}]}`, ourVal)
	want := `{"a":1,"b":[{"c":"d"}],"statusLine":` + ourVal + `}`
	if got != want {
		t.Fatalf("got %s", got)
	}
}

func TestSetCRLFAndBOM(t *testing.T) {
	doc := "\xEF\xBB\xBF{\r\n\t\"a\": 1\r\n}\r\n"
	got := mustSet(t, doc, ourVal)
	if !strings.HasPrefix(got, "\xEF\xBB\xBF{\r\n\t\"a\": 1,\r\n\t\"statusLine\": {\r\n\t\t\"type\"") {
		t.Fatalf("got %q", got)
	}
	if strings.Contains(strings.ReplaceAll(got, "\r\n", ""), "\n") {
		t.Fatalf("mixed line endings: %q", got)
	}
}

func TestSetRejectsInvalid(t *testing.T) {
	for _, doc := range []string{`{"a":1,}`, `[1,2]`, `{"a": // c` + "\n" + `1}`} {
		if _, err := setMember([]byte(doc), "statusLine", []byte(ourVal)); err == nil {
			t.Fatalf("expected error for %q", doc)
		}
	}
}

func TestRemove(t *testing.T) {
	cases := map[string]string{
		"{\n  \"statusLine\": {\"x\": 1},\n  \"b\": 2\n}":              "{\n  \"b\": 2\n}",
		"{\n  \"a\": 1,\n  \"statusLine\": {\"x\": 1}\n}":              "{\n  \"a\": 1\n}",
		"{\n  \"a\": 1,\n  \"statusLine\": {\"x\": 1},\n  \"b\": 2\n}": "{\n  \"a\": 1,\n  \"b\": 2\n}",
		"{\"statusLine\": 5}": "{}",
		"{\"a\": 1}":          "{\"a\": 1}",
	}
	for in, want := range cases {
		got, err := removeMember([]byte(in), "statusLine")
		if err != nil || string(got) != want {
			t.Fatalf("remove(%q) = %q, %v; want %q", in, got, err, want)
		}
	}
}

func TestRoundTripRestoresPrevious(t *testing.T) {
	orig := "{\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": \"node \\\"C:\\\\x\\\\s.js\\\"\"\n  },\n  \"b\": 1\n}\n"
	prev, _ := getMember([]byte(orig), "statusLine")
	installed := mustSet(t, orig, ourVal)
	restored := mustSet(t, installed, string(prev))
	if restored != orig {
		t.Fatalf("round trip changed the file:\n%s\nvs\n%s", restored, orig)
	}
}

func TestCommandForQuotesArgs(t *testing.T) {
	c := commandFor("/opt/x/claude-usage-bar", []string{"--lang", "pt", "--hide", "model,context"})
	if c != `/opt/x/claude-usage-bar --lang pt --hide "model,context"` && c != `/opt/x/claude-usage-bar --lang pt --hide 'model,context'` {
		t.Fatalf("got %q", c)
	}
}
