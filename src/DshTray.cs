// DeepSeek Harness 托盘启动器
// 编译：csc /target:winexe /win32icon:dsh.ico /out:dsh-tray.exe DshTray.cs
//
// 行为：
//   双击     → 已在运行则只打开浏览器；未运行则无窗口启动 → 等端口就绪 → 打开浏览器
//   托盘左键 → 打开浏览器
//   托盘右键 → 打开 / 打开日志 / 打开工作区 / 退出（结束 DSH）
//   再次双击 → 用命名事件通知已在运行的实例，绝不重复启动第二个 DSH
//
// 本文件刻意使用 C# 5 语法：.NET Framework 4.0 自带的 csc 不支持插值字符串等新语法。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DshTray
{
    internal sealed class TrayApp : ApplicationContext
    {
        // 命名事件与互斥体都按端口区分：不同端口的实例互不干扰，
        // 因此同一台机器可以同时托管多份 DSH。
        internal static string OpenEventName(int port) { return "Local\\DshTray." + port + ".OpenBrowser"; }
        internal static string ExitEventName(int port) { return "Local\\DshTray." + port + ".ExitRequest"; }
        internal static string MutexName(int port) { return "Local\\DshTray." + port + ".SingleInstance"; }

        private readonly NotifyIcon _icon;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly string _workspace;
        private readonly int _port;
        private readonly string _url;
        private readonly string _logPath;
        private readonly string _appDir;

        private Process _dsh;              // 由本实例启动的进程
        private bool _owned;               // DSH 是否由本实例持有
        private bool _waitingForPort;
        private DateTime _startDeadline;
        private volatile bool _pendingOpen;
        private volatile bool _pendingExit;
        private bool _exiting;
        private volatile string _readyUrl;  // 从 dsh 输出抓到的「带 token」URL，必须用它才能认证
        private bool _waitingForUrl;
        private DateTime _urlDeadline;
        private int _missCount;            // 外部启动模式下端口连续丢失次数
        private readonly object _logLock = new object();

        public TrayApp(string appDir, string workspace, int port)
        {
            _appDir = appDir;
            _workspace = workspace;
            _port = port;
            _url = "http://127.0.0.1:" + port;
            _owned = false;

            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DshTray");
            Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, "dsh-web.log");

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem open = new ToolStripMenuItem("打开 DeepSeek Harness", null, OnOpen);
            open.Font = new Font(open.Font, FontStyle.Bold);   // 默认项加粗
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("打开日志", null, OnOpenLog));
            menu.Items.Add(new ToolStripMenuItem("打开工作区", null, OnOpenWorkspace));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("退出（结束 DSH）", null, OnExitClicked));

            _icon = new NotifyIcon();
            _icon.Icon = LoadAppIcon();
            _icon.Text = "DeepSeek Harness";
            _icon.ContextMenuStrip = menu;
            _icon.Visible = true;
            _icon.DoubleClick += OnOpen;
            _icon.BalloonTipClicked += OnOpen;

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 500;
            _timer.Tick += OnTick;
            _timer.Start();

            StartSignalThread();
            Bootstrap();
        }

        // ---------- 图标 ----------
        private Icon LoadAppIcon()
        {
            string icoPath = Path.Combine(_appDir, "dsh.ico");
            try
            {
                if (File.Exists(icoPath)) return new Icon(icoPath, SystemInformation.SmallIconSize);
            }
            catch { }
            try
            {
                Icon assoc = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (assoc != null) return assoc;
            }
            catch { }
            return SystemIcons.Application;
        }

        // ---------- 启动判定 ----------
        private void Bootstrap()
        {
            if (PortListening(_port))
            {
                SetTip("DeepSeek Harness — 运行中（外部启动）");
                Notify("DeepSeek Harness", "检测到已在运行，已连接到 " + _url);
                return;
            }
            StartDsh();
        }

        private void StartDsh()
        {
            string fileName;
            string arguments;
            string workDir;
            if (!ResolveEntry(_workspace, _port, out fileName, out arguments, out workDir))
            {
                NotifyError("找不到 dsh 入口",
                    "请确认 dsh 已安装，或设置 DSH_EXE 环境变量指向 dsh.cmd / bin.js。");
                return;
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
                psi.WorkingDirectory = workDir;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;                 // 控制台程序也不弹窗
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                Process p = new Process();
                p.StartInfo = psi;
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                { if (e.Data != null) OnDshLine(e.Data); };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                { if (e.Data != null) OnDshLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                _dsh = p;
                _owned = true;
                _waitingForPort = true;
                _startDeadline = DateTime.Now.AddSeconds(120);
                SetTip("DeepSeek Harness — 启动中…");
                Log("=== launcher: " + fileName + " " + arguments + " (cwd=" + workDir + ") ===");
            }
            catch (Exception ex)
            {
                NotifyError("启动失败", ex.Message);
            }
        }

        // 入口解析顺序：DSH_EXE → PATH 上的 dsh.cmd → npx 缓存里的 bin.js → npx 现拉
        internal static bool ResolveEntry(string workspace, int port,
            out string fileName, out string arguments, out string workDir)
        {
            fileName = null; arguments = null;
            workDir = workspace;   // 工作目录恒为 workspace，绝不能被下面的 out 参数覆盖

            string envExe = Environment.GetEnvironmentVariable("DSH_EXE");
            if (!string.IsNullOrEmpty(envExe) && File.Exists(envExe))
                return BuildInvocation(envExe, port, out fileName, out arguments);

            string pathCmd = FindOnPath("dsh.cmd");
            if (pathCmd != null)
                return BuildInvocation(pathCmd, port, out fileName, out arguments);

            string binJs = FindInstalledBinJs();
            if (binJs != null)
                return BuildInvocation(binJs, port, out fileName, out arguments);

            fileName = "cmd.exe";
            arguments = "/c npx -y @deepseek-ai/dsh web --port " + port + " --no-open";
            return true;
        }

        private static bool BuildInvocation(string entry, int port,
            out string fileName, out string arguments)
        {
            string lower = entry.ToLowerInvariant();
            // --no-open：由托盘程序自己用「带 token 的地址」打开浏览器，
            // 避免 dsh 再开一个（可能没带 token 的）标签页
            string tail = " web --port " + port + " --no-open";
            if (lower.EndsWith(".js") || lower.EndsWith(".mjs") || lower.EndsWith(".cjs"))
            {
                fileName = "node.exe";
                arguments = "\"" + entry + "\"" + tail;
            }
            else if (lower.EndsWith(".cmd") || lower.EndsWith(".bat"))
            {
                fileName = "cmd.exe";
                arguments = "/c \"\"" + entry + "\"" + tail + "\"";
            }
            else
            {
                fileName = entry;
                arguments = tail.TrimStart();
            }
            return true;
        }

        private static string FindOnPath(string exeName)
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path)) return null;
            string[] parts = path.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string dir = parts[i].Trim();
                if (dir.Length == 0) continue;
                try
                {
                    string full = Path.Combine(dir, exeName);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return null;
        }

        // 在常见安装位置里找 @deepseek-ai/dsh 的入口 bin.js，取修改时间最新的一个。
        // 覆盖：npm -g、pnpm -g（含带版本号一级目录）、npx 缓存（目录名是哈希）。
        private const string RelBinJs = "node_modules\\@deepseek-ai\\dsh\\lib\\bin.js";

        private static string FindInstalledBinJs()
        {
            List<string> hits = new List<string>();
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                AddIfExists(hits, Path.Combine(appData, RelBinJs));                        // npm -g
                AddIfExists(hits, Path.Combine(localAppData, "pnpm", RelBinJs));
                AddIfExists(hits, Path.Combine(programFiles, "nodejs", RelBinJs));

                string pnpmGlobal = Path.Combine(localAppData, "pnpm", "global");
                if (Directory.Exists(pnpmGlobal))
                {
                    string[] versions = Directory.GetDirectories(pnpmGlobal);
                    for (int i = 0; i < versions.Length; i++)
                        AddIfExists(hits, Path.Combine(versions[i], RelBinJs));
                }

                string npx = Path.Combine(localAppData, "npm-cache", "_npx");
                if (Directory.Exists(npx))
                {
                    string[] dirs = Directory.GetDirectories(npx);
                    for (int i = 0; i < dirs.Length; i++)
                        AddIfExists(hits, Path.Combine(dirs[i], RelBinJs));
                }
            }
            catch { }

            if (hits.Count == 0) return null;
            hits.Sort(delegate(string a, string b)
            {
                return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
            });
            return hits[0];
        }

        private static void AddIfExists(List<string> hits, string path)
        {
            try { if (File.Exists(path)) hits.Add(path); }
            catch { }
        }

        // ---------- 端口 ----------
        internal static bool PortListening(int port)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect("127.0.0.1", port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(400)) return false;
                    c.EndConnect(ar);
                    return true;
                }
            }
            catch { return false; }
        }

        internal static int FindPortOwner(int port)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netstat.exe", "-ano");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);
                    string needle = ":" + port + " ";
                    string[] lines = output.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();
                        if (line.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (line.IndexOf(needle, StringComparison.Ordinal) < 0) continue;
                        string[] cols = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (cols.Length < 5) continue;
                        int pid;
                        if (int.TryParse(cols[cols.Length - 1], out pid)) return pid;
                    }
                }
            }
            catch { }
            return 0;
        }

        private static void KillTree(int pid)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("taskkill.exe", "/PID " + pid + " /T /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi)) { p.WaitForExit(10000); }
            }
            catch { }
        }

        // ---------- 信号线程：第二个实例 → 本实例 ----------
        private void StartSignalThread()
        {
            Thread t = new Thread(delegate()
            {
                EventWaitHandle openEv = MakeEvent(OpenEventName(_port));
                EventWaitHandle exitEv = MakeEvent(ExitEventName(_port));
                WaitHandle[] handles = new WaitHandle[] { openEv, exitEv };
                while (true)
                {
                    int idx = WaitHandle.WaitAny(handles, 500);
                    if (idx == 0) _pendingOpen = true;
                    else if (idx == 1) _pendingExit = true;
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private static EventWaitHandle MakeEvent(string name)
        {
            try { return new EventWaitHandle(false, EventResetMode.AutoReset, name); }
            catch { return new EventWaitHandle(false, EventResetMode.AutoReset); }  // 兜底：永不触发
        }

        // ---------- 定时器：端口就绪 / 子进程退出 / 处理信号 ----------
        private void OnTick(object sender, EventArgs e)
        {
            if (_pendingOpen)
            {
                _pendingOpen = false;
                OpenBrowser();
            }
            if (_pendingExit)
            {
                _pendingExit = false;
                DoExit();
                return;
            }
            if (_waitingForPort)
            {
                if (PortListening(_port))
                {
                    // 端口就绪还不够：必须等 dsh 把「带 token 的 URL」打出来，
                    // 否则浏览器拿不到签名 cookie，API 会拒绝
                    _waitingForPort = false;
                    _waitingForUrl = true;
                    _urlDeadline = DateTime.Now.AddSeconds(10);
                    SetTip("DeepSeek Harness — 运行中");
                }
                else if (DateTime.Now > _startDeadline)
                {
                    _waitingForPort = false;
                    SetTip("DeepSeek Harness — 启动超时");
                    NotifyError("启动超时", "120 秒内端口未就绪，请打开日志排查。");
                }
                return;
            }
            if (_waitingForUrl)
            {
                if (_readyUrl != null || DateTime.Now > _urlDeadline)
                {
                    _waitingForUrl = false;
                    Notify("DeepSeek Harness 已启动", "正在打开浏览器…");
                    OpenBrowser();
                }
                return;
            }
            if (_owned && _dsh != null)
            {
                bool gone;
                try { gone = _dsh.HasExited; } catch { gone = true; }
                if (gone)
                {
                    SetTip("DeepSeek Harness — 已停止");
                    Notify("DeepSeek Harness 已停止", "进程已退出，托盘图标即将关闭。");
                    DoExit();
                }
                return;
            }
            // 外部启动模式：端口的生命周期就是本图标的生命周期
            if (!_owned)
            {
                if (PortListening(_port)) _missCount = 0;
                else
                {
                    _missCount++;
                    if (_missCount >= 2) DoExit();
                }
            }
        }

        // ---------- 菜单动作 ----------
        private void OnOpen(object sender, EventArgs e) { OpenBrowser(); }

        private void OpenBrowser()
        {
            // 优先用 dsh 自己打印的「带 token」地址；外部启动时退回裸地址
            string target = _readyUrl != null ? _readyUrl : _url;
            Log("open browser: " + target);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(target);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                NotifyError("无法打开浏览器", ex.Message + " —— 请手动访问 " + target);
            }
        }

        private void OnOpenLog(object sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(_logPath)) File.WriteAllText(_logPath, "");
                ProcessStartInfo psi = new ProcessStartInfo(_logPath);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex) { NotifyError("无法打开日志", ex.Message); }
        }

        private void OnOpenWorkspace(object sender, EventArgs e)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("explorer.exe", "\"" + _workspace + "\"");
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch (Exception ex) { NotifyError("无法打开工作区", ex.Message); }
        }

        private void OnExitClicked(object sender, EventArgs e) { DoExit(); }

        private void DoExit()
        {
            if (_exiting) return;
            _exiting = true;
            try { _timer.Stop(); } catch { }
            try { _icon.Visible = false; } catch { }

            if (_owned && _dsh != null)
            {
                int pid = 0;
                try { if (!_dsh.HasExited) pid = _dsh.Id; } catch { }
                if (pid > 0) KillTree(pid);
            }
            else
            {
                int pid = FindPortOwner(_port);
                if (pid > 0) KillTree(pid);
            }
            try { _icon.Dispose(); } catch { }
            ExitThread();
        }

        // ---------- 小工具 ----------
        private void SetTip(string text)
        {
            try { _icon.Text = text.Length > 63 ? text.Substring(0, 63) : text; }
            catch { }
        }

        private void Notify(string title, string text)
        {
            try
            {
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipText = text;
                _icon.ShowBalloonTip(4000);
            }
            catch { }
        }

        private void NotifyError(string title, string text) { Notify(title, text); }

        private void Log(string line)
        {
            try
            {
                lock (_logLock)
                {
                    FileInfo fi = new FileInfo(_logPath);
                    if (fi.Exists && fi.Length > 5 * 1024 * 1024) File.Delete(_logPath);
                    File.AppendAllText(_logPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch { }
        }

        // dsh 输出的每一行：写日志，并抓取「带 token」的根 URL
        private void OnDshLine(string line)
        {
            Log(line);
            if (_readyUrl == null)
            {
                string url = ExtractUrl(line);
                if (url != null) _readyUrl = url;
            }
        }

        // 形如：dsh web: http://127.0.0.1:3080/?token=xxxxxxxx
        private static string ExtractUrl(string line)
        {
            string clean = System.Text.RegularExpressions.Regex.Replace(
                line, "\\x1B\\[[0-9;]*[A-Za-z]", "");
            int i = clean.IndexOf("http://127.0.0.1", StringComparison.OrdinalIgnoreCase);
            if (i < 0) i = clean.IndexOf("http://localhost", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            string url = clean.Substring(i).Trim();
            int sp = url.IndexOfAny(new char[] { ' ', '\t', '\r', '\n' });
            if (sp > 0) url = url.Substring(0, sp);
            return url.Length > 12 ? url : null;
        }

        // ---------- 自检：纯静态诊断，不建托盘、不启动任何进程 ----------
        internal static string SelfTest(string appDir, string workspace, int port)
        {
            StringBuilder sb = new StringBuilder();
            string fileName, arguments, workDir;
            ResolveEntry(workspace, port, out fileName, out arguments, out workDir);

            sb.AppendLine("appDir       = " + appDir);
            sb.AppendLine("exe          = " + Application.ExecutablePath);
            sb.AppendLine("workspace    = " + workspace);
            sb.AppendLine("port         = " + port);
            sb.AppendLine("url          = http://127.0.0.1:" + port);
            sb.AppendLine("iconExists   = " + File.Exists(Path.Combine(appDir, "dsh.ico")));
            sb.AppendLine("resolvedFile = " + fileName);
            sb.AppendLine("resolvedArgs = " + arguments);
            sb.AppendLine("resolvedCwd  = " + workDir);
            sb.AppendLine("portOpen     = " + PortListening(port));
            sb.AppendLine("ownerPid     = " + FindPortOwner(port));
            sb.AppendLine("logPath      = " + Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DshTray", "dsh-web.log"));
            sb.AppendLine("urlParseTest = " + ExtractUrl("dsh web: http://127.0.0.1:3080/?token=ABC123def"));
            return sb.ToString();
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            string appDir = Path.GetDirectoryName(Application.ExecutablePath);
            // 默认工作区 = 用户主目录；用 dsh-tray.ini 的 workspace= 或环境变量 DSH_WORKSPACE 覆盖
            string workspace = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            int port = 3080;

            // 可选 ini：dsh-tray.ini（workspace=... / port=...）
            string ini = Path.Combine(appDir, "dsh-tray.ini");
            if (File.Exists(ini))
            {
                string[] lines = File.ReadAllLines(ini);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "workspace" && val.Length > 0) workspace = val;
                    else if (key == "port")
                    {
                        int p;
                        if (int.TryParse(val, out p) && p > 0 && p < 65536) port = p;
                    }
                }
            }

            string envWs = Environment.GetEnvironmentVariable("DSH_WORKSPACE");
            if (!string.IsNullOrEmpty(envWs)) workspace = envWs;
            string envPort = Environment.GetEnvironmentVariable("DSH_PORT");
            if (!string.IsNullOrEmpty(envPort))
            {
                int p;
                if (int.TryParse(envPort, out p) && p > 0 && p < 65536) port = p;
            }

            // --selftest <outfile>：只诊断，不出界面
            if (args.Length >= 2 && args[0] == "--selftest")
            {
                File.WriteAllText(args[1], TrayApp.SelfTest(appDir, workspace, port), Encoding.UTF8);
                return;
            }

            // --open / --stop：控制已在运行的实例
            if (args.Length >= 1 && (args[0] == "--open" || args[0] == "--stop"))
            {
                string evName = args[0] == "--open"
                    ? TrayApp.OpenEventName(port) : TrayApp.ExitEventName(port);
                try
                {
                    EventWaitHandle ev = EventWaitHandle.OpenExisting(evName);
                    ev.Set();
                }
                catch
                {
                    if (args[0] == "--open")
                    {
                        try
                        {
                            ProcessStartInfo psi = new ProcessStartInfo("http://127.0.0.1:" + port);
                            psi.UseShellExecute = true;
                            Process.Start(psi);
                        }
                        catch { }
                    }
                }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 单实例：第二个实例只通知第一个实例打开浏览器，然后立即退出
            bool createdNew;
            Mutex mutex = new Mutex(true, TrayApp.MutexName(port), out createdNew);
            if (!createdNew)
            {
                try
                {
                    EventWaitHandle ev = EventWaitHandle.OpenExisting(TrayApp.OpenEventName(port));
                    ev.Set();
                }
                catch { }
                return;
            }

            GC.KeepAlive(mutex);
            Application.Run(new TrayApp(appDir, workspace, port));
        }
    }
}
