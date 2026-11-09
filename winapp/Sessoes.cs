// Gerenciador das sessões do Claude.
// Cada sessão é uma janela do Windows Terminal portátil; só a ativa fica à mostra, as outras
// ficam escondidas no mesmo lugar. A barra de abas, o rodapé e a moldura são nossos e mudam
// para a janela ativa. Um processo só (o primeiro "Claude Code.exe"); os outros só pedem
// "foca" ou "abre outra" para ele pela janela invisível "ClaudeCodeHost".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

class Sessao
{
    public string Id;                      // vai para CLAUDE_JANELA (null = janela antiga adotada)
    public IntPtr Hwnd;                    // janela do terminal (Zero enquanto abre)
    public volatile uint CmdPid, ClaudePid;
    public string Titulo = Txt.T("Abrindo…", "Opening…");
    public string Status = "";             // do próprio Claude: busy, idle, waiting, blocked
    public string SessionId;
    public string Modelo = "", Esforco = "";
    public DateTime EstadoLido = DateTime.MinValue, EnviadoEm = DateTime.MinValue;
    public readonly Clawd Anim;
    public bool Ocupado { get { return Status == "busy"; } }
    public bool Esperando { get { return Status == "waiting" || Status == "blocked"; } }

    static int sementes = Environment.TickCount;
    public Sessao(string id) { Id = id; Anim = new Clawd(Interlocked.Increment(ref sementes) * 7919); }
}

