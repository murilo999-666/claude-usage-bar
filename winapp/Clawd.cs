// O Clawd trabalhando, em pixel art (60 x 29 px).
// Estados: parado, virando, trabalhando, terminando, voltando, comemorando.
// Uso: Trabalhar() / Parar(), Avancar(ms) a cada quadro e Quadro() devolve o Bitmap.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

class Clawd
{
    public const int W = 60, H = 29;

    // ---------------- 1. o desenho (blocos de terminal do Claude Code) ----------------
    const int SL = 18, SA = 12;
    const byte VAZIO = 0, CORPO = 1, OLHO = 2;
    static readonly string QUADS = " ▘▝▀▖▌▞▛▗▚▐▜▄▙▟█";

    struct Bracos { public string r1e, r1d, r2e, r2d; public Bracos(string a, string b, string c, string d) { r1e = a; r1d = b; r2e = c; r2d = d; } }
    static readonly Dictionary<string, Bracos> BRACOS = new Dictionary<string, Bracos> {
        { "baixo",  new Bracos(" ▐", "",  "▝▜", "█▀") },
        { "cima",   new Bracos("▗▟", "▄", " ▜", "█▘") },
    };
    static readonly Dictionary<string, string> OLHOS = new Dictionary<string, string> {
        { "abertos", "▛███▛█" }, { "esquerda", "▟███▟█" }, { "direita", "█▟███▟" },
        { "fechados", "_███_█" },
    };
    const string PES = " ▝▝   ▝▝ ";

    static byte[][] Grade()
    {
        byte[][] g = new byte[SA][];
        for (int y = 0; y < SA; y++) g[y] = new byte[SL];
        return g;
    }

    static byte[][] Copiar(byte[][] g)
    {
        byte[][] n = new byte[SA][];
        for (int y = 0; y < SA; y++) n[y] = (byte[])g[y].Clone();
        return n;
    }

    static void PintarLinha(byte[][] g, int linha, string texto, int col0, int pretoDe, int pretoAte)
    {
        for (int i = 0; i < texto.Length; i++)
        {
            char ch = texto[i];
            int c = col0 + i, x = c * 2, y = linha * 4;
            if (ch == '_')
            {
                for (int dy = 0; dy < 4; dy++) g[y + dy][x] = g[y + dy][x + 1] = dy == 3 ? OLHO : CORPO;
                continue;
            }
            int bits = QUADS.IndexOf(ch);
            bool preto = c >= pretoDe && c < pretoAte;
            for (int q = 0; q < 4; q++)
            {
                byte v = ((bits >> q) & 1) != 0 ? CORPO : (preto ? OLHO : VAZIO);
                if (v == 0) continue;
                int qx = x + (q & 1), qy = y + (q >> 1) * 2;
                g[qy][qx] = v;
                g[qy + 1][qx] = v;
            }
        }
    }

    static readonly Dictionary<string, byte[][]> cacheFrente = new Dictionary<string, byte[][]>();
    static byte[][] PoseFrente(string bracos, string olhos)
    {
        string chave = bracos + "|" + olhos;
        byte[][] g;
        if (cacheFrente.TryGetValue(chave, out g)) return g;
        g = Grade();
        Bracos b = BRACOS[bracos];
        PintarLinha(g, 0, b.r1e, 0, 0, 0);
        PintarLinha(g, 0, OLHOS[olhos], 2, 2, 8);
        if (b.r1d.Length > 0) PintarLinha(g, 0, b.r1d, 8, 0, 0);
        PintarLinha(g, 1, b.r2e, 0, 0, 0);
        PintarLinha(g, 1, "█████", 2, 2, 7);
        PintarLinha(g, 1, b.r2d, 7, 0, 0);
        PintarLinha(g, 2, PES, 0, 0, 0);
        cacheFrente[chave] = g;
        return g;
    }

    static byte[][] PoseGiro(string[] txt, int[] de, int[] ate)
    {
        byte[][] g = Grade();
        for (int i = 0; i < 3; i++) PintarLinha(g, i, txt[i], 0, de[i], ate[i]);
        return g;
    }

