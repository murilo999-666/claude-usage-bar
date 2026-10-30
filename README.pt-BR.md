# claude-usage-bar

Uma barra colorida de uso para a barra de status do [Claude Code](https://claude.com/claude-code). Ela mostra quanto você já gastou da sessão de 5 horas e do limite da semana, e se está no ritmo, sem precisar digitar `/usage`.

![claude-usage-bar na barra de status do Claude Code](docs/demo-pt.png)

**[Read in English](README.md)**

## O que aparece

- **sessão**: a janela de 5 horas e quanto falta para renovar
- **semana**: o limite de 7 dias e o dia e a hora em que renova
- **a marquinha branca**: o ritmo, ou seja, quanto da janela já passou. Se a cor passou da marquinha, você está gastando mais rápido do que dá para durar até renovar.
- o uso do contexto, o modelo, o nível de esforço (●●●○○) e o ⚡ quando o modo rápido está ligado
- as cores vão de verde → amarelo → laranja → vermelho conforme chega perto do limite

Os números são da conta inteira (claude.ai, apps do computador e do celular e todas as janelas do Claude Code), os mesmos do `/usage`.

## Instalar

### Windows

1. Baixe o **[claude-usage-bar-windows-amd64.exe](https://github.com/murilo999-666/claude-usage-bar/releases/latest/download/claude-usage-bar-windows-amd64.exe)** ([versão ARM](https://github.com/murilo999-666/claude-usage-bar/releases/latest/download/claude-usage-bar-windows-arm64.exe))
2. Dê dois cliques nele
3. Reinicie o Claude Code

> O Windows pode avisar "O Windows protegeu o computador" porque o programa não tem assinatura digital (o certificado é pago). Clique em **Mais informações → Executar assim mesmo**. A linha do PowerShell aí embaixo pula essa tela.

Ou cole isto no PowerShell:

```powershell
irm https://raw.githubusercontent.com/murilo999-666/claude-usage-bar/main/install.ps1 | iex
```

### macOS e Linux

```sh
curl -fsSL https://raw.githubusercontent.com/murilo999-666/claude-usage-bar/main/install.sh | sh
```

Depois reinicie o Claude Code. Não precisa de Node, Python nem de mais nada.

### O que o instalador faz

- copia o programa para `~/.claude/claude-usage-bar/`
- faz uma cópia de segurança do seu `settings.json` (`settings.json.bak-claude-usage-bar`) e liga a barra. Ele só mexe na chave `statusLine`; o resto do arquivo fica igualzinho, byte por byte.
- guarda a sua barra anterior, para a desinstalação trazer ela de volta
- mostra uma prévia com o seu uso de agora

## Atualizar

Rode o instalador de novo.

## Desinstalar

**Windows:** aperte `Win + R`, cole isto e dê Enter:

```
%USERPROFILE%\.claude\claude-usage-bar\claude-usage-bar.exe uninstall
```

**macOS / Linux:**

```sh
~/.claude/claude-usage-bar/claude-usage-bar uninstall
```

A sua barra anterior volta e a pasta do programa é apagada.

## Opções

Passe para o instalador e elas ficam gravadas no comando da barra, por exemplo `claude-usage-bar-windows-amd64.exe install --lang pt --hide model`:

| Opção | O que faz |
|---|---|
| `--lang pt` ou `--lang en` | idioma (padrão: o do sistema) |
| `--hide session,week,context,model,effort` | esconde partes da barra |
| `--bar-width N` | largura da barra em células (padrão 10) |
| `--plain` | sem cores (`NO_COLOR` também funciona) |
| `--no-fetch` | não consulta o uso da conta; usa só o que o Claude Code envia |
| `--refresh N` | redesenha a cada N segundos (padrão 30) |

Na linha do macOS/Linux: `curl -fsSL .../install.sh | sh -s -- --lang pt`.

`claude-usage-bar preview` mostra a barra com o seu uso de agora em qualquer terminal.

## Como funciona e privacidade

- O Claude Code roda o programa a cada poucos segundos e manda as informações da sessão (modelo, contexto, limites) pela entrada padrão. O programa devolve uma linha.
- Para os números da conta inteira, ele consulta o mesmo endereço que o `/usage` usa (`api.anthropic.com/api/oauth/usage`), com o login que o Claude Code já salvou (`~/.claude/.credentials.json`, ou o Keychain no macOS). O token só vai para `api.anthropic.com`. Nada mais é enviado para lugar nenhum e não tem telemetria.
- Faz no máximo uma consulta por minuto, dividida entre todas as janelas abertas (guardada em `~/.claude/claude-usage-bar/usage.json`), em segundo plano, então a barra nunca fica esperando a internet. Se a consulta falha, espera 1 minuto (5 minutos depois de HTTP 429) e continua mostrando os números que o Claude Code envia.
- Esse endereço não é uma API pública documentada. Se ele mudar, a barra continua com os números que o próprio Claude Code envia.
- Feito para as assinaturas Claude Pro e Max. Com chave de API não existe limite de sessão nem de semana, então só aparecem o contexto e o modelo.

Testado no Windows 11 com o Claude Code 2.1. As versões de macOS e Linux passam nos testes automáticos (instalação e desinstalação inclusive) no GitHub Actions; relatos de uso real são bem-vindos.

## Compilar o código

Precisa do Go 1.24 ou mais novo. Não usa nenhuma biblioteca fora a padrão do Go.

```sh
go test ./...
go build -trimpath -ldflags "-s -w" -o claude-usage-bar .
```

Os arquivos das versões são compilados pelo GitHub Actions a partir deste código ([workflow](.github/workflows/build.yml)), com `checksums.txt` e um atestado de origem que dá para conferir com `gh attestation verify <arquivo> --repo murilo999-666/claude-usage-bar`.

## Licença

[MIT](LICENSE). Livre para usar, modificar e compartilhar.

Projeto não oficial, sem ligação com a Anthropic nem aprovação dela. Claude e Claude Code são marcas da Anthropic.
