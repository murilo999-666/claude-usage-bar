// Rodapé, menus, moldura da caixa de digitação e leitura da tela do terminal.
// Tudo aqui trabalha com a sessão ATIVA do gerenciador (Host.Atual.Ativa).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

class Opcao
{
    public string Valor, Nome, Descricao;
    public int Nivel; // só no esforço: 1..5 bolinhas
    public Opcao(string valor, string nome, string descricao, int nivel)
    { Valor = valor; Nome = nome; Descricao = descricao; Nivel = nivel; }
}

static class Tema
{
    public static readonly Color Titulo = Color.FromArgb(0x12, 0x0F, 0x0E);   // barra de abas
    public static readonly Color Fundo = Color.FromArgb(0x19, 0x15, 0x13);    // terminal e aba ativa
    public static readonly Color AbaHover = Color.FromArgb(0x1C, 0x18, 0x16);
    public static readonly Color Rodape = Color.FromArgb(0x1E, 0x19, 0x16);
    public static readonly Color Linha = Color.FromArgb(0x33, 0x29, 0x22);
    public static readonly Color Borda = Color.FromArgb(0x3A, 0x2E, 0x27);
    public static readonly Color Texto = Color.FromArgb(0xEC, 0xE4, 0xD8);
    public static readonly Color Apagado = Color.FromArgb(0x92, 0x86, 0x7C);
    public static readonly Color Hover = Color.FromArgb(0x2B, 0x23, 0x1F);
    public static readonly Color Aberto = Color.FromArgb(0x34, 0x2A, 0x24);
    public static readonly Color Terracota = Color.FromArgb(0xE0, 0x80, 0x5F);
    public static readonly Color MenuFundo = Color.FromArgb(0x23, 0x1D, 0x1A);
    public static readonly Color Trilho = Color.FromArgb(0x45, 0x3B, 0x35);
    public static readonly Color[] Esforco = {
        Color.FromArgb(125, 211, 252), Color.FromArgb(96, 165, 250), Color.FromArgb(129, 140, 248),
        Color.FromArgb(167, 139, 250), Color.FromArgb(232, 121, 249) };