    // frente -> 12° -> 30° -> 55° (de 3/4, olhando pro monitor)
    static readonly byte[][][] QUADROS = {
        null,
        PoseGiro(new[] { " ▐█▜██▛█ ", "▝▜██████▀", " ▝▝   ▝▝ " }, new[] { 2, 2, 0 }, new[] { 8, 7, 0 }),
        PoseGiro(new[] { "  █▛██▛▌ ", " ▝█████▛ ", "  ▘▘  ▘▘ " }, new[] { 2, 2, 0 }, new[] { 7, 7, 0 }),
        PoseGiro(new[] { "  ▐█▛█▜  ", "  ▐████  ", "  ▝▝ ▝▝  " }, new[] { 3, 3, 0 }, new[] { 7, 7, 0 }),
    };
    const int ULTIMO = 3;

    static readonly Dictionary<byte[][], byte[][]> cachePisca = new Dictionary<byte[][], byte[][]>();
    static readonly Dictionary<byte[][], byte[][]> cacheCima = new Dictionary<byte[][], byte[][]>();

    static byte[][] Piscar(byte[][] g)
    {
        byte[][] n;
        if (cachePisca.TryGetValue(g, out n)) return n;
        n = Copiar(g);
        Dictionary<int, int> colunas = new Dictionary<int, int>();
        for (int y = 0; y < 8; y++) for (int x = 0; x < SL; x++)
                if (g[y][x] == OLHO) { colunas[x] = y; n[y][x] = CORPO; }
        foreach (KeyValuePair<int, int> kv in colunas)
        {
            int x = kv.Key, yb = kv.Value;
            n[yb][x] = OLHO;
            if (x > 0 && n[yb][x - 1] == CORPO) n[yb][x - 1] = OLHO;
        }
        cachePisca[g] = n;
        return n;
    }

    static byte[][] OlharPraCima(byte[][] g)
    {
        byte[][] n;
        if (cacheCima.TryGetValue(g, out n)) return n;
        n = Copiar(g);
        List<int[]> olhos = new List<int[]>();
        for (int y = 0; y < 8; y++) for (int x = 0; x < SL; x++)
                if (g[y][x] == OLHO) { olhos.Add(new[] { x, y }); n[y][x] = CORPO; }
        foreach (int[] o in olhos) if (o[1] - 2 >= 0 && n[o[1] - 2][o[0]] == CORPO) n[o[1] - 2][o[0]] = OLHO;
        cacheCima[g] = n;
        return n;
    }

    // spinner do Claude Code (· ✢ * ✶ ✻ ✽, ida e volta) em 5x5
    static readonly string[][] FAISCA = {
        new[] { ".....", ".....", "..#..", ".....", "....." },
        new[] { ".....", "..#..", ".###.", "..#..", "....." },
        new[] { "..#..", "..#..", "#####", "..#..", "..#.." },
        new[] { "..#..", ".###.", "##+##", ".###.", "..#.." },
        new[] { "#.#.#", ".###.", "##+##", ".###.", "#.#.#" },
        new[] { "#.#.#", ".#+#.", "#+++#", ".#+#.", "#.#.#" },
    };
    static readonly int[] SEQ_FAISCA = { 0, 1, 2, 3, 4, 5, 4, 3, 2, 1 };

    // ---------------- 2. cores e cenário ----------------
    static int C(int rgb) { return unchecked((int)0xFF000000) | rgb; }
    static readonly int Corpo = C(0xD77757), CorpoSombra = C(0xA9583F), Olho = C(0x000000), FaiscaLuz = C(0xF6C9B2),
        MesaTopo = C(0xC9A27C), MesaFrente = C(0x9C7556), MesaPe = C(0x7C5B42),
        Moldura = C(0x3B3A37), MolduraLuz = C(0x5A5953), TelaDesligada = C(0x181817), TelaLigada = C(0x1E2129),
        Teclas = C(0x8C8A83), Teclas2 = C(0x5E5D58), TecladoBase = C(0x3B3A37),
        LedLigado = C(0xD77757), LedApagado = C(0x55534D), Cursor = C(0xE8E6DC),
        KW = C(0xD77757), ID = C(0xE8E6DC), FN = C(0x6A9BCC), STR = C(0x97B07A), PONT = C(0x9C9A92), COM = C(0x6B6A64), OK = C(0x7FB069);
    const int Sombra = 0x21000000; // preto com 13% de opacidade

