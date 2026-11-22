// WIN/ZW 資料搜尋/清理工具（視窗版，舊名：系統垃圾清理工具）— 由原始批次檔改寫
// 編譯：mcs -sdk:4.5 -target:winexe -platform:x64 -codepage:utf8 -optimize+
//        -r:System.Windows.Forms.dll -r:System.Drawing.dll -win32icon:app.ico
//        -resource:<圖示>.png,<圖示>.png（圖示資料夾每個 svg 轉 256px png 後全部嵌入，缺少會導致啟動錯誤） "-out:WIN/ZW 資料搜尋/清理工具.exe" Cleaner.cs I18n.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("WIN/ZW 資料搜尋/清理工具")]
[assembly: AssemblyDescription("ZW3D 環境與暫存資料夾清理輔助工具")]
[assembly: AssemblyCompany("FAST-PLUS")]
[assembly: AssemblyProduct("ZW3D Cleaner")]
[assembly: AssemblyVersion("3.10.1.0")]
[assembly: AssemblyFileVersion("3.10.1.0")]

// 第一次執行時，把內嵌的使用說明 PDF（依目前語系）釋出為「[讀我].pdf」，放在 EXE 同資料夾（無寫入權限則放到 %AppData%\SystemJunkCleaner）
static class Readme
{
    // ===== 以「一般使用者」身分開啟檔案（取得桌面 explorer 的權杖）：本程式是管理員身分，直接開檔時 Adobe 等閱讀器常無反應 =====
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr proc, uint access, out IntPtr tok);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool DuplicateTokenEx(IntPtr tok, uint access, IntPtr attr, int impLevel, int tokType, out IntPtr newTok);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool CreateProcessWithTokenW(IntPtr tok, int logon, string app, string cmd, int flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO { public int cb; public string lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    static bool OpenAsUser(string path)
    {
        IntPtr hp = IntPtr.Zero, tok = IntPtr.Zero, ptok = IntPtr.Zero;
        try
        {
            uint pid; GetWindowThreadProcessId(GetShellWindow(), out pid);
            if (pid == 0) return false;
            hp = OpenProcess(0x0400, false, pid);
            if (hp == IntPtr.Zero || !OpenProcessToken(hp, 0x0001 | 0x0002 | 0x0008, out tok)) return false;
            if (!DuplicateTokenEx(tok, 0xF01FF, IntPtr.Zero, 2, 1, out ptok)) return false;
            string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var si = new STARTUPINFO(); si.cb = Marshal.SizeOf(typeof(STARTUPINFO)); si.lpDesktop = "winsta0\\default";
            PROCESS_INFORMATION pi;
            if (!CreateProcessWithTokenW(ptok, 0, cmd, "\"" + cmd + "\" /c start \"\" \"" + path + "\"", 0x08000000, IntPtr.Zero, Environment.SystemDirectory, ref si, out pi)) return false;
            CloseHandle(pi.hProcess); CloseHandle(pi.hThread);
            return true;
        }
        catch { return false; }
        finally { if (ptok != IntPtr.Zero) CloseHandle(ptok); if (tok != IntPtr.Zero) CloseHandle(tok); if (hp != IntPtr.Zero) CloseHandle(hp); }
    }

    static void Open(string p, bool force)
    {
        if (!force) foreach (string a in Environment.GetCommandLineArgs()) if (a == "/relaunch") return;   // 切換語系／以管理員重新啟動時不再重複開啟
        // 本程式以系統管理員執行，直接 ShellExecute 常被 PDF 閱讀器（如 Edge）拒絕；改交給 explorer.exe 以一般使用者身分開啟，失敗再退回直接開啟
        bool ok = OpenAsUser(p);
        if (!ok) { try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + p + "\"") { UseShellExecute = false }); ok = true; } catch { } }
        if (!ok) { try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); ok = true; } catch { } }
        if (!ok) return;
        // 讀我先顯示（最大化，不縮小）；使用者看完按「確定」後，主程式才開啟
        using (var f = new Form { TopMost = true, ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), Size = new Size(1, 1) })
        {
            f.Show();
            MessageBox.Show(f, I18n.Tr("請先閱讀「讀我」說明，看完後按「確定」開始使用。"), I18n.Tr("WIN/ZW 資料搜尋/清理工具"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    public static void Ensure()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemJunkCleaner");
        string flag = Path.Combine(dir, "readme_pdf.done");
        string existing = null;
        try { if (File.Exists(flag)) existing = File.ReadAllText(flag).Trim(); } catch { }
        string res, name;
        switch (I18n.Code)
        {
            case "zh_TW": res = "readme_zh_TW.pdf"; name = "[讀我].pdf"; break;
            case "zh_CN": res = "readme_zh_CN.pdf"; name = "[读我].pdf"; break;
            case "ja_JP": res = "readme_ja.pdf"; name = "[讀我].pdf"; break;
            case "ko_KR": res = "readme_ko.pdf"; name = "[讀我].pdf"; break;
            default: res = "readme_en.pdf"; name = "[讀我].pdf"; break;   // 沒有翻譯的語系一律用英文版
        }
        // 旗標格式：語系|PDF 路徑。語系相同且檔案還在 → 不再開啟；語系變了（切換語系）或檔案不見 → 強制重新產生
        string want = I18n.Code + "|", oldPath = null;
        if (existing != null && existing.StartsWith(want)) { string ep = existing.Substring(want.Length); if (File.Exists(ep)) { return; } }
        else if (existing != null && existing.IndexOf('|') > 0) oldPath = existing.Substring(existing.IndexOf('|') + 1);
        byte[] data;
        using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
        {
            if (st == null) return;
            data = new byte[st.Length];
            int n = 0; while (n < data.Length) { int r = st.Read(data, n, data.Length - n); if (r <= 0) break; n += r; }
        }
        string target = null;
        foreach (string d in new[] { Path.GetDirectoryName(Application.ExecutablePath), dir })
        {
            try { Directory.CreateDirectory(d); string p = Path.Combine(d, name); try { File.WriteAllBytes(p, data); } catch { if (!File.Exists(p)) throw; } target = p; break; } catch { }
        }
        if (target != null) { Directory.CreateDirectory(dir); File.WriteAllText(flag, I18n.Code + "|" + target); try { if (oldPath != null && !string.Equals(oldPath, target, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath)) File.Delete(oldPath); } catch { } Open(target, true); }
    }
}

#if !TEST
static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        try { SetProcessDPIAware(); } catch { }
        try { I18n.Init(); } catch { }
        // 全域錯誤保護：發生未預期的錯誤時只提示並繼續執行，不讓程式當掉
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => {
            try { MessageBox.Show(I18n.Tr("發生未預期的錯誤，已略過並繼續執行：\n\n") + e.Exception.Message, I18n.Tr("WIN/ZW 資料搜尋/清理工具"), MessageBoxButtons.OK, MessageBoxIcon.Warning); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) => {
            try { MessageBox.Show(I18n.Tr("發生未預期的錯誤：\n\n") + e.ExceptionObject, I18n.Tr("WIN/ZW 資料搜尋/清理工具"), MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
        };
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Readme.Ensure(); } catch { }   // 必須在 SetCompatibleTextRenderingDefault 之後（Ensure 會建立視窗），否則主程式會直接結束
        Application.Run(new MainForm());
    }
}
#endif


// ================= 清理邏輯（高速引擎） =================
//
// 加速重點：
//  1. 以 FindFirstFileEx(LARGE_FETCH, Basic) 一次取得檔名、大小與屬性，不再逐檔查詢
//  2. 多執行緒平行掃描與刪除（工作佇列）
//  3. 全部勾選項目合併成「每個根目錄只走一次」；全磁碟項目與其底下的資料夾只掃描一遍
//  4. 直接呼叫 DeleteFileW / RemoveDirectoryW，失敗不拋例外；唯讀檔先清屬性再刪
//  5. 使用 \\?\ 長路徑，超過 260 字元的路徑也能處理

enum Risk { Safe, Caution, Danger }

class Stats
{
    public int Files, Failed;
    public long Bytes;
    public string Note;
}

class Ctx
{
    public bool Dry;
    public volatile bool Cancel;
    // 即時進度（Interlocked 更新，UI 以計時器讀取）
    public long DirsFound, DirsDone, FilesSeen, Deleted, DeletedBytes, Failed;
    // 收集找到的檔案（預覽清單、快速搜尋結果）
    public ConcurrentQueue<Found> Collect;
    public int CollectMax = 200000, Collected;
    public volatile bool Limit;
    public void Check() { if (Cancel) throw new OperationCanceledException(); }
}

// 找到的檔案或資料夾
class Found
{
    public string Path, Tag;
    public long Size, ModFt;
    public bool IsDir;
    string name, dir;
    // 以字串處理取得檔名與資料夾（.NET 的 Path 函式遇到超過 260 字元的路徑會拋出例外）
    public string Name { get { return name ?? (name = Native.NameOf(Path)); } }
    public string Dir { get { return dir ?? (dir = Native.Parent(Path)); } }
    public string Ext { get { string n = Name; int i = n.LastIndexOf('.'); return i > 0 ? n.Substring(i) : ""; } }
    public DateTime Modified { get { try { return DateTime.FromFileTimeUtc(ModFt).ToLocalTime(); } catch { return DateTime.MinValue; } } }
}

// 一個項目要清理的位置
class Spec
{
    public string Root;
    public string[] Patterns;      // "*" 或 "*.ext"
    public bool Recursive = true;
    public bool CleanDir;          // 清空後移除子資料夾（保留根資料夾）
    public bool RemoveRoot;        // 連根資料夾一起移除
}

class CleanItem
{
    public string Name, Desc, Icon = "file";
    public Risk Risk;
    public bool Default;
    public List<Spec> Specs = new List<Spec>();
    public Action<Ctx, Stats> After;   // 檔案處理完後執行的特殊動作（回收筒、cleanmgr、帳號）
}

static class Native
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WIN32_FIND_DATA
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime, ftLastAccessTime, ftLastWriteTime;
        public uint nFileSizeHigh, nFileSizeLow, dwReserved0, dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindFirstFileExW(string name, int infoLevel, out WIN32_FIND_DATA data, int searchOp, IntPtr filter, int flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool FindNextFileW(IntPtr h, out WIN32_FIND_DATA data);
    [DllImport("kernel32.dll")] public static extern bool FindClose(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool DeleteFileW(string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool RemoveDirectoryW(string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool SetFileAttributesW(string path, uint attrs);

    public const uint DIR = 0x10, READONLY = 0x1, REPARSE = 0x400;
    static readonly IntPtr INVALID = new IntPtr(-1);

    // 是否可用 Win32 API（在 Windows 以外的環境改用 .NET 列舉，方便測試）
    public static readonly bool Win32 = Environment.OSVersion.Platform == PlatformID.Win32NT;

    public static string Long(string p) { return p.StartsWith(@"\\") ? p : @"\\?\" + p; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern uint GetFileAttributesW(string path);

    // 支援超長路徑的存在檢查
    public static bool Exists(string p, bool dir)
    {
        if (string.IsNullOrEmpty(p)) return false;
        if (Win32)
        {
            uint a = GetFileAttributesW(Long(p));
            if (a == 0xFFFFFFFF) return false;
            return dir == ((a & DIR) != 0);
        }
        try { return dir ? Directory.Exists(p) : File.Exists(p); } catch { return false; }
    }

    static int LastSep(string p) { return p.TrimEnd('\\', '/').LastIndexOfAny(new[] { '\\', '/' }); }

    public static string NameOf(string p)
    {
        if (string.IsNullOrEmpty(p)) return "";
        string t = p.Length > 3 ? p.TrimEnd('\\', '/') : p;
        int i = t.LastIndexOfAny(new[] { '\\', '/' });
        return i >= 0 && i < t.Length - 1 ? t.Substring(i + 1) : t;
    }

    // 上層資料夾；"C:\abc" → "C:\"；根目錄回傳空字串
    public static string Parent(string p)
    {
        if (string.IsNullOrEmpty(p)) return "";
        string t = p.Length > 3 ? p.TrimEnd('\\', '/') : p;
        int i = t.LastIndexOfAny(new[] { '\\', '/' });
        if (i < 0 || (i == t.Length - 1 && t.Length <= 3)) return "";
        string d = t.Substring(0, i);
        if (d.Length == 2 && d[1] == ':') d += "\\";
        if (d.Length == 0) d = t.Substring(0, 1);   // Unix "/"
        return d;
    }

    // 永久刪除整個資料夾（支援超長路徑）
    public static bool DeleteTree(string dir)
    {
        var buf = new List<Entry>();
        List(dir, buf);
        foreach (var e in buf.ToArray())
        {
            string full = dir.TrimEnd('\\', '/') + (Win32 ? "\\" : "/") + e.Name;
            if ((e.Attr & DIR) != 0 && (e.Attr & REPARSE) == 0) DeleteTree(full);
            else if ((e.Attr & DIR) != 0) RemoveDir(full);   // 連結本身移除，不跟隨
            else DeleteFile(full, e.Attr);
        }
        return RemoveDir(dir);
    }

    public struct Entry { public string Name; public uint Attr; public long Size, Mod; }

    public static void List(string dir, List<Entry> outList)
    {
        outList.Clear();
        if (Win32)
        {
            WIN32_FIND_DATA d;
            IntPtr h = FindFirstFileExW(Long(dir.TrimEnd(new[] { '\\' })) + @"\*", 1 /*Basic*/, out d, 0, IntPtr.Zero, 2 /*LARGE_FETCH*/);
            if (h == INVALID) return;
            try
            {
                do
                {
                    string n = d.cFileName;
                    if (n == "." || n == "..") continue;
                    outList.Add(new Entry { Name = n, Attr = d.dwFileAttributes, Size = ((long)d.nFileSizeHigh << 32) | d.nFileSizeLow,
                        Mod = ((long)d.ftLastWriteTime.dwHighDateTime << 32) | (uint)d.ftLastWriteTime.dwLowDateTime });
                } while (FindNextFileW(h, out d));
            }
            finally { FindClose(h); }
            return;
        }
        try
        {
            foreach (var fi in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                var a = (uint)fi.Attributes;
                outList.Add(new Entry { Name = fi.Name, Attr = a, Size = (a & DIR) != 0 ? 0 : ((FileInfo)fi).Length, Mod = fi.LastWriteTimeUtc.ToFileTimeUtc() });
            }
        }
        catch { }
    }

    public static bool DeleteFile(string path, uint attr)
    {
        if (Win32)
        {
            string lp = Long(path);
            if ((attr & READONLY) != 0) SetFileAttributesW(lp, 0x80);   // 相當於 del /f
            if (DeleteFileW(lp)) return true;
            if ((attr & READONLY) == 0 && SetFileAttributesW(lp, 0x80)) return DeleteFileW(lp);
            return false;
        }
        try { File.Delete(path); return true; } catch { return false; }
    }

    public static bool RemoveDir(string path)
    {
        if (Win32) return RemoveDirectoryW(Long(path));
        try { Directory.Delete(path, false); return true; } catch { return false; }
    }
}

static class Cleaner
{
    [StructLayout(LayoutKind.Sequential)]
    struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

    static readonly HashSet<string> SkipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "System Volume Information", "$Recycle.Bin", "WinSxS" };

    static string Env(string k) { return Environment.GetEnvironmentVariable(k) ?? ""; }
    static string J(params string[] p)
    {
        foreach (var s in p) if (string.IsNullOrEmpty(s)) return "";
        return Path.Combine(p);
    }

    static Spec Dir(string root) { return new Spec { Root = root, Patterns = new[] { "*" }, CleanDir = true }; }
    static Spec Pat(string root, params string[] pats) { return new Spec { Root = root, Patterns = pats }; }

    static int RunHidden(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true };
        using (var p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; }
    }

    public static List<CleanItem> Build()
    {
        string sd = Env("SystemDrive"); if (sd == "") sd = "C:";
        string root = sd + "\\";
        string win = Env("windir"), up = Env("USERPROFILE"), la = Env("LOCALAPPDATA"), ad = Env("APPDATA");

        var list = new List<CleanItem>();
        CleanItem it;

        it = new CleanItem { Name = I18n.Tr("暫存檔 *.tmp / *._mp"), Desc = I18n.Tr("掃描整個 ") + root + I18n.Tr(" 磁碟"), Risk = Risk.Safe, Default = true, Icon = "file" };
        it.Specs.Add(Pat(root, "*.tmp", "*._mp")); list.Add(it);

        it = new CleanItem { Name = I18n.Tr("系統與使用者暫存資料夾"), Desc = I18n.Tr(@"Windows\Temp、%TEMP%、Program Files\Temp"), Risk = Risk.Safe, Default = true, Icon = "folder" };
        foreach (var d in new[] { J(win, "Temp"), J(Env("SystemRoot"), "Temp"), Env("TEMP"), J(la, "Temp"), J(up, "Local Settings", "Temp"), J(root, "Program Files", "Temp") })
            it.Specs.Add(Dir(d));
        list.Add(it);

        it = new CleanItem { Name = I18n.Tr("網際網路暫存檔"), Desc = I18n.Tr("IE / 舊版 Edge 快取（INetCache）"), Risk = Risk.Safe, Default = true, Icon = "folder" };
        foreach (var d in new[] { J(la, "Microsoft", "Windows", "INetCache"), J(la, "Microsoft", "Windows", "Temporary Internet Files"), J(up, "Local Settings", "Temporary Internet Files") })
            it.Specs.Add(Dir(d));
        list.Add(it);

        it = new CleanItem { Name = I18n.Tr("最近使用的文件清單"), Desc = I18n.Tr("Recent 捷徑（保留工作列釘選的跳躍清單）"), Risk = Risk.Safe, Default = true, Icon = "folder" };
        foreach (var d in new[] { J(ad, "Microsoft", "Windows", "Recent"), J(up, "Recent") })
            it.Specs.Add(new Spec { Root = d, Patterns = new[] { "*" }, Recursive = false });
        list.Add(it);

        it = new CleanItem { Name = I18n.Tr("預讀檔 Prefetch"), Desc = I18n.Tr(@"Windows\Prefetch（下次開機會稍慢一次）"), Risk = Risk.Safe, Default = true, Icon = "folder" };
        it.Specs.Add(Dir(J(win, "Prefetch"))); list.Add(it);

        it = new CleanItem { Name = I18n.Tr("清空資源回收筒"), Desc = I18n.Tr("所有磁碟的資源回收筒，刪除後無法復原"), Risk = Risk.Caution, Icon = "delete" };
        it.Specs.Add(Dir(J(root, "recycled")));
        it.After = (c, s) => {
            var q = new SHQUERYRBINFO(); q.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
            if (SHQueryRecycleBin(null, ref q) == 0) { s.Files += (int)q.i64NumItems; s.Bytes += q.i64Size; }
            if (!c.Dry && q.i64NumItems > 0) SHEmptyRecycleBin(IntPtr.Zero, null, 0x7);
        };
        list.Add(it);

        it = new CleanItem { Name = I18n.Tr("記錄檔 *.log"), Desc = I18n.Tr("掃描整個磁碟，部分程式的紀錄會消失"), Risk = Risk.Caution, Icon = "file" };
        it.Specs.Add(Pat(root, "*.log")); list.Add(it);

        it = new CleanItem { Name = "*.gid / *.chk / *.old", Desc = I18n.Tr("說明索引、磁碟檢查碎片、舊版檔案（全磁碟）"), Risk = Risk.Caution, Icon = "file" };
        it.Specs.Add(Pat(root, "*.gid", "*.chk", "*.old")); list.Add(it);

        it = new CleanItem { Name = I18n.Tr("Windows 備份檔 *.bak"), Desc = I18n.Tr("Windows 資料夾內的 .bak 檔"), Risk = Risk.Caution, Icon = "file" };
        it.Specs.Add(Pat(win, "*.bak")); list.Add(it);

        it = new CleanItem { Name = "Cookies", Desc = I18n.Tr("IE / 舊版 Edge 的 Cookies，網站會被登出"), Risk = Risk.Caution, Icon = "folder" };
        foreach (var d in new[] { J(up, "Cookies"), J(la, "Microsoft", "Windows", "INetCookies"), J(ad, "Microsoft", "Windows", "Cookies") })
            it.Specs.Add(Dir(d));
        list.Add(it);

        it = new CleanItem { Name = I18n.Tr("Windows 磁碟清理 (cleanmgr)"), Desc = I18n.Tr("執行 cleanmgr /sagerun:99，需先按「設定磁碟清理」"), Risk = Risk.Caution, Icon = "radio" };
        it.After = (c, s) => {
            if (c.Dry) { s.Note = I18n.Tr("預覽模式不執行"); return; }
            try { using (var p = Process.Start("cleanmgr.exe", "/sagerun:99")) p.WaitForExit(); s.Note = I18n.Tr("磁碟清理已完成"); }
            catch (Exception e) { s.Note = I18n.Tr("執行失敗：") + e.Message; }
        };
        list.Add(it);

        string dd = J(root, "d");
        it = new CleanItem { Name = I18n.Tr("刪除 ") + dd + I18n.Tr(" 整個資料夾"), Desc = I18n.Tr("原批次檔的項目；資料夾內所有檔案將永久刪除！"), Risk = Risk.Danger, Icon = "delete" };
        it.Specs.Add(new Spec { Root = dd, Patterns = new[] { "*" }, CleanDir = true, RemoveRoot = true });
        list.Add(it);

        it = new CleanItem { Name = I18n.Tr("刪除 aspnet 使用者帳號"), Desc = I18n.Tr("原批次檔的項目；僅舊版 IIS/.NET 會建立此帳號"), Risk = Risk.Danger, Icon = "delete" };
        it.After = (c, s) => {
            if (RunHidden("net.exe", "user aspnet") != 0) { s.Note = I18n.Tr("此電腦沒有 aspnet 帳號"); return; }
            if (c.Dry) { s.Note = I18n.Tr("找到 aspnet 帳號（預覽模式不刪除）"); return; }
            s.Note = RunHidden("net.exe", "user aspnet /delete") == 0 ? I18n.Tr("已刪除 aspnet 帳號") : I18n.Tr("刪除失敗（需要系統管理員權限）");
        };
        list.Add(it);

        return list;
    }

    // ---------- 平行引擎 ----------

    class Rule
    {
        public string Root, Key, ItemName;
        public string[] Patterns;
        public bool AllFiles, Recursive, CleanDir, RemoveRoot;
        public Stats St;
        public bool Match(string name)
        {
            if (AllFiles) return true;
            foreach (var p in Patterns)
                if (name.EndsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    struct Work { public string Path; public Rule[] Active; public int Depth; }

    static string Norm(string p)
    {
        try { p = Path.GetFullPath(p); } catch { return null; }
        if (p.Length > 3) p = p.TrimEnd('\\', '/');
        return p;
    }
    static string KeyOf(string p) { return p.ToUpperInvariant(); }
    static bool IsUnder(string child, string parent)   // child 位於 parent 底下（不含相等）
    {
        string pp = parent.EndsWith("\\") || parent.EndsWith("/") ? parent : parent + Path.DirectorySeparatorChar;
        return child.Length > pp.Length && child.StartsWith(pp, StringComparison.OrdinalIgnoreCase);
    }
    public static string Child(string dir, string name)
    {
        return dir.EndsWith("\\") || dir.EndsWith("/") ? dir + name : dir + Path.DirectorySeparatorChar + name;
    }

    public static Dictionary<CleanItem, Stats> Execute(List<CleanItem> sel, Ctx ctx, Action<string> phase)
    {
        var result = new Dictionary<CleanItem, Stats>();
        var rules = new List<Rule>();
        var seen = new HashSet<string>();
        foreach (var it in sel)
        {
            var st = new Stats();
            result[it] = st;
            foreach (var sp in it.Specs)
            {
                if (string.IsNullOrEmpty(sp.Root)) continue;
                string r = Norm(sp.Root);
                if (r == null || !seen.Add(it.Name + "|" + KeyOf(r))) continue;
                bool exists = false;
                try { exists = Directory.Exists(r) && (new DirectoryInfo(r).Attributes & FileAttributes.ReparsePoint) == 0; } catch { }
                if (!exists) { if (sp.RemoveRoot) st.Note = I18n.Tr("資料夾不存在"); continue; }
                rules.Add(new Rule {
                    Root = r, Key = KeyOf(r), ItemName = it.Name, Patterns = sp.Patterns.Select(x => x == "*" ? "*" : x.TrimStart(new[] { '*' })).ToArray(),
                    AllFiles = sp.Patterns.Contains("*"), Recursive = sp.Recursive, CleanDir = sp.CleanDir, RemoveRoot = sp.RemoveRoot, St = st
                });
            }
        }

        if (rules.Count > 0)
        {
            phase(I18n.Tr("掃描並清理檔案"));
            RunParallel(rules, ctx);
        }

        // 檔案處理完後，執行特殊動作
        foreach (var it in sel)
        {
            if (it.After == null) continue;
            if (ctx.Cancel) break;
            phase(it.Name);
            try { it.After(ctx, result[it]); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { result[it].Note = I18n.Tr("發生錯誤：") + ex.Message; }
        }
        return result;
    }

    static void RunParallel(List<Rule> rules, Ctx ctx)
    {
        // 依根目錄分組：根目錄若位於另一個「遞迴」規則底下，就併入那次掃描，不重複走訪
        var byRoot = new Dictionary<string, List<Rule>>();
        foreach (var r in rules)
        {
            List<Rule> l;
            if (!byRoot.TryGetValue(r.Key, out l)) byRoot[r.Key] = l = new List<Rule>();
            l.Add(r);
        }
        var ancestors = new HashSet<string>();   // 規則根目錄的所有上層，用來決定是否要往下走
        foreach (var r in rules)
        {
            string p = r.Root;
            while (true)
            {
                string parent;
                try { parent = Path.GetDirectoryName(p); } catch { break; }
                if (string.IsNullOrEmpty(parent)) break;
                ancestors.Add(KeyOf(parent));
                p = parent;
            }
        }

        var q = new ConcurrentQueue<Work>();
        int pending = 0;
        foreach (var kv in byRoot)
        {
            string root = kv.Value[0].Root;
            bool covered = rules.Any(o => IsUnder(root, o.Root));   // 會從上層根目錄走到這裡
            if (covered) continue;
            var active = kv.Value.OrderByDescending(x => x.Root.Length).ToArray();
            q.Enqueue(new Work { Path = root, Active = active, Depth = 0 });
            pending++;
            Interlocked.Increment(ref ctx.DirsFound);
        }

        var emptyDirs = new ConcurrentBag<KeyValuePair<int, string>>();
        int threads = Math.Max(4, Math.Min(16, Environment.ProcessorCount * 2));
        var workers = new Thread[threads];
        for (int t = 0; t < threads; t++)
        {
            workers[t] = new Thread(() => {
                var buf = new List<Native.Entry>(256);
                var spin = 0;
                while (!ctx.Cancel)
                {
                    Work w;
                    if (q.TryDequeue(out w))
                    {
                        spin = 0;
                        try { ProcessDir(w, buf, q, ref pending, byRoot, ancestors, emptyDirs, ctx); }
                        catch { }
                        Interlocked.Increment(ref ctx.DirsDone);
                        if (Interlocked.Decrement(ref pending) == 0) break;
                    }
                    else
                    {
                        if (Volatile.Read(ref pending) == 0) break;
                        if (++spin < 20) Thread.Yield(); else Thread.Sleep(1);
                    }
                }
            }) { IsBackground = true, Name = "cleaner-" + t };
            workers[t].Start();
        }
        foreach (var w in workers) w.Join();
        if (ctx.Dry || ctx.Cancel) return;

        // 由深到淺移除已清空的子資料夾
        foreach (var d in emptyDirs.OrderByDescending(x => x.Key)) Native.RemoveDir(d.Value);
        foreach (var r in rules) if (r.RemoveRoot) Native.RemoveDir(r.Root);
    }

    static void ProcessDir(Work w, List<Native.Entry> buf, ConcurrentQueue<Work> q, ref int pending,
        Dictionary<string, List<Rule>> byRoot, HashSet<string> ancestors, ConcurrentBag<KeyValuePair<int, string>> emptyDirs, Ctx ctx)
    {
        Native.List(w.Path, buf);
        var active = w.Active;
        foreach (var e in buf)
        {
            if (ctx.Cancel) return;
            string full = Child(w.Path, e.Name);
            if ((e.Attr & Native.DIR) != 0)
            {
                if ((e.Attr & Native.REPARSE) != 0) continue;            // 不跟隨連結、交接點
                string key = KeyOf(full);
                List<Rule> here;
                byRoot.TryGetValue(key, out here);
                if (here == null && SkipNames.Contains(e.Name)) continue;

                // 子資料夾的生效規則：此處新增的規則（較深）在前，再接上層可遞迴的規則
                var next = new List<Rule>(active.Length + 2);
                if (here != null) next.AddRange(here.OrderByDescending(x => x.Root.Length));
                foreach (var r in active) if (r.Recursive) next.Add(r);
                if (next.Count == 0 && !ancestors.Contains(key)) continue;

                foreach (var r in next)
                    if (r.CleanDir && IsUnder(full, r.Root)) { emptyDirs.Add(new KeyValuePair<int, string>(w.Depth + 1, full)); break; }

                Interlocked.Increment(ref pending);
                Interlocked.Increment(ref ctx.DirsFound);
                q.Enqueue(new Work { Path = full, Active = next.ToArray(), Depth = w.Depth + 1 });
                continue;
            }

            Interlocked.Increment(ref ctx.FilesSeen);
            if (active.Length == 0 || (e.Attr & Native.REPARSE) != 0) continue;   // 略過雲端預留位置檔
            Rule hit = null;
            foreach (var r in active) if (r.Match(e.Name)) { hit = r; break; }
            if (hit == null) continue;

            if (ctx.Dry || Native.DeleteFile(full, e.Attr))
            {
                if (ctx.Dry && ctx.Collect != null && Interlocked.Increment(ref ctx.Collected) <= ctx.CollectMax)
                    ctx.Collect.Enqueue(new Found { Path = full, Size = e.Size, ModFt = e.Mod, Tag = hit.ItemName });
                Interlocked.Increment(ref hit.St.Files);
                Interlocked.Add(ref hit.St.Bytes, e.Size);
                Interlocked.Increment(ref ctx.Deleted);
                Interlocked.Add(ref ctx.DeletedBytes, e.Size);
            }
            else
            {
                Interlocked.Increment(ref hit.St.Failed);
                Interlocked.Increment(ref ctx.Failed);
            }
        }
    }

    public static string Human(long b)
    {
        if (b < 1024) return b + " B";
        double f = b; string[] u = { "KB", "MB", "GB", "TB" };
        foreach (var x in u) { f /= 1024; if (f < 1024) return f.ToString("0.0") + " " + x; }
        return (f / 1024).ToString("0.0") + " PB";
    }
}

// ================= 快速搜尋 =================

// 搜尋條件：類型 / 副檔名 / 完全符合
class SearchOptions
{
    public string[] Exts = new string[0];   // 例如 ".pdf"；空陣列 = 不限
    public bool FilesOnly, DirsOnly, Exact;
    public bool Permanent;                  // 結果視窗的刪除預設為永久刪除
    public string Label = I18n.Tr("全部");
    public int Zw3d;                        // ZW3D 判斷：0=關閉 1=全部 ZW3D 2=僅暫存垃圾 3=僅模型檔

    // ---- ZW3D 判斷規則
    public static readonly string[] ZwExtJunk = { ".z3bak", ".z3gr", ".cfg", ".log", ".stl", ".job", ".ptss", ".projector" };
    public static readonly string[] ZwExtModel = { ".z3", ".z3prt", ".z3asm", ".z3drw", ".z3sht", ".z3fnt", ".z3cvx" };
    public static readonly string[] ZwNameKeys = { "zw3d", "topologicrep", "proxy", "analyticalrep", "z3_a7880" };
    public string ExtFilter = "";           // 下拉式選單選的單一副檔名，例如 ".z3prt"；空字串 = 不限

    static bool EndsAny(string n, string[] a) { foreach (var x in a) if (n.EndsWith(x, StringComparison.OrdinalIgnoreCase)) return true; return false; }
    static bool HasKey(string n) { foreach (var k in ZwNameKeys) if (n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true; return false; }

    static string ExtOf(string n) { int i = n.LastIndexOf('.'); return i > 0 ? n.Substring(i) : ""; }
    static bool In(string x, string[] a) { foreach (var y in a) if (string.Equals(x, y, StringComparison.OrdinalIgnoreCase)) return true; return false; }
    static bool Has(string s, string k) { return s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0; }

    // 依 ZW3D 模式取得已知副檔名（1=全部 2=僅暫存垃圾 3=僅模型檔）
    public static string[] ZwExts(int mode)
    {
        if (mode == 2) return ZwExtJunk;
        if (mode == 3) return ZwExtModel;
        if (mode == 1) return ZwExtModel.Concat(ZwExtJunk).ToArray();
        return new string[0];
    }

    // ZW3D 判斷：只比對已知副檔名，不搜尋資料夾
    // ZW3D 資料夾規則：資料夾內至少要有其中一種副檔名，該資料夾才列入 ZW3D 搜尋
    public static readonly string[] ZwAnchorExt = { ".z3", ".z3prt", ".dwg" };
    public static bool ZwAnchor(string name) { return In(ExtOf(name), ZwAnchorExt); }

    public static bool ZwMatch(int mode, string name, bool isDir)
    {
        if (mode <= 0) return true;
        if (isDir) return false;
        return In(ExtOf(name), ZwExts(mode));
    }

    public static string[] ParseExts(string text)
    {
        return text.Split(new[] { ',', '，', ';', '；', ' ', '　', '、', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().TrimStart(new[] { '*' }).TrimStart(new[] { '.' }).ToLowerInvariant())
            .Where(x => x.Length > 0).Distinct().Select(x => "." + x).ToArray();
    }
}

static class Searcher
{
    class Term
    {
        readonly System.Text.RegularExpressions.Regex rx;
        readonly string s;
        public Term(string t)
        {
            if (t.IndexOfAny(new[] { '*', '?' }) >= 0)
                rx = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(t).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            else s = t;
        }
        public bool Match(string n) { return rx != null ? rx.IsMatch(n) : n.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0; }
    }

    static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "System Volume Information" };

    // ===== NTFS MFT 快速掃描（類似 Everything）=====
    // 以 FSCTL_ENUM_USN_DATA 循序讀取整個磁碟的檔案清單，不必逐資料夾尋道，傳統硬碟（HDD）上特別快。
    // 需要系統管理員權限、且磁碟為 NTFS；任何一步失敗就回傳 false，由呼叫端退回一般資料夾掃描。
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int ret, IntPtr ov);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetVolumeInformationW(string root, System.Text.StringBuilder vol, int volLen, out uint serial, out uint maxComp, out uint flags, System.Text.StringBuilder fs, int fsLen);

    class DirRec { public ulong Parent; public string Name; }
    struct Cand { public ulong Parent; public uint Attr; public string Name; }

    static bool IsDriveRoot(string r)
    {
        return r != null && r.Length >= 2 && char.IsLetter(r[0]) && r[1] == ':' && (r.Length == 2 || (r.Length == 3 && (r[2] == '\\' || r[2] == '/')));
    }

    static bool InZwInstall(string path)
    {
        var parts = path.Split('\\');
        for (int i = 1; i < parts.Length; i++)
            if (string.Equals(parts[i], "ZWSOFT", StringComparison.OrdinalIgnoreCase) && parts[i - 1].StartsWith("Program Files", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static bool ScanMft(string root, Term[] terms, string exact, string[] exts, int zw, string extF, string winDir, SearchOptions opt, Ctx ctx)
    {
        if (!Native.Win32) return false;
        string vol = root.Substring(0, 2);
        var fsn = new System.Text.StringBuilder(32);
        uint s1, s2, s3;
        if (!GetVolumeInformationW(vol + "\\", null, 0, out s1, out s2, out s3, fsn, 32) || fsn.ToString() != "NTFS") return false;
        IntPtr h = CreateFileW(@"\\.\" + vol, 0x80000000u, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h == new IntPtr(-1)) return false;
        long seenFiles = 0, seenDirs = 0;
        try
        {
            const ulong MASK = 0x0000FFFFFFFFFFFFUL;
            var dirs = new Dictionary<ulong, DirRec>();
            var cand = new List<KeyValuePair<ulong, Cand>>();
            var anchorParents = zw > 0 ? new HashSet<ulong>() : null;
            var inb = new byte[24];
            BitConverter.GetBytes(long.MaxValue).CopyTo(inb, 16);   // HighUsn；StartFRN = 0、LowUsn = 0
            var buf = new byte[1 << 20];
            bool failed = false;
            while (!ctx.Cancel)
            {
                int ret;
                if (!DeviceIoControl(h, 0x000900B3, inb, inb.Length, buf, buf.Length, out ret, IntPtr.Zero))
                {
                    if (Marshal.GetLastWin32Error() == 38) break;   // ERROR_HANDLE_EOF：已讀完
                    failed = true; break;
                }
                if (ret <= 8) break;
                ulong next = BitConverter.ToUInt64(buf, 0);
                int off = 8;
                long f0 = 0, d0 = 0;
                while (off + 60 <= ret)
                {
                    int rec = off;
                    int rl = BitConverter.ToInt32(buf, rec);
                    if (rl <= 0 || rec + rl > ret) break;
                    off += rl;
                    if (BitConverter.ToUInt16(buf, rec + 4) != 2) { failed = true; break; }   // 只處理 USN_RECORD_V2
                    ulong frn = BitConverter.ToUInt64(buf, rec + 8) & MASK;
                    ulong par = BitConverter.ToUInt64(buf, rec + 16) & MASK;
                    uint attr = BitConverter.ToUInt32(buf, rec + 52);
                    int nl = BitConverter.ToUInt16(buf, rec + 56), no = BitConverter.ToUInt16(buf, rec + 58);
                    if (frn < 24) continue;   // NTFS 系統中繼檔（$MFT、$LogFile…）與根目錄本身
                    string name = System.Text.Encoding.Unicode.GetString(buf, rec + no, nl);
                    bool isDir = (attr & Native.DIR) != 0;
                    if (isDir) { dirs[frn] = new DirRec { Parent = par, Name = name }; d0++; }
                    else
                    {
                        f0++;
                        if (zw > 0 && SearchOptions.ZwAnchor(name)) anchorParents.Add(par);
                    }
                    // 與資料夾掃描相同的比對條件
                    bool ok = !(isDir && zw > 0);
                    if (isDir ? (opt.FilesOnly || exts.Length > 0) : opt.DirsOnly) ok = false;
                    if (ok && exts.Length > 0)
                    {
                        ok = false;
                        foreach (var x in exts) if (name.EndsWith(x, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                    }
                    if (ok && extF.Length > 0) ok = !isDir && name.EndsWith(extF, StringComparison.OrdinalIgnoreCase);
                    if (ok && zw > 0) ok = SearchOptions.ZwMatch(zw, name, isDir);
                    if (ok && exact != null) ok = string.Equals(name, exact, StringComparison.OrdinalIgnoreCase);
                    else if (ok) foreach (var term in terms) if (!term.Match(name)) { ok = false; break; }
                    if (ok) cand.Add(new KeyValuePair<ulong, Cand>(frn, new Cand { Parent = par, Attr = attr, Name = name }));
                }
                seenFiles += f0; seenDirs += d0;
                Interlocked.Add(ref ctx.FilesSeen, f0);
                Interlocked.Add(ref ctx.DirsFound, d0);
                if (failed) break;
                BitConverter.GetBytes(next).CopyTo(inb, 0);
            }
            if (failed)
            {
                // 退回一般掃描前，扣掉這一輪已計入的進度
                Interlocked.Add(ref ctx.FilesSeen, -seenFiles);
                Interlocked.Add(ref ctx.DirsFound, -seenDirs);
                return false;
            }
            Interlocked.Add(ref ctx.DirsDone, seenDirs);
            if (ctx.Cancel) return true;

            // 第二階段：還原完整路徑、套用路徑相關規則、取得大小與修改時間，再輸出結果
            var cache = new Dictionary<ulong, string>();
            Func<ulong, string> dirPath = null;
            dirPath = frn => {
                var chain = new List<ulong>();
                ulong cur = frn;
                string basePath;
                int guard = 0;
                while (true)
                {
                    if (cur == 5) { basePath = vol + "\\"; break; }
                    if (cache.TryGetValue(cur, out basePath)) break;
                    DirRec dr;
                    if (!dirs.TryGetValue(cur, out dr) || ++guard > 1024) return null;
                    chain.Add(cur); cur = dr.Parent;
                }
                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    basePath = (basePath.EndsWith("\\") ? basePath : basePath + "\\") + dirs[chain[i]].Name;
                    cache[chain[i]] = basePath;
                }
                return basePath;
            };
            foreach (var kv in cand)
            {
                if (ctx.Cancel) break;
                var c = kv.Value;
                string pp = dirPath(c.Parent);
                if (pp == null) continue;
                if ((pp + "\\").IndexOf("\\System Volume Information\\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (zw > 0)
                {
                    if (!anchorParents.Contains(c.Parent)) continue;   // 資料夾內須有 .z3 / .z3prt / .dwg
                    if (winDir != null && (string.Equals(pp, winDir, StringComparison.OrdinalIgnoreCase) || pp.StartsWith(winDir + "\\", StringComparison.OrdinalIgnoreCase))) continue;
                    if (InZwInstall(pp)) continue;
                }
                bool isDir = (c.Attr & Native.DIR) != 0;
                string full = Cleaner.Child(pp, c.Name);
                long size = 0, mod = 0;
                Native.WIN32_FIND_DATA fd;
                IntPtr fh = Native.FindFirstFileExW(Native.Long(full), 1, out fd, 0, IntPtr.Zero, 0);
                if (fh != new IntPtr(-1))
                {
                    size = ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
                    mod = ((long)fd.ftLastWriteTime.dwHighDateTime << 32) | (uint)fd.ftLastWriteTime.dwLowDateTime;
                    Native.FindClose(fh);
                }
                if (Interlocked.Increment(ref ctx.Collected) > ctx.CollectMax) { ctx.Limit = true; ctx.Cancel = true; break; }
                ctx.Collect.Enqueue(new Found { Path = full, Size = size, ModFt = mod, IsDir = isDir });
            }
            return true;
        }
        catch { return false; }
        finally { CloseHandle(h); }
    }

    // 多個關鍵字以空白分隔（需全部符合）；可使用萬用字元 * ?，例如 *.pdf、報告*2024*
    public static void Run(List<string> roots, string query, Ctx ctx, SearchOptions opt)
    {
        query = (query ?? "").Trim();
        Term[] terms = opt.Exact
            ? (query.Length > 0 ? new[] { new Term(query) } : new Term[0])
            : query.Split(new[] { ' ', '\t', '　' }, StringSplitOptions.RemoveEmptyEntries).Select(t => new Term(t)).ToArray();
        string exact = opt.Exact && query.IndexOfAny(new[] { '*', '?' }) < 0 ? query : null;
        var exts = opt.Exts ?? new string[0];
        int zw = opt.Zw3d;
        string winDir = null;
        try { winDir = Environment.GetEnvironmentVariable("windir"); if (winDir != null) winDir = winDir.TrimEnd('\\'); } catch { }
        string extF = opt.ExtFilter ?? "";
        if (zw > 0) exts = new string[0];
        if (terms.Length == 0 && exts.Length == 0 && !opt.DirsOnly && zw == 0 && extF.Length == 0) return;
        // 磁碟機根目錄優先用 NTFS MFT 快速掃描（失敗則退回一般掃描）；其他資料夾用一般掃描
        var walkList = new List<string>();
        var mftThreads = new List<Thread>();
        var mftFailed = new ConcurrentQueue<string>();
        foreach (var rt in roots)
        {
            if (IsDriveRoot(rt) && Directory.Exists(rt))
            {
                string rr = rt;
                var th = new Thread(() => {
                    bool done = false;
                    try { done = ScanMft(rr, terms, exact, exts, zw, extF, winDir, opt, ctx); } catch { }
                    if (!done) mftFailed.Enqueue(rr);
                }) { IsBackground = true, Name = "mft-" + rt };
                th.Start(); mftThreads.Add(th);
            }
            else walkList.Add(rt);
        }
        Action<List<string>> walk = rs => {
        var q = new ConcurrentQueue<string>();
        int pending = 0;
        foreach (var r in rs)
        {
            if (!Directory.Exists(r)) continue;
            q.Enqueue(r); pending++;
            Interlocked.Increment(ref ctx.DirsFound);
        }
        if (pending == 0) return;

        int threads = Math.Max(4, Math.Min(16, Environment.ProcessorCount * 2));
        var workers = new Thread[threads];
        for (int t = 0; t < threads; t++)
        {
            workers[t] = new Thread(() => {
                var buf = new List<Native.Entry>(256);
                int spin = 0;
                while (!ctx.Cancel)
                {
                    string d;
                    if (q.TryDequeue(out d))
                    {
                        spin = 0;
                        try
                        {
                            Native.List(d, buf);
                            // ZW3D 模式：只搜尋 ZWSOFT 建立或使用者建立的資料夾（略過 Windows 系統目錄），且資料夾內須有 .z3 / .z3prt / .dwg；並排除安裝目錄 Program Files*\\ZWSOFT
                            bool zwDir = true;
                            if (zw > 0)
                            {
                                zwDir = false;
                                foreach (var e0 in buf) if ((e0.Attr & Native.DIR) == 0 && SearchOptions.ZwAnchor(e0.Name)) { zwDir = true; break; }
                            }
                            foreach (var e in buf)
                            {
                                if (ctx.Cancel) break;
                                bool isDir = (e.Attr & Native.DIR) != 0;
                                string full = Cleaner.Child(d, e.Name);
                                if (!isDir) Interlocked.Increment(ref ctx.FilesSeen);
                                bool ok = !(isDir && zw > 0);   // ZW3D 模式不搜尋資料夾
                                if (isDir ? (opt.FilesOnly || exts.Length > 0) : opt.DirsOnly) ok = false;
                                if (ok && exts.Length > 0)
                                {
                                    ok = false;
                                    foreach (var x in exts) if (e.Name.EndsWith(x, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                                }
                                if (ok && extF.Length > 0) ok = !isDir && e.Name.EndsWith(extF, StringComparison.OrdinalIgnoreCase);
                                if (ok && zw > 0) ok = zwDir && SearchOptions.ZwMatch(zw, e.Name, isDir);
                                if (ok && exact != null) ok = string.Equals(e.Name, exact, StringComparison.OrdinalIgnoreCase);
                                else if (ok) foreach (var term in terms) if (!term.Match(e.Name)) { ok = false; break; }
                                if (ok)
                                {
                                    if (Interlocked.Increment(ref ctx.Collected) > ctx.CollectMax) { ctx.Limit = true; ctx.Cancel = true; break; }
                                    ctx.Collect.Enqueue(new Found { Path = full, Size = e.Size, ModFt = e.Mod, IsDir = isDir });
                                }
                                if (isDir && (e.Attr & Native.REPARSE) == 0 && !Skip.Contains(e.Name) && !(zw > 0 && ((winDir != null && string.Equals(full, winDir, StringComparison.OrdinalIgnoreCase)) || (string.Equals(e.Name, "ZWSOFT", StringComparison.OrdinalIgnoreCase) && Native.NameOf(d).StartsWith("Program Files", StringComparison.OrdinalIgnoreCase)))))
                                {
                                    Interlocked.Increment(ref pending);
                                    Interlocked.Increment(ref ctx.DirsFound);
                                    q.Enqueue(full);
                                }
                            }
                        }
                        catch { }
                        Interlocked.Increment(ref ctx.DirsDone);
                        if (Interlocked.Decrement(ref pending) == 0) break;
                    }
                    else
                    {
                        if (Volatile.Read(ref pending) == 0) break;
                        if (++spin < 20) Thread.Yield(); else Thread.Sleep(1);
                    }
                }
            }) { IsBackground = true, Name = "search-" + t };
            workers[t].Start();
        }
        foreach (var w in workers) w.Join();
        };
        walk(walkList);
        foreach (var mt in mftThreads) mt.Join();
        if (!mftFailed.IsEmpty && !ctx.Cancel) walk(mftFailed.ToList());
    }
}

// 磁碟機偵測（含 USB 外接硬碟、隨身碟、記憶卡、光碟機、網路磁碟）
static class Drives
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int ret, IntPtr ov);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    public class Info { public string Root, Text, Short, Icon; public bool InAll; }

    // 查詢磁碟的匯流排類型：7=USB、4=1394、0xC=SD、0xD=MMC
    static int BusType(string root)
    {
        if (!Native.Win32 || root.Length < 2) return -1;
        IntPtr h = CreateFileW(@"\\.\" + root.Substring(0, 2), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h == new IntPtr(-1)) return -1;
        try
        {
            var q = new byte[12];          // STORAGE_PROPERTY_QUERY：StorageDeviceProperty、PropertyStandardQuery
            var o = new byte[1024];
            int ret;
            if (DeviceIoControl(h, 0x002D1400, q, q.Length, o, o.Length, out ret, IntPtr.Zero) && ret >= 32)
                return BitConverter.ToInt32(o, 28);
        }
        catch { }
        finally { CloseHandle(h); }
        return -1;
    }

    public static List<Info> List()
    {
        var list = new List<Info>();
        string sys = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd(new[] { '\\' });
        DriveInfo[] all;
        try { all = DriveInfo.GetDrives(); } catch { return list; }
        foreach (var d in all)
        {
            try
            {
                string root = d.Name, id = root.TrimEnd(new[] { '\\' });
                var t = d.DriveType;
                if (t == DriveType.Network)
                {
                    // 網路磁碟不先檢查是否可用，避免斷線時選單卡住
                    list.Add(new Info { Root = root, Text = id + I18n.Tr("　網路磁碟"), Short = id + I18n.Tr(" 網路磁碟"), Icon = "drive" });
                    continue;
                }
                if (t == DriveType.NoRootDirectory || t == DriveType.Unknown || !d.IsReady) continue;   // 空的讀卡機、光碟機
                int bus = BusType(root);
                bool usb = bus == 7 || bus == 4 || bus == 0xC || bus == 0xD;
                bool ext = t == DriveType.Removable || usb;
                string kind = t == DriveType.CDRom ? I18n.Tr("光碟機")
                            : t == DriveType.Removable ? (bus == 0xC || bus == 0xD ? I18n.Tr("記憶卡") : I18n.Tr("外接裝置（卸除式）"))
                            : usb ? I18n.Tr("外接硬碟（USB）")
                            : string.Equals(id, sys, StringComparison.OrdinalIgnoreCase) ? I18n.Tr("本機磁碟（系統）") : I18n.Tr("本機磁碟");
                string label = "";
                try { label = d.VolumeLabel; } catch { }
                string name = id + (label.Length > 0 ? " " + label : "");
                string space = "";
                try { space = I18n.Tr("　可用 ") + Cleaner.Human(d.AvailableFreeSpace) + " / " + Cleaner.Human(d.TotalSize); } catch { }
                list.Add(new Info {
                    Root = root, Text = name + I18n.Tr("　") + kind + space, Short = name,
                    Icon = ext ? "usb" : t == DriveType.CDRom ? "radio" : "drive",
                    InAll = t == DriveType.Fixed || t == DriveType.Removable
                });
            }
            catch { }
        }
        return list;
    }
}

// 開啟檔案 / 檔案位置 / 資料夾
static class ShellOpen
{
    static readonly HashSet<string> RiskyExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd", ".com", ".msi", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".scr", ".pif", ".reg", ".hta", ".cpl" };

    static bool Exists(Found f) { return Native.Exists(f.Path, f.IsDir); }

    public static void OpenFile(IWin32Window owner, Found f)
    {
        if (!Exists(f)) { Missing(owner, f); return; }
        if (!f.IsDir && RiskyExt.Contains(f.Ext) &&
            MessageBox.Show(owner, I18n.Tr("「") + f.Name + I18n.Tr("」是可執行的程式或指令檔，開啟後會直接執行。\n\n確定要執行嗎？"), I18n.Tr("開啟檔案"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try { Process.Start(new ProcessStartInfo(f.Path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(owner, I18n.Tr("無法開啟：") + ex.Message, I18n.Tr("開啟檔案"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    // 在檔案總管中開啟並選取該檔案
    public static void Reveal(IWin32Window owner, Found f)
    {
        if (!Exists(f)) { Missing(owner, f); return; }
        try { Process.Start("explorer.exe", "/select,\"" + f.Path + "\""); }
        catch (Exception ex) { MessageBox.Show(owner, I18n.Tr("無法開啟位置：") + ex.Message, I18n.Tr("開啟檔案位置"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    // 資料夾：開啟本身；檔案：開啟所在資料夾
    public static void OpenFolder(IWin32Window owner, Found f)
    {
        string d = f.IsDir ? f.Path : f.Dir;
        while (!string.IsNullOrEmpty(d) && !Native.Exists(d, true)) d = Native.Parent(d);
        if (string.IsNullOrEmpty(d)) { Missing(owner, f); return; }
        try { Process.Start("explorer.exe", "\"" + d + "\""); }
        catch (Exception ex) { MessageBox.Show(owner, I18n.Tr("無法開啟資料夾：") + ex.Message, I18n.Tr("開啟資料夾"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    // ---- 刪除：預設移到資源回收筒；permanent = 永久刪除 ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom, pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);

    static readonly string[] Protected = {
        Environment.GetEnvironmentVariable("windir"), Environment.GetEnvironmentVariable("ProgramFiles"),
        Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramData") };

    public static bool IsSystemPath(string p)
    {
        foreach (var r in Protected)
            if (!string.IsNullOrEmpty(r) && p.StartsWith(r.TrimEnd(new[] { '\\' }) + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // 回傳成功刪除的項目；tooLong = 路徑過長、無法移到資源回收筒的數量
    public static List<Found> Delete(IWin32Window owner, List<Found> items, bool permanent, out int tooLong)
    {
        tooLong = 0;
        var done = new List<Found>();
        var targets = items.Where(Exists).ToList();
        foreach (var f in items) if (!Exists(f)) done.Add(f);     // 已不存在的直接從清單移除
        if (targets.Count == 0) return done;

        if (Native.Win32)
        {
            // 一次交給 Windows 處理（大量檔案時會顯示 Windows 的進度視窗）
            var op = new SHFILEOPSTRUCT {
                hwnd = owner is Control ? ((Control)owner).Handle : IntPtr.Zero,
                wFunc = 3,   // FO_DELETE
                pFrom = string.Join("\0", targets.Select(t => t.Path)) + "\0\0",
                fFlags = (ushort)(0x0010 | 0x0400 | (permanent ? 0 : 0x0040 | 0x4000)),   // NOCONFIRMATION | NOERRORUI | (ALLOWUNDO | WANTNUKEWARNING)
                lpszProgressTitle = permanent ? I18n.Tr("永久刪除") : I18n.Tr("移到資源回收筒")
            };
            var normal = targets.Where(t => t.Path.Length < 260).ToList();
            if (normal.Count > 0)
            {
                op.pFrom = string.Join("\0", normal.Select(t => t.Path)) + "\0\0";
                try { SHFileOperationW(ref op); } catch { }
            }
            // 超過 260 字元的路徑：Windows 資源回收筒不支援，只能永久刪除
            foreach (var f in targets.Where(t => t.Path.Length >= 260))
            {
                if (!permanent) { tooLong++; continue; }
                try { if (f.IsDir) Native.DeleteTree(f.Path); else Native.DeleteFile(f.Path, 0); } catch { }
            }
        }
        else
        {
            foreach (var f in targets)
                try { if (f.IsDir) Directory.Delete(f.Path, true); else File.Delete(f.Path); } catch { }
        }
        foreach (var f in targets) if (!Exists(f)) done.Add(f);
        return done;
    }

    static void Missing(IWin32Window owner, Found f)
    {
        if (MessageBox.Show(owner, I18n.Tr("「") + f.Name + I18n.Tr("」已不存在（可能已被清理或移動）。\n\n要開啟它原本所在的資料夾嗎？"), I18n.Tr("找不到項目"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            OpenFolder(owner, new Found { Path = f.Dir, IsDir = true });
    }
}

class DarkMenuColors : ProfessionalColorTable
{
    public override Color MenuItemSelected { get { return Theme.Hover; } }
    public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover; } }
    public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover; } }
    public override Color MenuItemPressedGradientBegin { get { return Theme.BtnHover; } }
    public override Color MenuItemPressedGradientEnd { get { return Theme.BtnHover; } }
    public override Color MenuItemBorder { get { return Theme.BtnBorder; } }
    public override Color MenuBorder { get { return Theme.Border; } }
    public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
    public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
    public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
    public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
    public override Color SeparatorDark { get { return Theme.Border; } }
    public override Color SeparatorLight { get { return Theme.Panel; } }
}

// 結果視窗：快速搜尋結果 / 預覽清單
class ResultsForm : Form
{
    readonly float k;
    readonly bool showTag;
    readonly List<Found> all = new List<Found>();
    List<Found> view = new List<Found>();
    readonly ConcurrentQueue<Found> inbox = new ConcurrentQueue<Found>();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 250 };
    readonly Stopwatch sw = new Stopwatch();
    readonly Bitmap icoFile, icoFolder;
    ListView lv;
    TextBox filterBox;
    Label status, title;
    Button btnOpen, btnReveal, btnFolder, btnCopy, btnStop, btnDelete, btnNuke, btnSelAll;
    Ctx ctx;
    bool running;
    string filter = "";
    int sortCol = -1; bool sortAsc = true;
    string doneText = "";

    int S(int v) { return (int)(v * k); }

    [StructLayout(LayoutKind.Sequential)]
    struct LVITEM { public uint mask; public int iItem, iSubItem; public uint state, stateMask; public IntPtr pszText; public int cchTextMax, iImage; public IntPtr lParam; public int iIndent, iGroupId; public uint cColumns; public IntPtr puColumns; public IntPtr piColFmt; public int iGroup; }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref LVITEM l);

    // 虛擬清單一次選取／取消全部（LVM_SETITEMSTATE, index = -1 代表全部項目）
    void SelectAllItems(bool on)
    {
        if (view.Count == 0) return;
        var it = new LVITEM { stateMask = 0x2, state = on ? 0x2u : 0u };   // LVIS_SELECTED
        lv.BeginUpdate();
        try { SendMessage(lv.Handle, 0x102B, (IntPtr)(-1), ref it); } finally { lv.EndUpdate(); }
        lv.Invalidate();
        UpdateButtons();
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(Handle); }

    public ResultsForm(string caption, bool showTag, float scale, Font baseFont, Icon icon)
    {
        k = scale; this.showTag = showTag;
        Text = caption;
        Font = baseFont;
        Icon = icon;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(S(960), S(620));
        MinimumSize = new Size(S(640), S(400));
        BackColor = Theme.Bg; ForeColor = Theme.Text;
        KeyPreview = true;
        icoFile = Theme.Ico("file", S(20));
        icoFolder = Theme.Ico("folder", S(20));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(S(12), S(8), S(12), S(8)) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        // 標題 + 篩選
        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 0, 0, S(10)) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        string filterHint = I18n.Tr("在結果中篩選（名稱或路徑）…");
        // 篩選欄寬依目前語系提示文字長度自動調整（限制在合理範圍）
        int filterW = Math.Min(S(520), Math.Max(S(200), TextRenderer.MeasureText(filterHint, baseFont).Width + S(40)));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, filterW));
        var logo = new PictureBox { Image = Theme.Ico("scan", S(28)), Size = new Size(S(28), S(28)), Margin = new Padding(0, 0, S(8), 0) };
        title = new Label { Text = caption, AutoSize = true, Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), ForeColor = Theme.Text, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
        filterBox = Theme.DarkTextBox(filterHint);
        var filterWrap = Theme.Framed(filterBox, S(8));
        filterWrap.Anchor = AnchorStyles.Left | AnchorStyles.Right; filterWrap.Dock = DockStyle.None;
        filterWrap.Height = S(26);
        filterBox.TextChanged += (o, e) => { filter = filterBox.Text.Trim(); Rebuild(); };
        top.Controls.Add(logo, 0, 0);
        top.Controls.Add(title, 1, 0);
        top.Controls.Add(filterWrap, 2, 0);
        root.Controls.Add(top, 0, 0);

        status = new Label { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Height = S(24), ForeColor = Theme.Sub, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(S(2), 0, 0, S(6)) };
        root.Controls.Add(status, 0, 1);

        lv = new ListView {
            View = View.Details, VirtualMode = true, OwnerDraw = true, FullRowSelect = true, MultiSelect = true,
            HideSelection = false, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text,
            HeaderStyle = ColumnHeaderStyle.Clickable
        };
        lv.SmallImageList = new ImageList { ImageSize = new Size(1, S(22)) };   // 列高（配合 10pt 字高）
        lv.Columns.Add(I18n.Tr("名稱"), S(260));
        lv.Columns.Add(I18n.Tr("位置"), S(360));
        lv.Columns.Add(I18n.Tr("大小"), S(90), HorizontalAlignment.Right);
        lv.Columns.Add(I18n.Tr("修改日期"), S(140));
        if (showTag) lv.Columns.Add(I18n.Tr("清理項目"), S(160));
        Theme.DarkScrollbars(lv);
        lv.RetrieveVirtualItem += (o, e) => {
            if (e.ItemIndex < 0 || e.ItemIndex >= view.Count) { e.Item = new ListViewItem(""); for (int i = 1; i < lv.Columns.Count; i++) e.Item.SubItems.Add(""); return; }
            var f = view[e.ItemIndex];
            var it = new ListViewItem(f.Name);
            it.SubItems.Add(f.Dir); it.SubItems.Add(SizeText(f)); it.SubItems.Add(DateText(f));
            if (showTag) it.SubItems.Add(f.Tag ?? "");
            e.Item = it;
        };
        lv.DrawColumnHeader += DrawHeader;
        lv.DrawItem += (o, e) => { };
        lv.DrawSubItem += DrawCell;
        lv.ColumnClick += (o, e) => { if (sortCol == e.Column) sortAsc = !sortAsc; else { sortCol = e.Column; sortAsc = e.Column != 2 && e.Column != 3; } SortView(); lv.Invalidate(); };
        lv.DoubleClick += (o, e) => Act(ShellOpen.Reveal, 1);
        lv.SelectedIndexChanged += (o, e) => UpdateButtons();
        lv.VirtualItemsSelectionRangeChanged += (o, e) => UpdateButtons();
        lv.Resize += (o, e) => FitColumns();
        lv.ContextMenuStrip = BuildMenu();
        root.Controls.Add(Theme.Framed(lv, 0), 0, 2);

        // 底部按鈕
        btnSelAll = Theme.MakeButton(I18n.Tr("全選") + "/" + I18n.Tr("全不選"), false, "selectall", k, Font);
        // 單一按鈕：目前已全選就取消全選，否則全選；全選狀態下只有刪除／永久刪除／關閉可用
        btnSelAll.Click += (o, e) => {
            bool allOn = view.Count > 0 && lv.SelectedIndices.Count == view.Count;
            SelectAllItems(!allOn);
            selAllMode = !allOn && view.Count > 0;
            UpdateButtons();
            lv.Focus();
        };
        btnOpen = Theme.MakeButton(I18n.Tr("開啟檔案"), false, "file", k, Font);
        btnReveal = Theme.MakeButton(I18n.Tr("開啟檔案位置"), true, "scan", k, Font);
        btnFolder = Theme.MakeButton(I18n.Tr("開啟資料夾"), false, "folder", k, Font);
        btnCopy = Theme.MakeButton(I18n.Tr("複製路徑"), false, "selectall", k, Font);
        btnOpen.Click += (o, e) => Act(ShellOpen.OpenFile, 1);
        btnReveal.Click += (o, e) => Act(ShellOpen.Reveal, 5);
        btnFolder.Click += (o, e) => Act(ShellOpen.OpenFolder, 5);
        btnCopy.Click += (o, e) => CopyPaths();
        btnDelete = Theme.MakeButton(I18n.Tr("刪除檔案"), false, "recycle", k, Font);
        btnDelete.ForeColor = Theme.Danger;
        btnDelete.FlatAppearance.BorderColor = Color.FromArgb(120, Theme.Danger);
        btnDelete.EnabledChanged += (o, e) => btnDelete.ForeColor = btnDelete.Enabled ? Theme.Danger : Theme.Sub;
        btnDelete.Click += (o, e) => {
            // 按住 Shift 點擊 = 永久刪除
            DeleteSelected(permanentMode || (Control.ModifierKeys & Keys.Shift) != 0);
        };
        new ToolTip().SetToolTip(btnDelete, I18n.Tr("移到資源回收筒（按住 Shift 點擊為永久刪除）"));
        btnNuke = Theme.MakeButton(I18n.Tr("永久刪除"), false, "delete", k, Font);
        btnNuke.ForeColor = Theme.Danger;
        btnNuke.FlatAppearance.BorderColor = Color.FromArgb(120, Theme.Danger);
        btnNuke.EnabledChanged += (o, e) => btnNuke.ForeColor = btnNuke.Enabled ? Theme.Danger : Theme.Sub;
        btnNuke.Click += (o, e) => DeleteSelected(true);
        new ToolTip().SetToolTip(btnNuke, I18n.Tr("永久刪除（不經資源回收筒，無法還原）｜Shift+Delete"));
        btnStop = Theme.MakeButton(I18n.Tr("停止搜尋"), false, "stop", k, Font);
        btnStop.Click += (o, e) => { if (ctx != null) ctx.Cancel = true; };
        var btnClose = Theme.MakeButton(I18n.Tr("關閉"), false, "unselect", k, Font);
        btnClose.Click += (o, e) => Close();

        // 兩行按鈕：每行按鈕等寬、等間距，從最左填滿到最右（無空白）；
        // 其他語系文字太長放不下時，該行按鈕自動變成兩行高，文字換行顯示
        var bottom = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, S(12), 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        int baseH = new[] { btnReveal, btnFolder, btnOpen, btnCopy, btnSelAll, btnDelete, btnNuke, btnStop, btnClose }.Max(x => x.GetPreferredSize(Size.Empty).Height);
        Func<Button[], Padding, Control> mkRow = (btns, margin) => {
            var tl = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = btns.Length * 2 - 1, RowCount = 1, Margin = margin };
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int i = 0; i < btns.Length; i++)
            {
                tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / btns.Length));
                if (i < btns.Length - 1) tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(6)));
                var b = btns[i];
                b.AutoSize = false; b.AutoEllipsis = false; b.ImageAlign = ContentAlignment.MiddleCenter; b.TextAlign = ContentAlignment.MiddleCenter;
                b.TextImageRelation = TextImageRelation.ImageBeforeText; b.Padding = new Padding(S(2), 0, S(2), 0);
                b.Dock = DockStyle.Fill; b.Margin = new Padding(0);
                tl.Controls.Add(b, i * 2, 0);
            }
            bool busy = false;
            Action reflow = () => {
                if (busy || tl.ClientSize.Width <= 0) return;
                busy = true;
                try
                {
                    int vis = btns.Count(x => x.Visible);
                    if (vis == 0) return;
                    int lastVis = Array.FindLastIndex(btns, x => x.Visible);
                    int gaps = 0;
                    for (int i = 0; i < btns.Length - 1; i++)
                    {
                        bool gap = btns[i].Visible && i < lastVis;
                        tl.ColumnStyles[i * 2 + 1].Width = gap ? S(6) : 0;
                        if (gap) gaps += S(6);
                    }
                    for (int i = 0; i < btns.Length; i++) { tl.ColumnStyles[i * 2].SizeType = SizeType.Percent; tl.ColumnStyles[i * 2].Width = btns[i].Visible ? 100f / vis : 0; }
                    int colW = (tl.ClientSize.Width - gaps) / vis;
                    bool wrap = btns.Any(x => x.Visible && TextRenderer.MeasureText(x.Text, x.Font).Width + S(18) + S(14) > colW);
                    int h = wrap ? baseH + Font.Height : baseH;
                    foreach (var x in btns) { x.MinimumSize = new Size(0, h); x.MaximumSize = new Size(0, h); }
                }
                finally { busy = false; }
            };
            tl.SizeChanged += (o, e) => reflow();
            foreach (var x in btns) x.VisibleChanged += (o, e) => reflow();
            reflow();
            return tl;
        };
        bottom.Controls.Add(mkRow(new Button[] { btnReveal, btnFolder, btnOpen, btnCopy }, new Padding(0, 0, 0, S(6))), 0, 0);
        bottom.Controls.Add(mkRow(new Button[] { btnSelAll, btnDelete, btnNuke, btnStop, btnClose }, new Padding(0)), 0, 1);
        root.Controls.Add(bottom, 0, 3);

        btnStop.Visible = false;
        UpdateButtons();
        timer.Tick += (o, e) => Drain();
        KeyDown += OnKey;
        FormClosed += (o, e) => { if (ctx != null) ctx.Cancel = true; timer.Stop(); };
        Shown += (o, e) => FitColumns();
    }

    bool selAllMode;
    bool permanentMode;
    // 由搜尋選單「永久刪除檔案」開啟時，刪除按鈕與 Delete 鍵直接永久刪除
    public void SetPermanent(bool on)
    {
        permanentMode = on;
        btnDelete.Text = on ? I18n.Tr("永久刪除") : I18n.Tr("刪除檔案");
        new ToolTip().SetToolTip(btnDelete, on ? I18n.Tr("永久刪除（不經資源回收筒，無法還原）") : I18n.Tr("移到資源回收筒（按住 Shift 點擊為永久刪除）"));
    }

    ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()), BackColor = Theme.Panel, ForeColor = Color.White, ShowImageMargin = true };
        m.ImageScalingSize = new Size(S(20), S(20));
        Func<string, string, Action, ToolStripMenuItem> add = (t, ic, a) => {
            var i = new ToolStripMenuItem(t, Theme.Ico(ic, S(20)), (o, e) => a()) { ForeColor = Color.White };
            m.Items.Add(i); return i;
        };
        add(I18n.Tr("開啟檔案位置（選取該檔案）"), "scan", () => Act(ShellOpen.Reveal, 5));
        add(I18n.Tr("開啟資料夾"), "folder", () => Act(ShellOpen.OpenFolder, 5));
        add(I18n.Tr("開啟檔案"), "file", () => Act(ShellOpen.OpenFile, 1));
        m.Items.Add(new ToolStripSeparator());
        add(I18n.Tr("複製完整路徑"), "selectall", CopyPaths);
        add(I18n.Tr("複製檔名"), "selectall", () => CopyText(Selected().Select(f => f.Name)));
        m.Items.Add(new ToolStripSeparator());
        var del = add(I18n.Tr("刪除（移到資源回收筒）"), "recycle", () => DeleteSelected(false));
        del.ForeColor = Color.White; del.ShortcutKeyDisplayString = "Delete";
        var nuke = add(I18n.Tr("永久刪除…"), "delete", () => DeleteSelected(true));
        nuke.ForeColor = Color.White; nuke.ShortcutKeyDisplayString = "Shift+Delete";
        m.Opening += (o, e) => { if (lv.SelectedIndices.Count == 0) e.Cancel = true; };
        return m;
    }

    void OnKey(object o, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { if (running && ctx != null) ctx.Cancel = true; else Close(); }
        if (!lv.Focused) return;
        if (e.KeyCode == Keys.Enter) { Act(ShellOpen.Reveal, 5); e.Handled = true; }
        if (e.Control && e.KeyCode == Keys.C) { CopyPaths(); e.Handled = true; }
        if (e.KeyCode == Keys.Delete) { DeleteSelected(permanentMode || e.Shift); e.Handled = true; }
    }

    List<Found> Selected()
    {
        var r = new List<Found>();
        foreach (int i in lv.SelectedIndices) if (i < view.Count) r.Add(view[i]);
        return r;
    }

    void Act(Action<IWin32Window, Found> a, int max)
    {
        var sel = Selected();
        if (sel.Count == 0) return;
        if (sel.Count > max)
        {
            if (MessageBox.Show(this, I18n.Tr("已選取 ") + sel.Count + I18n.Tr(" 個項目，只會開啟前 ") + max + I18n.Tr(" 個。要繼續嗎？"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            sel = sel.Take(max).ToList();
        }
        foreach (var f in sel) a(this, f);
    }

    void DeleteSelected(bool permanent)
    {
        var sel = Selected();
        if (sel.Count == 0) return;
        long size = sel.Where(f => !f.IsDir).Sum(f => f.Size);
        int dirs = sel.Count(f => f.IsDir);
        string names = string.Join("\n", sel.Take(8).Select(f => "• " + f.Name)) + (sel.Count > 8 ? I18n.Tr("\n…等共 ") + sel.Count.ToString("N0") + I18n.Tr(" 個項目") : "");
        string what = sel.Count.ToString("N0") + I18n.Tr(" 個項目") + (dirs > 0 ? I18n.Tr("（含 ") + dirs + I18n.Tr(" 個資料夾及其內容）") : "") + I18n.Tr("，檔案合計 ") + Cleaner.Human(size);
        string msg = permanent
            ? I18n.Tr("確定要「永久刪除」下列 ") + what + I18n.Tr(" 嗎？\n永久刪除後無法從資源回收筒還原！\n\n") + names
            : I18n.Tr("確定要將下列 ") + what + I18n.Tr(" 移到資源回收筒嗎？\n之後仍可從資源回收筒還原。\n\n") + names;
        if (MessageBox.Show(this, msg, permanent ? I18n.Tr("永久刪除") : I18n.Tr("刪除檔案"), MessageBoxButtons.YesNo,
                permanent ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        var sys = sel.Where(f => ShellOpen.IsSystemPath(f.Path)).ToList();
        if (sys.Count > 0 && MessageBox.Show(this,
                I18n.Tr("其中 ") + sys.Count + I18n.Tr(" 個項目位於 Windows 或程式安裝資料夾，例如：\n") + sys[0].Path +
                I18n.Tr("\n\n刪除這些檔案可能導致程式或系統無法正常運作。仍要刪除嗎？"), I18n.Tr("系統檔案警告"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        Cursor = Cursors.WaitCursor;
        List<Found> done;
        int tooLong = 0;
        try { done = ShellOpen.Delete(this, sel, permanent, out tooLong); }
        finally { Cursor = Cursors.Default; }

        var gone = new HashSet<Found>(done);
        all.RemoveAll(gone.Contains);
        view.RemoveAll(gone.Contains);
        lv.SelectedIndices.Clear();
        lv.VirtualListSize = view.Count;
        lv.Invalidate();
        UpdateButtons();
        long freed = done.Where(f => !f.IsDir).Sum(f => f.Size);
        int failed = sel.Count - done.Count;
        UpdateStatus();
        string result = (permanent ? I18n.Tr("已永久刪除 ") : I18n.Tr("已移到資源回收筒 ")) + done.Count.ToString("N0") + I18n.Tr(" 個項目（") + Cleaner.Human(freed) + I18n.Tr("）")
            + (failed - tooLong > 0 ? I18n.Tr("；") + (failed - tooLong) + I18n.Tr(" 個無法刪除（使用中或權限不足）") : "")
            + (tooLong > 0 ? I18n.Tr("；") + tooLong + I18n.Tr(" 個路徑過長，無法移到資源回收筒（可改用 Shift+Delete 永久刪除）") : "");
        status.Text = result;
        if (failed > 0) MessageBox.Show(this, result + I18n.Tr("\n\n可關閉正在使用這些檔案的程式，或以系統管理員身分執行後再試。"), I18n.Tr("刪除檔案"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void CopyPaths() { CopyText(Selected().Select(f => f.Path)); }
    void CopyText(IEnumerable<string> lines)
    {
        string t = string.Join(Environment.NewLine, lines);
        if (t.Length == 0) return;
        try { Clipboard.SetText(t); status.Text = I18n.Tr("已複製到剪貼簿。"); } catch { }
    }

    void UpdateButtons()
    {
        int n = lv.SelectedIndices.Count;
        if (selAllMode && n != view.Count) selAllMode = false;
        // 使用「全選」後，只有刪除檔案／永久刪除可用（關閉、全選/全不選本身不受影響），其餘按鈕灰選
        btnDelete.Enabled = btnNuke.Enabled = n > 0;
        btnOpen.Enabled = btnReveal.Enabled = btnFolder.Enabled = btnCopy.Enabled = n > 0 && !selAllMode;
    }

    static string SizeText(Found f) { return f.IsDir ? I18n.Tr("資料夾") : Cleaner.Human(f.Size); }
    static string DateText(Found f) { var d = f.Modified; return d == DateTime.MinValue || d.Year < 1980 ? "" : d.ToString("yyyy/MM/dd HH:mm"); }

    void FitColumns()
    {
        int others = 0;
        for (int i = 0; i < lv.Columns.Count; i++) if (i != 1) others += lv.Columns[i].Width;
        lv.Columns[1].Width = Math.Max(S(160), lv.ClientSize.Width - others - S(2));
    }

    void DrawHeader(object o, DrawListViewColumnHeaderEventArgs e)
    {
        using (var b = new SolidBrush(Theme.Btn)) e.Graphics.FillRectangle(b, e.Bounds);
        using (var p = new Pen(Theme.Border))
        {
            e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + S(4), e.Bounds.Right - 1, e.Bounds.Bottom - S(4));
        }
        string t = e.Header.Text + (sortCol == e.ColumnIndex ? (sortAsc ? "  ▲" : "  ▼") : "");
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
                    (e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
        var r = Rectangle.Inflate(e.Bounds, -S(8), 0);
        TextRenderer.DrawText(e.Graphics, t, new Font(Font, FontStyle.Bold), r, sortCol == e.ColumnIndex ? Theme.Accent : Theme.Sub, flags);
    }

    void DrawCell(object o, DrawListViewSubItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= view.Count) return;
        var f = view[e.ItemIndex];
        bool sel = lv.SelectedIndices.Contains(e.ItemIndex);
        Color bg = sel ? Color.FromArgb(22, 74, 96) : (e.ItemIndex % 2 == 0 ? Theme.Panel : Color.FromArgb(17, 33, 46));
        using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
        if (sel && e.ColumnIndex == 0)
            using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, e.Bounds.Left, e.Bounds.Top + S(4), Math.Max(2, S(3)), e.Bounds.Height - S(8));

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        var r = Rectangle.Inflate(e.Bounds, -S(8), 0);
        switch (e.ColumnIndex)
        {
            case 0:
                var ic = f.IsDir ? icoFolder : icoFile;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(ic, e.Bounds.Left + S(10), e.Bounds.Top + (e.Bounds.Height - ic.Height) / 2, ic.Width, ic.Height);
                r = new Rectangle(e.Bounds.Left + S(38), e.Bounds.Top, e.Bounds.Width - S(42), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, f.Name, Font, r, f.IsDir ? Theme.Accent : Theme.Text, flags);
                break;
            case 1: TextRenderer.DrawText(e.Graphics, f.Dir, Font, r, Theme.Desc, flags | TextFormatFlags.PathEllipsis); break;
            case 2: TextRenderer.DrawText(e.Graphics, SizeText(f), Font, r, f.IsDir ? Theme.Sub : Theme.Text, flags | TextFormatFlags.Right); break;
            case 3: TextRenderer.DrawText(e.Graphics, DateText(f), Font, r, Theme.Desc, flags); break;
            case 4: TextRenderer.DrawText(e.Graphics, f.Tag ?? "", Font, r, Theme.Sub, flags); break;
        }
    }

    bool Pass(Found f)
    {
        if (filter.Length == 0) return true;
        foreach (var t in filter.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries))
            if (f.Path.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    void Rebuild()
    {
        view = filter.Length == 0 ? new List<Found>(all) : all.Where(Pass).ToList();
        SortView();
        lv.SelectedIndices.Clear();
        lv.VirtualListSize = view.Count;
        lv.Invalidate();
        UpdateStatus();
        UpdateButtons();
    }

    void SortView()
    {
        if (sortCol < 0) return;
        Comparison<Found> cmp;
        switch (sortCol)
        {
            case 0: cmp = (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); break;
            case 1: cmp = (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase); break;
            case 2: cmp = (a, b) => (a.IsDir ? -1 : a.Size).CompareTo(b.IsDir ? -1 : b.Size); break;
            case 3: cmp = (a, b) => a.ModFt.CompareTo(b.ModFt); break;
            default: cmp = (a, b) => string.Compare(a.Tag, b.Tag, StringComparison.CurrentCulture); break;
        }
        if (sortAsc) view.Sort(cmp); else view.Sort((a, b) => cmp(b, a));
    }

    void Drain()
    {
        int added = 0;
        Found f;
        while (inbox.TryDequeue(out f))
        {
            all.Add(f);
            if (Pass(f)) { view.Add(f); added++; }
        }
        if (added > 0) lv.VirtualListSize = view.Count;
        UpdateStatus();
    }

    void UpdateStatus()
    {
        string head = view.Count == all.Count ? I18n.Tr("共 ") + all.Count.ToString("N0") + I18n.Tr(" 筆") : I18n.Tr("顯示 ") + view.Count.ToString("N0") + " / " + all.Count.ToString("N0") + I18n.Tr(" 筆");
        long total = 0; foreach (var x in view) if (!x.IsDir) total += x.Size;
        head += I18n.Tr("（檔案合計 ") + Cleaner.Human(total) + I18n.Tr("）");
        if (running && ctx != null)
            status.Text = I18n.Tr("搜尋中… ") + head + I18n.Tr("｜已掃描 ") + Interlocked.Read(ref ctx.DirsDone).ToString("N0") + I18n.Tr(" 個資料夾、") +
                Interlocked.Read(ref ctx.FilesSeen).ToString("N0") + I18n.Tr(" 個檔案｜") + sw.Elapsed.TotalSeconds.ToString("0.0") + I18n.Tr(" 秒");
        else
            status.Text = head + doneText + I18n.Tr("　｜　雙擊或按 Enter 開啟檔案位置，右鍵有更多選項");
    }

    // 載入固定清單（預覽結果）
    public void LoadItems(IEnumerable<Found> items, string note)
    {
        all.AddRange(items);
        doneText = note ?? "";
        sortCol = 2; sortAsc = false;   // 預設依大小排序，大檔在前
        Rebuild();
    }

    public void StartSearch(List<string> roots, string query, SearchOptions opt)
    {
        ctx = new Ctx { Dry = true, Collect = inbox, CollectMax = 200000 };
        var c = ctx;
        running = true;
        btnStop.Visible = true;
        sw.Start();
        timer.Start();
        var th = new Thread(() => {
            try { Searcher.Run(roots, query, c, opt); } catch { }
            sw.Stop();
            try
            {
                if (!IsDisposed) BeginInvoke((Action)(() => {
                    running = false;
                    timer.Stop();
                    Drain();
                    btnStop.Visible = false;
                    doneText = c.Limit ? I18n.Tr("　已達 200,000 筆上限，請輸入更精確的關鍵字")
                             : c.Cancel ? I18n.Tr("　已停止搜尋")
                             : I18n.Tr("　搜尋完成，耗時 ") + sw.Elapsed.TotalSeconds.ToString("0.0") + I18n.Tr(" 秒（掃描 ") + c.DirsDone.ToString("N0") + I18n.Tr(" 個資料夾）");
                    if (sortCol >= 0) Rebuild(); else UpdateStatus();
                    if (all.Count == 0) status.Text = I18n.Tr("找不到符合的檔案或資料夾。") + doneText;
                }));
            }
            catch { }
        }) { IsBackground = true };
        th.Start();
    }
}
// ================= 視窗介面（深石墨＋青色主題） =================

static class Theme
{
    public static readonly Color Bg = C("#0b141d");        // 視窗底色
    public static readonly Color Panel = C("#0f1c27");     // 清單、紀錄底色
    public static readonly Color Hover = C("#18293a");     // 滑鼠移過
    public static readonly Color Border = C("#2a4152");
    public static readonly Color Text = C("#eaf5fb");
    public static readonly Color Sub = C("#83a6b9");
    public static readonly Color Desc = C("#a3bac8");
    public static readonly Color Accent = C("#52e8ff");
    public static readonly Color Primary = C("#0e7fa0");
    public static readonly Color PrimaryHover = C("#1497bd");
    public static readonly Color Btn = C("#16283a");
    public static readonly Color BtnHover = C("#203a50");
    public static readonly Color BtnBorder = C("#3a5668");
    public static readonly Color Safe = C("#5ee08a");
    public static readonly Color Caution = C("#ffb454");
    public static readonly Color Danger = C("#ff6678");
    static Color C(string h) { return ColorTranslator.FromHtml(h); }

    public static Color RiskColor(Risk r) { return r == Risk.Safe ? Safe : r == Risk.Caution ? Caution : Danger; }
    public static string RiskText(Risk r) { return r == Risk.Safe ? I18n.Tr("安全") : r == Risk.Caution ? I18n.Tr("注意") : I18n.Tr("危險"); }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

    // 深色標題列（Windows 10/11）與 Windows 11 標題列配色
    public static void DarkTitle(IntPtr h)
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4);
            int cap = 0x001d140b, txt = 0x00fbf5ea, brd = 0x00524129;
            DwmSetWindowAttribute(h, 35, ref cap, 4);
            DwmSetWindowAttribute(h, 36, ref txt, 4);
            DwmSetWindowAttribute(h, 34, ref brd, 4);
        }
        catch { }
    }

    // 深色提示框
    public static ToolTip DarkTip(Font font)
    {
        var tip = new ToolTip { OwnerDraw = true, InitialDelay = 250, AutoPopDelay = 15000, ReshowDelay = 100 };
        tip.Popup += (o, e) => { var sz = TextRenderer.MeasureText(tip.GetToolTip(e.AssociatedControl), font); e.ToolTipSize = new Size(sz.Width + 20, sz.Height + 12); };
        tip.Draw += (o, e) => {
            using (var b = new SolidBrush(Panel)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Accent)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, font, e.Bounds, Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        return tip;
    }

    // 深色輸入框（含提示文字）
    public static TextBox DarkTextBox(string cue)
    {
        var t = new TextBox { BorderStyle = BorderStyle.None, BackColor = Btn, ForeColor = Text };
        t.HandleCreated += (o, e) => { try { SendMessage(t.Handle, 0x1501, (IntPtr)1, cue); } catch { } };
        return t;
    }

    // 1px 邊框外框
    public static Panel Framed(Control inner, int pad)
    {
        var bg = inner.BackColor;
        var outer = new Panel { Dock = DockStyle.Fill, BackColor = Border, Padding = new Padding(1), Margin = new Padding(0) };
        var mid = new Panel { Dock = DockStyle.Fill, BackColor = bg, Padding = new Padding(pad, 0, pad, 0) };
        inner.Dock = DockStyle.Fill;
        if (pad > 0)
        {
            // 單行輸入框垂直置中
            inner.Dock = DockStyle.None;
            EventHandler center = (o, e) => {
                int ih = inner is TextBox ? ((TextBox)inner).PreferredHeight : inner.Height;
                if (inner is TextBox && ((TextBox)inner).BorderStyle == BorderStyle.None) ih = TextRenderer.MeasureText("國Ag", inner.Font).Height;
                inner.SetBounds(pad, Math.Max(0, (mid.ClientSize.Height - ih + 1) / 2), Math.Max(10, mid.ClientSize.Width - pad * 2), ih);
            };
            mid.Resize += center; mid.Layout += (o, e) => center(o, e); inner.HandleCreated += center; inner.FontChanged += center;
        }
        mid.Controls.Add(inner);
        outer.Controls.Add(mid);
        return outer;
    }

    public static Button MakeButton(string text, bool primary, string icon, float k, Font font)
    {
        Func<int, int> S = v => (int)(v * k);
        var b = new Button {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Image = Ico(icon, S(18)), ImageAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Padding = new Padding(S(4), S(1), S(8), S(1)), Margin = new Padding(0, 0, S(6), 0),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
            ForeColor = Text, Font = font
        };
        if (primary)
        {
            b.BackColor = Primary;
            b.FlatAppearance.BorderColor = Accent;
            b.FlatAppearance.MouseOverBackColor = PrimaryHover;
            b.FlatAppearance.MouseDownBackColor = Primary;
            b.Font = new Font(font, FontStyle.Bold);
        }
        else
        {
            b.BackColor = Btn;
            b.FlatAppearance.BorderColor = BtnBorder;
            b.FlatAppearance.MouseOverBackColor = BtnHover;
            b.FlatAppearance.MouseDownBackColor = Hover;
        }
        var img = b.Image;
        b.EnabledChanged += (o, e) => {
            b.Image = b.Enabled ? img : Fade(img);
            b.ForeColor = b.Enabled ? Text : Sub;
            if (primary) { b.BackColor = b.Enabled ? Primary : Btn; b.FlatAppearance.BorderColor = b.Enabled ? Accent : BtnBorder; }
        };
        return b;
    }

    public static Bitmap Fade(Image src)
    {
        var b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        {
            var ia = new ImageAttributes();
            ia.SetColorMatrix(new ColorMatrix { Matrix33 = .45f });
            g.DrawImage(src, new Rectangle(0, 0, b.Width, b.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
        }
        return b;
    }
    public static void DarkScrollbars(Control c)
    {
        c.HandleCreated += (o, e) => { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
    }

    // 內嵌的金屬風格圖示（PNG 資源），高品質縮放
    public static Bitmap Ico(string name, int size)
    {
        using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(name + ".png"))
        {
            if (st == null) return new Bitmap(size, size, PixelFormat.Format32bppArgb);   // 缺圖示時顯示空白，不中斷
        }
        using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(name + ".png"))
        using (var src = new Bitmap(st))
        {
            var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(src, 0, 0, size, size);
            }
            return b;
        }
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// 自繪的項目清單：金屬勾選框＋項目圖示＋風險標籤
class ItemList : Panel
{
    class Row { public CleanItem Item; public string Header; public int Y, H; public Bitmap Icon; }

    readonly List<Row> rows = new List<Row>();
    readonly Bitmap chkOn, chkOff;
    readonly float k;
    readonly Font nameFont, descFont, headFont, pillFont;
    public readonly Dictionary<CleanItem, bool> Checked = new Dictionary<CleanItem, bool>();
    int hover = -1;
    readonly int nameW, pillW;

    int S(int v) { return (int)(v * k); }

    public ItemList(List<CleanItem> items, string[] icons, float scale, Font baseFont)
    {
        k = scale;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Panel;
        AutoScroll = true;
        Theme.DarkScrollbars(this);
        nameFont = new Font(baseFont.FontFamily, 10f);
        descFont = new Font(baseFont.FontFamily, 9f);
        headFont = new Font(baseFont.FontFamily, 9f, FontStyle.Bold);
        pillFont = new Font(baseFont.FontFamily, 8f, FontStyle.Bold);
        chkOn = Theme.Ico("chk_on", S(18));
        chkOff = Theme.Ico("chk_off", S(18));
        // 依目前語系實際量測名稱與風險標籤寬度，避免 15 語系裁切
        int nw = 0, pw = 0;
        foreach (var ci in items) nw = Math.Max(nw, TextRenderer.MeasureText(ci.Name, nameFont).Width);
        foreach (Risk rk in new[] { Risk.Safe, Risk.Caution, Risk.Danger }) pw = Math.Max(pw, TextRenderer.MeasureText(Theme.RiskText(rk), pillFont).Width);
        nameW = Math.Max(S(150), Math.Min(S(300), nw + S(6)));
        pillW = Math.Max(S(40), pw + S(14));

        string[] heads = { I18n.Tr("建議清理　SAFE"), I18n.Tr("進階項目　請確認後再勾選"), I18n.Tr("危險項目　原批次檔中的刪除動作，無法復原") };
        int y = S(6);
        for (int r = 0; r <= 2; r++)
        {
            rows.Add(new Row { Header = heads[r], Y = y, H = S(24) }); y += S(24);
            for (int i = 0; i < items.Count; i++)
            {
                if ((int)items[i].Risk != r) continue;
                rows.Add(new Row { Item = items[i], Y = y, H = S(30), Icon = Theme.Ico(i < icons.Length ? icons[i] : "file", S(20)) });
                Checked[items[i]] = items[i].Default;
                y += S(30);
            }
            y += S(4);
        }
        AutoScrollMinSize = new Size(0, y);
    }

    public void SetAll(Func<CleanItem, bool> f)
    {
        foreach (var r in rows) if (r.Item != null) Checked[r.Item] = f(r.Item);
        Invalidate();
    }

    int HitRow(Point p)
    {
        int y = p.Y - AutoScrollPosition.Y;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Item != null && y >= rows[i].Y && y < rows[i].Y + rows[i].H) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = Enabled ? HitRow(e.Location) : -1;
        if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (!Enabled || e.Button != MouseButtons.Left) return;
        int h = HitRow(e.Location);
        if (h < 0) return;
        var it = rows[h].Item;
        if (!Checked[it] && it.Risk == Risk.Danger)
        {
            string wt = I18n.Tr("警告");
            if (MessageBox.Show(FindForm(), I18n.Tr("不懂就別亂勾"), wt, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            if (MessageBox.Show(FindForm(), I18n.Tr("如果無法開機或帳號被刪除，活該"), wt, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        }
        Checked[it] = !Checked[it];
        Invalidate();
    }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); hover = -1; Invalidate(); }
    protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.TranslateTransform(0, AutoScrollPosition.Y);
        int w = ClientSize.Width;
        int alpha = Enabled ? 255 : 120;
        var fmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Header != null)
            {
                var rc = new RectangleF(S(12), r.Y + S(3), w - S(24), r.H - S(4));
                using (var b = new SolidBrush(Color.FromArgb(alpha, Theme.Sub))) g.DrawString(r.Header, headFont, b, rc, fmt);
                using (var p = new Pen(Color.FromArgb(alpha, Theme.Border))) g.DrawLine(p, S(14), r.Y + r.H - 1, w - S(14), r.Y + r.H - 1);
                continue;
            }
            var it = r.Item;
            bool on = Checked[it];
            var box = new RectangleF(S(6), r.Y + S(2), w - S(12), r.H - S(4));
            if (i == hover || on)
                using (var path = Theme.Round(box, S(6)))
                using (var b = new SolidBrush(i == hover ? Theme.Hover : Color.FromArgb(18, Theme.Accent)))
                    g.FillPath(b, path);
            if (on)
                using (var p = new Pen(Color.FromArgb(Enabled ? 200 : 90, Theme.Accent), Math.Max(2, S(3))))
                    g.DrawLine(p, S(7), r.Y + S(8), S(7), r.Y + r.H - S(8));

            int cy = r.Y + r.H / 2;
            var ia = new System.Drawing.Imaging.ImageAttributes();
            if (!Enabled) ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = .45f });
            var cb = on ? chkOn : chkOff;
            g.DrawImage(cb, new Rectangle(S(14), cy - cb.Height / 2, cb.Width, cb.Height), 0, 0, cb.Width, cb.Height, GraphicsUnit.Pixel, ia);
            g.DrawImage(r.Icon, new Rectangle(S(40), cy - r.Icon.Height / 2, r.Icon.Width, r.Icon.Height), 0, 0, r.Icon.Width, r.Icon.Height, GraphicsUnit.Pixel, ia);

            int nameX = S(68), pillX = nameX + nameW + S(6), descX = pillX + pillW + S(10);
            Color nc = it.Risk == Risk.Danger ? Theme.Danger : Theme.Text;
            using (var b = new SolidBrush(Color.FromArgb(alpha, nc)))
                g.DrawString(it.Name, nameFont, b, new RectangleF(nameX, r.Y, nameW, r.H), fmt);

            Color rc2 = Theme.RiskColor(it.Risk);
            var pill = new RectangleF(pillX, cy - S(9), pillW, S(18));
            using (var path = Theme.Round(pill, S(9)))
            {
                using (var b = new SolidBrush(Color.FromArgb(Enabled ? 40 : 20, rc2))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(alpha * 2 / 3, rc2), 1)) g.DrawPath(p, path);
            }
            using (var b = new SolidBrush(Color.FromArgb(alpha, rc2)))
                g.DrawString(Theme.RiskText(it.Risk), pillFont, b, pill, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

            using (var b = new SolidBrush(Color.FromArgb(alpha, Theme.Desc)))
                g.DrawString(it.Desc, descFont, b, new RectangleF(descX, r.Y, Math.Max(10, w - descX - S(12)), r.H), fmt);
        }
    }
}

// 自繪進度條（青色發光）
class Meter : Control
{
    int val, max = 1;
    public int Value { get { return val; } set { val = value; Invalidate(); } }
    public int Maximum { get { return max; } set { max = Math.Max(1, value); Invalidate(); } }
    public Meter()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float h = Height - 2;
        var track = new RectangleF(1, 1, Width - 3, h);
        using (var p = Theme.Round(track, h / 2))
        {
            using (var b = new SolidBrush(Theme.Panel)) g.FillPath(b, p);
            using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
        }
        float fw = (Width - 3) * Math.Min(val, max) / (float)max;
        if (fw >= h)
        {
            var fill = new RectangleF(1, 1, fw, h);
            using (var p = Theme.Round(fill, h / 2))
            using (var b = new LinearGradientBrush(fill, Theme.Primary, Theme.Accent, 0f))
                g.FillPath(b, p);
        }
    }
}

// 深色下拉選單：自繪項目與下拉箭頭，與 PickerButton 風格一致
class DarkCombo : ComboBox
{
    public DarkCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList; DrawMode = DrawMode.OwnerDrawFixed; FlatStyle = FlatStyle.Flat;
        BackColor = Theme.Btn; ForeColor = Color.White; ItemHeight = 18;
    }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ItemHeight = Font.Height + 4; }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool hot = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var b = new SolidBrush(hot ? Theme.Hover : Theme.Btn)) e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, Convert.ToString(Items[e.Index]), Font, Rectangle.Inflate(e.Bounds, -6, 0), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0xF)   // WM_PAINT：蓋掉系統繪製的白色邊框與下拉按鈕
        {
            try
            {
                using (var g = Graphics.FromHwnd(Handle))
                {
                    var r = ClientRectangle;
                    int aw = Math.Max(20, SystemInformation.VerticalScrollBarWidth + 4);
                    using (var p = new Pen(Theme.Btn, 2)) g.DrawRectangle(p, 1, 1, r.Width - 2, r.Height - 2);
                    using (var b = new SolidBrush(Theme.Btn)) g.FillRectangle(b, r.Right - aw, r.Top, aw - 1, r.Height);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    int cx = r.Right - aw / 2 - 1, cy = r.Height / 2;
                    using (var br = new SolidBrush(Theme.Accent))
                        g.FillPolygon(br, new[] { new Point(cx - 5, cy - 2), new Point(cx + 5, cy - 2), new Point(cx, cy + 4) });
                }
            }
            catch { }
        }
    }
}

// 深色選單按鈕：圖示＋精簡文字（超出欄位淡出隱藏）＋下拉箭頭，完整內容以提示框顯示
class PickerButton : Button
{
    readonly float k;
    readonly ToolTip tip;
    string shortText = "";
    Bitmap ico;
    bool hover;
    int S(int v) { return (int)(v * k); }

    public PickerButton(float scale, Font font, ToolTip tip)
    {
        k = scale; this.tip = tip;
        Font = font; Text = ""; AutoSize = false;
        FlatStyle = FlatStyle.Flat; UseVisualStyleBackColor = false; Cursor = Cursors.Hand;
        BackColor = Theme.Btn; ForeColor = Color.White;
        FlatAppearance.BorderColor = Theme.BtnBorder;
        FlatAppearance.MouseOverBackColor = Theme.BtnHover;
        FlatAppearance.MouseDownBackColor = Theme.Hover;
    }

    public string ShortText { get { return shortText; } }

    public void Set(string text, string full, Bitmap icon)
    {
        shortText = text; ico = icon;
        tip.SetToolTip(this, full);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        int ix = S(8);
        if (ico != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(ico, ix, (Height - ico.Height) / 2, ico.Width, ico.Height);
            ix += ico.Width + S(8);
        }
        var tr = new Rectangle(ix, 0, Math.Max(0, Width - ix - S(30)), Height);
        g.SetClip(tr);
        // 文字超過可用寬度時以「…」省略（完整文字見滑鼠提示）
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(g, shortText, Font, tr, Color.White, flags);
        g.ResetClip();
        int cx = Width - S(16), cy = Height / 2;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var br = new SolidBrush(Theme.Accent))
            g.FillPolygon(br, new[] { new Point(cx - S(5), cy - S(2)), new Point(cx + S(5), cy - S(2)), new Point(cx, cy + S(4)) });
    }
}

// 深色輸入對話框
static class InputBox
{
    public static string Ask(IWin32Window owner, string title, string prompt, string def, float k, Font font)
    {
        Func<int, int> S = v => (int)(v * k);
        using (var f = new Form {
            Text = title, Font = font, BackColor = Theme.Bg, ForeColor = Theme.Text, AutoScaleMode = AutoScaleMode.None,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(S(460), S(200)) })
        {
            f.HandleCreated += (o, e) => Theme.DarkTitle(f.Handle);
            var lbl = new Label { Text = prompt, ForeColor = Theme.Desc, Location = new Point(S(20), S(16)), Size = new Size(S(420), S(48)) };
            var tb = Theme.DarkTextBox("pdf, dwg, z3prt");
            tb.Text = def ?? "";
            var wrap = Theme.Framed(tb, S(10));
            wrap.Dock = DockStyle.None; wrap.Location = new Point(S(20), S(72)); wrap.Size = new Size(S(420), S(38));
            var ok = Theme.MakeButton(I18n.Tr("確定"), true, "confirm", k, font);
            var cancel = Theme.MakeButton(I18n.Tr("取消"), false, "unselect", k, font);
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            f.Controls.AddRange(new Control[] { lbl, wrap, ok, cancel });
            f.Load += (o, e) => {
                cancel.Location = new Point(f.ClientSize.Width - S(20) - cancel.Width, f.ClientSize.Height - S(16) - cancel.Height);
                ok.Margin = new Padding(0);
                ok.Location = new Point(cancel.Left - S(10) - ok.Width, cancel.Top);
                tb.Focus(); tb.SelectAll();
            };
            f.AcceptButton = ok; f.CancelButton = cancel;
            return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
        }
    }
}

// ZW3D 用戶目錄管理器（深色風格，沿用 Theme）
class UserFolderForm : Form
{
    readonly float k;
    readonly Font fnt;
    ComboBox ver;
    TextBox pathBox;
    Label status;
    Button[] acts;
    static readonly string[] OutputDirs = { "output", "autosave" };
    static readonly string[] CustomDirs = { "custom" };
    static readonly string[] TempDirs = { "temp" };

    int S(int v) { return (int)(v * k); }
    static string BaseDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZWSOFT"); }
    public static bool IsZwPublic(string n) { return IsZw(n); }
    static bool IsZw(string n) { return n.StartsWith("ZW", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("ZWSOFT Network License Manager", StringComparison.OrdinalIgnoreCase); }
    string Root { get { return ver.SelectedItem == null ? null : Path.Combine(BaseDir(), (string)ver.SelectedItem); } }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(Handle); }

    public UserFolderForm(float scale, Font font)
    {
        k = scale; fnt = font;
        Text = I18n.Tr("用戶目錄管理器"); Font = font; BackColor = Theme.Bg; ForeColor = Theme.Text; AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        int W = 480, pad = 20, bw = W - pad * 2, y = 18;
        ClientSize = new Size(S(W), S(484));

        Controls.Add(new Label { Text = I18n.Tr("選擇要刪除的版本："), ForeColor = Theme.Desc, AutoSize = true, Location = new Point(S(pad), S(y + 8)) });
        ver = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Btn, ForeColor = Theme.Text,
            Font = font, Location = new Point(S(pad + 170), S(y + 4)), Width = S(bw - 170) };
        Controls.Add(ver);
        y += 46;

        Controls.Add(new Label { Text = I18n.Tr("用戶目錄"), ForeColor = Theme.Desc, AutoSize = true, Location = new Point(S(pad), S(y + 9)) });
        pathBox = Theme.DarkTextBox("");
        pathBox.ReadOnly = true;
        var wrap = Theme.Framed(pathBox, S(10));
        wrap.Dock = DockStyle.None; wrap.Location = new Point(S(pad + 90), S(y)); wrap.Size = new Size(S(bw - 90 - 46), S(38));
        Controls.Add(wrap);
        var open = Theme.MakeButton("", false, "folder", k, font);
        open.AutoSize = false; open.Size = new Size(S(38), S(38)); open.Location = new Point(S(pad + bw - 38), S(y)); open.Padding = new Padding(0); open.ImageAlign = ContentAlignment.MiddleCenter;
        open.Click += (o, e) => { string r = Root; if (r != null && Directory.Exists(r)) try { Process.Start("explorer.exe", "\"" + r + "\""); } catch { } };
        Controls.Add(open);
        y += 54;

        var defs = new[] {
            new { T = I18n.Tr("清除自動輸出文件"), I = "recycle" },
            new { T = I18n.Tr("清除自訂配置"), I = "recycle" },
            new { T = I18n.Tr("清除暫存文件"), I = "recycle" },
            new { T = I18n.Tr("清空用戶目錄"), I = "delete" },
            new { T = I18n.Tr("保存當前自訂配置"), I = "confirm" },
            new { T = I18n.Tr("導入備份配置"), I = "reset" },
            new { T = I18n.Tr("刪除子目錄…"), I = "folder" } };
        acts = new Button[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            var b = Theme.MakeButton(defs[i].T, false, defs[i].I, k, font);
            b.AutoSize = false; b.Size = new Size(S(bw), S(36)); b.Location = new Point(S(pad), S(y)); b.Margin = new Padding(0);
            b.TextAlign = ContentAlignment.MiddleCenter;
            acts[i] = b; Controls.Add(b);
            y += 44;
        }
        acts[0].Click += (o, e) => Clear(I18n.Tr("清除自動輸出文件"), OutputDirs, false);
        acts[1].Click += (o, e) => Clear(I18n.Tr("清除自訂配置"), CustomDirs, false);
        acts[2].Click += (o, e) => Clear(I18n.Tr("清除暫存文件"), TempDirs, false);
        acts[3].Click += (o, e) => Clear(I18n.Tr("清空用戶目錄"), new string[] { "" }, true);
        acts[4].Click += (o, e) => Backup(true);
        acts[5].Click += (o, e) => Backup(false);
        acts[6].Click += (o, e) => {
            string r = Root; bool scan = r == null;
            if (scan) r = BaseDir();
            using (var sf = new UfSubForm(k, fnt, r, scan))
                if (sf.ShowDialog(this) == DialogResult.OK && sf.Picked.Count > 0) Clear(I18n.Tr("刪除子目錄…"), sf.Picked.ToArray(), true, r);
        };

        Controls.Add(new Label { Text = I18n.Tr("注意："), ForeColor = Theme.Sub, AutoSize = true, Location = new Point(S(pad), S(y + 6)) });
        Controls.Add(new Label { Text = I18n.Tr("清除操作將退出中望3D。"), ForeColor = Theme.Caution, AutoSize = true, Location = new Point(S(pad + 90), S(y + 6)) });

        status = new Label { Dock = DockStyle.Bottom, Height = S(28), BackColor = Theme.Panel, ForeColor = Theme.Sub, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(S(10), 0, 0, 0) };
        Controls.Add(status);

        string b0 = BaseDir();
        if (Directory.Exists(b0))
        {
            // 掃描 ZWSOFT 直接底下 ZW 開頭的資料夾；排除 ZWSOFT Network License Manager
            // 若該 ZW 資料夾底下還有 ZW 開頭的子資料夾（例如 ZW3D\ZW3D 2025），改列出這些子資料夾；否則列出它本身（例如 ZWCAD）
            var found = new List<string>();
            foreach (var d in Directory.GetDirectories(b0).Select(s => Path.GetFileName(s)).Where(s => IsZw(s)).OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                string[] subs = new string[0];
                try { subs = Directory.GetDirectories(Path.Combine(b0, d)).Select(s => Path.GetFileName(s)).Where(s => IsZw(s)).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray(); } catch { }
                if (subs.Length == 0) found.Add(d);
                else foreach (var s in subs) found.Add(d + "\\" + s);
            }
            foreach (var f in found) ver.Items.Add(f);
        }
        // 找不到對應資料夾的功能反灰，「刪除子目錄」永遠可用
        Action refresh = () => {
            string r = Root;
            Func<string[], bool> has = subs => { if (r == null) return false; foreach (var s in subs) if (Directory.Exists(Path.Combine(r, s))) return true; return false; };
            pathBox.Text = r ?? b0;
            bool rootOk = r != null && Directory.Exists(r);
            open.Enabled = rootOk;
            acts[0].Enabled = has(OutputDirs);
            acts[1].Enabled = has(CustomDirs);
            acts[2].Enabled = has(TempDirs);
            acts[3].Enabled = rootOk;
            acts[4].Enabled = has(CustomDirs);
            acts[5].Enabled = rootOk;
            acts[6].Enabled = true;
        };
        ver.SelectedIndexChanged += (o, e) => refresh();
        if (ver.Items.Count > 0) { ver.SelectedIndex = 0; refresh(); Say(I18n.Tr("初始化成功")); }
        else { ver.Enabled = false; refresh(); Say(I18n.Tr("未偵測到 ZW3D 用戶目錄")); }
    }

    void Say(string s) { status.Text = s; }

    public static void CountPublic(string dir, ref int files, ref int dirs, ref long size) { Count(dir, ref files, ref dirs, ref size); }
    static void Count(string dir, ref int files, ref int dirs, ref long size)
    {
        var l = new List<Native.Entry>();
        Native.List(dir, l);
        foreach (var e in l)
        {
            if ((e.Attr & Native.DIR) != 0 && (e.Attr & Native.REPARSE) == 0) { dirs++; Count(dir.TrimEnd(new[] { '\\' }) + "\\" + e.Name, ref files, ref dirs, ref size); }
            else if ((e.Attr & Native.DIR) != 0) dirs++;
            else { files++; size += e.Size; }
        }
    }

    // 刪除資料夾內容（保留資料夾本身）
    static void Empty(string dir)
    {
        var l = new List<Native.Entry>();
        Native.List(dir, l);
        foreach (var e in l)
        {
            string full = dir.TrimEnd(new[] { '\\' }) + "\\" + e.Name;
            if ((e.Attr & Native.DIR) != 0) { if ((e.Attr & Native.REPARSE) == 0) Native.DeleteTree(full); else Native.RemoveDir(full); }
            else Native.DeleteFile(full, e.Attr);
        }
    }

    void Clear(string title, string[] subs, bool all, string rootOverride = null)
    {
        try
        {
            string r = rootOverride ?? Root; if (r == null) return;
            var targets = new List<string>();
            foreach (var s in subs) { string p = s.Length == 0 ? r : Path.Combine(r, s); if (Directory.Exists(p)) targets.Add(p); }
            if (targets.Count == 0) { Say(I18n.Tr("（目錄不存在，無需清理）")); return; }
            int f = 0, d = 0; long sz = 0;
            foreach (var t in targets) Count(t, ref f, ref d, ref sz);
            string msg = title + "\n\n" + string.Join("\n", targets.ToArray()) + "\n\n" + string.Format(I18n.Tr("共 {0} 個檔案 · {1} 個資料夾 · {2}"), f, d, Cleaner.Human(sz)) + "\n\n" + (all ? I18n.Tr("刪除後，回復原廠設定") + "\n\n" : "") + I18n.Tr("清除操作將退出中望3D。");
            if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, all ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            foreach (var p in Process.GetProcessesByName("ZW3D")) { try { p.Kill(); p.WaitForExit(5000); } catch { } }
            foreach (var t in targets) Empty(t);
            Say(title + "：" + I18n.Tr("操作完成"));
        }
        catch (Exception ex) { Say(I18n.Tr("操作失敗：") + ex.Message); }
    }

    static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    // save=true：把自訂配置備份到所選資料夾；false：由所選資料夾匯入
    void Backup(bool save)
    {
        try
        {
            string r = Root; if (r == null) return;
            string custom = Path.Combine(r, CustomDirs[0]);
            using (var dlg = new FolderBrowserDialog { Description = I18n.Tr("選擇備份資料夾") })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (save)
                {
                    if (!Directory.Exists(custom)) { Say(I18n.Tr("（目錄不存在，無需清理）")); return; }
                    CopyTree(custom, dlg.SelectedPath);
                }
                else
                {
                    if (MessageBox.Show(this, I18n.Tr("清除操作將退出中望3D。"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    foreach (var p in Process.GetProcessesByName("ZW3D")) { try { p.Kill(); p.WaitForExit(5000); } catch { } }
                    CopyTree(dlg.SelectedPath, custom);
                }
            }
            Say(I18n.Tr("操作完成"));
        }
        catch (Exception ex) { Say(I18n.Tr("操作失敗：") + ex.Message); }
    }
}

// 子目錄選擇對話框（深色風格）：勾選要清空的子目錄
class UfSubForm : Form
{
    readonly float k;
    CheckedListBox clb;
    public List<string> Picked = new List<string>();
    int S(int v) { return (int)(v * k); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(Handle); }

    public UfSubForm(float scale, Font font, string root, bool zwScan)
    {
        k = scale;
        Text = I18n.Tr("刪除子目錄…").TrimEnd(new[] { '…' }); Font = font; BackColor = Theme.Bg; ForeColor = Theme.Text; AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(S(480), S(420));
        Controls.Add(new Label { Text = I18n.Tr("選擇要刪除的子目錄："), ForeColor = Theme.Desc, AutoSize = true, Location = new Point(S(20), S(16)) });
        clb = new CheckedListBox { BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text, CheckOnClick = true, IntegralHeight = false, Font = font };
        var wrap = Theme.Framed(clb, 0);
        wrap.Dock = DockStyle.None; wrap.Location = new Point(S(20), S(46)); wrap.Size = new Size(S(440), S(280));
        Controls.Add(wrap);
        var names = new List<string>();
        try
        {
            foreach (var d in Directory.GetDirectories(root))
            {
                string dn = Path.GetFileName(d);
                if (!zwScan) { names.Add(dn); continue; }
                // 未偵測到版本時：列出 ZW 開頭資料夾底下的子資料夾
                if (!UserFolderForm.IsZwPublic(dn)) continue;
                foreach (var sd in Directory.GetDirectories(d)) names.Add(dn + "\\" + Path.GetFileName(sd));
            }
        }
        catch { }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
        {
            int f = 0, dd = 0; long sz = 0;
            UserFolderForm.CountPublic(Path.Combine(root, n), ref f, ref dd, ref sz);
            clb.Items.Add(n + "    (" + f + " / " + Cleaner.Human(sz) + ")");
        }
        if (names.Count == 0) clb.Items.Add(I18n.Tr("（無子目錄）"));
        var all = Theme.MakeButton(I18n.Tr("全選"), false, "selectall", k, font);
        var none = Theme.MakeButton(I18n.Tr("全不選"), false, "unselect", k, font);
        var ok = Theme.MakeButton(I18n.Tr("刪除所選"), true, "delete", k, font);
        var cancel = Theme.MakeButton(I18n.Tr("取消"), false, "unselect", k, font);
        ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
        Controls.AddRange(new Control[] { all, none, ok, cancel });
        all.Click += (o, e) => { if (names.Count > 0) for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, true); };
        none.Click += (o, e) => { for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, false); };
        Load += (o, e) => {
            int by = ClientSize.Height - S(16) - ok.Height;
            all.Margin = new Padding(0); none.Margin = new Padding(0); ok.Margin = new Padding(0); cancel.Margin = new Padding(0);
            all.Location = new Point(S(20), by); none.Location = new Point(all.Right + S(8), by);
            cancel.Location = new Point(ClientSize.Width - S(20) - cancel.Width, by);
            ok.Location = new Point(cancel.Left - S(8) - ok.Width, by);
        };
        FormClosing += (o, e) => {
            if (DialogResult != DialogResult.OK) return;
            Picked.Clear();
            foreach (int i in clb.CheckedIndices) if (i < names.Count) Picked.Add(names[i]);
            if (Picked.Count == 0) e.Cancel = true;
        };
        CancelButton = cancel;
    }
}

class MainForm : Form
{
    readonly List<CleanItem> items = Cleaner.Build();
    readonly bool admin;
    readonly float k;
    ItemList list;
    RichTextBox log;
    Meter bar;
    Label status;
    Button btnPreview, btnRun, btnStop, btnAll, btnNone, btnDefault, btnSage, btnAdmin, btnList, btnSearch;
    TextBox searchBox;
    string customFolder;
    List<Found> lastPreview;
    Ctx ctx;

    int S(int v) { return (int)(v * k); }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(Handle); }

    public MainForm()
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
        admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        Text = I18n.Tr("WIN/ZW 資料搜尋/清理工具");
        Font = new Font(I18n.FontName, 10f);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(S(900), S(660));
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimumSize = new Size(S(700), S(500));
        StartPosition = FormStartPosition.CenterScreen;
        WindowState_Load();
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(S(12), S(8), S(12), S(8)) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 66));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        // 分頁：[快速搜尋] 與 [清理] 兩頁切換，右側為 [用戶目錄管理器]
        var searchPage = BuildSearchTab();
        var cleanPage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Visible = false };
        cleanPage.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cleanPage.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cleanPage.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var tb = BuildToolbar(); tb.Margin = new Padding(0, 0, 0, S(6));
        cleanPage.Controls.Add(tb, 0, 0);
        cleanPage.Controls.Add(Framed(BuildList()), 0, 1);
        var host = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        host.Controls.Add(cleanPage);
        host.Controls.Add(searchPage);
        root.Controls.Add(BuildTabStrip(searchPage, cleanPage), 0, 1);
        root.Controls.Add(host, 0, 2);
        root.Controls.Add(new Label { Text = I18n.Tr("執行紀錄　LOG"), AutoSize = true, ForeColor = Theme.Sub, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold), Margin = new Padding(S(2), S(6), 0, S(3)) }, 0, 3);
        root.Controls.Add(Framed(BuildLog()), 0, 4);
        var bottomRow = BuildBottom();
        root.Controls.Add(bottomRow, 0, 5);

        // 依目前語系實際量測不會自動換行的列（標題、搜尋列、底部按鈕），算出最小視窗寬度，避免 15 語系跑版
        Load += (o, e) => {
            try
            {
                int need = 0;
                foreach (Control c in root.Controls)
                {
                    if (c is FlowLayoutPanel && ((FlowLayoutPanel)c).WrapContents) continue;   // 工具列可換行，不限制
                    if (c is Panel && !(c is TableLayoutPanel)) continue;                         // 列表／紀錄會自適應延伸
                    if (!(c is TableLayoutPanel)) continue;
                    if ("blocks".Equals(c.Tag)) { need = Math.Max(need, searchMinW + S(12)); continue; }   // 搜尋列寬度不足時自動換成兩列
                    need = Math.Max(need, c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal);
                }
                need += root.Padding.Horizontal + S(24);
                int frameW = Width - ClientSize.Width, frameH = Height - ClientSize.Height;
                var wa = Screen.FromControl(this).WorkingArea;
                int minW = Math.Min(wa.Width, Math.Max(S(700), need + frameW));
                int minH = Math.Min(wa.Height, Math.Max(S(500), S(440) + frameH));
                MinimumSize = new Size(minW, minH);
                if (Width < minW || Height < minH) Size = new Size(Math.Max(Width, minW), Math.Max(Height, minH));
                if (savedR.Width > 0 && WindowState == FormWindowState.Normal && (Width < savedR.Width || Height < savedR.Height)) Size = new Size(Math.Max(Width, savedR.Width), Math.Max(Height, savedR.Height));   // 切換語系重啟後，還原上次視窗大小，不自動縮小
                WindowState_Clamp();
            }
            catch { }
        };
        FormClosed += (o, e) => WindowState_Save();

        Shown += (o, e) => Log(I18n.Tr("請點選要清理的項目。建議先按「預覽」查看可釋放的空間。"), Theme.Sub);
        FormClosing += (o, e) => {
            if (ctx != null) {
                if (MessageBox.Show(this, I18n.Tr("清理仍在進行中，確定要停止並關閉嗎？"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                    e.Cancel = true;
                else ctx.Cancel = true;
            }
        };
    }

    // ===== 視窗大小與位置記憶：%AppData%\SystemJunkCleaner\window.txt =====
    static string WindowFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemJunkCleaner", "window.txt"); } }
    bool restoreMax; Rectangle savedR;

    void WindowState_Load()
    {
        try
        {
            if (!File.Exists(WindowFile)) return;
            var v = File.ReadAllText(WindowFile).Trim().Split(new[] { ',' });
            if (v.Length < 4) return;
            int x = int.Parse(v[0]), y = int.Parse(v[1]), w = int.Parse(v[2]), h = int.Parse(v[3]);
            if (w < 200 || h < 150) return;
            var r = new Rectangle(x, y, w, h);
            // 必須與某個螢幕的工作區有足夠交集，否則（例如外接螢幕已拔除）改回置中
            bool visible = false;
            foreach (var sc in Screen.AllScreens)
            {
                var i = Rectangle.Intersect(sc.WorkingArea, r);
                if (i.Width >= 100 && i.Height >= 50) { visible = true; break; }
            }
            if (!visible) { Size = new Size(w, h); return; }
            StartPosition = FormStartPosition.Manual;
            Bounds = r; savedR = r;
            restoreMax = v.Length >= 5 && v[4].Trim() == "1";
            WindowState_Clamp();
            if (restoreMax) Shown += (o, e) => { try { WindowState = FormWindowState.Maximized; } catch { } };
        }
        catch { }
    }

    // 將視窗大小縮到所在螢幕工作區內，並把位置推回螢幕邊界內
    void WindowState_Clamp()
    {
        try
        {
            if (WindowState != FormWindowState.Normal) return;
            var wa = Screen.FromRectangle(Bounds).WorkingArea;
            int w = Math.Min(Width, wa.Width), h = Math.Min(Height, wa.Height);
            int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - w));
            int y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - h));
            if (x != Left || y != Top || w != Width || h != Height) Bounds = new Rectangle(x, y, w, h);
        }
        catch { }
    }

    void WindowState_Save()
    {
        try
        {
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (b.Width < 200 || b.Height < 150) return;
            Directory.CreateDirectory(Path.GetDirectoryName(WindowFile));
            File.WriteAllText(WindowFile, string.Format("{0},{1},{2},{3},{4}", b.X, b.Y, b.Width, b.Height, WindowState == FormWindowState.Maximized ? 1 : 0));
        }
        catch { }
    }


    // 1px 邊框外框
    Control Framed(Control inner)
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Padding = new Padding(1), Margin = new Padding(0) };
        inner.Dock = DockStyle.Fill;
        p.Controls.Add(inner);
        return p;
    }

    Control BuildHeader()
    {
        var p = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 3, Margin = new Padding(0, 0, 0, S(6)) };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var logo = new PictureBox { Image = Theme.Ico("app", S(40)), Size = new Size(S(40), S(40)), Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, S(8), 0) };
        p.Controls.Add(logo, 0, 0);
        p.SetRowSpan(logo, 3);
        p.Controls.Add(new Label { Text = I18n.Tr("WIN/ZW 資料搜尋/清理工具"), AutoSize = true, Font = new Font(Font.FontFamily, 12f, FontStyle.Bold), ForeColor = Theme.Text, Margin = new Padding(0) }, 1, 0);
        p.Controls.Add(new Label { Text = "WIN/ZW DATA SEARCH/CLEANER · METAL UI · v2.11", AutoSize = true, Font = new Font("Segoe UI", 7.5f), ForeColor = Theme.Sub, Margin = new Padding(S(2), 0, 0, S(2)) }, 1, 1);
        p.Controls.Add(new Label { AutoSize = true, Margin = new Padding(S(2), 0, 0, 0),
            Font = new Font(Font.FontFamily, 9f),
            Text = admin ? I18n.Tr("● 系統管理員權限") : I18n.Tr("● 一般權限：系統資料夾、帳號等項目會無法清除"),
            ForeColor = admin ? Theme.Safe : Theme.Caution }, 1, 2);
        var right = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0), BackColor = Color.Transparent };
        // 介面語言選單：選項固定顯示「語言碼 + 該語言自稱」，不隨介面翻譯
        var btnLang = MakeButton(I18n.Display(I18n.Code), false, "radio");
        btnLang.Margin = new Padding(0, 0, 0, S(3));
        var langMenu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()), BackColor = Theme.Panel, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f) };
        for (int i = 0; i < I18n.Codes.Length; i++)
        {
            string code = I18n.Codes[i];
            var mi = new ToolStripMenuItem(I18n.Display(code)) { Checked = code == I18n.Code, ForeColor = Color.White };
            mi.Click += (o, e) => SwitchLanguage(code);
            langMenu.Items.Add(mi);
        }
        btnLang.Click += (o, e) => langMenu.Show(btnLang, new Point(0, btnLang.Height));
        right.Controls.Add(btnLang);
        if (!admin)
        {
            btnAdmin = MakeButton(I18n.Tr("以系統管理員重新啟動"), false, "shield");
            btnAdmin.Margin = new Padding(0);
            btnAdmin.Click += (o, e) => RelaunchAdmin();
            right.Controls.Add(btnAdmin);
        }
        p.Controls.Add(right, 2, 0);
        p.SetRowSpan(right, 3);
        return p;
    }

    // 快速搜尋列：標籤、輸入框、類型、位置、搜尋按鈕統一高度，圖示統一 26px（依 DPI 縮放）
    const int RowH = 28, IconPx = 18;
    SearchOptions searchOpt = new SearchOptions();

    // 檔案類型預設（名稱、副檔名）
    static readonly string[][] TypePresets = {
        new[] { I18n.Tr("文件"), "doc docx pdf txt rtf odt md wps" },
        new[] { I18n.Tr("試算表"), "xls xlsx xlsm csv ods" },
        new[] { I18n.Tr("簡報"), "ppt pptx odp key" },
        new[] { I18n.Tr("圖片"), "jpg jpeg png gif bmp webp heic tif tiff svg ico raw" },
        new[] { I18n.Tr("影片"), "mp4 mov avi mkv wmv flv webm m4v" },
        new[] { I18n.Tr("音樂"), "mp3 wav flac aac m4a ogg wma" },
        new[] { I18n.Tr("壓縮檔"), "zip rar 7z tar gz bz2 xz iso cab" },
        new[] { I18n.Tr("程式與安裝檔"), "exe msi bat cmd ps1 com" },
        new[] { I18n.Tr("CAD / 3D 圖檔"), "dwg dxf dwf z3 z3prt z3asm z3drw step stp igs iges stl sat x_t x_b 3dm sldprt sldasm slddrw ipt iam catpart catproduct" },
        new[] { I18n.Tr("暫存與記錄檔"), "tmp log bak old chk" },
    };

    static string ExtList(string[] exts, int max)
    {
        var shown = exts.Take(max).Select(x => x.TrimStart(new[] { '.' }));
        return string.Join(", ", shown) + (exts.Length > max ? "…" : "");
    }

    // ===== [快速搜尋] 分頁：搜尋列 + 下方結果清單 =====
    ListView qsList;
    readonly ConcurrentQueue<Found> qsInbox = new ConcurrentQueue<Found>();
    readonly System.Windows.Forms.Timer qsTimer = new System.Windows.Forms.Timer { Interval = 250 };
    readonly Stopwatch qsSw = new Stopwatch();
    Ctx qsCtx;
    int qsTotal;
    readonly List<Found> qsItems = new List<Found>();      // 清單內容（虛擬模式）
    readonly HashSet<Found> qsChecked = new HashSet<Found>();   // 已勾選的項目
    long qsLogMs;
    Meter qsBar;
    Button qsAll, qsNone, qsRecycle, qsNuke;
    bool qsSelAll;   // 按過 [全選] 後為 true：右鍵只留下刪除功能
    long qsBytes;

    Control BuildSearchTab()
    {
        var sp = BuildSearch(); sp.Dock = DockStyle.Top;
        var tab = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
        tab.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tab.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tab.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tab.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tab.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tab.Controls.Add(sp, 0, 0);
        var fr = Theme.Framed(BuildQsList(), 0);
        fr.Margin = new Padding(0, S(8), 0, 0);
        tab.Controls.Add(fr, 0, 1);
        // 進度條在功能欄上方
        qsBar = new Meter { Dock = DockStyle.Fill, Height = S(10), MinimumSize = new Size(S(120), S(10)), Margin = new Padding(0, S(8), 0, S(8)) };
        tab.Controls.Add(qsBar, 0, 2);
        tab.Controls.Add(BuildQsBar(), 0, 3);
        qsTimer.Tick += (o, e) => QsDrain();
        return tab;
    }

    Control BuildQsList()
    {
        // 虛擬模式（同結果視窗）：項目數再多也不會卡住；勾選框由自繪處理，勾選狀態存在 qsChecked
        qsList = new ListView {
            View = View.Details, VirtualMode = true, OwnerDraw = true, FullRowSelect = true, MultiSelect = true,
            HideSelection = false, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        qsList.SmallImageList = new ImageList { ImageSize = new Size(1, S(22)) };
        qsList.Columns.Add(I18n.Tr("名稱"), S(240));
        qsList.Columns.Add(I18n.Tr("位置"), S(360));
        qsList.Columns.Add(I18n.Tr("大小"), S(90), HorizontalAlignment.Right);
        Theme.DarkScrollbars(qsList);
        qsList.RetrieveVirtualItem += (o, e) => {
            if (e.ItemIndex < 0 || e.ItemIndex >= qsItems.Count) { e.Item = new ListViewItem(""); e.Item.SubItems.Add(""); e.Item.SubItems.Add(""); return; }
            var f = qsItems[e.ItemIndex];
            var it = new ListViewItem(f.Name);
            it.SubItems.Add(f.Dir);
            it.SubItems.Add(f.IsDir ? I18n.Tr("資料夾") : Cleaner.Human(f.Size));
            e.Item = it;
        };
        qsList.DrawColumnHeader += (o, e) => {
            using (var b = new SolidBrush(Theme.Btn)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var fl = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | (e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, Rectangle.Inflate(e.Bounds, -S(8), 0), Theme.Sub, fl);
        };
        qsList.DrawItem += (o, e) => { };
        qsList.DrawSubItem += (o, e) => {
            if (e.ItemIndex < 0 || e.ItemIndex >= qsItems.Count) return;
            var f = qsItems[e.ItemIndex];
            bool sel = qsList.SelectedIndices.Contains(e.ItemIndex);
            Color bg = sel ? Color.FromArgb(22, 74, 96) : (e.ItemIndex % 2 == 0 ? Theme.Panel : Color.FromArgb(17, 33, 46));
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
            var fl = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            var r = Rectangle.Inflate(e.Bounds, -S(8), 0);
            if (e.ColumnIndex == 0)
            {
                // 勾選框（深色風格）
                bool on = qsChecked.Contains(f);
                int cb = S(14);
                var box = new Rectangle(e.Bounds.Left + S(8), e.Bounds.Top + (e.Bounds.Height - cb) / 2, cb, cb);
                using (var pen = new Pen(on ? Theme.Accent : Theme.Sub)) e.Graphics.DrawRectangle(pen, box);
                if (on)
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var pen = new Pen(Theme.Accent, Math.Max(2, S(2))))
                        e.Graphics.DrawLines(pen, new[] { new Point(box.Left + S(3), box.Top + cb / 2), new Point(box.Left + cb / 2 - 1, box.Bottom - S(4)), new Point(box.Right - S(3), box.Top + S(3)) });
                }
                r = new Rectangle(box.Right + S(8), e.Bounds.Top, Math.Max(1, e.Bounds.Right - box.Right - S(12)), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, f.Name, Font, r, f.IsDir ? Theme.Accent : Theme.Text, fl);
            }
            else if (e.ColumnIndex == 1) TextRenderer.DrawText(e.Graphics, f.Dir, Font, r, Theme.Desc, fl | TextFormatFlags.PathEllipsis);
            else TextRenderer.DrawText(e.Graphics, f.IsDir ? I18n.Tr("資料夾") : Cleaner.Human(f.Size), Font, r, f.IsDir ? Theme.Sub : Theme.Text, fl | TextFormatFlags.Right);
        };
        qsList.SelectedIndexChanged += (o, e) => qsList.Invalidate();
        qsList.VirtualItemsSelectionRangeChanged += (o, e) => qsList.Invalidate();
        // 右鍵：開啟檔案位置／開啟資料夾／開啟檔案
        var m = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()), BackColor = Theme.Panel, ForeColor = Color.White, ShowImageMargin = true };
        m.ImageScalingSize = new Size(S(20), S(20));
        Action<string, string, Action<IWin32Window, Found>, int> addM = (t, ic, a, max) => m.Items.Add(new ToolStripMenuItem(t, Theme.Ico(ic, S(20)), (o2, e2) => QsAct(a, max)) { ForeColor = Color.White });
        addM(I18n.Tr("開啟檔案位置（選取該檔案）"), "scan", ShellOpen.Reveal, 5);
        addM(I18n.Tr("開啟資料夾"), "folder", ShellOpen.OpenFolder, 5);
        addM(I18n.Tr("開啟檔案"), "file", ShellOpen.OpenFile, 1);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem(I18n.Tr("丟入資源回收"), Theme.Ico("recycle", S(20)), (o2, e2) => QsDelete(false)) { ForeColor = Color.White });
        m.Items.Add(new ToolStripMenuItem(I18n.Tr("永久刪除"), Theme.Ico("delete", S(20)), (o2, e2) => QsDelete(true)) { ForeColor = Color.White });
        m.Opening += (o, e) => {
            bool hasSel = qsList.SelectedIndices.Count > 0;
            if (!hasSel && qsChecked.Count == 0) { e.Cancel = true; return; }
            // 使用 [全選] 後，開啟類功能灰選，只留下 [丟入資源回收]、[永久刪除]
            // 勾選兩個以上的項目，規則同 [全選]
            bool multi = qsSelAll || qsChecked.Count >= 2;
            for (int i = 0; i < 3; i++) m.Items[i].Enabled = hasSel && !multi;
        };
        qsList.ContextMenuStrip = m;
        qsList.MouseDown += (o, e) => {
            var hit = qsList.HitTest(e.Location);
            if (hit.Item == null) return;
            int idx = hit.Item.Index;
            if (idx < 0 || idx >= qsItems.Count) return;
            if (e.Button == MouseButtons.Right)
            {
                if (!qsList.SelectedIndices.Contains(idx)) { qsList.SelectedIndices.Clear(); qsList.SelectedIndices.Add(idx); }
            }
            else if (e.Button == MouseButtons.Left && e.X < S(30))
            {
                var f = qsItems[idx];
                if (!qsChecked.Remove(f)) qsChecked.Add(f);
                qsSelAll = false;
                qsList.RedrawItems(idx, idx, false);
                QsUpdateButtons();
            }
        };
        // 空白鍵：切換反白列的勾選
        qsList.KeyDown += (o, e) => {
            if (e.KeyCode != Keys.Space) return;
            foreach (int i in qsList.SelectedIndices) if (i < qsItems.Count) { var f = qsItems[i]; if (!qsChecked.Remove(f)) qsChecked.Add(f); }
            qsSelAll = false;
            qsList.Invalidate(); QsUpdateButtons(); e.Handled = true;
        };
        qsList.DoubleClick += (o, e) => QsAct(ShellOpen.Reveal, 5);
        qsList.Resize += (o, e) => {
            if (qsList.Columns.Count < 3) return;
            int w = qsList.ClientSize.Width - S(2) - qsList.Columns[2].Width;
            int nameW = Math.Max(S(160), w * 40 / 100);
            qsList.Columns[0].Width = nameW;
            qsList.Columns[1].Width = Math.Max(S(160), w - nameW);
        };
        return qsList;
    }

    // 功能欄：全選／全不選／丟入資源回收／永久刪除，四顆等寬、格式同 [清理] 分頁的按鈕列
    Control BuildQsBar()
    {
        qsAll = MakeButton(I18n.Tr("全選"), false, "selectall");
        qsNone = MakeButton(I18n.Tr("全不選"), false, "unselect");
        qsRecycle = MakeButton(I18n.Tr("丟入資源回收"), false, "recycle");
        qsNuke = MakeButton(I18n.Tr("永久刪除"), false, "delete");
        foreach (var d in new[] { qsRecycle, qsNuke })
        {
            d.ForeColor = Theme.Danger;
            d.FlatAppearance.BorderColor = Color.FromArgb(120, Theme.Danger);
            var dd = d; d.EnabledChanged += (o, e) => dd.ForeColor = dd.Enabled ? Theme.Danger : Theme.Sub;
        }
        qsAll.Click += (o, e) => QsCheckAll(true);
        qsNone.Click += (o, e) => QsCheckAll(false);
        qsRecycle.Click += (o, e) => QsDelete(false);
        qsNuke.Click += (o, e) => QsDelete(true);
        new ToolTip().SetToolTip(qsRecycle, I18n.Tr("移到資源回收筒（按住 Shift 點擊為永久刪除）"));
        new ToolTip().SetToolTip(qsNuke, I18n.Tr("永久刪除（不經資源回收筒，無法還原）｜Shift+Delete"));
        var btns = new Button[] { qsAll, qsNone, qsRecycle, qsNuke };
        var f = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 7, RowCount = 1, Margin = new Padding(0), BackColor = Theme.Bg };
        f.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        int ph = btns.Max(b => b.GetPreferredSize(Size.Empty).Height);
        for (int i = 0; i < btns.Length; i++)
        {
            f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            if (i < btns.Length - 1) f.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(6)));
            var b = btns[i];
            b.AutoSize = false; b.AutoEllipsis = false; b.ImageAlign = ContentAlignment.MiddleCenter; b.TextAlign = ContentAlignment.MiddleCenter; b.TextImageRelation = TextImageRelation.ImageBeforeText; b.Padding = new Padding(S(2), 0, S(2), 0); b.Dock = DockStyle.Fill; b.Margin = new Padding(0); b.MinimumSize = new Size(0, ph); b.Height = ph;
            f.Controls.Add(b, i * 2, 0);
        }
        // 文字太長放不下時，按鈕自動變成兩行高（同 [清理] 分頁）
        Action fit = () => {
            int W = f.ClientSize.Width; if (W <= 0) return;
            int colW = (W - S(6) * (btns.Length - 1)) / btns.Length;
            bool wrap = btns.Any(x => TextRenderer.MeasureText(x.Text, x.Font).Width + S(18) + S(14) > colW);
            int hh = wrap ? ph + Font.Height : ph;
            foreach (var x in btns) { x.MinimumSize = new Size(0, hh); x.MaximumSize = new Size(0, hh); }
        };
        f.SizeChanged += (o, e) => fit();
        fit();
        QsUpdateButtons();
        return f;
    }

    void QsUpdateButtons()
    {
        if (qsAll == null || qsList == null) return;
        bool any = qsChecked.Count > 0;
        qsRecycle.Enabled = qsNuke.Enabled = any;
        qsNone.Enabled = any;
        qsAll.Enabled = qsItems.Count > 0;
    }

    void QsCheckAll(bool on)
    {
        qsSelAll = on && qsItems.Count > 0;
        if (on) qsChecked.UnionWith(qsItems); else qsChecked.Clear();
        qsList.Invalidate();
        QsUpdateButtons();
    }

    void QsAct(Action<IWin32Window, Found> a, int max)
    {
        var sel = new List<Found>();
        foreach (int i in qsList.SelectedIndices) if (i < qsItems.Count) sel.Add(qsItems[i]);
        if (sel.Count == 0) return;
        if (sel.Count > max)
        {
            if (MessageBox.Show(this, I18n.Tr("已選取 ") + sel.Count + I18n.Tr(" 個項目，只會開啟前 ") + max + I18n.Tr(" 個。要繼續嗎？"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            sel = sel.Take(max).ToList();
        }
        foreach (var f in sel) a(this, f);
    }

    // 刪除清單中已勾選的項目
    void QsDelete(bool permanent)
    {
        permanent = permanent || (Control.ModifierKeys & Keys.Shift) != 0;
        // 有勾選就刪勾選的項目；沒勾選（右鍵）就刪反白選取的列
        var sel = qsChecked.Count > 0 ? qsItems.Where(qsChecked.Contains).ToList() : qsList.SelectedIndices.Cast<int>().Where(i => i < qsItems.Count).Select(i => qsItems[i]).ToList();
        if (sel.Count == 0) return;
        long size = sel.Where(f => !f.IsDir).Sum(f => f.Size);
        int dirs = sel.Count(f => f.IsDir);
        string names = string.Join("\n", sel.Take(8).Select(f => "• " + f.Name)) + (sel.Count > 8 ? I18n.Tr("\n…等共 ") + sel.Count.ToString("N0") + I18n.Tr(" 個項目") : "");
        string what = sel.Count.ToString("N0") + I18n.Tr(" 個項目") + (dirs > 0 ? I18n.Tr("（含 ") + dirs + I18n.Tr(" 個資料夾及其內容）") : "") + I18n.Tr("，檔案合計 ") + Cleaner.Human(size);
        string msg = permanent
            ? I18n.Tr("確定要「永久刪除」下列 ") + what + I18n.Tr(" 嗎？\n永久刪除後無法從資源回收筒還原！\n\n") + names
            : I18n.Tr("確定要將下列 ") + what + I18n.Tr(" 移到資源回收筒嗎？\n之後仍可從資源回收筒還原。\n\n") + names;
        if (MessageBox.Show(this, msg, permanent ? I18n.Tr("永久刪除") : I18n.Tr("刪除檔案"), MessageBoxButtons.YesNo,
                permanent ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        var sys = sel.Where(f => ShellOpen.IsSystemPath(f.Path)).ToList();
        if (sys.Count > 0 && MessageBox.Show(this,
                I18n.Tr("其中 ") + sys.Count + I18n.Tr(" 個項目位於 Windows 或程式安裝資料夾，例如：\n") + sys[0].Path +
                I18n.Tr("\n\n刪除這些檔案可能導致程式或系統無法正常運作。仍要刪除嗎？"), I18n.Tr("系統檔案警告"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        Cursor = Cursors.WaitCursor;
        List<Found> done; int tooLong = 0;
        try { done = ShellOpen.Delete(this, sel, permanent, out tooLong); }
        finally { Cursor = Cursors.Default; }
        var gone = new HashSet<Found>(done);
        qsItems.RemoveAll(gone.Contains);
        qsChecked.ExceptWith(gone);
        qsSelAll = false;
        qsList.SelectedIndices.Clear();
        qsList.VirtualListSize = qsItems.Count;
        qsList.Invalidate();
        qsTotal = qsItems.Count;
        QsUpdateButtons();
        long freed = done.Where(f => !f.IsDir).Sum(f => f.Size);
        int failed = sel.Count - done.Count;
        string result = (permanent ? I18n.Tr("已永久刪除 ") : I18n.Tr("已移到資源回收筒 ")) + done.Count.ToString("N0") + I18n.Tr(" 個項目（") + Cleaner.Human(freed) + I18n.Tr("）")
            + (failed - tooLong > 0 ? I18n.Tr("；") + (failed - tooLong) + I18n.Tr(" 個無法刪除（使用中或權限不足）") : "")
            + (tooLong > 0 ? I18n.Tr("；") + tooLong + I18n.Tr(" 個路徑過長，無法移到資源回收筒（可改用 Shift+Delete 永久刪除）") : "");
        status.Text = result;
        Log(result, Theme.Sub);
        if (failed > 0) MessageBox.Show(this, result + I18n.Tr("\n\n可關閉正在使用這些檔案的程式，或以系統管理員身分執行後再試。"), I18n.Tr("刪除檔案"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void QsDrain()
    {
        Found f;
        int added = 0;
        while (qsInbox.TryDequeue(out f))
        {
            qsTotal++;
            if (!f.IsDir) qsBytes += f.Size;
            qsItems.Add(f); added++;
        }
        if (added > 0) { qsList.VirtualListSize = qsItems.Count; QsUpdateButtons(); }
        if (qsCtx != null) { qsBar.Maximum = (int)Math.Min(int.MaxValue, Math.Max(1, Interlocked.Read(ref qsCtx.DirsFound))); qsBar.Value = (int)Math.Min(int.MaxValue, Interlocked.Read(ref qsCtx.DirsDone)); }
        if (qsCtx != null && qsSw.ElapsedMilliseconds - qsLogMs >= 1000)
        {
            qsLogMs = qsSw.ElapsedMilliseconds;
            Log(I18n.Tr("搜尋中… ") + I18n.Tr("共 ") + qsTotal.ToString("N0") + I18n.Tr(" 筆") + I18n.Tr("｜已掃描 ") + Interlocked.Read(ref qsCtx.DirsDone).ToString("N0") + I18n.Tr(" 個資料夾、") + Interlocked.Read(ref qsCtx.FilesSeen).ToString("N0") + I18n.Tr(" 個檔案｜") + qsSw.Elapsed.TotalSeconds.ToString("0.0") + I18n.Tr(" 秒"), Theme.Sub);
        }
        if (qsCtx != null)
            status.Text = I18n.Tr("搜尋中… ") + I18n.Tr("共 ") + qsTotal.ToString("N0") + I18n.Tr(" 筆") + I18n.Tr("｜已掃描 ") + Interlocked.Read(ref qsCtx.DirsDone).ToString("N0") + I18n.Tr(" 個資料夾、") +
                Interlocked.Read(ref qsCtx.FilesSeen).ToString("N0") + I18n.Tr(" 個檔案｜") + qsSw.Elapsed.TotalSeconds.ToString("0.0") + I18n.Tr(" 秒");
    }

    Control BuildSearch()
    {
        int h = Math.Max(S(RowH), Font.Height + S(12));   // 高 DPI 時列高至少容納字高，避免文字上下被切或重疊
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 6, RowCount = 2, Margin = new Padding(0), Tag = "searchbar", BackColor = Theme.Bg };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));   // 類型、副檔名、位置欄寬：下方依目前語系量測後設定
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // 標籤（圖示＋文字）
        var lblFont = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        string lblText = I18n.Tr("快速搜尋");
        int tw = TextRenderer.MeasureText(lblText, lblFont).Width;
        var searchLabel = new Label {
            Text = lblText, Font = lblFont, ForeColor = Theme.Sub, AutoSize = false,
            Image = Theme.Ico("scan", S(IconPx)), ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleRight,
            Size = new Size(S(IconPx) + S(6) + tw, h), Margin = new Padding(0, 0, S(8), 0)
        };
        t.Controls.Add(searchLabel, 0, 0);

        // 輸入框
        searchBox = Theme.DarkTextBox(I18n.Tr("檔名或關鍵字，可用 *.pdf、報告*2024；選了類型可留空"));
        var sbWrap = Theme.Framed(searchBox, S(8));
        sbWrap.Dock = DockStyle.None; sbWrap.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        sbWrap.Height = h; sbWrap.MinimumSize = new Size(S(160), h); sbWrap.Margin = new Padding(0, 0, S(6), 0);
        searchBox.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(); } };
        t.Controls.Add(sbWrap, 1, 0);

        var tip = Theme.DarkTip(Font);
        var icoCache = new Dictionary<string, Bitmap>();
        Func<string, Bitmap> ico = n => { Bitmap bm; if (!icoCache.TryGetValue(n, out bm)) icoCache[n] = bm = Theme.Ico(n, S(IconPx)); return bm; };
        Func<ContextMenuStrip> newMenu = () => {
            var m = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()), BackColor = Theme.Panel, ForeColor = Color.White, ShowItemToolTips = true, Font = Font };
            m.ImageScalingSize = new Size(S(IconPx), S(IconPx));
            m.Items.Add("…");   // Opening 需要至少一個項目才會觸發
            return m;
        };
        Func<ContextMenuStrip, string, string, bool, Action, ToolStripMenuItem> item = (m, text, icon, check, click) => {
            var mi = new ToolStripMenuItem(text, ico(icon)) { ForeColor = Color.White, Padding = new Padding(0, S(3), 0, S(3)), Checked = check };
            mi.Click += (o, e) => click();
            m.Items.Add(mi);
            return mi;
        };
        Action<ContextMenuStrip, string> header = (m, text) => m.Items.Add(new ToolStripLabel(text) { ForeColor = Color.White, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold), Padding = new Padding(S(4), S(6), 0, S(2)) });

        // ---- 類型 ----
        var btnType = new PickerButton(k, Font, tip) { Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = h, Margin = new Padding(0, 0, S(6), 0) };
        Action fitType = null;   // 依目前類型文字調整「類型」欄寬（下方量測後指定）
        Action applyType = () => {
            var o = searchOpt;
            string shortText = o.Label + (o.Exact ? I18n.Tr("・完全符合") : "");
            string full = I18n.Tr("類型：") + o.Label + (o.Exts.Length > 0 ? I18n.Tr("（") + string.Join(" ", o.Exts) + I18n.Tr("）") : "") + (o.Exact ? I18n.Tr("\n完全符合名稱") : "");
            if (o.Zw3d > 0) full = I18n.Tr("類型：") + o.Label + (o.Exact ? I18n.Tr("\n完全符合名稱") : "");
            string icon = o.Zw3d > 0 ? "zw3d" : o.DirsOnly ? "folder" : o.Exts.Length > 0 ? "file" : o.FilesOnly ? "file" : "selectall";
            btnType.Set(shortText, full, ico(icon));
            RefreshExtCombo();
            if (fitType != null) fitType();
        };
        Action<string, string[], bool, bool> setType = (label, exts, filesOnly, dirsOnly) => {
            bool exact = searchOpt.Exact;
            searchOpt = new SearchOptions { Label = label, Exts = exts, FilesOnly = filesOnly, DirsOnly = dirsOnly, Exact = exact, Permanent = searchOpt.Permanent };
            applyType();
        };
        var typeMenu = newMenu();
        typeMenu.Opening += (o, e) => {
            typeMenu.Items.Clear();
            var cur = searchOpt;
            bool isAll = cur.Exts.Length == 0 && !cur.FilesOnly && !cur.DirsOnly && cur.Zw3d == 0;
            item(typeMenu, I18n.Tr("全部（檔案與資料夾）"), "selectall", isAll, () => setType(I18n.Tr("全部"), new string[0], false, false));
            item(typeMenu, I18n.Tr("僅檔案"), "file", cur.FilesOnly && cur.Exts.Length == 0, () => setType(I18n.Tr("僅檔案"), new string[0], true, false));
            item(typeMenu, I18n.Tr("僅資料夾"), "folder", cur.DirsOnly, () => setType(I18n.Tr("僅資料夾"), new string[0], false, true));
            typeMenu.Items.Add(new ToolStripSeparator());
            header(typeMenu, I18n.Tr("常用檔案類型"));
            foreach (var p in TypePresets)
            {
                var name = p[0]; var exts = SearchOptions.ParseExts(p[1]);
                var mi = item(typeMenu, name + I18n.Tr("　") + ExtList(exts, 5), "file", cur.Label == name, () => setType(name, exts, true, false));
                mi.ToolTipText = string.Join(" ", exts);
            }
            typeMenu.Items.Add(new ToolStripSeparator());
            header(typeMenu, I18n.Tr("ZW3D 檔案"));
            var zwModes = new[] {
                new { M = 1, T = I18n.Tr("ZW3D 全部（暫存垃圾＋模型檔）") },
                new { M = 2, T = I18n.Tr("ZW3D 僅暫存垃圾") },
                new { M = 3, T = I18n.Tr("ZW3D 僅模型檔") } };
            foreach (var z in zwModes)
            {
                int mode = z.M; string text = z.T;
                var zmi = item(typeMenu, text, "zw3d", cur.Zw3d == mode, () => {
                    setType(text, new string[0], false, false);
                    searchOpt.Zw3d = mode; applyType();
                });
                string junkTip = I18n.Tr("暫存垃圾：") + "\n" + WrapList(SearchOptions.ZwExtJunk, 8);
                string modelTip = I18n.Tr("模型檔：") + "\n" + WrapList(SearchOptions.ZwExtModel, 8);
                zmi.ToolTipText = mode == 1 ? junkTip + "\n" + modelTip : mode == 2 ? junkTip : modelTip;
            }
            typeMenu.Items.Add(new ToolStripSeparator());
            bool isCustom = cur.Exts.Length > 0 && !TypePresets.Any(p => p[0] == cur.Label);
            item(typeMenu, I18n.Tr("自訂副檔名…") + (isCustom ? I18n.Tr("（目前：") + ExtList(cur.Exts, 6) + I18n.Tr("）") : ""), "scan", isCustom, () => {
                string def = isCustom ? string.Join(", ", cur.Exts.Select(x => x.TrimStart(new[] { '.' }))) : "";
                string input = InputBox.Ask(this, I18n.Tr("自訂副檔名"), I18n.Tr("輸入要搜尋的副檔名，多個請用逗號或空白分隔：\n例如：pdf, dwg, z3prt"), def, k, Font);
                if (input == null) return;
                var exts = SearchOptions.ParseExts(input);
                if (exts.Length == 0) { setType(I18n.Tr("全部"), new string[0], false, false); return; }
                setType(exts.Length <= 3 ? string.Join(" ", exts) : ExtList(exts, 3), exts, true, false);
            });
            typeMenu.Items.Add(new ToolStripSeparator());
            var ex = item(typeMenu, I18n.Tr("完全符合名稱（搜尋指定檔名）"), "selectall", cur.Exact, () => { searchOpt.Exact = !searchOpt.Exact; applyType(); });
            ex.ToolTipText = I18n.Tr("開啟後，檔名需與輸入內容完全相同，例如 setup.exe");
            var pd = item(typeMenu, I18n.Tr("永久刪除檔案（不經資源回收筒）"), "delete", cur.Permanent, () => { searchOpt.Permanent = !searchOpt.Permanent; applyType(); });
            pd.ForeColor = Color.White;
            pd.ToolTipText = I18n.Tr("開啟後，搜尋結果視窗的「刪除」直接永久刪除，無法從資源回收筒還原");
        };
        btnType.Click += (o, e) => { typeMenu.MinimumSize = new Size(btnType.Width, 0); typeMenu.Show(btnType, new Point(0, btnType.Height)); };
        applyType();
        t.Controls.Add(btnType, 2, 0);

        // ---- 副檔名下拉式選單（可直接輸入）----
        extCombo = new DarkCombo { Font = Font, MaxDropDownItems = 20, IntegralHeight = false };
        Theme.DarkScrollbars(extCombo);
        var extWrap = Theme.Framed(extCombo, S(1));
        extWrap.BackColor = Theme.BtnBorder;
        extWrap.Dock = DockStyle.None; extWrap.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        extWrap.Height = h; extWrap.MinimumSize = new Size(S(60), h); extWrap.Margin = new Padding(0, 0, S(6), 0);
        RefreshExtCombo();
        extCombo.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(); } };
        tip.SetToolTip(extCombo, I18n.Tr("選擇單一副檔名；選了 ZW3D 類型時只列出 ZW3D 已知副檔名"));
        t.Controls.Add(extWrap, 3, 0);

        // ---- 位置：每次開啟選單時重新偵測磁碟機，含外接裝置 ----
        string sd = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\";
        var btnScope = new PickerButton(k, Font, tip) { Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = h, Margin = new Padding(0, 0, S(6), 0) };
        Action<int, string, string, string, string> choose = (kind, root, shortText, fullText, icon) => {
            scopeKind = kind; scopeRoot = root;
            btnScope.Set(shortText, I18n.Tr("位置：") + fullText, ico(icon));
        };
        choose(1, null, I18n.Tr("所有磁碟"), I18n.Tr("所有磁碟（含外接裝置）"), "radio");
        var menu = newMenu();
        menu.Opening += (o, e) => {
            menu.Items.Clear();
            item(menu, I18n.Tr("系統磁碟（") + sd + I18n.Tr("）"), "drive", scopeKind == 0, () => choose(0, null, I18n.Tr("系統磁碟 ") + sd, I18n.Tr("系統磁碟 ") + sd, "drive"));
            item(menu, I18n.Tr("所有磁碟（含外接裝置）"), "radio", scopeKind == 1, () => choose(1, null, I18n.Tr("所有磁碟"), I18n.Tr("所有磁碟（含外接裝置）"), "radio"));
            menu.Items.Add(new ToolStripSeparator());
            header(menu, I18n.Tr("磁碟機"));
            var drives = Drives.List();
            if (drives.Count == 0) header(menu, I18n.Tr("　（找不到可用的磁碟機）"));
            foreach (var d in drives)
            {
                var dd = d;
                item(menu, d.Text, d.Icon, scopeKind == 2 && string.Equals(scopeRoot, d.Root, StringComparison.OrdinalIgnoreCase),
                    () => choose(2, dd.Root, dd.Short, dd.Text, dd.Icon));
            }
            menu.Items.Add(new ToolStripSeparator());
            item(menu, I18n.Tr("使用者資料夾"), "folder", scopeKind == 3, () => choose(3, null, I18n.Tr("使用者資料夾"), I18n.Tr("使用者資料夾：") + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "folder"));
            item(menu, customFolder == null ? I18n.Tr("選擇資料夾…") : I18n.Tr("選擇資料夾…（目前：") + customFolder + I18n.Tr("）"), "folder", scopeKind == 4, () => {
                using (var dlg = new FolderBrowserDialog { Description = I18n.Tr("選擇要搜尋的資料夾"), ShowNewFolderButton = false, SelectedPath = customFolder ?? "" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    customFolder = dlg.SelectedPath;
                    string leaf = Native.NameOf(customFolder);
                    choose(4, customFolder, string.IsNullOrEmpty(leaf) ? customFolder : leaf, I18n.Tr("資料夾：") + customFolder, "folder");
                }
            });
        };
        btnScope.Click += (o, e) => { menu.MinimumSize = new Size(btnScope.Width, 0); menu.Show(btnScope, new Point(0, btnScope.Height)); };
        t.Controls.Add(btnScope, 4, 0);

        // 搜尋按鈕
        btnSearch = MakeButton(I18n.Tr("搜尋"), true, "scan");
        int bw = 2 * h + S(6);   // 正方形：邊長 = 下方兩列（輸入列＋選單列）加間距的總高
        btnSearch.AutoSize = false; btnSearch.Size = new Size(bw, bw);
        btnSearch.Image = Theme.Ico("scan", S(32)); btnSearch.ImageAlign = ContentAlignment.MiddleCenter; btnSearch.TextAlign = ContentAlignment.MiddleCenter;
        btnSearch.TextImageRelation = TextImageRelation.ImageAboveText; btnSearch.Padding = new Padding(0);
        btnSearch.Margin = new Padding(S(6), 0, 0, 0);
        btnSearch.Click += (o, e) => DoSearch();
        t.Controls.Add(btnSearch, 5, 0);
        t.SetRowSpan(btnSearch, 2);

        // ---- 依目前語系量測欄寬（不寫死），寬度不足時自動換成兩列，避免 de/hu/ru 等長字串在高縮放下裁切重疊 ----
        var mf = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        Func<IEnumerable<string>, int> maxW = list => list.Select(x => TextRenderer.MeasureText(x, Font, Size.Empty, mf).Width).DefaultIfEmpty(0).Max();
        int pickerChrome = S(8) + S(IconPx) + S(8) + S(30) + S(6);   // 左內距＋圖示＋間距＋箭頭區＋右邊界
        var typeTexts = new List<string> { I18n.Tr("全部"), I18n.Tr("僅檔案"), I18n.Tr("僅資料夾"),
            I18n.Tr("ZW3D 全部（暫存垃圾＋模型檔）"), I18n.Tr("ZW3D 僅暫存垃圾"), I18n.Tr("ZW3D 僅模型檔") };
        typeTexts.AddRange(TypePresets.Select(p => p[0]));
        // 「類型」欄寬依目前類型文字長度自動調整，最長不超過「ZW3D 全部（暫存垃圾＋模型檔）」的寬度，超過則以「…」省略；搜尋欄位吃掉剩餘寬度
        int typeMax = maxW(new[] { I18n.Tr("ZW3D 全部（暫存垃圾＋模型檔）") }) + pickerChrome;
        int typeMin = Math.Min(typeMax, S(110));
        Func<int> calcTypeW = () => Math.Min(typeMax, Math.Max(typeMin, maxW(new[] { btnType.ShortText }) + pickerChrome));
        int typeW = calcTypeW();
        int extW = Math.Min(S(240), Math.Max(S(110), maxW(new[] { I18n.Tr("（全部 ZW3D 已知副檔名）"), I18n.Tr("（不限副檔名）") })
            + SystemInformation.VerticalScrollBarWidth + S(16)));
        int scopeW = Math.Min(S(260), Math.Max(S(140), maxW(new[] { I18n.Tr("系統磁碟 ") + sd, I18n.Tr("所有磁碟"), I18n.Tr("使用者資料夾") }) + pickerChrome));
        int labelW = searchLabel.Width + searchLabel.Margin.Horizontal;
        int needOneRow = labelW + S(220) + typeW + extW + scopeW + bw;
        searchMinW = labelW + S(220) + bw;
        var pickers = new Control[] { btnType, extWrap, btnScope };
        var origM = pickers.Select(c => c.Margin).ToArray();
        // 換行時第二列獨立成 5 欄表格（3 個等寬選單＋2 個固定間距），整列從最左（標籤）對齊到最右（搜尋鈕），間距一致
        var pr = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 5, RowCount = 1, Margin = new Padding(0, S(6), 0, 0), Visible = false };
        pr.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        pr.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(6)));
        pr.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        pr.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(6)));
        pr.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
        pr.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(pr, 0, 1);
        t.SetColumnSpan(pr, 5);
        int wrapState = -1;
        Action<bool> setWrap = wrap => {
            if (wrapState == (wrap ? 1 : 0)) return;
            wrapState = wrap ? 1 : 0;
            t.SuspendLayout();
            if (!wrap)
            {
                pr.Visible = false;
                t.ColumnStyles[1].SizeType = SizeType.Percent; t.ColumnStyles[1].Width = 100;
                t.ColumnStyles[2].SizeType = SizeType.Absolute; t.ColumnStyles[2].Width = typeW;
                t.ColumnStyles[3].SizeType = SizeType.Absolute; t.ColumnStyles[3].Width = extW;
                t.ColumnStyles[4].SizeType = SizeType.Absolute; t.ColumnStyles[4].Width = scopeW;
                for (int i = 0; i < pickers.Length; i++)
                {
                    t.Controls.Add(pickers[i], 2 + i, 0);
                    pickers[i].Margin = origM[i];
                }
            }
            else
            {
                t.ColumnStyles[1].SizeType = SizeType.Percent; t.ColumnStyles[1].Width = 100;
                t.ColumnStyles[2].SizeType = SizeType.Absolute; t.ColumnStyles[2].Width = 0;
                t.ColumnStyles[3].SizeType = SizeType.Absolute; t.ColumnStyles[3].Width = 0;
                t.ColumnStyles[4].SizeType = SizeType.Absolute; t.ColumnStyles[4].Width = 0;
                for (int i = 0; i < pickers.Length; i++)
                {
                    pr.Controls.Add(pickers[i], i * 2, 0);
                    pickers[i].Margin = new Padding(0);
                }
                pr.Visible = true;
            }
            t.ResumeLayout(true);
        };
        fitType = () => {
            typeW = calcTypeW();
            needOneRow = labelW + S(220) + typeW + extW + scopeW + bw;
        };
        setWrap(true);   // 固定兩列：上列輸入框、下列三個選單，搜尋鈕為跨兩列的正方形
        return t;
    }

    int searchMinW;

    // 長清單分行（每行 per 個）
    static string WrapList(string[] items, int per)
    {
        var lines = new List<string>();
        for (int i = 0; i < items.Length; i += per) lines.Add(string.Join(" ", items.Skip(i).Take(per)));
        return string.Join("\n", lines);
    }

    ComboBox extCombo;
    int extComboMode = -1;
    // 依目前類型填入下拉選單：ZW3D 類型只列出該模式的已知副檔名，其他類型列出常用副檔名
    void RefreshExtCombo()
    {
        if (extCombo == null || extComboMode == searchOpt.Zw3d) return;
        extComboMode = searchOpt.Zw3d;
        extCombo.BeginUpdate();
        extCombo.Items.Clear();
        extCombo.Items.Add(extComboMode > 0 ? I18n.Tr("（全部 ZW3D 已知副檔名）") : I18n.Tr("（不限副檔名）"));
        IEnumerable<string> list = extComboMode > 0 ? (IEnumerable<string>)SearchOptions.ZwExts(extComboMode)
            : TypePresets.SelectMany(p => SearchOptions.ParseExts(p[1])).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var x in list) extCombo.Items.Add(x);
        extCombo.SelectedIndex = 0;
        int dw = 0;
        foreach (var o in extCombo.Items) dw = Math.Max(dw, TextRenderer.MeasureText(o.ToString(), extCombo.Font).Width);
        extCombo.DropDownWidth = Math.Max(extCombo.Width, dw + SystemInformation.VerticalScrollBarWidth + 12);
        extCombo.EndUpdate();
    }
    string ExtPick()
    {
        if (extCombo == null || extCombo.SelectedIndex <= 0) return "";
        return extCombo.SelectedItem as string ?? "";
    }

    int scopeKind;       // 0 系統磁碟、1 所有磁碟、2 指定磁碟機、3 使用者資料夾、4 自選資料夾
    string scopeRoot;

    List<string> SearchRoots()
    {
        var r = new List<string>();
        switch (scopeKind)
        {
            case 0: r.Add((Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\"); break;
            case 1: foreach (var d in Drives.List()) if (d.InAll) r.Add(d.Root); break;
            case 2: if (!string.IsNullOrEmpty(scopeRoot)) r.Add(scopeRoot); break;
            case 3: r.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)); break;
            default: if (!string.IsNullOrEmpty(customFolder)) r.Add(customFolder); break;
        }
        return r.Where(Directory.Exists).ToList();
    }

    void DoSearch()
    {
        string q = searchBox.Text.Trim();
        var opt = searchOpt;
        opt.ExtFilter = ExtPick();
        if (q.Length == 0 && opt.Exts.Length == 0 && opt.Zw3d == 0 && opt.ExtFilter.Length == 0)
        {
            searchBox.Focus();
            status.Text = opt.Exact ? I18n.Tr("「完全符合名稱」需要輸入完整檔名，例如 setup.exe。") : I18n.Tr("請輸入檔名或關鍵字，或從「類型」選擇要搜尋的檔案類型。");
            return;
        }
        var roots = SearchRoots();
        if (roots.Count == 0) { MessageBox.Show(this, I18n.Tr("找不到所選的磁碟或資料夾，可能已被移除或尚未插入。\n請從「位置」重新選擇。"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        string title = (q.Length > 0 ? I18n.Tr("搜尋「") + q + I18n.Tr("」") : I18n.Tr("搜尋")) + (opt.Label != I18n.Tr("全部") ? I18n.Tr("　類型：") + opt.Label : "") + (opt.ExtFilter.Length > 0 ? I18n.Tr("　副檔名：") + opt.ExtFilter : "") + (opt.Exact ? I18n.Tr("　完全符合") : "") + (opt.Permanent ? I18n.Tr("　永久刪除") : "");
        // 結果直接輸出到 [快速搜尋] 分頁下方的清單；若上一次搜尋仍在進行，先停止
        if (qsCtx != null) qsCtx.Cancel = true;
        Found dump; while (qsInbox.TryDequeue(out dump)) { }
        qsItems.Clear(); qsChecked.Clear(); qsSelAll = false; qsList.SelectedIndices.Clear(); qsList.VirtualListSize = 0; qsList.Invalidate();
        QsUpdateButtons(); qsBar.Maximum = 1; qsBar.Value = 0;
        qsTotal = 0; qsBytes = 0;
        var c = new Ctx { Dry = true, Collect = qsInbox, CollectMax = 200000 };
        qsCtx = c;
        qsSw.Restart(); qsLogMs = 0;
        qsTimer.Start();
        Log(title + I18n.Tr("：") + string.Join(I18n.Tr("、"), roots), Theme.Sub);
        new Thread(() => {
            try { Searcher.Run(roots, q, c, opt); } catch { }
            UI(() => {
                if (qsCtx != c) return;   // 已被新的搜尋取代
                qsTimer.Stop();
                qsSw.Stop();
                qsCtx = null;
                QsDrain();
                qsBar.Maximum = 1; qsBar.Value = c.Cancel ? 0 : 1;
                string head = I18n.Tr("共 ") + qsTotal.ToString("N0") + I18n.Tr(" 筆") + I18n.Tr("（檔案合計 ") + Cleaner.Human(qsBytes) + I18n.Tr("）");
                string tail = c.Limit ? I18n.Tr("　已達 200,000 筆上限，請輸入更精確的關鍵字")
                            : c.Cancel ? I18n.Tr("　已停止搜尋")
                            : I18n.Tr("　搜尋完成，耗時 ") + qsSw.Elapsed.TotalSeconds.ToString("0.0") + I18n.Tr(" 秒（掃描 ") + c.DirsDone.ToString("N0") + I18n.Tr(" 個資料夾）");
                status.Text = qsTotal == 0 ? I18n.Tr("找不到符合的檔案或資料夾。") + tail : head + tail;
                Log(status.Text, Theme.Sub);
            });
        }) { IsBackground = true, Name = "qsearch" }.Start();
    }

    void ShowPreviewList()
    {
        if (lastPreview == null || lastPreview.Count == 0) return;
        var rf = new ResultsForm(I18n.Tr("預覽清單：將被清理的檔案"), true, k, Font, Icon);
        rf.StartPosition = FormStartPosition.Manual;
        rf.Location = new Point(Left + S(40), Top + S(40));
        rf.Show(this);
        rf.LoadItems(lastPreview, lastPreview.Count >= 200000 ? I18n.Tr("　（只列出前 200,000 筆）") : null);
    }

    // ===== 分頁列：[快速搜尋] [清理] ……… [用戶目錄管理器] =====
    Control BuildTabStrip(Control searchPage, Control cleanPage)
    {
        var strip = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 0, 0, S(8)), Tag = "blocks" };
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        strip.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var tabSearch = MakeButton(I18n.Tr("快速搜尋"), false, "scan");
        var tabClean = MakeButton(I18n.Tr("清理項目"), false, "recycle");
        var btnUf = MakeButton(I18n.Tr("用戶目錄管理器"), false, "folder");
        btnUf.Click += (o, e) => { using (var uf = new UserFolderForm(k, Font)) uf.ShowDialog(this); };
        btnUf.Margin = new Padding(0);
        foreach (var b in new[] { tabSearch, tabClean }) b.Padding = new Padding(S(10), S(4), S(14), S(4));
        Action<bool> show = first => {
            searchPage.Visible = first; cleanPage.Visible = !first;
            foreach (var p in new[] { new { B = tabSearch, On = first }, new { B = tabClean, On = !first } })
            {
                p.B.BackColor = p.On ? Theme.Primary : Theme.Btn;
                p.B.FlatAppearance.BorderColor = p.On ? Theme.Accent : Theme.BtnBorder;
                p.B.FlatAppearance.BorderSize = p.On ? 2 : 1;
                p.B.Font = new Font(Font, p.On ? FontStyle.Bold : FontStyle.Regular);
            }
        };
        tabSearch.Click += (o, e) => show(true);
        tabClean.Click += (o, e) => show(false);
        strip.Controls.Add(tabSearch, 0, 0);
        strip.Controls.Add(tabClean, 1, 0);
        strip.Controls.Add(btnUf, 3, 0);
        show(true);
        return strip;
    }

    Control BuildToolbar()
    {
        btnAll = MakeButton(I18n.Tr("全選（不含危險項）"), false, "selectall");
        btnNone = MakeButton(I18n.Tr("全不選"), false, "unselect");
        btnDefault = MakeButton(I18n.Tr("還原磁碟清理"), false, "reset");
        btnSage = MakeButton(I18n.Tr("設定磁碟清理…"), false, "radio");
        btnAll.Click += (o, e) => list.SetAll(ci => ci.Risk != Risk.Danger);
        btnNone.Click += (o, e) => list.SetAll(ci => false);
        btnDefault.Click += (o, e) => list.SetAll(ci => ci.Default);
        btnSage.Click += (o, e) => {
            try { Process.Start("cleanmgr.exe", "/sageset:99"); Log(I18n.Tr("已開啟「磁碟清理」設定視窗，勾選要清理的項目後按確定。"), Theme.Sub); }
            catch (Exception ex) { MessageBox.Show(this, I18n.Tr("無法開啟磁碟清理：") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        // 四顆按鈕等寬、等間距，填滿第二區塊
        var btns = new Button[] { btnAll, btnNone, btnDefault, btnSage };
        var f = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 7, RowCount = 1, Margin = new Padding(0), BackColor = Theme.Bg };
        f.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        int ph = btns.Max(b => b.GetPreferredSize(Size.Empty).Height);
        int[] prefW = btns.Select(b => b.GetPreferredSize(Size.Empty).Width).ToArray();
        for (int i = 0; i < btns.Length; i++)
        {
            f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            if (i < btns.Length - 1) f.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(6)));
            var b = btns[i];
            b.AutoSize = false; b.AutoEllipsis = false; b.ImageAlign = ContentAlignment.MiddleCenter; b.TextAlign = ContentAlignment.MiddleCenter; b.TextImageRelation = TextImageRelation.ImageBeforeText; b.Padding = new Padding(S(2), 0, S(2), 0); b.Dock = DockStyle.Fill; b.Margin = new Padding(0); b.MinimumSize = new Size(0, ph); b.Height = ph;
            f.Controls.Add(b, i * 2, 0);
        }
        // 寬度夠就四顆等寬；視窗較窄時改依各按鈕文字寬度等比分配，避免文字換行造成圖示位移
        Action fit = () => {
            int W = f.ClientSize.Width; if (W <= 0) return;
            bool equal = W >= prefW.Max() * btns.Length + S(6) * (btns.Length - 1);
            for (int i = 0; i < btns.Length; i++) f.ColumnStyles[i * 2].Width = equal ? 25 : prefW[i];
            // 其他語系名稱太長放不下時，按鈕自動變成兩行高，文字換行顯示
            int colW = equal ? (W - S(6) * (btns.Length - 1)) / btns.Length : 0;
            bool wrap = false;
            for (int i = 0; i < btns.Length; i++)
            {
                int cw = equal ? colW : (int)((W - S(6) * (btns.Length - 1)) * (double)prefW[i] / prefW.Sum());
                if (TextRenderer.MeasureText(btns[i].Text, btns[i].Font).Width + S(18) + S(14) > cw) wrap = true;
            }
            int hh = wrap ? ph + Font.Height : ph;
            foreach (var x in btns) { x.MinimumSize = new Size(0, hh); x.MaximumSize = new Size(0, hh); }
        };
        f.SizeChanged += (o, e) => fit();
        fit();
        return f;
    }

    Control BuildList()
    {
        list = new ItemList(items, items.Select(i => i.Icon).ToArray(), k, Font);
        return list;
    }

    Control BuildLog()
    {
        log = new RichTextBox { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Desc, Font = new Font(Font.FontFamily, 9f) };
        Theme.DarkScrollbars(log);
        var wrap = new Panel { BackColor = Theme.Panel, Padding = new Padding(S(8), S(4), S(4), S(4)) };
        log.Dock = DockStyle.Fill;
        wrap.Controls.Add(log);
        return wrap;
    }

    Control BuildBottom()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 3, Margin = new Padding(0, S(6), 0, 0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        status = new Label { Text = I18n.Tr("就緒"), AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Height = S(20), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Sub, Margin = new Padding(S(2), 0, 0, S(2)) };
        bar = new Meter { Dock = DockStyle.Fill, Height = S(10), MinimumSize = new Size(S(120), S(10)), Margin = new Padding(0, S(9), S(12), S(9)) };
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        btnStop = MakeButton(I18n.Tr("停止"), false, "stop"); btnStop.Enabled = false;
        btnPreview = MakeButton(I18n.Tr("預覽"), false, "scan");
        btnList = MakeButton(I18n.Tr("檢視清單"), false, "file");
        btnList.Enabled = false;
        btnList.Click += (o, e) => ShowPreviewList();
        btnRun = MakeButton(I18n.Tr("開始清理"), true, "confirm");
        btnRun.Margin = new Padding(0);
        btnStop.Click += (o, e) => { if (ctx != null) { ctx.Cancel = true; status.Text = I18n.Tr("正在停止…"); } };
        btnPreview.Click += (o, e) => Start(true);
        btnRun.Click += (o, e) => Start(false);
        f.Controls.AddRange(new Control[] { btnStop, btnList, btnPreview, btnRun });
        t.Controls.Add(status, 0, 0);
        t.SetColumnSpan(status, 2);
        t.Controls.Add(bar, 0, 1);
        t.Controls.Add(f, 1, 1);
        // 署名：固定顯示於「開始清理」下方，靠右下對齊
        var sign = new Label { Text = "by阿家", AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            Font = new Font("Microsoft JhengHei UI", 8.5f), ForeColor = Theme.Sub, Margin = new Padding(0, S(2), 0, 0) };
        t.Controls.Add(sign, 1, 2);
        return t;
    }

    Button MakeButton(string text, bool primary, string icon) { return Theme.MakeButton(text, primary, icon, k, Font); }

    void SwitchLanguage(string code)
    {
        if (code == I18n.Code) return;
        WindowState_Save();
        I18n.Save(code);
        Close();
        if (IsDisposed || !Visible)
        {
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "/relaunch") { UseShellExecute = false }); } catch { }
        }
    }

    void RelaunchAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Application.ExecutablePath, "/relaunch") { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch { MessageBox.Show(this, I18n.Tr("未取得系統管理員權限，將以一般權限繼續。"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    void UI(Action a)
    {
        try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { }
    }

    void Log(string text, Color c)
    {
        if (InvokeRequired) { UI(() => Log(text, c)); return; }
        log.SelectionStart = log.TextLength;
        log.SelectionColor = c;
        log.AppendText(text + Environment.NewLine);
        log.ScrollToCaret();
    }

    void SetBusy(bool busy)
    {
        list.Enabled = btnRun.Enabled = btnPreview.Enabled = btnAll.Enabled = btnNone.Enabled = btnDefault.Enabled = btnSage.Enabled = !busy;
        if (btnAdmin != null) btnAdmin.Enabled = !busy;
        btnStop.Enabled = busy;
        btnList.Enabled = !busy && lastPreview != null && lastPreview.Count > 0;
    }

    void Start(bool dry)
    {
        var sel = new List<CleanItem>();
        var danger = new List<string>();
        foreach (var ci in items)
            if (list.Checked[ci]) { sel.Add(ci); if (ci.Risk == Risk.Danger) danger.Add("• " + ci.Name); }

        if (sel.Count == 0) { MessageBox.Show(this, I18n.Tr("尚未勾選任何項目。"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        if (!dry)
        {
            if (danger.Count > 0)
            {
                if (MessageBox.Show(this, I18n.Tr("您勾選了下列危險項目，將永久刪除資料夾或使用者帳號，無法復原：\n\n") + string.Join("\n", danger) + I18n.Tr("\n\n確定要繼續嗎？"),
                        I18n.Tr("警告"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                if (MessageBox.Show(this, I18n.Tr("最後確認：真的要執行危險項目嗎？"), I18n.Tr("警告"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            }
            else if (MessageBox.Show(this, I18n.Tr("確定要清理勾選的 ") + sel.Count + I18n.Tr(" 個項目嗎？"), Text,
                         MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        }

        log.Clear();
        SetBusy(true);
        bar.Maximum = 1; bar.Value = 0;
        ctx = new Ctx { Dry = dry, Collect = dry ? new ConcurrentQueue<Found>() : null };
        var c = ctx;
        if (!dry) lastPreview = null;      // 清理後舊的預覽清單已失效
        string phase = I18n.Tr("準備中");
        var sw = Stopwatch.StartNew();
        var timer = new System.Windows.Forms.Timer { Interval = 200 };
        timer.Tick += (o, e) => ShowProgress(c, phase, sw);
        timer.Start();
        Log(dry ? I18n.Tr("▶ 預覽模式（只計算，不會刪除任何檔案）") : I18n.Tr("▶ 開始清理"), Theme.Accent);

        var th = new Thread(() => {
            Dictionary<CleanItem, Stats> res;
            try { res = Cleaner.Execute(sel, c, p => { phase = p; Log("  · " + p + "…", Theme.Sub); }); }
            catch (Exception ex) { res = new Dictionary<CleanItem, Stats>(); Log(I18n.Tr("發生錯誤：") + ex.Message, Theme.Danger); }
            sw.Stop();
            bool canceled = c.Cancel;
            int tf = 0, tfail = 0; long tb = 0;
            string verb = dry ? I18n.Tr("可刪除") : I18n.Tr("已刪除");
            foreach (var ci in sel)
            {
                Stats st;
                if (!res.TryGetValue(ci, out st)) continue;
                string line = "  " + ci.Name + I18n.Tr("：");
                if (st.Note != null) line += st.Note + (st.Files > 0 ? I18n.Tr("；") : "");
                if (st.Note == null || st.Files > 0) line += verb + " " + st.Files.ToString("N0") + I18n.Tr(" 個檔案，") + Cleaner.Human(st.Bytes);
                if (st.Failed > 0) line += I18n.Tr("（") + st.Failed.ToString("N0") + I18n.Tr(" 個使用中或無權限，已略過）");
                Log(line, st.Note != null && st.Note.StartsWith(I18n.Tr("發生錯誤")) ? Theme.Danger : Theme.Desc);
                tf += st.Files; tb += st.Bytes; tfail += st.Failed;
            }
            double secs = Math.Max(0.1, sw.Elapsed.TotalSeconds);
            string sum = canceled ? I18n.Tr("已停止。") : dry ? I18n.Tr("預覽完成：") : I18n.Tr("清除系統垃圾檔案完成！");
            sum += (dry ? I18n.Tr("共可刪除 ") : I18n.Tr("共刪除 ")) + tf.ToString("N0") + I18n.Tr(" 個檔案，") + (dry ? I18n.Tr("約可釋放 ") : I18n.Tr("釋放 ")) + Cleaner.Human(tb);
            string extra = (tfail > 0 ? "\n" + tfail.ToString("N0") + I18n.Tr(" 個檔案因使用中或權限不足而略過") : "")
                + I18n.Tr("\n共掃描 ") + c.DirsDone.ToString("N0") + I18n.Tr(" 個資料夾、") + c.FilesSeen.ToString("N0") + I18n.Tr(" 個檔案")
                + I18n.Tr("\n耗時 ") + secs.ToString("0.0") + I18n.Tr(" 秒（每秒約 ") + (c.FilesSeen / secs).ToString("N0") + I18n.Tr(" 個檔案）");
            Log("■ " + sum + extra.Replace("\n", I18n.Tr("　")), canceled ? Theme.Caution : Theme.Safe);
            if (dry && c.Collect != null)
            {
                var found = c.Collect.ToList();
                UI(() => lastPreview = found);
                if (found.Count > 0) Log(I18n.Tr("  可按下方「檢視清單」查看這些檔案，並直接開啟檔案位置。"), Theme.Accent);
            }
            UI(() => {
                timer.Stop();
                ShowProgress(c, I18n.Tr("完成"), sw);
                bar.Maximum = 1; bar.Value = 1;
                ctx = null;
                SetBusy(false);
                status.Text = sum;
                if (!IsDisposed && Visible)
                    MessageBox.Show(this, sum + extra, Text, MessageBoxButtons.OK, canceled ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            });
        });
        th.IsBackground = true;
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
    }

    void ShowProgress(Ctx c, string phase, Stopwatch sw)
    {
        long found = Interlocked.Read(ref c.DirsFound), done = Interlocked.Read(ref c.DirsDone);
        long files = Interlocked.Read(ref c.FilesSeen), del = Interlocked.Read(ref c.Deleted), bytes = Interlocked.Read(ref c.DeletedBytes);
        bar.Maximum = (int)Math.Max(1, Math.Min(int.MaxValue, found));
        bar.Value = (int)Math.Min(bar.Maximum, done);
        double secs = Math.Max(0.1, sw.Elapsed.TotalSeconds);
        status.Text = phase + I18n.Tr("｜已掃描 ") + done.ToString("N0") + I18n.Tr(" 個資料夾、") + files.ToString("N0") + I18n.Tr(" 個檔案｜")
            + (c.Dry ? I18n.Tr("可刪除 ") : I18n.Tr("已刪除 ")) + del.ToString("N0") + I18n.Tr(" 個（") + Cleaner.Human(bytes) + I18n.Tr("）｜每秒 ") + (files / secs).ToString("N0") + I18n.Tr(" 個檔案");
    }
}