    static string familia;
    public static Font Fonte(float px, bool forte)
    {
        if (familia == null)
        {
            familia = "Segoe UI";
            foreach (FontFamily f in FontFamily.Families)
                if (f.Name == "Segoe UI Variable Text") { familia = f.Name; break; }
        }
        return new Font(forte ? "Segoe UI Semibold" : familia, px, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    public static GraphicsPath Redondo(RectangleF r, float raio)
    {
        GraphicsPath p = new GraphicsPath();
        float d = raio * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // só os cantos de cima arredondados (aba)
    public static GraphicsPath Aba(RectangleF r, float raio)
    {
        GraphicsPath p = new GraphicsPath();
        float d = raio * 2;
        p.AddLine(r.Left - raio, r.Bottom, r.Left, r.Bottom);
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddLine(r.Right, r.Bottom, r.Right + raio, r.Bottom);
        p.CloseFigure();
        return p;
    }

    public static void Chevron(Graphics g, float cx, float cy, float s, Color cor)
    {
        using (Pen p = new Pen(cor, 1.5f * s))
        {
            p.StartCap = p.EndCap = LineCap.Round;
            g.DrawLines(p, new PointF[] {
                new PointF(cx - 3.5f * s, cy - 1.8f * s), new PointF(cx, cy + 1.8f * s), new PointF(cx + 3.5f * s, cy - 1.8f * s) });
        }
    }

    public static void Xis(Graphics g, float cx, float cy, float r, float s, Color cor)
    {
        using (Pen p = new Pen(cor, 1.5f * s))
        {
            p.StartCap = p.EndCap = LineCap.Round;
            g.DrawLine(p, cx - r, cy - r, cx + r, cy + r);
            g.DrawLine(p, cx - r, cy + r, cx + r, cy - r);
        }
    }

    public static void Bolinhas(Graphics g, float x, float cy, float s, int nivel)
    {
        for (int i = 0; i < 5; i++)
        {
            Color c = i < nivel ? Esforco[Math.Max(0, nivel - 1)] : Trilho;
            float r = 2.6f * s;
            using (SolidBrush b = new SolidBrush(c))
                g.FillEllipse(b, x + i * 7 * s, cy - r, r * 2, r * 2);
        }
    }

    public static float LarguraBolinhas(float s) { return 4 * 7 * s + 5.2f * s; }
}

// Janela nossa presa à janela do terminal ativa (sem barra de tarefas, sem roubar o foco).
class Sobreposta : Form
{
    protected IntPtr wt;
    protected float s = 1f;

    public Sobreposta()
    {
        // ShowInTaskbar fica true de propósito: com false o WinForms põe uma janela escondida
        // como dona. Quem tira da barra de tarefas é o WS_EX_TOOLWINDOW.
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        Bounds = new Rectangle(-32000, -32000, 10, 10);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.Style = unchecked((int)0x80000000);   // WS_POPUP
            cp.ExStyle = (cp.ExStyle | 0x08000000 | 0x00000080) & ~0x00040000; // NOACTIVATE | TOOLWINDOW, sem APPWINDOW
            // (o WinForms põe APPWINDOW quando ShowInTaskbar = true, e aí a barra de abas aparecia
            // como miniatura no botão da barra de tarefas)
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    public virtual void Prender(IntPtr terminal)
    {
        wt = terminal;
        Posicionar();
    }

    public void Esconder()
    {
        if (IsHandleCreated && Visible)
            W32.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, W32.SWP_NOSIZE | W32.SWP_NOMOVE | W32.SWP_NOZORDER | W32.SWP_NOACTIVATE | W32.SWP_HIDEWINDOW);
    }

    // retângulo do cliente do terminal na tela; false se não dá para mostrar agora
    protected bool Cliente(out Rectangle r)
    {
        r = Rectangle.Empty;
        if (wt == IntPtr.Zero || !W32.IsWindow(wt) || W32.IsIconic(wt) || !W32.IsWindowVisible(wt)) return false;
        W32.RECT rc; W32.GetClientRect(wt, out rc);
        W32.POINT p = new W32.POINT(); W32.ClientToScreen(wt, ref p);
        r = new Rectangle(p.X, p.Y, rc.R, rc.B);
        s = W32.Escala(wt);
        return rc.R > 0 && rc.B > 0;
    }

    protected void Colocar(int x, int y, int w, int h)
    {
        W32.SetWindowPos(Handle, IntPtr.Zero, x, y, w, h, W32.SWP_NOZORDER | W32.SWP_NOACTIVATE | W32.SWP_SHOWWINDOW);
        W32.Dono(Handle, wt);
    }

    public virtual void Posicionar() { }
}

class Rodape : Sobreposta
{
    public const int ALTURA = 40; // DIP; o padding de baixo do terminal é um pouco maior

    public static readonly Opcao[] MODELOS = {
        new Opcao("fable",  "Fable 5.1",  Txt.T("Pode pedir confirmação de uso extra", "May ask you to confirm extra usage"), 0),
        new Opcao("opus",   "Opus 5.5",   Txt.T("Para trabalho complexo e raciocínio longo", "For complex work and long reasoning"), 0),
        new Opcao("sonnet", "Sonnet 5.5", Txt.T("Rápido e capaz, bom para o dia a dia", "Fast and capable, good for everyday work"), 0),
        new Opcao("haiku",  "Haiku 5.5",  Txt.T("O mais rápido e o mais leve no uso", "The fastest and lightest on usage"), 0),
    };
    public static readonly Opcao[] ESFORCOS = {
        new Opcao("low",    Txt.T("Baixo", "Low"),  Txt.T("Responde rápido, pensa pouco", "Answers fast, thinks less"), 1),
        new Opcao("medium", Txt.T("Médio", "Medium"),  Txt.T("Equilíbrio entre rapidez e cuidado", "Balance between speed and care"), 2),
        new Opcao("high",   Txt.T("Alto", "High"),   Txt.T("Pensa com cuidado antes de agir", "Thinks carefully before acting"), 3),
        new Opcao("xhigh",  Txt.T("Extra", "Extra high"),  Txt.T("Raciocínio estendido", "Extended reasoning"), 4),
        new Opcao("max",    Txt.T("Máximo", "Max"), Txt.T("O mais profundo; mais lento e gasta mais", "The deepest; slower and uses more"), 5),
    };

    float fase;
    string aviso; DateTime avisoAte;
    RectangleF rModelo, rEsforco;
    int hover, aberto;
    Size ultimoTamanho;
    bool ultimoMax;
    MenuSuspenso menu;

    public Rodape()
    {
        BackColor = Tema.Rodape;
        Text = "Claude Code rodapé";
    }

    static Sessao S { get { return Host.Atual != null ? Host.Atual.Ativa : null; } }

    public override void Posicionar()
    {
        Rectangle c;
        if (!Cliente(out c)) return;
        bool max = W32.IsZoomed(wt);
        int h = (int)Math.Round(ALTURA * s);
        if (c.Height < h) return;
        Colocar(c.X, c.Bottom - h, c.Width, h);
        if (ultimoTamanho != new Size(c.Width, h) || ultimoMax != max)
        {
            ultimoTamanho = new Size(c.Width, h); ultimoMax = max;
            // cantos de baixo arredondados como os da janela do Windows 11
            if (max) W32.SetWindowRgn(Handle, IntPtr.Zero, true);
            else
            {
                int r = (int)Math.Round(8 * s);
                IntPtr a = W32.CreateRectRgn(0, 0, c.Width, h - r);
                IntPtr b = W32.CreateRoundRectRgn(0, 0, c.Width + 1, h + 1, r * 2, r * 2);
                W32.CombineRgn(a, a, b, 2);
                W32.DeleteObject(b);
                W32.SetWindowRgn(Handle, a, true);
            }
            Invalidate();
        }
    }

    // chamado pelo relógio de animação do gerenciador
    public void Pulsar(double ms)
    {
        if (aviso != null && DateTime.Now > avisoAte) { aviso = null; Invalidate(); }
        Sessao a = S;
        if (a == null || !(a.Ocupado || a.Esperando)) return;
        fase += (float)(ms / 250.0);
        Invalidate(new Rectangle(0, 0, (int)(40 * s), Height));
    }

    void Avisar(string texto)
    {
        aviso = texto;
        avisoAte = DateTime.Now.AddSeconds(5);
        Invalidate();
    }

    // ---------- desenho ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Tema.Rodape);
        using (Pen p = new Pen(Tema.Linha)) g.DrawLine(p, 0, 0, Width, 0);
        float cy = Height / 2f + 0.5f;
        StringFormat sf = StringFormat.GenericTypographic;
        Sessao a = S;
        bool ocupado = a != null && a.Ocupado, esperando = a != null && a.Esperando;

        // botões, à direita (primeiro, para o texto da esquerda saber até onde pode ir)
        float direita = Width - 12 * s;
        rEsforco = BotaoEsforco(g, direita, cy, a);
        rModelo = BotaoModelo(g, rEsforco.Left - 8 * s, cy, a);

        // estado, à esquerda
        float x = 20 * s;
        if (ocupado || esperando)
        {
            float k = 0.5f + 0.5f * (float)Math.Sin(fase);
            float rh = (5 + 3 * k) * s;
            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(70 * (1 - k) + 20), Tema.Terracota)))
                g.FillEllipse(b, x - rh, cy - rh, rh * 2, rh * 2);
        }
        float rp = 3.6f * s;
        using (SolidBrush b = new SolidBrush(ocupado || esperando ? Tema.Terracota : Tema.Trilho))
            g.FillEllipse(b, x - rp, cy - rp, rp * 2, rp * 2);
        string st = esperando ? Txt.T("Esperando você", "Waiting for you") : ocupado ? Txt.T("Trabalhando", "Working") : Txt.T("Pronto", "Ready");
        if (aviso != null) st += "  ·  " + aviso;
        RectangleF caixaTexto = new RectangleF(x + 12 * s, 0, Math.Max(0, rModelo.Left - 12 * s - (x + 12 * s)), Height);
        using (Font f = Tema.Fonte(12.5f * s, false))
        using (SolidBrush b = new SolidBrush(aviso != null ? Tema.Terracota : ocupado || esperando ? Tema.Texto : Tema.Apagado))
        using (StringFormat corta = new StringFormat(StringFormatFlags.NoWrap))
        {
            corta.Trimming = StringTrimming.EllipsisCharacter;
            corta.LineAlignment = StringAlignment.Center;
            g.DrawString(st, f, b, caixaTexto, corta);
        }
    }

    void FundoBotao(Graphics g, RectangleF r, int qual)
    {
        Color fundo = aberto == qual ? Tema.Aberto : hover == qual ? Tema.Hover : Tema.Rodape;
        using (GraphicsPath p = Tema.Redondo(r, r.Height / 2f))
        {
            using (SolidBrush b = new SolidBrush(fundo)) g.FillPath(b, p);
            using (Pen pen = new Pen(aberto == qual ? Tema.Terracota : Tema.Borda, 1f)) g.DrawPath(pen, p);
        }
    }

    RectangleF BotaoModelo(Graphics g, float direita, float cy, Sessao a)
    {
        StringFormat sf = StringFormat.GenericTypographic;
        string nome = a != null && a.Modelo.Length > 0 ? a.Modelo : Txt.T("Modelo", "Model");
        using (Font f = Tema.Fonte(12.5f * s, true))
        {
            float tw = g.MeasureString(nome, f, 1000, sf).Width;
            float w = 12 * s + tw + 8 * s + 7 * s + 12 * s, h = 26 * s;
            RectangleF r = new RectangleF(direita - w, cy - h / 2f, w, h);
            FundoBotao(g, r, 1);
            using (SolidBrush b = new SolidBrush(Tema.Texto))
                g.DrawString(nome, f, b, r.Left + 12 * s, cy - f.Height / 2f, sf);
            Tema.Chevron(g, r.Right - 12 * s - 3.5f * s, cy, s, Tema.Apagado);
            return r;
        }
    }

    RectangleF BotaoEsforco(Graphics g, float direita, float cy, Sessao a)
    {
        StringFormat sf = StringFormat.GenericTypographic;
        Opcao o = null;
        if (a != null) foreach (Opcao x in ESFORCOS) if (x.Valor == a.Esforco) o = x;
        string nome = o != null ? o.Nome : Txt.T("Esforço", "Effort");
        int nivel = o != null ? o.Nivel : 0;
        using (Font f = Tema.Fonte(12.5f * s, false))
        {
            float tw = g.MeasureString(nome, f, 1000, sf).Width;
            float bw = Tema.LarguraBolinhas(s);
            float w = 12 * s + bw + 8 * s + tw + 8 * s + 7 * s + 12 * s, h = 26 * s;
            RectangleF r = new RectangleF(direita - w, cy - h / 2f, w, h);
            FundoBotao(g, r, 2);
            Tema.Bolinhas(g, r.Left + 12 * s, cy, s, nivel);
            using (SolidBrush b = new SolidBrush(Tema.Texto))
                g.DrawString(nome, f, b, r.Left + 12 * s + bw + 8 * s, cy - f.Height / 2f, sf);
            Tema.Chevron(g, r.Right - 12 * s - 3.5f * s, cy, s, Tema.Apagado);
            return r;
        }
    }

    // ---------- mouse ----------

    int Qual(Point p)
    {
        if (rModelo.Contains(p)) return 1;
        if (rEsforco.Contains(p)) return 2;
        return 0;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int q = Qual(e.Location);
        Cursor = q != 0 ? Cursors.Hand : Cursors.Default;
        if (q != hover) { hover = q; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (hover != 0) { hover = 0; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int q = Qual(e.Location);
        if (q == 0 || S == null) return;
        bool mesmo = menu != null && !menu.IsDisposed && aberto == q;
        if (menu != null && !menu.IsDisposed) menu.Close();
        if (mesmo) return;
        AbrirMenu(q);
    }

    void AbrirMenu(int q)
    {
        RectangleF r = q == 1 ? rModelo : rEsforco;
        Rectangle ancora = RectangleToScreen(Rectangle.Round(r));
        ancora.Y = PointToScreen(Point.Empty).Y; // o menu abre acima do rodapé
        Sessao a = S;
        string atual = q == 1 ? AliasDoModelo(a.Modelo) : a.Esforco;
        menu = new MenuSuspenso(q == 1 ? Txt.T("Modelo", "Model") : Txt.T("Esforço", "Effort"), new List<Opcao>(q == 1 ? MODELOS : ESFORCOS),
            atual, q == 2, s, ancora, false);
        int tipo = q;
        menu.Escolheu += delegate (Opcao o) { Aplicar(tipo, o); };
        menu.FormClosed += delegate { aberto = 0; Invalidate(); };
        aberto = q;
        Invalidate();
        menu.Show(this);
        menu.Activate();
    }

    static string AliasDoModelo(string modelo)
    {
        string m = modelo.ToLowerInvariant();
        foreach (Opcao o in MODELOS) if (m.StartsWith(o.Valor)) return o.Valor;
        return "";
    }

    // ---------- trocar modelo/esforço ----------

    void Aplicar(int tipo, Opcao o)
    {
        Sessao a = S;
        if (a == null) return;
        string comando = (tipo == 1 ? "/model " : "/effort ") + o.Valor;
        TelaDoTerminal.Resultado t = TelaDoTerminal.Ver(a.CmdPid);
        if (!t.Pode) { Avisar(t.Motivo); return; }
        if (!Teclado.Focar(a.Hwnd)) { Avisar(Txt.T("não consegui focar o terminal", "couldn't focus the terminal")); return; }
        if (t.TemTexto) Teclado.CtrlS();          // guarda o que você já digitou (volta sozinho depois)
        Teclado.Digitar(comando);
        Teclado.Enter();
        // mostra já a escolha; o arquivo de estado só vale de novo quando for mais novo que isto
        if (tipo == 1) a.Modelo = o.Nome; else a.Esforco = o.Valor;
        a.EnviadoEm = DateTime.UtcNow;
        Invalidate();
    }
}

// Menu suspenso no estilo do seletor de modelo do site: cartão escuro, cantos redondos,
// entra com fade e um leve deslize (sobe, se abre acima; desce, se abre abaixo).
class MenuSuspenso : Form
{
    public event Action<Opcao> Escolheu;
    readonly List<Opcao> ops;
    readonly string titulo, atual;
    readonly bool niveis, abaixo;
    readonly float s, pad, cab, itemH;
    int hover = -1;
    int yFinal;
    readonly Timer anim = new Timer();
    DateTime inicio;
    bool escolhido;

    // ancora: retângulo na tela; o menu abre acima do topo dela ou abaixo da base
    public MenuSuspenso(string titulo, List<Opcao> ops, string atual, bool niveis, float s, Rectangle ancora, bool abaixo)
    {
        this.titulo = titulo; this.ops = ops; this.atual = atual; this.niveis = niveis; this.s = s; this.abaixo = abaixo;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Tema.MenuFundo;
        Opacity = 0;
        pad = 6 * s; cab = 30 * s; itemH = 50 * s;
        int w = (int)(300 * s), h = (int)(pad + cab + ops.Count * itemH + pad);
        Rectangle area = Screen.FromRectangle(ancora).WorkingArea;
        int x = abaixo ? Math.Max(area.Left + 8, Math.Min(ancora.Left, area.Right - w - 8))
                       : Math.Max(area.Left + 8, Math.Min(ancora.Right - w, area.Right - w - 8));
        yFinal = abaixo ? Math.Min(area.Bottom - h - 8, ancora.Bottom + (int)(6 * s))
                        : Math.Max(area.Top + 8, ancora.Top - h - (int)(8 * s));
        Bounds = new Rectangle(x, yFinal + Deslize(0), w, h);
        anim.Interval = 15;
        anim.Tick += delegate
        {
            double p = Math.Min(1.0, (DateTime.Now - inicio).TotalMilliseconds / 160.0);
            double e = 1 - Math.Pow(1 - p, 3);
            Opacity = e;
            Top = yFinal + Deslize(e);
            if (p >= 1) anim.Stop();
        };
        for (int i = 0; i < ops.Count; i++) if (ops[i].Valor == atual) hover = i;
    }

    int Deslize(double e) { return (int)Math.Round((1 - e) * 10 * s * (abaixo ? -1 : 1)); }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            cp.ExStyle = (cp.ExStyle | 0x00000080) & ~0x00040000; // TOOLWINDOW, sem APPWINDOW
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        int canto = 2, borda = 0x00272E3A;
        W32.DwmSetWindowAttribute(Handle, 33, ref canto, 4); // cantos redondos (Windows 11)
        W32.DwmSetWindowAttribute(Handle, 34, ref borda, 4);
        inicio = DateTime.Now;
        anim.Start();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!escolhido) BeginInvoke(new Action(Close));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        else if (e.KeyCode == Keys.Down) { hover = (hover + 1) % ops.Count; Invalidate(); }
        else if (e.KeyCode == Keys.Up) { hover = (hover - 1 + ops.Count) % ops.Count; Invalidate(); }
        else if (e.KeyCode == Keys.Enter && hover >= 0) Escolher(hover);
    }

    int Item(Point p)
    {
        float y = p.Y - pad - cab;
        if (y < 0 || p.X < pad || p.X > Width - pad) return -1;
        int i = (int)(y / itemH);
        return i < ops.Count ? i : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = Item(e.Location);
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        if (i >= 0 && i != hover) { hover = i; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        int i = Item(e.Location);
        if (i >= 0) Escolher(i);
    }

    void Escolher(int i)
    {
        escolhido = true;
        Opcao o = ops[i];
        Hide();
        if (Escolheu != null) Escolheu(o);
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Tema.MenuFundo);
        StringFormat sf = StringFormat.GenericTypographic;

        using (Font f = Tema.Fonte(11.5f * s, false))
        using (SolidBrush b = new SolidBrush(Tema.Apagado))
            g.DrawString(titulo, f, b, pad + 12 * s, pad + 9 * s, sf);

        using (Font fn = Tema.Fonte(13.5f * s, true))
        using (Font fd = Tema.Fonte(11.5f * s, false))
        using (SolidBrush bn = new SolidBrush(Tema.Texto))
        using (SolidBrush bd = new SolidBrush(Tema.Apagado))
        {
            for (int i = 0; i < ops.Count; i++)
            {
                RectangleF r = new RectangleF(pad, pad + cab + i * itemH, Width - 2 * pad, itemH - 2 * s);
                if (i == hover)
                    using (GraphicsPath p = Tema.Redondo(r, 8 * s))
                    using (SolidBrush bh = new SolidBrush(Tema.Hover)) g.FillPath(bh, p);
                float tx = r.Left + 12 * s;
                if (niveis)
                {
                    Tema.Bolinhas(g, tx, r.Top + r.Height / 2f, s, ops[i].Nivel);
                    tx += Tema.LarguraBolinhas(s) + 12 * s;
                }
                g.DrawString(ops[i].Nome, fn, bn, tx, r.Top + 8 * s, sf);
                g.DrawString(ops[i].Descricao, fd, bd, tx, r.Top + 27 * s, sf);
                if (ops[i].Valor == atual)
                {
                    float cx = r.Right - 20 * s, cy = r.Top + r.Height / 2f;
                    using (Pen p = new Pen(Tema.Terracota, 2f * s))
                    {
                        p.StartCap = p.EndCap = LineCap.Round;
                        g.DrawLines(p, new PointF[] {
                            new PointF(cx - 5 * s, cy), new PointF(cx - 1.5f * s, cy + 3.5f * s), new PointF(cx + 5 * s, cy - 4 * s) });
                    }
                }
            }
        }
    }
}

