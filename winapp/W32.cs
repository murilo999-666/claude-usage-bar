// Funções do Windows usadas por todas as partes do launcher.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

static class W32
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length, flags, showCmd;
        public POINT ptMin, ptMax;
        public RECT rcNormal;
    }

    public delegate bool EnumProc(IntPtr h, IntPtr l);
    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr h, int obj, int filho, uint th, uint t);

    [DllImport("user32")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] public static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string nome);
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
    [DllImport("user32")] public static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT wp);
    [DllImport("user32")] public static extern bool SetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT wp);
    [DllImport("user32")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    [DllImport("user32")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32")] public static extern bool ReleaseCapture();
    [DllImport("user32")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc p, uint pid, uint tid, uint flags);
    [DllImport("user32")] public static extern bool UnhookWinEvent(IntPtr h);
    [DllImport("user32")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("gdi32")] public static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("gdi32")] public static extern IntPtr CreateRectRgn(int a, int b, int c, int d);
    [DllImport("gdi32")] public static extern IntPtr CreateRoundRectRgn(int a, int b, int c, int d, int w, int h);
    [DllImport("gdi32")] public static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int modo);
    [DllImport("gdi32")] public static extern bool DeleteObject(IntPtr o);
    [DllImport("dwmapi")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("dwmapi")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT val, int size);
    [DllImport("user32")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint cor, byte alfa, uint f);
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
    [DllImport("user32")] public static extern IntPtr MonitorFromRect(ref RECT r, uint f);
    [DllImport("user32")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);

    // O rcNormal do WINDOWPLACEMENT é medido a partir da área de trabalho do monitor (sem a barra
    // de tarefas), não da tela. Converte um retângulo da tela para essa medida.
    public static RECT ParaAreaDeTrabalho(RECT r) { return Deslocar(r, -1); }
    public static RECT DaAreaDeTrabalho(RECT r) { return Deslocar(r, 1); }

    static RECT Deslocar(RECT r, int sinal)
    {
        IntPtr m = MonitorFromRect(ref r, 2);
        MONITORINFO mi = new MONITORINFO();
        mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (m == IntPtr.Zero || !GetMonitorInfo(m, ref mi)) return r;
        int dx = (mi.rcWork.L - mi.rcMonitor.L) * sinal, dy = (mi.rcWork.T - mi.rcMonitor.T) * sinal;
        r.L += dx; r.R += dx; r.T += dy; r.B += dy;
        return r;
    }

    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10,
        SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80;

    public static string Titulo(IntPtr h)
    {
        StringBuilder t = new StringBuilder(512);
        GetWindowText(h, t, t.Capacity);
        return t.ToString();
    }

    public static float Escala(IntPtr h)
    {
        try { uint d = GetDpiForWindow(h); return d > 0 ? d / 96f : 1f; }
        catch (EntryPointNotFoundException) { return 1f; }
    }

    // Janelas do Windows Terminal abertas (visíveis ou não).
    public static List<IntPtr> JanelasDeTerminal(bool soVisiveis)
    {
        List<IntPtr> lista = new List<IntPtr>();
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            if (soVisiveis && !IsWindowVisible(h)) return true;
            StringBuilder c = new StringBuilder(256);
            GetClassName(h, c, c.Capacity);
            if (c.ToString() == "CASCADIA_HOSTING_WINDOW_CLASS") lista.Add(h);
            return true;
        }, IntPtr.Zero);
        return lista;
    }

    // Dono de uma janela nossa = janela do terminal (anda, minimiza e fica atrás junto).
    // O WinForms zera o dono ao mostrar a janela, então isso é conferido sempre.
    // Trocar o dono não muda a ordem na tela: sem o HWND_TOP a nossa ficaria atrás da janela nova.
    public static void Dono(IntPtr nossa, IntPtr terminal)
    {
        if (GetWindow(nossa, 4) == terminal) return;
        SetWindowLongPtr(nossa, -8, terminal);
        SetWindowPos(nossa, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE); // HWND_TOP
    }
}
