// Barra de abas na barra de título: uma aba por sessão, com o Clawd animado (trabalhando ou
// parado), o título da sessão e o × para fechar; no fim, o + para abrir outra.
// Cobre a parte da barra de título do Terminal e deixa só os botões da janela à direita.
// Arrastar pela barra move a janela (encaixa em meia tela, um quarto ou maximiza nas bordas);
// clique duplo maximiza.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

class Barra : Sobreposta
{
    public const int ALTURA = 40, BOTOES_JANELA = 138; // DIP: altura da barra de título e os 3 botões (46 cada)

    List<RectangleF> rAbas = new List<RectangleF>();
    List<RectangleF> rFechar = new List<RectangleF>();
    List<RectangleF> rAnim = new List<RectangleF>();
    RectangleF rMais;
    int hoverAba = -1, hoverFechar = -1;
    bool hoverMais;
    DateTime ultimoClique = DateTime.MinValue;
    Point pontoClique;
    bool apertado;          // botão apertado no espaço vazio (ainda não virou arrasto)
    bool arrastando;
    Point agarre;           // onde o mouse pegou a janela, a partir do canto dela
    Previa previa;
    // encaixe feito por nós: onde a janela ficou e o tamanho de antes (para voltar ao arrastar)
    static Rectangle Encaixada;
    static Size AntesDoEncaixe;
    Bitmap ampliado;        // rascunho para ampliar o Clawd sem borrar
    MenuSuspenso confirmar;

    // Parte do quadro do Clawd que tem desenho (medido em todos os quadros: colunas 6..54,
    // linhas 4..24), com 1 px de folga. Na aba estreita, só ele (sem a mesa).
    static readonly Rectangle CENA = new Rectangle(5, 3, 51, 23), SO_ELE = new Rectangle(5, 3, 27, 23);
    const int ALTURA_DESENHO = 21;

    public Barra()
    {
        BackColor = Tema.Titulo;
        Text = "Claude Code abas";
    }

    static Host H { get { return Host.Atual; } }

    public override void Posicionar()
    {
        Rectangle c;
        if (!Cliente(out c)) return;
        // janela antiga, com a barra de título do Windows em cima: os botões da janela ficam
        // lá em cima, então as abas ocupam a largura toda (senão aparece texto do terminal ao lado)
        W32.RECT jr; W32.GetWindowRect(wt, out jr);
        bool barraDoWindows = c.Y - jr.T > 20 * s;
        int w = c.Width - (barraDoWindows ? 0 : (int)Math.Round(BOTOES_JANELA * s)), h = (int)Math.Round(ALTURA * s);
        if (w <= 0) return;
        bool mudou = Width != w || Height != h;
        Colocar(c.X, c.Y, w, h);
        if (mudou) Invalidate();
    }

    // só repinta onde o Clawd está (25 vezes por segundo)
    public void Animar()
    {
        if (!Visible) return;
        foreach (RectangleF r in rAnim) Invalidate(Rectangle.Inflate(Rectangle.Round(r), 1, 1));
    }

    // ---------- desenho ----------

    void Medir(Graphics g)
    {
        rAbas.Clear(); rFechar.Clear(); rAnim.Clear();
        List<Sessao> ss = H.Sessoes;
        float x0 = 8 * s, topo = 6 * s, alt = Height - topo;
        float mais = 32 * s;
        float disponivel = Width - x0 - mais - 12 * s;
        float largura = ss.Count == 0 ? 0 : Math.Min(250 * s, Math.Max(44 * s, disponivel / ss.Count));
        float x = x0;
        for (int i = 0; i < ss.Count; i++)
        {
            RectangleF r = new RectangleF(x, topo, largura, alt);
            rAbas.Add(r);
            float fx = r.Right - 20 * s, fy = r.Top + r.Height / 2f;
            rFechar.Add(new RectangleF(fx - 9 * s, fy - 9 * s, 18 * s, 18 * s));
            x += largura;
        }
        rMais = new RectangleF(x + 6 * s, topo + (alt - 28 * s) / 2f, 28 * s, 28 * s);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Tema.Titulo);
        if (H == null) return;
        Medir(g);
        List<Sessao> ss = H.Sessoes;