// Lê a tela do terminal (o buffer do console por trás do ConPTY) para saber se a caixa
// de texto do Claude está à vista. Sem isso, o Enter do /model poderia responder uma
// pergunta de permissão sem querer.
static class TelaDoTerminal
{
    public class Resultado { public bool Pode, TemTexto; public string Motivo; }

    public static Resultado Ver(uint pid)
    {
        Resultado r = new Resultado();
        string[] linhas; ushort[][] atributos; int cols;
        if (pid == 0 || !Ler(pid, out linhas, out atributos, out cols))
        { r.Motivo = Txt.T("não consegui ler o terminal", "couldn't read the terminal"); return r; }

        int r1, r2;
        if (!AcharCaixa(linhas, out r1, out r2)) { r.Motivo = Txt.T("o Claude está mostrando outra coisa", "Claude is showing something else"); return r; }
        string primeira = linhas[r1 + 1].TrimStart();
        if (Regex.IsMatch(primeira, @"^[❯>]\s*\d+\."))
        { r.Motivo = Txt.T("o Claude está esperando uma resposta", "Claude is waiting for an answer"); return r; }
        for (int i = Math.Max(0, r1 - 15); i <= r2; i++)
            if (linhas[i].Contains("Do you want") || linhas[i].Contains("Deseja "))
            { r.Motivo = Txt.T("o Claude está esperando uma resposta", "Claude is waiting for an answer"); return r; }

        // Tem texto digitado? O texto de exemplo (placeholder) vem apagado, cor 8.
        for (int i = r1 + 1; i < r2; i++)
        {
            string l = linhas[i];
            int ini = i == r1 + 1 ? l.IndexOfAny(new char[] { '❯', '>' }) + 1 : 0;
            for (int x = ini; x < l.Length && x < atributos[i].Length; x++)
                if (!char.IsWhiteSpace(l[x]) && (atributos[i][x] & 0x0F) != 0x08) { r.TemTexto = true; break; }
            if (r.TemTexto) break;
        }
        r.Pode = true;
        return r;
    }

