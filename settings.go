package main

// Edição do settings.json que mexe só na chave "statusLine": o resto do
// arquivo (ordem das chaves, espaços, quebras de linha) fica igualzinho.

import (
	"bytes"
	"encoding/json"
	"errors"
	"strings"
)

var utf8BOM = []byte{0xEF, 0xBB, 0xBF}

type member struct {
	key                        string
	keyStart, valStart, valEnd int
}

type jsonObject struct {
	open, close int
	members     []member
}

func skipWS(b []byte, i int) int {
	for i < len(b) && (b[i] == ' ' || b[i] == '\t' || b[i] == '\n' || b[i] == '\r') {
		i++
	}
	return i
}

func skipString(b []byte, i int) int {
	for i++; i < len(b); i++ {
		switch b[i] {
		case '\\':
			i++
		case '"':
			return i + 1
		}
	}
	return i
}

func skipValue(b []byte, i int) int {
	if i >= len(b) {
		return i
	}
	switch b[i] {
	case '"':
		return skipString(b, i)
	case '{', '[':
		depth := 0
		for i < len(b) {
			switch b[i] {
			case '"':
				i = skipString(b, i)
				continue
			case '{', '[':
				depth++
			case '}', ']':
				depth--
				if depth == 0 {
					return i + 1
				}
			}
			i++
		}
		return i
	default:
		for i < len(b) && !strings.ContainsRune(",}] \t\r\n", rune(b[i])) {
			i++
		}
		return i
	}
}

// parseObject acha as chaves de primeiro nível e onde cada valor começa e termina.
func parseObject(b []byte) (*jsonObject, error) {
	if !json.Valid(b) {
		return nil, errors.New("invalid JSON")
	}
	i := skipWS(b, 0)
	if i >= len(b) || b[i] != '{' {
		return nil, errors.New("settings is not a JSON object")
	}
	o := &jsonObject{open: i}
	i++
	for {
		i = skipWS(b, i)
		if b[i] == '}' {
			o.close = i
			return o, nil
		}
		ks := i
		ke := skipString(b, i)
		var key string
		_ = json.Unmarshal(b[ks:ke], &key)
		i = skipWS(b, ke) // ':'
		vs := skipWS(b, i+1)
		ve := skipValue(b, vs)
		o.members = append(o.members, member{key, ks, vs, ve})
		i = skipWS(b, ve)
		if b[i] == ',' {
			i++
		}
	}
}

func (o *jsonObject) find(key string) int {
	for i := len(o.members) - 1; i >= 0; i-- {
		if o.members[i].key == key {
			return i
		}
	}
	return -1
}

// layout descobre o estilo do arquivo: recuo, quebra de linha e se é minificado.
func layout(b []byte, o *jsonObject) (indent, nl string, pretty bool) {
	nl = "\n"
	if bytes.Contains(b, []byte("\r\n")) {
		nl = "\r\n"
	}
	if len(o.members) == 0 {
		return "  ", nl, true
	}
	first := o.members[0].keyStart
	lineStart := bytes.LastIndexByte(b[:first], '\n')
	if lineStart < o.open {
		return "", "", false // tudo numa linha só
	}
	return string(b[lineStart+1 : first]), nl, true
}

func formatValue(val []byte, indent, nl string, pretty bool) ([]byte, error) {
	var buf bytes.Buffer
	var err error
	if pretty {
		err = json.Indent(&buf, val, indent, indent)
	} else {
		err = json.Compact(&buf, val)
	}
	if err != nil {
		return nil, err
	}
	out := buf.Bytes()
	if nl == "\r\n" {
		out = bytes.ReplaceAll(out, []byte("\n"), []byte("\r\n"))
	}
	return out, nil
}

func splitBOM(b []byte) ([]byte, bool) {
	if bytes.HasPrefix(b, utf8BOM) {
		return b[len(utf8BOM):], true
	}
	return b, false
}

func withBOM(b []byte, bom bool) []byte {
	if !bom {
		return b
	}
	return append(append([]byte{}, utf8BOM...), b...)
}

func emptyToObject(b []byte) []byte {
	if len(bytes.TrimSpace(b)) == 0 {
		return []byte("{}\n")
	}
	return b
}

// getMember devolve o valor cru da chave (nil se não existir).
func getMember(doc []byte, key string) ([]byte, error) {
	b, _ := splitBOM(doc)
	b = emptyToObject(b)
	o, err := parseObject(b)
	if err != nil {
		return nil, err
	}
	if i := o.find(key); i >= 0 {
		m := o.members[i]
		return append([]byte{}, b[m.valStart:m.valEnd]...), nil
	}
	return nil, nil
}

// setMember troca (ou acrescenta no fim) a chave, no estilo do próprio arquivo.
func setMember(doc []byte, key string, val []byte) ([]byte, error) {
	b, bom := splitBOM(doc)
	b = emptyToObject(b)
	o, err := parseObject(b)
	if err != nil {
		return nil, err
	}
	indent, nl, pretty := layout(b, o)
	v, err := formatValue(val, indent, nl, pretty)
	if err != nil {
		return nil, err
	}
	var out []byte
	if i := o.find(key); i >= 0 {
		m := o.members[i]
		out = concat(b[:m.valStart], v, b[m.valEnd:])
	} else {
		k, _ := json.Marshal(key)
		sep := ": "
		if !pretty {
			sep = ":"
		}
		entry := concat(k, []byte(sep), v)
		if len(o.members) == 0 {
			out = concat(b[:o.open+1], []byte(nl+indent), entry, []byte(nl), b[o.close:])
		} else {
			last := o.members[len(o.members)-1]
			out = concat(b[:last.valEnd], []byte(","+nl+indent), entry, b[last.valEnd:])
		}
	}
	return withBOM(out, bom), nil
}

// removeMember tira a chave e a vírgula que sobraria.
func removeMember(doc []byte, key string) ([]byte, error) {
	b, bom := splitBOM(doc)
	b = emptyToObject(b)
	o, err := parseObject(b)
	if err != nil {
		return nil, err
	}
	i := o.find(key)
	if i < 0 {
		return doc, nil
	}
	m := o.members[i]
	var out []byte
	switch {
	case len(o.members) == 1:
		out = concat(b[:o.open+1], b[o.close:])
	case i == 0:
		out = concat(b[:m.keyStart], b[o.members[1].keyStart:])
	default:
		out = concat(b[:o.members[i-1].valEnd], b[m.valEnd:])
	}
	return withBOM(out, bom), nil
}

func concat(parts ...[]byte) []byte {
	n := 0
	for _, p := range parts {
		n += len(p)
	}
	out := make([]byte, 0, n)
	for _, p := range parts {
		out = append(out, p...)
	}
	return out
}

// marshalNoEscape é o json.Marshal sem trocar & < > por & etc.
func marshalNoEscape(v any) ([]byte, error) {
	var buf bytes.Buffer
	enc := json.NewEncoder(&buf)
	enc.SetEscapeHTML(false)
	if err := enc.Encode(v); err != nil {
		return nil, err
	}
	return bytes.TrimRight(buf.Bytes(), "\n"), nil
}
