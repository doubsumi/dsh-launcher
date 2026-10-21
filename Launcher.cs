// DeepSeek Harness Launcher - a tiny native launcher (.NET Framework 4.x).
// Doubles as the desktop shortcut target so the shortcut is a normal program.
//   (no args)            -> start DSH in a HIDDEN cmd window (log to file)
//   --visible / visible  -> start DSH in a VISIBLE cmd window (kept open)
//   --monitor <port> [idleSeconds]
//                        -> watch the web UI; stop dsh when the browser tab is
//                           closed (no connections for the idle grace period)
//
// Environment check: if dsh is not installed, ask (Windows popup) whether to
// install it via npm. Refuse -> exit. Agree -> open a cmd window and run the
// install command. It shells out to start-dsh.cmd (same folder) for launching.
//
// dsh lookup is generic (not tied to a fixed path):
//   1. resolve dsh.cmd through PATH
//   2. query `npm prefix -g` (honors custom npm global prefixes)
//   3. fall back to npm's default location %APPDATA%\npm
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    // base64-encoded UTF-8 Chinese strings (keeps this source file pure ASCII)
    private const string TITLE_B64 = "RGVlcFNlZWsgSGFybmVzcyDlkK/liqjlmag=";
    private const string MSG_NODSH_B64 = "5pyq5qOA5rWL5YiwIERlZXBTZWVrIEhhcm5lc3PvvIhkc2jvvInnjq/looPjgIINCg0K5piv5ZCm56uL5Y2z6Ieq5Yqo5LiL6L295bm25a6J6KOF77yfDQoNCuWwhuaJp+ihjO+8mm5wbSBpbnN0YWxsIC1nIEBkZWVwc2Vlay1haS9kc2gNCu+8iOmcgOimgeW3suWuieijhSBOb2RlLmpzIOS4jiBucG3vvIk=";
    // splash window texts (Chinese, base64 keeps this source file pure ASCII)
    private const string SPLASH_START_B64 = "5q2j5Zyo5ZCv5YqoIERTSC4uLg==";
    private const string SPLASH_READY_B64 = "5bey5ZCv5Yqo77yM5q2j5Zyo5omT5byA6aG16Z2iLi4u";
    private const string SPLASH_FAIL_B64 = "5ZCv5Yqo5aSx6LSl77yM6K+35p+l55yL5pel5b+X77yM5oiW5pS555So5Y+z6ZSu6I+c5Y2V55qE44CM5L+d5oyBY21k56qX5Y+j6L+Q6KGM44CN";

    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const uint MIB_TCP_STATE_LISTEN = 2;
    private const uint MIB_TCP_STATE_ESTAB = 5;
    private const int MONITOR_POLL_MS = 1000;
    private const int MONITOR_STARTUP_WAIT_SECONDS = 60;

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint state;
        public uint localAddr;
        public int localPort;
        public uint remoteAddr;
        public int remotePort;
        public int owningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--monitor", StringComparison.OrdinalIgnoreCase))
        {
            return RunMonitor(args);
        }

        bool visible = false;
        if (args.Length > 0)
        {
            string a = args[0].Trim().ToLowerInvariant();
            visible = (a == "--visible" || a == "visible" || a == "/visible");
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string cmdPath = Path.Combine(baseDir, "start-dsh.cmd");

        if (!File.Exists(cmdPath))
        {
            MessageBox.Show(
                "Launcher script not found:" + Environment.NewLine + cmdPath,
                "DeepSeek Harness Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }

        // --- environment check: is dsh available? ---
        if (FindDsh() == null)
        {
            DialogResult r = MessageBox.Show(
                Decode(MSG_NODSH_B64),
                Decode(TITLE_B64),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (r != DialogResult.Yes) return 0;   // user refused -> exit quietly
            return RunInstall();                   // user agreed -> open cmd + install
        }

        return Launch(cmdPath, baseDir, visible);
    }

    private static int Launch(string cmdPath, string baseDir, bool visible)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/d /c \"\"" + cmdPath + "\" " + (visible ? "visible" : "hidden") + "\"",
            WorkingDirectory = baseDir,
            UseShellExecute = false,
            CreateNoWindow = !visible,
            WindowStyle = visible ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden
        };
        try
        {
            Process batch = Process.Start(psi);
            // Normal (console-less) launch: show a small progress splash until
            // the web UI is listening. --visible keeps the classic cmd window.
            if (!visible && (Environment.GetEnvironmentVariable("DSH_NO_SPLASH") ?? string.Empty) != "1")
            {
                ShowSplash(batch, baseDir);
            }
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Failed to start launcher:" + Environment.NewLine + ex.Message,
                "DeepSeek Harness Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    // ------------------------------------------------------------------
    // Startup splash for the normal, console-less launch: a small
    // semi-transparent window that mirrors logs\progress.txt (written by
    // start-dsh.cmd) and closes itself once the web UI is listening.
    // ------------------------------------------------------------------
    private static void ShowSplash(Process batch, string baseDir)
    {
        int port = 3080;
        string portEnv = Environment.GetEnvironmentVariable("DSH_PORT");
        if (!string.IsNullOrWhiteSpace(portEnv))
        {
            int parsed;
            if (int.TryParse(portEnv.Trim(), out parsed)) port = parsed;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (SplashForm form = new SplashForm(batch, port, Path.Combine(baseDir, "logs")))
        {
            Application.Run(form);
        }
    }

    private sealed class SplashForm : Form
    {
        private const double TargetOpacity = 0.92;

        private readonly Process _batch;
        private readonly int _port;
        private readonly string _progressPath;
        private readonly Label _statusLabel;
        private readonly TextBox _logBox;
        private readonly ProgressBar _bar;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly DateTime _started = DateTime.Now;
        private long _readPos;
        private bool _finishing;

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public SplashForm(Process batch, int port, string logDir)
        {
            _batch = batch;
            _port = port;
            _progressPath = Path.Combine(logDir, "progress.txt");

            Text = "DeepSeek Harness";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 292);
            BackColor = Color.FromArgb(30, 30, 34);
            DoubleBuffered = true;
            Opacity = 0.05;

            Label title = new Label();
            title.Text = "DeepSeek Harness";
            title.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(245, 245, 250);
            title.SetBounds(20, 14, 380, 30);
            Controls.Add(title);

            Label close = new Label();
            close.Text = "\u00d7";
            close.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            close.ForeColor = Color.FromArgb(150, 150, 158);
            close.TextAlign = ContentAlignment.MiddleCenter;
            close.SetBounds(430, 12, 20, 22);
            close.Cursor = Cursors.Hand;
            close.Click += delegate { Close(); };
            Controls.Add(close);

            _statusLabel = new Label();
            _statusLabel.Text = Decode(SPLASH_START_B64);
            _statusLabel.Font = new Font("Microsoft YaHei UI", 9F);
            _statusLabel.ForeColor = Color.FromArgb(180, 180, 190);
            _statusLabel.AutoEllipsis = true;
            _statusLabel.SetBounds(20, 50, 420, 20);
            Controls.Add(_statusLabel);

            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.BackColor = Color.FromArgb(20, 20, 24);
            _logBox.ForeColor = Color.FromArgb(150, 200, 160);
            _logBox.BorderStyle = BorderStyle.None;
            _logBox.Font = new Font("Consolas", 8.25F);
            _logBox.SetBounds(20, 76, 420, 178);
            Controls.Add(_logBox);

            _bar = new ProgressBar();
            _bar.Style = ProgressBarStyle.Marquee;
            _bar.MarqueeAnimationSpeed = 30;
            _bar.SetBounds(20, 262, 420, 12);
            Controls.Add(_bar);

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 250;
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (Opacity < TargetOpacity) Opacity = Math.Min(TargetOpacity, Opacity + 0.12);

            ReadProgress();
            if (_finishing) return;

            if (GetListenerPid(_port) > 0)
            {
                _finishing = true;
                _statusLabel.Text = Decode(SPLASH_READY_B64);
                _bar.Style = ProgressBarStyle.Continuous;
                _bar.Value = 100;
                _timer.Stop();
                System.Windows.Forms.Timer closer = new System.Windows.Forms.Timer();
                closer.Interval = 650;
                closer.Tick += delegate { closer.Stop(); closer.Dispose(); Close(); };
                closer.Start();
                return;
            }

            // The launcher cmd window already exited without the port opening:
            // startup failed. Keep the window so the user can read the log.
            if (_batch != null && _batch.HasExited && (DateTime.Now - _started).TotalSeconds > 2)
            {
                _finishing = true;
                _statusLabel.Text = Decode(SPLASH_FAIL_B64);
                _statusLabel.ForeColor = Color.FromArgb(240, 160, 120);
                _bar.Style = ProgressBarStyle.Continuous;
                _bar.Value = 0;
                _timer.Stop();
            }
        }

        private void ReadProgress()
        {
            try
            {
                if (!File.Exists(_progressPath)) return;
                using (FileStream fs = new FileStream(_progressPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length <= _readPos) return;
                    fs.Seek(_readPos, SeekOrigin.Begin);
                    using (StreamReader reader = new StreamReader(fs, Encoding.UTF8))
                    {
                        string text = reader.ReadToEnd();
                        _readPos = fs.Position;
                        if (text.Length > 0) _logBox.AppendText(text);
                    }
                }
            }
            catch { /* the file may be replaced mid-write */ }
        }
    }

    // Open a visible cmd window and run the global install, kept open so the
    // user can read the result.
    private static int RunInstall()
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/k npm install -g @deepseek-ai/dsh",
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseShellExecute = false,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal
        };
        try
        {
            Process.Start(psi);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Failed to start installer:" + Environment.NewLine + ex.Message,
                "DeepSeek Harness Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    // ------------------------------------------------------------------
    // Generic dsh lookup: PATH -> `npm prefix -g` -> %APPDATA%\npm
    // ------------------------------------------------------------------
    private static string FindDsh()
    {
        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string dir in pathEnv.Split(';'))
        {
            string d = dir.Trim().Trim('"');
            if (d.Length == 0) continue;
            try
            {
                string cand = Path.Combine(d, "dsh.cmd");
                if (File.Exists(cand)) return cand;
            }
            catch { /* ignore invalid PATH entries */ }
        }

        try
        {
            string prefix = RunNpmPrefix();
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                string cand = Path.Combine(prefix.Trim(), "dsh.cmd");
                if (File.Exists(cand)) return cand;
            }
        }
        catch { /* npm unavailable or query failed */ }

        string npmDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
        string p = Path.Combine(npmDefault, "dsh.cmd");
        if (File.Exists(p)) return p;

        return null;
    }

    private static string RunNpmPrefix()
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/d /c npm prefix -g",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        using (Process proc = Process.Start(psi))
        {
            string output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(10000)) { try { proc.Kill(); } catch { } }
            return output.Trim();
        }
    }

    // ------------------------------------------------------------------
    // --monitor: stop dsh when the web UI has no open connections for a
    // grace period (i.e. the user closed the browser tab).
    // ------------------------------------------------------------------
    private static int RunMonitor(string[] args)
    {
        int port = 3080;
        int graceSeconds = 1;
        if (args.Length > 1) int.TryParse(args[1], out port);
        if (args.Length > 2) int.TryParse(args[2], out graceSeconds);
        if (graceSeconds < 1) graceSeconds = 1;

        // phase 1: wait for dsh to start listening (spawned before/around dsh)
        int listenerPid = 0;
        int startupPolls = MONITOR_STARTUP_WAIT_SECONDS * 1000 / MONITOR_POLL_MS;
        for (int i = 0; i < startupPolls && listenerPid <= 0; i++)
        {
            Thread.Sleep(MONITOR_POLL_MS);
            listenerPid = GetListenerPid(port);
        }
        if (listenerPid <= 0) return 0;   // dsh never came up

        // Only the instance this monitor was created for is managed. If the
        // listener later changes (the server was restarted or replaced), this
        // monitor exits instead of stopping somebody else's server.
        int monitoredPid = listenerPid;

        // phase 2: kill when the page is closed (idle for the grace period)
        bool seenAnyConnection = false;
        int idleMs = 0;
        while (true)
        {
            Thread.Sleep(MONITOR_POLL_MS);

            int currentPid = GetListenerPid(port);
            if (currentPid <= 0) return 0;             // dsh already stopped
            if (currentPid != monitoredPid) return 0;  // a different instance took over

            int estab = CountEstablished(port);
            if (estab > 0)
            {
                seenAnyConnection = true;              // someone is using the page
                idleMs = 0;
            }
            else if (seenAnyConnection)
            {
                idleMs += MONITOR_POLL_MS;
                if (idleMs >= graceSeconds * 1000)
                {
                    // close the tab -> stop dsh (only if the listener is node.exe)
                    if (IsNodeProcess(currentPid)) KillProcess(currentPid);
                    return 0;
                }
            }
        }
    }

    private static MibTcpRowOwnerPid[] GetTcpRows()
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return new MibTcpRowOwnerPid[0];
            int count = Marshal.ReadInt32(buf);
            MibTcpRowOwnerPid[] rows = new MibTcpRowOwnerPid[count];
            IntPtr p = IntPtr.Add(buf, 4);
            for (int i = 0; i < count; i++)
            {
                rows[i] = (MibTcpRowOwnerPid)Marshal.PtrToStructure(p, typeof(MibTcpRowOwnerPid));
                p = IntPtr.Add(p, Marshal.SizeOf(typeof(MibTcpRowOwnerPid)));
            }
            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static ushort NetOrder(int port)
    {
        return (ushort)(((port & 0xFF) << 8) | ((port >> 8) & 0xFF));
    }

    private static int GetListenerPid(int port)
    {
        ushort nport = NetOrder(port);
        foreach (MibTcpRowOwnerPid row in GetTcpRows())
        {
            if (row.state == MIB_TCP_STATE_LISTEN && row.localPort == nport)
                return row.owningPid;
        }
        return 0;
    }

    private static int CountEstablished(int port)
    {
        ushort nport = NetOrder(port);
        int n = 0;
        foreach (MibTcpRowOwnerPid row in GetTcpRows())
        {
            if (row.state == MIB_TCP_STATE_ESTAB &&
                (row.localPort == nport || row.remotePort == nport))
                n++;
        }
        return n;
    }

    private static bool IsNodeProcess(int pid)
    {
        try
        {
            return Process.GetProcessById(pid).ProcessName.Equals("node", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void KillProcess(int pid)
    {
        try { Process.GetProcessById(pid).Kill(); } catch { /* already gone */ }
    }

    private static string Decode(string b64)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
    }
}