    // Linhas (contando do topo da área visível) das duas réguas da caixa de digitação.
    public static bool Caixa(uint pid, out int r1, out int r2)
    {
        r1 = r2 = -1;
        string[] linhas; ushort[][] atributos; int cols;
        if (pid == 0 || !Ler(pid, out linhas, out atributos, out cols)) return false;
        return AcharCaixa(linhas, out r1, out r2);
    }

    // Colunas e linhas do terminal (para abrir uma sessão nova do mesmo tamanho).
    public static bool Tamanho(uint pid, out int cols, out int linhasN)
    {
        string[] linhas; ushort[][] atributos;
        cols = linhasN = 0;
        if (pid == 0 || !Ler(pid, out linhas, out atributos, out cols)) return false;
        linhasN = linhas.Length;
        return true;
    }

    static bool AcharCaixa(string[] linhas, out int r1, out int r2)
    {
        r1 = r2 = -1;
        int fim = linhas.Length - 1;
        for (int i = fim; i >= 0 && i >= fim - 14; i--) if (Regua(linhas[i])) { r2 = i; break; }
        for (int i = r2 - 1; i >= 0 && i >= r2 - 20; i--) if (Regua(linhas[i])) { r1 = i; break; }
        if (r1 < 0) return false;
        string primeira = linhas[r1 + 1].TrimStart();
        return primeira.StartsWith("❯") || primeira.StartsWith(">");
    }