        for (int i = 0; i < ss.Count; i++)
        {
            Sessao se = ss[i];
            RectangleF r = rAbas[i];
            bool ativa = se == H.Ativa;
            if (ativa || i == hoverAba)
                using (GraphicsPath p = Tema.Aba(r, 8 * s))
                using (SolidBrush b = new SolidBrush(ativa ? Tema.Fundo : Tema.AbaHover)) g.FillPath(b, p);
            else if (i + 1 < ss.Count && ss[i + 1] != H.Ativa && i + 1 != hoverAba)
                using (Pen p = new Pen(Tema.Linha)) g.DrawLine(p, r.Right, r.Top + 10 * s, r.Right, r.Bottom - 8 * s);

            bool mostraFechar = ativa || i == hoverAba;
            float direitaTexto = r.Right - (mostraFechar ? 32 * s : 10 * s);

            // Clawd: o desenho ocupa a altura da aba quase toda (sobra 2 px em cima e embaixo);
            // cena inteira se cabe, só ele (sem a mesa) se a aba é estreita
            float cy = r.Top + r.Height / 2f;
            Rectangle origem = r.Width < 190 * s ? SO_ELE : CENA;
            float z = (r.Height - 4 * s) / ALTURA_DESENHO;
            float aw = origem.Width * z, ah = origem.Height * z;
            RectangleF ra = new RectangleF((float)Math.Round(r.Left + 6 * s), (float)Math.Round(cy - ah / 2f), aw, ah);
            if (ra.Right > direitaTexto) ra = RectangleF.Empty;
            rAnim.Add(ra);
            if (!ra.IsEmpty) PixelArt(g, se.Anim.Quadro(), origem, ra);

            // título (com bolinha laranja se o Claude está esperando você)
            float tx = (ra.IsEmpty ? r.Left + 10 * s : ra.Right + 4 * s);
            if (se.Esperando)
            {
                float rp = 3.2f * s;
                using (SolidBrush b = new SolidBrush(Tema.Terracota)) g.FillEllipse(b, tx, cy - rp, rp * 2, rp * 2);
                tx += rp * 2 + 6 * s;
            }
            if (direitaTexto - tx > 14 * s)
            {
                using (Font f = Tema.Fonte(12.5f * s, ativa))
                using (SolidBrush b = new SolidBrush(ativa ? Tema.Texto : Tema.Apagado))
                using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
                {
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(se.Titulo, f, b, new RectangleF(tx, r.Top, direitaTexto - tx, r.Height), sf);
                }
            }

            if (mostraFechar)
            {
                RectangleF rf = rFechar[i];
                if (i == hoverFechar)
                    using (SolidBrush b = new SolidBrush(Tema.Hover)) g.FillEllipse(b, rf);
                Tema.Xis(g, rf.Left + rf.Width / 2f, rf.Top + rf.Height / 2f, 3.6f * s, s, i == hoverFechar ? Tema.Texto : Tema.Apagado);
            }
        }

