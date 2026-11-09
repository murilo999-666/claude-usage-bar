// App "Claude Code" em abas (Windows), parte do claude-usage-bar.
// - O primeiro "Claude Code.exe" vira o gerenciador (Sessoes.cs): abre o Windows Terminal portátil,
//   desenha as abas, o rodapé e a moldura, e fica vivo enquanto houver sessão aberta.
// - Os próximos só pedem ao gerenciador: "foca" (clique normal) ou "abre outra" (--novo).
// - As janelas são marcadas com o AppUserModelID do atalho fixado, para ficarem no mesmo botão da barra.
// Comandos: --new (abre outra sessão), --setup / --unsetup (atalhos do menu Iniciar e da área de
// trabalho; o instalador usa), --quit (fecha o gerenciador; as janelas continuam), --instalar
// (grava o ID num atalho fixado à mão na barra de tarefas).
// Compilar: build.cmd (csc do .NET Framework que vem no Windows, C# 5).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class ClaudeCode
{
    public const string APP_ID = "Murilo.ClaudeCode";
    const string NOME = "Claude Code";
    static string exe;

    [STAThread]
    static int Main(string[] args)
    {
        try { W32.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
        exe = Process.GetCurrentProcess().MainModule.FileName;
        SetCurrentProcessExplicitAppUserModelID(APP_ID);

        string cmd = args.Length > 0 ? args[0] : "";
        if (cmd == "--instalar") return Instalar();
        if (cmd == "--setup") return CriarAtalhos();
        if (cmd == "--unsetup") return ApagarAtalhos();
        if (cmd == "--quit") return Sair();
        bool novo = Array.IndexOf(args, "--novo") >= 0 || Array.IndexOf(args, "--new") >= 0;

        if (!File.Exists(Host.TERMINAL))
        {
            MessageBox.Show(Txt.T("Não achei o Windows Terminal ao lado do programa:\n" + Host.TERMINAL +
                "\n\nRode o instalador do claude-usage-bar de novo.",
                "Windows Terminal was not found next to the program:\n" + Host.TERMINAL +
                "\n\nRun the claude-usage-bar installer again."), NOME, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        bool criado;
        using (Mutex m = new Mutex(true, @"Local\ClaudeCodeHost", out criado))
        {
            if (!criado)
            {
                // já tem um gerenciador rodando: só pede para ele
                IntPtr h = IntPtr.Zero;
                for (int i = 0; i < 50 && h == IntPtr.Zero; i++)
                {
                    h = W32.FindWindow(null, "ClaudeCodeHost");
                    if (h == IntPtr.Zero) Thread.Sleep(100);
                }
                if (h != IntPtr.Zero)
                {
                    W32.AllowSetForegroundWindow(-1); // deixa ele trazer a janela para a frente
                    W32.PostMessage(h, (uint)(novo ? HostJanela.NOVA : HostJanela.FOCAR), IntPtr.Zero, IntPtr.Zero);
                }
                return 0;
            }
            Application.Run(new Host(novo));
        }
        return 0;
    }

    // ---------- marcar janelas ----------

    public static void Marcar(IntPtr h)
    {
        IPropertyStore ps;
        Guid iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(h, ref iid, out ps) != 0) return;
        try
        {
            Gravar(ps, PK(2), "\"" + exe + "\"");   // RelaunchCommand
            Gravar(ps, PK(4), NOME);                // RelaunchDisplayNameResource
            Gravar(ps, PK(3), exe + ",0");          // RelaunchIconResource
            Gravar(ps, PK(5), APP_ID);              // ID (por último)
        }
        finally { Marshal.ReleaseComObject(ps); }
    }

    public static string LerAppId(IntPtr h)
    {
        IPropertyStore ps;
        Guid iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(h, ref iid, out ps) != 0) return null;
        try { return Ler(ps, PK(5)); }
        finally { Marshal.ReleaseComObject(ps); }
    }

    // Cada janela aberta pelo launcher tem um id, que vai para a variável CLAUDE_JANELA do claude.
    public static void GuardarId(IntPtr h, string id)
    {
        try { File.WriteAllText(Path.Combine(Host.Pasta, @"janelas\" + h.ToInt64() + ".txt"), id); } catch (IOException) { }
    }

    public static string IdDaJanela(IntPtr h)
    {
        try
        {
            string f = Path.Combine(Host.Pasta, @"janelas\" + h.ToInt64() + ".txt");
            if (File.Exists(f)) return File.ReadAllText(f).Trim();
        }
        catch (IOException) { }
        return null;
    }

    // ---------- atalho fixado ----------

    static int Instalar()
    {
        string pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        int feitos = 0;
        foreach (string lnk in Directory.GetFiles(pasta, "*.lnk"))
        {
            IShellLinkW sl = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)sl).Load(lnk, 2); // STGM_READWRITE
                StringBuilder alvo = new StringBuilder(1024);
                sl.GetPath(alvo, alvo.Capacity, IntPtr.Zero, 0);
                if (!string.Equals(Path.GetFullPath(alvo.ToString()), exe, StringComparison.OrdinalIgnoreCase))
                    continue;
                IPropertyStore ps = (IPropertyStore)sl;
                Gravar(ps, PK(5), APP_ID);
                ps.Commit();
                ((IPersistFile)sl).Save(null, true);
                feitos++;
            }
            catch (Exception) { }
            finally { Marshal.ReleaseComObject(sl); }
        }
        return feitos > 0 ? 0 : 1;
    }

    // ---------- atalhos do menu Iniciar e da área de trabalho (instalador) ----------

    // "Claude Code.lnk" no menu Iniciar e na área de trabalho; se já existe um atalho com esse
    // nome de outro programa, ele fica e o nosso vira "Claude Code (abas).lnk"
    static List<string> AtalhosPossiveis()
    {
        List<string> r = new List<string>();
        foreach (Environment.SpecialFolder f in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.DesktopDirectory })
        {
            string d = Environment.GetFolderPath(f);
            r.Add(Path.Combine(d, NOME + ".lnk"));
            r.Add(Path.Combine(d, NOME + " (" + Txt.T("abas", "tabs") + ").lnk"));
        }
        return r;
    }

    static string AlvoDoAtalho(string lnk)
    {
        IShellLinkW sl = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)sl).Load(lnk, 0);
            StringBuilder alvo = new StringBuilder(1024);
            sl.GetPath(alvo, alvo.Capacity, IntPtr.Zero, 0);
            return alvo.ToString();
        }
        catch (Exception) { return null; }
        finally { Marshal.ReleaseComObject(sl); }
    }

    static bool EhNosso(string lnk)
    {
        string alvo = AlvoDoAtalho(lnk);
        try { return alvo != null && string.Equals(Path.GetFullPath(alvo), exe, StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return false; }
    }

    static int CriarAtalhos()
    {
        int erros = 0;
        List<string> todos = AtalhosPossiveis();
        for (int i = 0; i < todos.Count; i += 2)
        {
            string lnk = todos[i];
            if (File.Exists(lnk) && !EhNosso(lnk)) lnk = todos[i + 1];
            IShellLinkW sl = (IShellLinkW)new ShellLink();
            try
            {
                sl.SetPath(exe);
                sl.SetWorkingDirectory(Path.GetDirectoryName(exe));
                sl.SetIconLocation(exe, 0);
                sl.SetDescription(Txt.T("Claude Code em abas", "Claude Code in tabs"));
                IPropertyStore ps = (IPropertyStore)sl;
                Gravar(ps, PK(5), APP_ID); // mesmo ID das janelas: ficam no mesmo botão da barra
                ps.Commit();
                ((IPersistFile)sl).Save(lnk, true);
                Console.WriteLine(lnk);
            }
            catch (Exception e) { erros++; Console.Error.WriteLine(lnk + ": " + e.Message); }
            finally { Marshal.ReleaseComObject(sl); }
        }
        return erros == 0 ? 0 : 1;
    }

    // apaga só os atalhos que apontam para este exe
    static int ApagarAtalhos()
    {
        foreach (string lnk in AtalhosPossiveis())
            if (File.Exists(lnk) && EhNosso(lnk))
                try { File.Delete(lnk); Console.WriteLine(lnk); } catch (Exception) { }
        return 0;
    }

    // fecha o gerenciador que estiver rodando (as sessões continuam abertas; o próximo adota)
    static int Sair()
    {
        IntPtr h = W32.FindWindow(null, "ClaudeCodeHost");
        if (h == IntPtr.Zero) return 0;
        W32.PostMessage(h, (uint)HostJanela.SAIR, IntPtr.Zero, IntPtr.Zero);
        for (int i = 0; i < 50; i++)
        {
            Thread.Sleep(100);
            if (W32.FindWindow(null, "ClaudeCodeHost") == IntPtr.Zero) return 0;
        }
        return 1;
    }

    // ---------- propriedades ----------

    static PROPERTYKEY PK(int pid)
    {
        PROPERTYKEY k;
        k.fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
        k.pid = pid;
        return k;
    }

    static void Gravar(IPropertyStore ps, PROPERTYKEY k, string valor)
    {
        PROPVARIANT v = new PROPVARIANT();
        v.vt = 31; // VT_LPWSTR
        v.p = Marshal.StringToCoTaskMemUni(valor);
        try { ps.SetValue(ref k, ref v); }
        finally { PropVariantClear(ref v); }
    }

    static string Ler(IPropertyStore ps, PROPERTYKEY k)
    {
        PROPVARIANT v = new PROPVARIANT();
        if (ps.GetValue(ref k, out v) != 0) return null;
        try { return v.vt == 31 ? Marshal.PtrToStringUni(v.p) : null; }
        finally { PropVariantClear(ref v); }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PROPERTYKEY { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint n);
        [PreserveSig] int GetAt(uint i, out PROPERTYKEY k);
        [PreserveSig] int GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
        void SetValue(ref PROPERTYKEY k, ref PROPVARIANT v);
        void Commit();
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder f, int n, IntPtr fd, int flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int n);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int n);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int n);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetHotkey(out short k);
        void SetHotkey(short k);
        void GetShowCmd(out int c);
        void SetShowCmd(int c);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int n, out int i);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string s, int i);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string s, int r);
        void Resolve(IntPtr h, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string s);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistFile
    {
        void GetClassID(out Guid c);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string f, int mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string f, bool remember);
    }

    [DllImport("shell32")] static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid iid, out IPropertyStore ps);
    [DllImport("shell32", CharSet = CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("ole32")] static extern int PropVariantClear(ref PROPVARIANT v);
}

// Textos na língua do Windows: português ou inglês.
static class Txt
{
    public static readonly bool PT =
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt";
    public static string T(string pt, string en) { return PT ? pt : en; }
}