    static bool Regua(string l)
    {
        string t = l.Trim();
        if (t.Length < 10) return false;
        int n = 0;
        foreach (char c in t) if (c == '─') n++;
        return n >= t.Length * 0.9;
    }

    static readonly object trava = new object();

    static bool Ler(uint pid, out string[] linhas, out ushort[][] atributos, out int cols)
    {
        linhas = null; atributos = null; cols = 0;
        lock (trava) // um processo só pode estar preso a um console por vez
        {
            SetConsoleCtrlHandler(IntPtr.Zero, true); // Ctrl+C no terminal não derruba o launcher enquanto ele lê
            if (!AttachConsole(pid)) return false;
            try
            {
                IntPtr h = CreateFile("CONOUT$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (h == new IntPtr(-1)) return false;
                try
                {
                    CSBI i;
                    if (!GetConsoleScreenBufferInfo(h, out i)) return false;
                    int w = i.size.X, n = i.win.B - i.win.T + 1;
                    cols = i.win.R - i.win.L + 1;
                    linhas = new string[n]; atributos = new ushort[n][];
                    for (int k = 0; k < n; k++)
                    {
                        COORD c; c.X = 0; c.Y = (short)(i.win.T + k);
                        StringBuilder sb = new StringBuilder(w + 1); uint lidos;
                        ReadConsoleOutputCharacterW(h, sb, (uint)w, c, out lidos);
                        ushort[] at = new ushort[w];
                        ReadConsoleOutputAttribute(h, at, (uint)w, c, out lidos);
                        linhas[k] = sb.ToString().TrimEnd();
                        atributos[k] = at;
                    }
                    return true;
                }
                finally { CloseHandle(h); }
            }
            finally { FreeConsole(); }
        }
    }

    [StructLayout(LayoutKind.Sequential)] struct COORD { public short X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SR { public short L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct CSBI { public COORD size; public COORD cursor; public ushort attr; public SR win; public COORD max; }
    [DllImport("kernel32")] static extern bool AttachConsole(uint pid);
    [DllImport("kernel32")] static extern bool FreeConsole();
    [DllImport("kernel32")] static extern bool SetConsoleCtrlHandler(IntPtr h, bool add);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFile(string n, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32")] static extern bool GetConsoleScreenBufferInfo(IntPtr h, out CSBI i);
    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    static extern bool ReadConsoleOutputCharacterW(IntPtr h, StringBuilder s, uint len, COORD c, out uint lidos);
    [DllImport("kernel32")]
    static extern bool ReadConsoleOutputAttribute(IntPtr h, [Out] ushort[] a, uint len, COORD c, out uint lidos);
}

// Digita no terminal como se fosse o teclado.
static class Teclado
{
    public static bool Focar(IntPtr wt)
    {
        W32.SetForegroundWindow(wt);
        for (int i = 0; i < 25; i++)
        {
            if (W32.GetForegroundWindow() == wt) { System.Threading.Thread.Sleep(40); return true; }
            System.Threading.Thread.Sleep(20);
        }
        return false;
    }

    public static void CtrlS()
    {
        Enviar(new INPUT[] { Tecla(0x11, false), Tecla(0x53, false), Tecla(0x53, true), Tecla(0x11, true) });
        System.Threading.Thread.Sleep(120);
    }

    public static void Digitar(string texto)
    {
        List<INPUT> l = new List<INPUT>();
        foreach (char c in texto) { l.Add(Unicode(c, false)); l.Add(Unicode(c, true)); }
        Enviar(l.ToArray());
        System.Threading.Thread.Sleep(80);
    }

    public static void Enter()
    {
        Enviar(new INPUT[] { Tecla(0x0D, false), Tecla(0x0D, true) });
    }

    // Alt+V (colar imagem no Claude do Windows). Solta o Ctrl antes: Ctrl+Alt seria o AltGr.
    public static void AltV()
    {
        Enviar(new INPUT[] { Tecla(0x11, true), Tecla(0x12, false), Tecla(0x56, false), Tecla(0x56, true), Tecla(0x12, true) });
    }

    static INPUT Tecla(ushort vk, bool solta)
    {
        INPUT i = new INPUT(); i.type = 1; i.wVk = vk; i.dwFlags = solta ? 2u : 0u; return i;
    }

    static INPUT Unicode(char c, bool solta)
    {
        INPUT i = new INPUT(); i.type = 1; i.wScan = c; i.dwFlags = 4u | (solta ? 2u : 0u); return i;
    }

    static void Enviar(INPUT[] t) { SendInput((uint)t.Length, t, Marshal.SizeOf(typeof(INPUT))); }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public ushort wVk;
        [FieldOffset(10)] public ushort wScan;
        [FieldOffset(12)] public uint dwFlags;
        [FieldOffset(16)] public uint time;
        [FieldOffset(24)] public IntPtr extra;
    }
    [DllImport("user32")] static extern uint SendInput(uint n, INPUT[] i, int tamanho);
}

// Moldura da caixa de digitação: o Terminal não deixa desenhar na margem, então esta janelinha
// cobre só as duas margens laterais na altura da caixa e desenha ali o resto de um retângulo
// arredondado. As réguas do Claude viram o topo e a base dele.
class Moldura : Sobreposta
{
    // calibração (pixels do cliente do terminal), feita olhando uma imagem da janela
    int calW = -1, calH = -1, offY, xIni, xFim, esp = 1;
    float celula;
    Color corLinha, corFundo;
    DateTime ultimaCal = DateTime.MinValue, conferido = DateTime.MinValue;
    int yt = -1, yb = -1, larg; // posição atual das réguas
    int falhas;                 // leituras seguidas sem achar a caixa (só some depois de 3)

    public Moldura() { Text = "Claude Code moldura"; }

    public override void Prender(IntPtr terminal)
    {
        wt = terminal;
        calW = calH = -1; celula = 0; yt = yb = -1; falhas = 0;
        Esconder();
    }

    // r1/r2: linhas das réguas na área visível (-1 = não achou a caixa).
    public void Atualizar(int r1, int r2)
    {
        Rectangle c;
        if (!Cliente(out c)) { Esconder(); return; }
        if (r1 < 0) { Falhou(); return; }
        if (c.Width != calW || c.Height != calH || celula <= 0)
        {
            if ((DateTime.Now - ultimaCal).TotalMilliseconds < 700 || !Calibrar(r1, r2)) { Falhou(); return; }
        }
        int nt = offY + (int)Math.Round(r1 * celula), nb = offY + (int)Math.Round(r2 * celula);
        // Nada mudou e conferi há pouco: não lê a tela de novo (ler pixel da tela é caro).
        if (Visible && nt == yt && nb == yb && c.Width == larg && (DateTime.Now - conferido).TotalMilliseconds < 1000)
        { falhas = 0; return; }
        conferido = DateTime.Now;
        // Confere na tela se a régua está mesmo ali (se você rolou o terminal para cima, não está).
        if (!PixelDeLinha(nt) || !PixelDeLinha(nb))
        {
            if ((DateTime.Now - ultimaCal).TotalSeconds > 2 && Calibrar(r1, r2))
            {
                nt = offY + (int)Math.Round(r1 * celula); nb = offY + (int)Math.Round(r2 * celula);
                if (!PixelDeLinha(nt) || !PixelDeLinha(nb)) { Falhou(); return; }
            }
            else { Falhou(); return; }
        }
        falhas = 0;
        int topo = nt - 2, alt = nb - nt + esp + 4;
        bool mudou = nt != yt || nb != yb || c.Width != larg || !Visible;
        yt = nt; yb = nb; larg = c.Width;
        Colocar(c.X, c.Y + topo, c.Width, alt);
        if (mudou)
        {
            IntPtr a = W32.CreateRectRgn(0, 0, xIni, alt);
            IntPtr b = W32.CreateRectRgn(xFim + 1, 0, c.Width, alt);
            W32.CombineRgn(a, a, b, 2);
            W32.DeleteObject(b);
            W32.SetWindowRgn(Handle, a, true);
            Invalidate();
        }
    }

    // Uma leitura ruim (o Claude redesenhando a tela bem na hora) não esconde a moldura:
    // só some depois de 3 seguidas (~1 s), que aí é de verdade (rolou a tela, abriu um menu...).
    void Falhou()
    {
        if (++falhas >= 3) Esconder();
    }

    // Janela arrastada: acompanha na hora, sem ler a tela de novo.
    public override void Posicionar()
    {
        if (!IsHandleCreated || !Visible || yt < 0) return;
        Rectangle c;
        if (!Cliente(out c) || c.Width != calW || c.Height != calH) { Esconder(); return; }
        W32.SetWindowPos(Handle, IntPtr.Zero, c.X, c.Y + yt - 2, 0, 0, W32.SWP_NOSIZE | W32.SWP_NOZORDER | W32.SWP_NOACTIVATE);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(corFundo);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half; // linha de 1 px cai inteira num pixel, igual à do Claude
        float meio = esp / 2f;
        float L = (float)Math.Round(10 * s) + meio, R = larg - (float)Math.Round(10 * s) - meio;
        float T = 2 + meio, B = 2 + (yb - yt) + meio;
        float raio = Math.Min(10 * s, (B - T) / 2f);
        using (GraphicsPath p = Tema.Redondo(new RectangleF(L, T, R - L, B - T), raio))
        using (Pen pen = new Pen(corLinha, esp))
            g.DrawPath(pen, p);
    }

    bool PixelDeLinha(int y)
    {
        Color c = PixelDaTela(xIni + 3, y), d = PixelDaTela(xFim - 3, y);
        return Perto(c, corLinha, 18) && Perto(d, corLinha, 18);
    }

    Color PixelDaTela(int x, int y)
    {
        W32.POINT p = new W32.POINT(); p.X = x; p.Y = y; W32.ClientToScreen(wt, ref p);
        IntPtr dc = W32.GetDC(IntPtr.Zero);
        uint v = W32.GetPixel(dc, p.X, p.Y);
        W32.ReleaseDC(IntPtr.Zero, dc);
        return Color.FromArgb((int)(v & 0xFF), (int)((v >> 8) & 0xFF), (int)((v >> 16) & 0xFF));
    }

    static bool Perto(Color a, Color b, int tol)
    {
        return Math.Abs(a.R - b.R) <= tol && Math.Abs(a.G - b.G) <= tol && Math.Abs(a.B - b.B) <= tol;
    }

    // Tira uma "foto" da janela do terminal e acha nela as réguas: com isso sabe a altura da
    // linha de texto em pixels, onde a régua começa e termina, a cor dela e a cor do fundo.
    bool Calibrar(int r1, int r2)
    {
        ultimaCal = DateTime.Now;
        W32.RECT wr; W32.GetWindowRect(wt, out wr);
        W32.RECT rc; W32.GetClientRect(wt, out rc);
        W32.POINT p0 = new W32.POINT(); W32.ClientToScreen(wt, ref p0);
        int ox = p0.X - wr.L, oy = p0.Y - wr.T;
        int w = wr.R - wr.L, h = wr.B - wr.T;
        if (w <= 0 || h <= 0 || rc.R <= 0 || rc.B <= 0) return false;
        using (Bitmap bmp = new Bitmap(w, h))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr dc = g.GetHdc();
                W32.PrintWindow(wt, dc, 2);
                g.ReleaseHdc(dc);
            }
            Color fundo = bmp.GetPixel(ox + 3, oy + rc.B / 2);
            List<int> linhas = new List<int>(); List<int> espessuras = new List<int>();
            int x1 = ox + rc.R * 3 / 10, x2 = ox + rc.R / 2, x3 = ox + rc.R * 7 / 10;
            int run = 0;
            int fimBusca = oy + rc.B - (int)Math.Round(Rodape.ALTURA * s); // o rodapé fica fora
            for (int y = oy; y < fimBusca; y++)
            {
                Color a = bmp.GetPixel(x1, y), b = bmp.GetPixel(x2, y), c = bmp.GetPixel(x3, y);
                bool regua = Perto(a, b, 4) && Perto(b, c, 4) && !Perto(b, fundo, 30);
                if (regua) run++;
                else if (run > 0) { linhas.Add(y - oy - run); espessuras.Add(run); run = 0; }
            }
            if (linhas.Count < 2 || r2 <= r1) return false;
            int a1 = linhas[linhas.Count - 2], a2 = linhas[linhas.Count - 1];
            float cel = (a2 - a1) / (float)(r2 - r1);
            if (cel < 8 || cel > 80) return false;
            celula = cel;
            offY = a1 - (int)Math.Round(r1 * celula);
            esp = Math.Max(1, espessuras[espessuras.Count - 1]);
            corLinha = bmp.GetPixel(x2, oy + a2);
            corFundo = fundo;
            int yy = oy + a2;
            xIni = 0; while (xIni < rc.R / 2 && !Perto(bmp.GetPixel(ox + xIni, yy), corLinha, 18)) xIni++;
            xFim = rc.R - 1; while (xFim > rc.R / 2 && !Perto(bmp.GetPixel(ox + xFim, yy), corLinha, 18)) xFim--;
            calW = rc.R; calH = rc.B;
            BackColor = corFundo;
            try
            {
                StringBuilder log = new StringBuilder();
                log.AppendLine(DateTime.Now + " cliente " + rc.R + "x" + rc.B + " r1=" + r1 + " r2=" + r2);
                for (int i = 0; i < linhas.Count; i++) log.AppendLine("  régua y=" + linhas[i] + " esp=" + espessuras[i]);
                log.AppendLine("  celula=" + celula + " offY=" + offY + " esp=" + esp + " x=" + xIni + ".." + xFim + " cor=" + corLinha + " fundo=" + corFundo);
                File.WriteAllText(Path.Combine(Host.Pasta, "calibracao.txt"), log.ToString());
            }
            catch (Exception) { }
            return true;
        }
    }
}