class Host : ApplicationContext
{
    public static Host Atual;
    public static readonly string Pasta = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "claude-launcher");
    static readonly string SessoesClaude = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".claude\sessions");
    // o Windows Terminal portátil e o ícone ficam na pasta do exe
    public static readonly string TERMINAL = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"terminal\wt.exe");

    public readonly List<Sessao> Sessoes = new List<Sessao>();
    public Sessao Ativa;

    readonly Barra barra = new Barra();
    readonly Rodape rodape = new Rodape();
    readonly Moldura moldura = new Moldura();
    readonly Timer relogio = new Timer(), anim = new Timer(), caixa = new Timer(), espera = new Timer();
    readonly Stopwatch cronometro = Stopwatch.StartNew();
    long ultimoQuadro;
    W32.WinEventProc proc;
    IntPtr hookJanela, hookFim, hookFrente;
    uint wtPid;
    readonly HostJanela msg;
    Sessao pendente;
    HashSet<IntPtr> antesDaNova;
    DateTime pendenteDesde;
    W32.WINDOWPLACEMENT posicao; bool temPosicao;
    int buscando;
    IntPtr icoPeq, icoGrande;

    public Host(bool novo)
    {
        Atual = this;
        Directory.CreateDirectory(Path.Combine(Pasta, "janelas"));
        Directory.CreateDirectory(Path.Combine(Pasta, "estado"));
        msg = new HostJanela(this);
        proc = new W32.WinEventProc(AoEvento);
        string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "claude.ico");
        icoPeq = LoadImage(IntPtr.Zero, ico, 1, 16, 16, 0x10);
        icoGrande = LoadImage(IntPtr.Zero, ico, 1, 32, 32, 0x10);
        barra.Show(); rodape.Show(); moldura.Show();
        Atalhos.Ligar(msg.Handle);

        // janelas que já estavam abertas (de um launcher anterior) viram abas
        foreach (IntPtr h in W32.JanelasDeTerminal(false))
        {
            if (ClaudeCode.LerAppId(h) != ClaudeCode.APP_ID) continue;
            Sessao s = new Sessao(ClaudeCode.IdDaJanela(h));
            s.Hwnd = h;
            s.Titulo = Limpar(W32.Titulo(h));
            Sessoes.Add(s);
            Vigiar(h);
        }
        Sessao ativa = null;
        foreach (Sessao s in Sessoes) if (W32.IsWindowVisible(s.Hwnd)) { ativa = s; break; }
        if (ativa == null && Sessoes.Count > 0) ativa = Sessoes[0];
        if (ativa != null) Ativar(ativa);
        if (Sessoes.Count == 0 || novo) Nova();

        relogio.Interval = 500; relogio.Tick += delegate { Tick(); }; relogio.Start();
        anim.Interval = 40; anim.Tick += delegate { Animar(); }; anim.Start();
        caixa.Interval = 300; caixa.Tick += delegate { Caixa(); }; caixa.Start();
        espera.Interval = 100; espera.Tick += delegate { Esperar(); };
        Tick();
    }

    // ---------- abrir, trocar, fechar ----------

    public void Nova()
    {
        if (pendente != null) return; // já tem uma abrindo
        string id = Guid.NewGuid().ToString("N").Substring(0, 8);
        Sessao s = new Sessao(id);
        Sessoes.Add(s);
        pendente = s; pendenteDesde = DateTime.Now;
        antesDaNova = new HashSet<IntPtr>(W32.JanelasDeTerminal(false));

        // abre já no lugar e do tamanho da janela atual, para a troca não pular
        string args = "-w new";
        if (Ativa != null && W32.IsWindow(Ativa.Hwnd))
        {
            if (W32.IsZoomed(Ativa.Hwnd)) args += " --maximized";
            else
            {
                W32.RECT r; W32.GetWindowRect(Ativa.Hwnd, out r);
                args += " --pos " + r.L + "," + r.T;
                int c, l;
                if (TelaDoTerminal.Tamanho(Ativa.CmdPid, out c, out l) && c > 10 && l > 5) args += " --size " + c + "," + l;
            }
        }
        args += " -p \"Claude\" cmd /c set CLAUDE_JANELA=" + id + "&& claude";
        try
        {
            ProcessStartInfo p = new ProcessStartInfo(TERMINAL, args);
            p.UseShellExecute = false;
            p.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Process.Start(p);
        }
        catch (Exception) { Sessoes.Remove(s); pendente = null; }
        espera.Start();
        barra.Invalidate();
    }

    // espera a janela da sessão nova aparecer e passa para ela
    void Esperar()
    {
        if (pendente == null) { espera.Stop(); return; }
        foreach (IntPtr h in W32.JanelasDeTerminal(false))
        {
            if (antesDaNova.Contains(h)) continue;
            bool usada = false;
            foreach (Sessao x in Sessoes) if (x.Hwnd == h) usada = true;
            if (usada) continue;
            Sessao s = pendente;
            pendente = null;
            espera.Stop();
            s.Hwnd = h;
            ClaudeCode.Marcar(h);
            ClaudeCode.GuardarId(h, s.Id);
            Vigiar(h);
            Ativar(s);
            return;
        }
        if ((DateTime.Now - pendenteDesde).TotalSeconds > 25)
        {
            Sessoes.Remove(pendente);
            pendente = null;
            espera.Stop();
            if (Sessoes.Count == 0) Sair();
            barra.Invalidate();
        }
    }

    public void Ativar(Sessao s)
    {
        if (s == null || s.Hwnd == IntPtr.Zero || !W32.IsWindow(s.Hwnd)) return;
        Sessao antes = Ativa;
        if (antes == s) { Focar(); return; }

        // a nova aparece exatamente onde a anterior estava (e por cima); depois some a anterior
        W32.WINDOWPLACEMENT wp = new W32.WINDOWPLACEMENT();
        bool tem = antes != null && LerLugar(antes.Hwnd, out wp);
        if (!tem && temPosicao) { wp = posicao; tem = true; }
        if (tem)
        {
            wp.flags = 0;
            if (wp.showCmd != 3) wp.showCmd = 1;
            W32.SetWindowPlacement(s.Hwnd, ref wp);
        }
        else W32.ShowWindow(s.Hwnd, W32.IsIconic(s.Hwnd) ? 9 : 5);

        Ativa = s;
        AplicarJanela(s.Hwnd);
        barra.Prender(s.Hwnd);
        rodape.Prender(s.Hwnd);
        moldura.Prender(s.Hwnd);
        W32.SetForegroundWindow(s.Hwnd);
        if (antes != null && antes != s && W32.IsWindow(antes.Hwnd)) W32.ShowWindow(antes.Hwnd, 0);
        GuardarPosicao();
        barra.Invalidate();
        rodape.Invalidate();
    }

    // Ctrl+→ / Ctrl+← (Atalhos.cs): a aba do lado, dando a volta no fim
    public void Proxima(int lado)
    {
        int n = Sessoes.Count, i = Sessoes.IndexOf(Ativa);
        if (n < 2 || i < 0) return;
        for (int passo = 1; passo < n; passo++)
        {
            Sessao s = Sessoes[((i + lado * passo) % n + n) % n];
            if (s.Hwnd != IntPtr.Zero && W32.IsWindow(s.Hwnd)) { Ativar(s); return; }
        }
    }

    public void Focar()
    {
        if (Ativa == null) return;
        if (W32.IsIconic(Ativa.Hwnd)) W32.ShowWindow(Ativa.Hwnd, 9);
        W32.SetForegroundWindow(Ativa.Hwnd);
    }

    public void Fechar(Sessao s)
    {
        int i = Sessoes.IndexOf(s);
        if (i < 0) return;
        if (s == Ativa)
        {
            Sessao viz = Vizinha(i);
            if (viz != null) Ativar(viz);
        }
        Sessoes.Remove(s);
        if (s == pendente) pendente = null;
        if (s.Hwnd != IntPtr.Zero) W32.PostMessage(s.Hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
        Esquecer(s);
        if (Sessoes.Count == 0) Sair();
        barra.Invalidate();
    }

    Sessao Vizinha(int i)
    {
        for (int k = i + 1; k < Sessoes.Count; k++) if (Sessoes[k].Hwnd != IntPtr.Zero) return Sessoes[k];
        for (int k = i - 1; k >= 0; k--) if (Sessoes[k].Hwnd != IntPtr.Zero) return Sessoes[k];
        return null;
    }

    // a janela sumiu sozinha (o Claude saiu, ou fecharam pelo X do Terminal)
    void Sumiu(Sessao s)
    {
        int i = Sessoes.IndexOf(s);
        if (i < 0) return;
        bool eraAtiva = s == Ativa;
        Sessao viz = eraAtiva ? Vizinha(i) : null;
        Sessoes.Remove(s);
        Esquecer(s);
        if (eraAtiva)
        {
            Ativa = null;
            if (viz != null) Ativar(viz);
            else { barra.Esconder(); rodape.Esconder(); moldura.Esconder(); }
        }
        if (Sessoes.Count == 0) Sair();
        barra.Invalidate();
    }

    void Esquecer(Sessao s)
    {
        try { if (s.Hwnd != IntPtr.Zero) File.Delete(Path.Combine(Pasta, @"janelas\" + s.Hwnd.ToInt64() + ".txt")); } catch (Exception) { }
    }

    void Sair()
    {
        if (pendente != null) return;
        Encerrar();
    }

    // pedido de fora (instalador atualizando): as janelas continuam; o próximo gerenciador adota
    public void Encerrar()
    {
        relogio.Stop(); anim.Stop(); caixa.Stop();
        foreach (IntPtr h in new[] { hookJanela, hookFim, hookFrente }) if (h != IntPtr.Zero) W32.UnhookWinEvent(h);
        ExitThread();
    }

    // ---------- acompanhar as janelas ----------

    void Vigiar(IntPtr h)
    {
        uint pid;
        W32.GetWindowThreadProcessId(h, out pid);
        if (pid == wtPid) return;
        wtPid = pid; // as janelas do Terminal portátil moram todas no mesmo processo
        Atalhos.Processo = pid;
        if (hookJanela != IntPtr.Zero) { W32.UnhookWinEvent(hookJanela); W32.UnhookWinEvent(hookFim); }
        hookJanela = W32.SetWinEventHook(0x800B, 0x800C, IntPtr.Zero, proc, pid, 0, 0); // posição, título
        hookFim = W32.SetWinEventHook(0x8001, 0x8001, IntPtr.Zero, proc, pid, 0, 0);    // janela destruída
        if (hookFrente == IntPtr.Zero) hookFrente = W32.SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, proc, 0, 0, 0);
    }

    void AoEvento(IntPtr hook, uint ev, IntPtr h, int obj, int filho, uint th, uint t)
    {
        if (ev == 0x0003) { if (Ativa != null && h == Ativa.Hwnd) AplicarJanela(h); return; }
        if (obj != 0) return;
        Sessao s = null;
        foreach (Sessao x in Sessoes) if (x.Hwnd == h) s = x;
        if (s == null) return;
        if (ev == 0x8001) { Sumiu(s); return; }
        if (ev == 0x800C) { AtualizarTitulo(s); return; }
        if (ev == 0x800B && s == Ativa)
        {
            barra.Posicionar(); rodape.Posicionar(); moldura.Posicionar();
            GuardarPosicao();
        }
    }

    void GuardarPosicao()
    {
        if (Ativa == null || W32.IsIconic(Ativa.Hwnd)) return;
        W32.WINDOWPLACEMENT wp;
        if (LerLugar(Ativa.Hwnd, out wp)) { posicao = wp; temPosicao = true; }
    }

    // Onde a janela está, para pôr outra no mesmo lugar: showCmd 3 = maximizada; senão o
    // retângulo de verdade. Encaixada em meia tela (Win+seta ou arrastando para a borda), o
    // Windows guarda no rcNormal o tamanho de ANTES do encaixe; usar ele fazia a sessão nova
    // (e a troca de aba) voltar para o meio da tela.
    static bool LerLugar(IntPtr h, out W32.WINDOWPLACEMENT wp)
    {
        wp = new W32.WINDOWPLACEMENT();
        wp.length = Marshal.SizeOf(typeof(W32.WINDOWPLACEMENT));
        if (!W32.IsWindow(h) || !W32.GetWindowPlacement(h, ref wp)) return false;
        if (W32.IsZoomed(h)) { wp.showCmd = 3; return true; }
        wp.showCmd = 1;
        if (!W32.IsIconic(h))
        {
            W32.RECT r;
            if (W32.GetWindowRect(h, out r) && r.R > r.L && r.B > r.T) wp.rcNormal = W32.ParaAreaDeTrabalho(r);
        }
        return true;
    }

    // cor da barra de título (vale para janelas antigas, com a barra do Windows) e o ícone
    void AplicarJanela(IntPtr h)
    {
        int fundo = 0x000E0F12, texto = 0x00D8E4EC, borda = 0x00222A3A;
        W32.DwmSetWindowAttribute(h, 35, ref fundo, 4);
        W32.DwmSetWindowAttribute(h, 36, ref texto, 4);
        W32.DwmSetWindowAttribute(h, 34, ref borda, 4);
        if (icoPeq != IntPtr.Zero)
        {
            W32.PostMessage(h, 0x80, (IntPtr)0, icoPeq);
            W32.PostMessage(h, 0x80, (IntPtr)1, icoGrande);
        }
    }

    void Tick()
    {
        foreach (Sessao s in Sessoes.ToArray())
        {
            if (s.Hwnd == IntPtr.Zero) continue;
            if (!W32.IsWindow(s.Hwnd)) { Sumiu(s); continue; }
            AtualizarTitulo(s);
            LerStatus(s);
            LerEstado(s);
            if (s.Ocupado) s.Anim.Trabalhar(); else s.Anim.Parar();
            // só a ativa fica à mostra
            if (s != Ativa && W32.IsWindowVisible(s.Hwnd)) W32.ShowWindow(s.Hwnd, 0);
            if (s.CmdPid == 0 || s.ClaudePid == 0 || !Vivo(s.ClaudePid)) BuscarProcessos();
        }
        if (Ativa == null)
            foreach (Sessao s in Sessoes) if (s.Hwnd != IntPtr.Zero) { Ativar(s); break; }
        if (Ativa != null) { barra.Posicionar(); rodape.Posicionar(); }
        barra.Invalidate();
        rodape.Invalidate();
    }

    void Animar()
    {
        long agora = cronometro.ElapsedMilliseconds;
        double dt = agora - ultimoQuadro;
        ultimoQuadro = agora;
        foreach (Sessao s in Sessoes) s.Anim.Avancar(dt);
        barra.Animar();
        rodape.Pulsar(dt);
    }

    void Caixa()
    {
        if (Ativa == null || Ativa.CmdPid == 0) { moldura.Atualizar(-1, -1); return; }
        int r1, r2;
        bool ok = TelaDoTerminal.Caixa(Ativa.CmdPid, out r1, out r2);
        moldura.Atualizar(ok ? r1 : -1, r2);
    }

    void AtualizarTitulo(Sessao s)
    {
        string t = W32.Titulo(s.Hwnd);
        string limpo = Limpar(t);
        // sem o arquivo do Claude, o "girando" no começo do título diz se ele está trabalhando
        if (s.ClaudePid == 0 && t.Length > 0) s.Status = "◐◓◑◒".IndexOf(t[0]) >= 0 ? "busy" : "idle";
        if (limpo != s.Titulo) { s.Titulo = limpo; barra.Invalidate(); }
    }

    static string Limpar(string t)
    {
        t = t.Trim();
        if (t.Length >= 2 && !char.IsLetterOrDigit(t[0]) && t[1] == ' ') t = t.Substring(2).Trim();
        if (t.Length == 0 || t == "claude" || t == "Claude Code" || t.EndsWith("cmd.exe")) return Txt.T("Nova sessão", "New session");
        return t;
    }

    // o próprio Claude grava ~/.claude/sessions/<pid>.json com "status" e o id da sessão
    void LerStatus(Sessao s)
    {
        if (s.ClaudePid == 0) return;
        try
        {
            string f = Path.Combine(SessoesClaude, s.ClaudePid + ".json");
            if (!File.Exists(f)) return;
            string j = File.ReadAllText(f);
            string st = Campo(j, "status"), id = Campo(j, "sessionId");
            if (st != null && st != s.Status) { s.Status = st; barra.Invalidate(); rodape.Invalidate(); }
            if (id != null) s.SessionId = id;
        }
        catch (IOException) { }
    }

    // a barra de status (claude-usage-bar) grava modelo e esforço por sessão em estado\<sessionId>.json
    void LerEstado(Sessao s)
    {
        if (s.SessionId == null) return;
        string arq = Path.Combine(Pasta, @"estado\" + s.SessionId + ".json");
        try
        {
            if (!File.Exists(arq)) return;
            DateTime w = File.GetLastWriteTimeUtc(arq);
            if (w == s.EstadoLido) return;
            if (w < s.EnviadoEm) return; // ainda é de antes da troca que mandei pelo rodapé
            s.EstadoLido = w;
            string j = File.ReadAllText(arq, Encoding.UTF8);
            string m = Campo(j, "modelo"), e = Campo(j, "esforco");
            if (m != null && m.Length > 0) s.Modelo = m;
            if (e != null) s.Esforco = e;
            if (s == Ativa) rodape.Invalidate();
        }
        catch (IOException) { }
    }

    static string Campo(string json, string nome)
    {
        Match m = Regex.Match(json, "\"" + nome + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
    }

    static bool Vivo(uint pid)
    {
        try { Process.GetProcessById((int)pid); return true; }
        catch (ArgumentException) { return false; }
    }

    // Acha o cmd.exe (filho do Terminal) e o claude.exe (filho do cmd) de cada sessão.
    // WMI é lento, então roda fora da tela, um de cada vez.
    void BuscarProcessos()
    {
        if (Interlocked.CompareExchange(ref buscando, 1, 0) != 0) return;
        Sessao[] lista = Sessoes.ToArray();
        uint pidTerminal = wtPid;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                List<uint[]> cmds = new List<uint[]>(); List<string> linhas = new List<string>();
                Dictionary<uint, uint> claudes = new Dictionary<uint, uint>(); // pai -> claude
                using (ManagementObjectSearcher q = new ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process WHERE Name='cmd.exe' OR Name='claude.exe'"))
                {
                    foreach (ManagementObject p in q.Get())
                    {
                        uint pid = (uint)p["ProcessId"], pai = (uint)p["ParentProcessId"];
                        if ((string)p["Name"] == "claude.exe") claudes[pai] = pid;
                        else if (pai == pidTerminal) { cmds.Add(new[] { pid }); linhas.Add((p["CommandLine"] as string) ?? ""); }
                    }
                }
                HashSet<uint> usados = new HashSet<uint>();
                foreach (Sessao s in lista) if (s.Id != null)
                        for (int i = 0; i < cmds.Count; i++)
                            if (linhas[i].Contains("CLAUDE_JANELA=" + s.Id)) { s.CmdPid = cmds[i][0]; usados.Add(cmds[i][0]); }
                foreach (Sessao s in lista) if (s.Id == null && s.Hwnd != IntPtr.Zero)
                        for (int i = 0; i < cmds.Count; i++)
                            if (!linhas[i].Contains("CLAUDE_JANELA=") && !usados.Contains(cmds[i][0])) { s.CmdPid = cmds[i][0]; usados.Add(cmds[i][0]); break; }
                foreach (Sessao s in lista)
                {
                    uint c;
                    if (s.CmdPid != 0 && claudes.TryGetValue(s.CmdPid, out c)) s.ClaudePid = c;
                }
            }
            catch (Exception) { }
            finally { Interlocked.Exchange(ref buscando, 0); }
        });
    }

    [DllImport("user32", CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr inst, string nome, uint tipo, int cx, int cy, uint f);
}

// Janela invisível "ClaudeCodeHost": recebe os pedidos de outros "Claude Code.exe".
class HostJanela : NativeWindow
{
    public const int NOVA = 0x8001, FOCAR = 0x8002, PROXIMA = 0x8003, SAIR = 0x8004; // WM_APP + n
    readonly Host host;

    public HostJanela(Host h)
    {
        host = h;
        CreateParams cp = new CreateParams();
        cp.Caption = "ClaudeCodeHost";
        CreateHandle(cp);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NOVA) host.Nova();
        else if (m.Msg == FOCAR) host.Focar();
        else if (m.Msg == PROXIMA) host.Proxima(m.WParam.ToInt32());
        else if (m.Msg == SAIR) host.Encerrar();
        else base.WndProc(ref m);
    }
}