        // +
        if (hoverMais)
            using (GraphicsPath p = Tema.Redondo(rMais, 7 * s))
            using (SolidBrush b = new SolidBrush(Tema.AbaHover)) g.FillPath(b, p);
        using (Pen p = new Pen(hoverMais ? Tema.Texto : Tema.Apagado, 1.6f * s))
        {
            float cx = rMais.Left + rMais.Width / 2f, cy = rMais.Top + rMais.Height / 2f, m = 6 * s;
            g.DrawLine(p, cx - m, cy, cx + m, cy);
            g.DrawLine(p, cx, cy - m, cx, cy + m);
        }
    }

    // Pixel art num tamanho quebrado (ex.: 1,4x) sem pixels tortos e sem borrar: amplia antes
    // por um inteiro, pixel a pixel, e depois reduz suavizando até o tamanho certo.
    void PixelArt(Graphics g, Bitmap quadro, Rectangle origem, RectangleF destino)
    {
        int n = Math.Max(1, (int)Math.Ceiling(destino.Height / origem.Height));
        int w = origem.Width * n, h = origem.Height * n;
        if (ampliado == null || ampliado.Width < w || ampliado.Height < h)
        {
            if (ampliado != null) ampliado.Dispose();
            ampliado = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        }
        using (Graphics a = Graphics.FromImage(ampliado))
        {
            a.CompositingMode = CompositingMode.SourceCopy;
            a.InterpolationMode = InterpolationMode.NearestNeighbor;
            a.PixelOffsetMode = PixelOffsetMode.Half;
            a.Clear(Color.Transparent);
            a.DrawImage(quadro, new Rectangle(0, 0, w, h), origem, GraphicsUnit.Pixel);
        }
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(ampliado, destino, new RectangleF(0, 0, w, h), GraphicsUnit.Pixel);
        g.PixelOffsetMode = PixelOffsetMode.Default;
    }

    // ---------- mouse ----------

    int Aba(Point p) { for (int i = 0; i < rAbas.Count; i++) if (rAbas[i].Contains(p)) return i; return -1; }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (arrastando) { Mover(Control.MousePosition); return; }
        if (apertado)
        {
            Size d = SystemInformation.DragSize;
            if ((e.Button & MouseButtons.Left) == 0) apertado = false;
            else if (Math.Abs(e.X - pontoClique.X) > d.Width / 2 || Math.Abs(e.Y - pontoClique.Y) > d.Height / 2)
            {
                apertado = false;
                ComecarArrasto(PointToScreen(pontoClique));
                Mover(Control.MousePosition);
            }
            return;
        }
        int a = Aba(e.Location);
        int f = a >= 0 && rFechar[a].Contains(e.Location) ? a : -1;
        bool m = rMais.Contains(e.Location);
        Cursor = a >= 0 || m ? Cursors.Hand : Cursors.Default;
        if (a != hoverAba || f != hoverFechar || m != hoverMais)
        {
            hoverAba = a; hoverFechar = f; hoverMais = m;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hoverAba = hoverFechar = -1; hoverMais = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        List<Sessao> ss = H.Sessoes;
        int a = Aba(e.Location);
        if (e.Button == MouseButtons.Middle && a >= 0) { PedirFechar(ss[a], a); return; }
        if (e.Button != MouseButtons.Left) return;
        if (a >= 0 && a < ss.Count)
        {
            if (rFechar[a].Contains(e.Location) && (ss[a] == H.Ativa || a == hoverAba)) PedirFechar(ss[a], a);
            else H.Ativar(ss[a]);
            return;
        }
        if (rMais.Contains(e.Location)) { H.Nova(); return; }

        // espaço vazio: como numa barra de título, o clique traz a janela para a frente;
        // clique duplo maximiza/restaura; arrastar move a janela
        if (W32.GetForegroundWindow() != wt) W32.SetForegroundWindow(wt);
        bool duplo = (DateTime.Now - ultimoClique).TotalMilliseconds < SystemInformation.DoubleClickTime
            && Math.Abs(e.X - pontoClique.X) < 6 && Math.Abs(e.Y - pontoClique.Y) < 6;
        ultimoClique = DateTime.Now; pontoClique = e.Location;
        if (duplo)
        {
            ultimoClique = DateTime.MinValue;
            W32.ShowWindow(wt, W32.IsZoomed(wt) ? 9 : 3);
            return;
        }
        apertado = true; // vira arrasto quando o mouse andar (OnMouseMove)
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        apertado = false;
        if (arrastando) TerminarArrasto(true);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (arrastando && !Capture) TerminarArrasto(false); // outra janela pegou o mouse: só para
    }

    // ---------- arrastar a janela ----------
    // A barra move o Terminal atrás do mouse. Pedir ao Windows o arrasto normal não funciona a
    // partir de uma janela de outro programa: o Terminal acha que o botão está solto, não anda,
    // e depois fica grudado no mouse. Então o encaixe do Windows é imitado aqui: mouse na borda
    // esquerda/direita = meia tela, no canto = um quarto, em cima = maximizar, com uma prévia
    // atrás da janela antes de soltar; arrastar de novo devolve o tamanho de antes.

    void ComecarArrasto(Point pegou)
    {
        W32.RECT r;
        if (!W32.GetWindowRect(wt, out r)) return;
        Rectangle atual = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
        W32.WINDOWPLACEMENT wp = new W32.WINDOWPLACEMENT();
        wp.length = System.Runtime.InteropServices.Marshal.SizeOf(typeof(W32.WINDOWPLACEMENT));
        W32.GetWindowPlacement(wt, ref wp);
        W32.RECT rn = W32.DaAreaDeTrabalho(wp.rcNormal);
        Rectangle normal = Rectangle.FromLTRB(rn.L, rn.T, rn.R, rn.B);

        // maximizada ou encaixada (por nós ou pelo Windows): sai com o tamanho de antes
        Size volta = Size.Empty;
        if (W32.IsZoomed(wt)) volta = normal.Size;
        else if (atual == Encaixada && !AntesDoEncaixe.IsEmpty) volta = AntesDoEncaixe;
        else if (normal.Width > 0 && normal != atual) volta = normal.Size;

        agarre = new Point(pegou.X - r.L, pegou.Y - r.T);
        if (!volta.IsEmpty && volta != atual.Size)
        {
            // o mouse fica no mesmo ponto da barra, proporcional à largura
            float fx = (pegou.X - r.L) / (float)Math.Max(1, atual.Width);
            agarre = new Point((int)Math.Round(volta.Width * fx), Math.Max(0, Math.Min(pegou.Y - Top, Height)) + (Top - r.T > 20 * s ? Top - r.T : 0));
            W32.RECT n;
            n.L = pegou.X - agarre.X; n.T = pegou.Y - agarre.Y;
            n.R = n.L + volta.Width; n.B = n.T + volta.Height;
            wp.flags = 0; wp.showCmd = 1;
            wp.rcNormal = W32.ParaAreaDeTrabalho(n);
            W32.SetWindowPlacement(wt, ref wp);
        }
        Encaixada = Rectangle.Empty;
        arrastando = true;
        // por cima das outras já (a ativação pelo clique chega atrasada no Terminal); senão a
        // prévia, que fica logo atrás dele, some atrás das outras janelas
        W32.SetWindowPos(wt, IntPtr.Zero, 0, 0, 0, 0, W32.SWP_NOMOVE | W32.SWP_NOSIZE | W32.SWP_NOACTIVATE);
    }

    void Mover(Point m)
    {
        int x = m.X - agarre.X, y = m.Y - agarre.Y;
        Rectangle area = Screen.FromPoint(m).WorkingArea;
        if (y < area.Top) y = area.Top; // a barra não some para cima da tela
        W32.SetWindowPos(wt, IntPtr.Zero, x, y, 0, 0, W32.SWP_NOSIZE | W32.SWP_NOZORDER | W32.SWP_NOACTIVATE);
        Rectangle alvo; bool max;
        if (Encaixe(m, out alvo, out max))
        {
            if (previa == null || previa.IsDisposed) previa = new Previa();
            previa.Mostrar(alvo, wt, s);
        }
        else if (previa != null) previa.Sumir();
    }

    void TerminarArrasto(bool encaixar)
    {
        arrastando = false;
        if (previa != null) previa.Sumir();
        Rectangle alvo; bool max;
        if (!encaixar || !Encaixe(Control.MousePosition, out alvo, out max)) return;
        if (max) { W32.ShowWindow(wt, 3); return; }
        W32.RECT r, v;
        W32.GetWindowRect(wt, out r);
        AntesDoEncaixe = new Size(r.R - r.L, r.B - r.T);
        // o retângulo da janela inclui as bordas invisíveis de redimensionar; o encaixe é da parte visível
        int bE = 0, bT = 0, bD = 0, bB = 0;
        if (W32.DwmGetWindowAttribute(wt, 9, out v, 16) == 0)
        {
            bE = v.L - r.L; bT = v.T - r.T; bD = r.R - v.R; bB = r.B - v.B;
        }
        W32.SetWindowPos(wt, IntPtr.Zero, alvo.Left - bE, alvo.Top - bT, alvo.Width + bE + bD, alvo.Height + bT + bB,
            W32.SWP_NOZORDER | W32.SWP_NOACTIVATE);
        W32.GetWindowRect(wt, out r);
        Encaixada = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
    }

    // Onde o encaixe do Windows poria a janela com o mouse em m (só nas bordas de fora da tela).
    bool Encaixe(Point m, out Rectangle alvo, out bool max)
    {
        alvo = Rectangle.Empty; max = false;
        Screen tela = Screen.FromPoint(m);
        Rectangle b = tela.Bounds, a = tela.WorkingArea;
        int perto = Math.Max(1, (int)Math.Round(2 * s)), canto = (int)Math.Round(90 * s);
        bool esq = m.X <= b.Left + perto && SemVizinho(b.Left - 8, m.Y, b);
        bool dir = m.X >= b.Right - 1 - perto && SemVizinho(b.Right + 8, m.Y, b);
        bool cima = m.Y <= b.Top + perto && SemVizinho(m.X, b.Top - 8, b);
        if (esq || dir)
        {
            int meio = a.Width / 2, meioY = a.Height / 2;
            int x = esq ? a.Left : a.Left + meio, w = esq ? meio : a.Width - meio;
            if (m.Y <= b.Top + canto) alvo = new Rectangle(x, a.Top, w, meioY);
            else if (m.Y >= b.Bottom - canto) alvo = new Rectangle(x, a.Top + meioY, w, a.Height - meioY);
            else alvo = new Rectangle(x, a.Top, w, a.Height);
            return true;
        }
        if (cima) { alvo = a; max = true; return true; }
        return false;
    }

    static bool SemVizinho(int x, int y, Rectangle desta)
    {
        return Screen.FromPoint(new Point(x, y)).Bounds == desta;
    }

    void PedirFechar(Sessao se, int i)
    {
        if (!se.Ocupado) { H.Fechar(se); return; }
        if (confirmar != null && !confirmar.IsDisposed) confirmar.Close();
        List<Opcao> ops = new List<Opcao> {
            new Opcao("fechar", Txt.T("Fechar sessão", "Close session"), Txt.T("O que ela está fazendo agora para", "What it is doing now stops"), 0),
            new Opcao("cancelar", Txt.T("Cancelar", "Cancel"), Txt.T("Deixar ela terminar", "Let it finish"), 0),
        };
        Rectangle ancora = RectangleToScreen(Rectangle.Round(rAbas[i]));
        confirmar = new MenuSuspenso(Txt.T("Esta sessão está trabalhando", "This session is working"), ops, "", false, s, ancora, true);
        confirmar.Escolheu += delegate (Opcao o) { if (o.Valor == "fechar") H.Fechar(se); };
        confirmar.Show(this);
        confirmar.Activate();
    }
}

