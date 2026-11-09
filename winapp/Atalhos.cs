// Atalhos de teclado nas janelas do Claude (as do Terminal portátil, que só roda Claude):
// - Ctrl+→ / Ctrl+←: vai para a aba da direita / da esquerda (dá a volta no fim).
// - Ctrl+V com imagem: o Terminal cola só texto e o Claude no Windows cola imagem com Alt+V;
//   se a área de transferência tem imagem e nenhum texto, o Ctrl+V vira Alt+V.
// O gancho de teclado roda numa thread só dele, para o resto do gerenciador nunca atrasar o teclado.
using System;
using System.Runtime.InteropServices;
using System.Threading;

static class Atalhos
{
    static long processo;        // o Terminal portátil; Host.Vigiar atualiza
    static long anfitriao;       // janela invisível do gerenciador (HostJanela), que troca de aba
    static uint fio;
    static HookProc chamada;     // guardado aqui para o coletor de lixo não levar
    const uint COLAR_IMAGEM = 0x8001; // para a thread do gancho

    public static uint Processo
    {
        get { return (uint)Interlocked.Read(ref processo); }
        set { Interlocked.Exchange(ref processo, value); }
    }

    public static void Ligar(IntPtr janelaDoGerenciador)
    {
        Interlocked.Exchange(ref anfitriao, janelaDoGerenciador.ToInt64());
        Thread t = new Thread(Rodar);
        t.IsBackground = true;
        t.Name = "Atalhos";
        t.Start();
    }

    static void Rodar()
    {
        fio = GetCurrentThreadId();
        chamada = Gancho;
        IntPtr h = SetWindowsHookEx(13, chamada, GetModuleHandle(null), 0); // WH_KEYBOARD_LL
        if (h == IntPtr.Zero) return;
        MSG m;
        while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0)
        {
            // fora do gancho: mandar teclas de dentro dele é pedir problema
            if (m.message == COLAR_IMAGEM) Teclado.AltV();
        }
        UnhookWindowsHookEx(h);
    }

    static IntPtr Gancho(int codigo, IntPtr w, IntPtr l)
    {
        if (codigo == 0 && (int)w == 0x100) // WM_KEYDOWN (vale também tecla simulada: acesso remoto manda assim)
        {
            KBD k = (KBD)Marshal.PtrToStructure(l, typeof(KBD));
            bool soCtrl = Apertada(0x11) && !Apertada(0x12) && !Apertada(0x10) && !Apertada(0x5B) && !Apertada(0x5C);
            if (soCtrl && (k.vk == 0x25 || k.vk == 0x27 || k.vk == 0x56) && DoTerminal(GetForegroundWindow()))
            {
                if (k.vk == 0x56)
                {
                    // o nosso Alt+V não volta para cá porque solta o Ctrl antes
                    if (SoImagem()) { PostThreadMessage(fio, COLAR_IMAGEM, IntPtr.Zero, IntPtr.Zero); return (IntPtr)1; }
                }
                else
                {
                    PostMessage((IntPtr)Interlocked.Read(ref anfitriao), HostJanela.PROXIMA,
                        (IntPtr)(k.vk == 0x27 ? 1 : -1), IntPtr.Zero);
                    return (IntPtr)1; // não chega no Claude
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero, codigo, w, l);
    }

    static bool Apertada(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

    static bool DoTerminal(IntPtr h)
    {
        uint pid, alvo = Processo;
        if (h == IntPtr.Zero || alvo == 0) return false;
        GetWindowThreadProcessId(h, out pid);
        return pid == alvo;
    }

    static uint png;
    static bool SoImagem()
    {
        if (IsClipboardFormatAvailable(13) || IsClipboardFormatAvailable(1)) return false; // tem texto
        if (IsClipboardFormatAvailable(15)) return false;                                   // arquivos: o Terminal cola o caminho
        if (png == 0) png = RegisterClipboardFormat("PNG");
        return IsClipboardFormatAvailable(8) || IsClipboardFormatAvailable(17) || IsClipboardFormatAvailable(2)
            || (png != 0 && IsClipboardFormatAvailable(png));
    }

    delegate IntPtr HookProc(int codigo, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct KBD { public uint vk, scan, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr w, l; public uint time; public int x, y; }

    [DllImport("user32")] static extern IntPtr SetWindowsHookEx(int tipo, HookProc p, IntPtr mod, uint fio);
    [DllImport("user32")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32")] static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
    [DllImport("user32")] static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
    [DllImport("user32")] static extern bool PostThreadMessage(uint fio, uint m, IntPtr w, IntPtr l);
    [DllImport("user32")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32")] static extern bool IsClipboardFormatAvailable(uint f);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string nome);
    [DllImport("kernel32")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string nome);
}