    const int X0 = 5, Y0 = 14, PASSO = 5;
    const int MESA_X0 = X0 + 20, MESA_X1 = X0 + 49, MESA_Y = Y0 + 6;
    const int TEC_X0 = X0 + 21, TEC_X1 = X0 + 29, TEC_Y = Y0 + 4;
    const int MON_X0 = X0 + 30, MON_X1 = X0 + 46, MON_Y0 = Y0 - 10, MON_Y1 = Y0 + 2;
    const int TEXTO_X = MON_X0 + 2, TEXTO_Y = MON_Y0 + 2, LARG_TEXTO = 13, VISIVEIS = 5;

    // ---------------- 3. ritmo ----------------
    const double MOLA = 11, VEL_BRACOS = 7, VEL_TELA = 2, TELA_PARADO = 0.35;
    const double ANTECIPA = 90, DUR_COMEMORA = 640, RESPIRA = 2400;

    // ---------------- utilidades ----------------
    uint semente;
    double Rand()
    {
        unchecked
        {
            semente += 0x6D2B79F5;
            uint t = semente;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + (t ^ (t >> 7)) * (t | 61);
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    static double Aproximar(double v, double alvo, double passo)
    {
        if (v < alvo) return Math.Min(alvo, v + passo);
        if (v > alvo) return Math.Max(alvo, v - passo);
        return v;
    }

    static int Misturar(int a, int b, double k)
    {
        int r = (int)Math.Round(((a >> 16) & 255) + (((b >> 16) & 255) - ((a >> 16) & 255)) * k);
        int g = (int)Math.Round(((a >> 8) & 255) + (((b >> 8) & 255) - ((a >> 8) & 255)) * k);
        int bb = (int)Math.Round((a & 255) + ((b & 255) - (a & 255)) * k);
        return C((r << 16) | (g << 8) | bb);
    }

    static int Arred(double v) { return (int)Math.Floor(v + 0.5); }

    // ---------------- 4. o "código" no monitor ----------------
    class Linha { public List<int> Alvo = new List<int>(), Px = new List<int>(); public bool Prompt; }

    class Codigo
    {
        readonly Clawd c;
        public List<Linha> Linhas = new List<Linha>();
        int nivel;
        public double Rolagem;

        public Codigo(Clawd c)
        {
            this.c = c;
            for (int i = 0; i < 3; i++) Linhas.Add(Gerar(true));
            Linhas.Add(PromptNovo());
        }

        public Linha Atual { get { return Linhas[Linhas.Count - 1]; } }

        static Linha PromptNovo() { Linha l = new Linha(); l.Alvo.Add(KW); l.Px.Add(KW); l.Prompt = true; return l; }

        int Ri(int min, int max) { return min + (int)Math.Floor(c.Rand() * (max - min + 1)); }

        Linha Gerar(bool cheia)
        {
            List<int> a = new List<int>();
            Action<int, int> seg = delegate (int cor, int n) { for (int i = 0; i < n; i++) a.Add(cor); };
            double x = c.Rand();
            string tipo;
            if (nivel > 0 && x < 0.2) tipo = "fecha";
            else if (nivel < 3 && x < 0.42) tipo = "abre";
            else if (x < 0.54) tipo = "comentario";
            else if (x < 0.6) tipo = "vazia";
            else tipo = c.Rand() < 0.5 ? "atrib" : "chamada";
            if (tipo == "fecha") nivel--;
            for (int i = 0; i < nivel * 2; i++) a.Add(0);
            switch (tipo)
            {
                case "fecha": seg(PONT, 1); break;
                case "abre": seg(KW, Ri(2, 3)); a.Add(0); seg(FN, Ri(2, 4)); seg(PONT, 1); seg(ID, Ri(1, 2)); seg(PONT, 1); a.Add(0); seg(PONT, 1); break;
                case "comentario": seg(COM, 2); a.Add(0); seg(COM, Ri(3, 7)); break;
                case "atrib": seg(KW, Ri(2, 3)); a.Add(0); seg(ID, Ri(2, 4)); a.Add(0); seg(PONT, 1); a.Add(0); seg(c.Rand() < 0.5 ? STR : FN, Ri(2, 4)); break;
                case "chamada": seg(FN, Ri(3, 5)); seg(PONT, 1); seg(STR, Ri(2, 4)); seg(PONT, 1); break;
            }
            if (tipo == "abre") nivel++;
            if (a.Count > LARG_TEXTO) a.RemoveRange(LARG_TEXTO, a.Count - LARG_TEXTO);
            while (a.Count > 0 && a[a.Count - 1] == 0) a.RemoveAt(a.Count - 1);
            Linha l = new Linha();
            l.Alvo = a;
            if (cheia) l.Px = new List<int>(a);
            return l;
        }

        public string Tecla()
        {
            Linha l = Atual;
            if (l.Prompt || l.Px.Count >= l.Alvo.Count)
            {
                Linhas.Add(Gerar(false));
                Podar();
                return "enter";
            }
            int cor = l.Alvo[l.Px.Count];
            l.Px.Add(cor);
            return cor != 0 ? "letra" : "espaco";
        }

        public void Pronto()
        {
            Linha l = Atual;
            if (l != null && !l.Prompt && l.Px.Count == 0) Linhas.RemoveAt(Linhas.Count - 1);
            Linha ok = new Linha();
            ok.Alvo.AddRange(new[] { OK, 0, ID, ID, ID, ID, ID });
            ok.Px.AddRange(ok.Alvo);
            Linhas.Add(ok);
            Linhas.Add(PromptNovo());
            Podar();
        }

        void Podar()
        {
            int sobra = Linhas.Count - 12;
            if (sobra > 0) { Linhas.RemoveRange(0, sobra); Rolagem -= sobra; }
        }

        public void Passo(double dt)
        {
            double alvo = Math.Max(0, Linhas.Count - VISIVEIS);
            Rolagem = Aproximar(Rolagem, alvo, dt / 110);
        }
    }

    // ---------------- 5. o Clawd ----------------
    class Tecla { public double Ini, Fim, Aperto; public int Braco; public int[] Alcance; public bool PensarDepois; }

    public string Estado { get { return estado; } }
    string estado = "parado";
    bool quer;
    double t, tEstado;
    double giro, vGiro, bracos, tela = TELA_PARADO, faisca;
    double antecipaAte = -1;
    Tecla tecla; int braco = 2;
    bool pensando; double fimPensar;
    int digitou; double tempoDigitando;
    double proxPiscada, piscaAte = -1, proxOlhar;
    string olharLado; double olharFim;
    readonly Codigo codigo;

    readonly int[] buf = new int[W * H];
    readonly Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
    bool sujo = true;

    public Clawd(int seed)
    {
        semente = (uint)seed;
        codigo = new Codigo(this);
        proxPiscada = 1500 + Rand() * 2500;
        proxOlhar = 3500 + Rand() * 4000;
    }

    public void Trabalhar() { quer = true; }
    public void Parar() { quer = false; }

    public void Avancar(double ms)
    {
        double resto = Math.Min(ms, 200);
        while (resto > 0)
        {
            double d = Math.Min(1000.0 / 120, resto);
            Passo(d);
            resto -= d;
        }
        sujo = true;
    }

    void Ir(string novo)
    {
        if (estado == novo) return;
        estado = novo;
        tEstado = t;
        if (novo == "parado")
        {
            digitou = 0;
            tempoDigitando = 0;
            proxOlhar = t + 3000 + Rand() * 4000;
        }
    }

    void Passo(double dt)
    {
        t += dt;
        double s = dt / 1000;
        switch (estado)
        {
            case "parado":
                if (quer) { antecipaAte = t + ANTECIPA; Ir("virando"); }
                break;
            case "virando":
                if (!quer) Ir("voltando");
                else if (giro > 0.97) Ir("trabalhando");
                break;
            case "trabalhando":
                if (!quer && PodeSoltar()) { tecla = null; pensando = false; Ir("terminando"); }
                break;
            case "terminando":
                if (quer) Ir("trabalhando");
                else if (bracos <= 0)
                {
                    if (digitou > 0) codigo.Pronto();
                    Ir("voltando");
                }
                break;
            case "voltando":
                if (quer) Ir("virando");
                else if (giro < 0.03) Ir(tempoDigitando >= 1000 ? "comemorando" : "parado");
                break;
            case "comemorando":
                if (t - tEstado >= DUR_COMEMORA) Ir("parado");
                break;
        }

        string e = estado;
        bool ligado = e == "virando" || e == "trabalhando" || e == "terminando";
        double alvoGiro = ligado && t >= antecipaAte ? 1 : 0;
        double acel = MOLA * MOLA * (alvoGiro - giro) - 2 * MOLA * vGiro;
        vGiro += acel * s;
        giro += vGiro * s;

        double alvoBracos = e == "trabalhando" ? (pensando ? 0.4 : 1) : 0;
        bracos = Aproximar(bracos, alvoBracos, s * VEL_BRACOS);
        tela = Aproximar(tela, ligado ? 1 : TELA_PARADO, s * VEL_TELA);
        faisca = Aproximar(faisca, e == "trabalhando" || e == "terminando" ? 1 : 0, s * 4);

        if (e == "trabalhando") Digitar(dt);
        codigo.Passo(dt);

        if (e != "comemorando" && t >= proxPiscada)
        {
            piscaAte = t + 120;
            bool dupla = Rand() < 0.22;
            proxPiscada = t + (dupla ? 260 : 1800 + Rand() * 3600);
        }
        if (e == "parado")
        {
            if (olharLado == null && t >= proxOlhar)
            {
                olharLado = Rand() < 0.5 ? "esquerda" : "direita";
                olharFim = t + 800 + Rand() * 900;
            }
            if (olharLado != null && t >= olharFim)
            {
                olharLado = null;
                proxOlhar = t + 4500 + Rand() * 6000;
            }
        }
        else olharLado = null;
    }

    bool PodeSoltar()
    {
        return tecla == null || pensando || t - tecla.Ini >= tecla.Aperto;
    }

    void Digitar(double dt)
    {
        if (pensando)
        {
            if (t < fimPensar && quer) return;
            pensando = false;
        }
        if (bracos < 0.999) return;
        tempoDigitando += dt;
        if (tecla != null && t < tecla.Fim) return;
        if (!quer) { tecla = null; return; }
        if (tecla != null && tecla.PensarDepois)
        {
            tecla = null;
            pensando = true;
            fimPensar = t + 700 + Rand() * 1000;
            return;
        }
        string tipo = codigo.Tecla();
        digitou++;
        braco = braco == 1 ? 2 : 1;
        double dur = tipo == "enter" ? 200 : 80 + Rand() * 90;
        Tecla k = new Tecla();
        k.Ini = t; k.Fim = t + dur; k.Aperto = dur * 0.55;
        k.Braco = tipo == "enter" ? 1 : braco;
        k.Alcance = new[] { 3 + (int)Math.Floor(Rand() * 2), 4 + (int)Math.Floor(Rand() * 2) };
        k.PensarDepois = tipo == "enter" && digitou > 2 && Rand() < 0.3;
        tecla = k;
    }

    // ---------------- desenho ----------------
    void Ret(int cor, int x, int y, int w, int h)
    {
        int a = (cor >> 24) & 255;
        for (int yy = Math.Max(0, y); yy < Math.Min(H, y + h); yy++)
            for (int xx = Math.Max(0, x); xx < Math.Min(W, x + w); xx++)
            {
                int i = yy * W + xx;
                if (a == 255) { buf[i] = cor; continue; }
                buf[i] = Sobre(cor, buf[i]);
            }
    }

    void RetAlfa(int cor, int x, int y, int w, int h, double alfa)
    {
        int a = (int)Math.Round(((cor >> 24) & 255) * alfa);
        Ret((a << 24) | (cor & 0xFFFFFF), x, y, w, h);
    }

    // "src over" com o destino que pode ser transparente
    static int Sobre(int src, int dst)
    {
        double sa = ((src >> 24) & 255) / 255.0, da = ((dst >> 24) & 255) / 255.0;
        double oa = sa + da * (1 - sa);
        if (oa <= 0) return 0;
        int r = (int)Math.Round((((src >> 16) & 255) * sa + ((dst >> 16) & 255) * da * (1 - sa)) / oa);
        int g = (int)Math.Round((((src >> 8) & 255) * sa + ((dst >> 8) & 255) * da * (1 - sa)) / oa);
        int b = (int)Math.Round(((src & 255) * sa + (dst & 255) * da * (1 - sa)) / oa);
        return ((int)Math.Round(oa * 255) << 24) | (r << 16) | (g << 8) | b;
    }

    struct Pose { public int Quadro, Dx, Agacha, Pulo; public string Bracos, Olhos; public bool Piscando, Pensando; }

    Pose PoseAtual()
    {
        double g = Math.Min(1, Math.Max(0, giro));
        Pose p = new Pose();
        p.Quadro = Arred(g * ULTIMO); p.Dx = Arred(g * PASSO);
        p.Bracos = "baixo"; p.Olhos = "abertos";
        p.Piscando = t < piscaAte; p.Pensando = pensando && estado == "trabalhando";
        if (estado == "parado")
        {
            if (t % RESPIRA >= RESPIRA * 0.6) p.Agacha = 1;
            if (olharLado != null) p.Olhos = olharLado;
        }
        if (estado == "virando" && t < antecipaAte) p.Agacha = 1;
        if ((estado == "virando" || estado == "voltando") && Math.Abs(vGiro) > 1.6) p.Pulo = 1;
        if (estado == "comemorando")
        {
            double k = t - tEstado;
            if (k < 90) p.Agacha = 1;
            else if (k < 460) { p.Bracos = "cima"; p.Olhos = "fechados"; p.Pulo = k < 200 ? 1 : k < 360 ? 2 : 1; }
            else if (k < 560) { p.Agacha = 1; p.Olhos = "fechados"; }
            p.Piscando = false;
        }
        if (p.Piscando) p.Olhos = "fechados";
        return p;
    }

    public Bitmap Quadro()
    {
        if (!sujo) return bmp;
        sujo = false;
        Array.Clear(buf, 0, buf.Length);
        Pose p = PoseAtual();
        Cenario();
        Corpo_(p);
        Faisca(p);
        // buf é ARGB direto; o bitmap é pré-multiplicado
        BitmapData d = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        int[] pm = new int[buf.Length];
        for (int i = 0; i < buf.Length; i++)
        {
            int v = buf[i], a = (v >> 24) & 255;
            if (a == 255 || a == 0) { pm[i] = a == 0 ? 0 : v; continue; }
            int r = ((v >> 16) & 255) * a / 255, g = ((v >> 8) & 255) * a / 255, b = (v & 255) * a / 255;
            pm[i] = (a << 24) | (r << 16) | (g << 8) | b;
        }
        for (int y = 0; y < H; y++) Marshal.Copy(pm, y * W, d.Scan0 + y * d.Stride, W);
        bmp.UnlockBits(d);
        return bmp;
    }

    void Cenario()
    {
        int largMesa = MESA_X1 - MESA_X0 + 1;
        Ret(Sombra, MESA_X0 + 1, Y0 + 10, largMesa - 2, 1);
        Ret(MesaPe, MESA_X0 + 1, MESA_Y + 2, 1, 2);
        Ret(MesaPe, MESA_X1 - 1, MESA_Y + 2, 1, 2);
        Ret(MesaTopo, MESA_X0, MESA_Y, largMesa, 1);
        Ret(MesaFrente, MESA_X0, MESA_Y + 1, largMesa, 1);

        Ret(Moldura, MON_X0 + 7, MON_Y1 + 1, 3, 2);
        Ret(Moldura, MON_X0 + 5, MON_Y1 + 3, 7, 1);
        Ret(Moldura, MON_X0, MON_Y0, MON_X1 - MON_X0 + 1, MON_Y1 - MON_Y0 + 1);
        Ret(MolduraLuz, MON_X0, MON_Y0, MON_X1 - MON_X0 + 1, 1);
        Ret(tela > 0.6 ? LedLigado : LedApagado, MON_X1 - 2, MON_Y1, 1, 1);
        int tx = MON_X0 + 1, ty = MON_Y0 + 1, tw = MON_X1 - MON_X0 - 1, th = MON_Y1 - MON_Y0 - 1;
        Ret(Misturar(TelaDesligada, TelaLigada, tela), tx, ty, tw, th);

        double alfa = 0.35 + 0.65 * tela;
        List<Linha> linhas = codigo.Linhas;
        for (int i = 0; i < linhas.Count; i++)
        {
            int y = TEXTO_Y + Arred((i - codigo.Rolagem) * 2);
            if (y < ty || y >= ty + th) continue;
            for (int k = 0; k < linhas[i].Px.Count; k++)
            {
                int c = linhas[i].Px[k];
                if (c != 0 && TEXTO_X + k < tx + tw) RetAlfa(c, TEXTO_X + k, y, 1, 1, alfa);
            }
        }
        Linha atual = codigo.Atual;
        if (Math.Floor(t / 530) % 2 == 0)
        {
            int cx = TEXTO_X + (atual.Prompt ? 2 : atual.Px.Count);
            int cy = TEXTO_Y + Arred((linhas.Count - 1 - codigo.Rolagem) * 2);
            if (cy >= ty && cy < ty + th && cx < tx + tw) RetAlfa(Cursor, cx, cy, 1, 1, alfa);
        }

        for (int x = TEC_X0; x <= TEC_X1; x++) Ret((x - TEC_X0) % 2 == 1 ? Teclas2 : Teclas, x, TEC_Y, 1, 1);
        Ret(TecladoBase, TEC_X0, TEC_Y + 1, TEC_X1 - TEC_X0 + 1, 1);
    }

    void Grade_(byte[][] g, int ox, int oy, int yDe, int yAte, int desce)
    {
        for (int y = yDe; y < yAte; y++) for (int x = 0; x < SL; x++)
            {
                byte v = g[y][x];
                if (v == 0) continue;
                Ret(v == OLHO ? Olho : Corpo, ox + x, oy + y + desce, 1, 1);
            }
    }

    void Corpo_(Pose p)
    {
        byte[][] g;
        if (p.Quadro == 0) g = PoseFrente(p.Bracos, p.Olhos);
        else
        {
            g = QUADROS[p.Quadro];
            if (p.Piscando) g = Piscar(g);
            else if (p.Pensando && p.Quadro == ULTIMO) g = OlharPraCima(g);
        }
        int ox = X0 + p.Dx, oy = Y0 - p.Pulo;
        int a = SL, b = -1;
        for (int x = 0; x < SL; x++) if (g[7][x] != 0) { a = Math.Min(a, x); b = Math.Max(b, x); }
        Ret(Sombra, ox + a + p.Pulo, Y0 + 10, b - a + 1 - 2 * p.Pulo, 1);

        Grade_(g, ox, oy, 8, SA, 0);
        Grade_(g, ox, oy, 0, 8, p.Agacha);

        if (p.Quadro == ULTIMO && bracos > 0.01)
        {
            int apertando = tecla != null && t - tecla.Ini < tecla.Aperto ? tecla.Braco : 0;
            int[] alc = tecla != null ? tecla.Alcance : new[] { 3, 4 };
            int yy = oy + p.Agacha;
            Ret(CorpoSombra, ox + 14, yy + (apertando == 2 ? 3 : 2), Arred(bracos * alc[1]), 2);
            Ret(Corpo, ox + 14, yy + (apertando == 1 ? 4 : 3), Arred(bracos * alc[0]), 2);
        }
    }

    void Faisca(Pose p)
    {
        if (faisca <= 0.01) return;
        double passo = pensando ? 70 : 110;
        string[] f = FAISCA[SEQ_FAISCA[(int)Math.Floor(t / passo) % SEQ_FAISCA.Length]];
        int cx = X0 + p.Dx + 7, cy = Y0 - 8 - p.Pulo;
        for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++)
            {
                char ch = f[y][x];
                if (ch != '.') RetAlfa(ch == '+' ? FaiscaLuz : Corpo, cx + x, cy + y, 1, 1, faisca);
            }
    }
}