// Prévia do encaixe: retângulo translúcido, logo atrás do Terminal, onde ele vai ficar ao soltar.
class Previa : Form
{
    const byte OPACO = 150; // de 255
    readonly Timer fade = new Timer();
    byte alfa;
    bool aberta;
    float s = 1f;

    public Previa()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = Tema.Titulo;
        Text = "Claude Code encaixe";
        fade.Interval = 15;
        fade.Tick += delegate
        {
            alfa = (byte)Math.Min(OPACO, alfa + 22);
            W32.SetLayeredWindowAttributes(Handle, 0, alfa, 2);
            if (alfa >= OPACO) fade.Stop();
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.Style = unchecked((int)0x80000000);            // WS_POPUP
            cp.ExStyle = (cp.ExStyle | 0x08000000 | 0x80 | 0x20 | 0x80000) & ~0x40000;  // NOACTIVATE, TOOLWINDOW, TRANSPARENT (clique passa), LAYERED; sem APPWINDOW
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    public void Mostrar(Rectangle r, IntPtr atrasDe, float escala)
    {
        s = escala;
        bool novo = !aberta || Bounds != r;
        if (novo)
        {
            alfa = 0;
            W32.SetLayeredWindowAttributes(Handle, 0, 0, 2);
            int raio = (int)Math.Round(16 * s);
            W32.SetWindowRgn(Handle, W32.CreateRoundRectRgn(0, 0, r.Width + 1, r.Height + 1, raio, raio), true);
        }
        // logo abaixo do Terminal: aparece atrás da janela que está sendo arrastada
        W32.SetWindowPos(Handle, atrasDe, r.X, r.Y, r.Width, r.Height, W32.SWP_NOACTIVATE | W32.SWP_SHOWWINDOW);
        aberta = true;
        if (novo) { Invalidate(); fade.Start(); }
    }

    public void Sumir()
    {
        if (!aberta) return;
        aberta = false;
        fade.Stop();
        W32.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            W32.SWP_NOMOVE | W32.SWP_NOSIZE | W32.SWP_NOZORDER | W32.SWP_NOACTIVATE | W32.SWP_HIDEWINDOW);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(0x4A, 0x3A, 0x31));
        float b = 3 * s;
        RectangleF r = new RectangleF(b / 2, b / 2, Width - b - 1, Height - b - 1);
        using (GraphicsPath p = Tema.Redondo(r, 15 * s))
        using (Pen pen = new Pen(Tema.Terracota, b)) g.DrawPath(pen, p);
    }
}
