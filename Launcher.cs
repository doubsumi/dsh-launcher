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
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
    private const string SPLASH_FAIL_B64 = "5ZCv5Yqo5aSx6LSl77yM6K+35p+l55yL5pel5b+X";

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
    // Startup splash for the normal, console-less launch: a fully
    // transparent layered window showing the DSH icon, the latest launcher
    // line and a rounded progress bar; it closes itself once the web UI is
    // listening. Data comes from logs\progress.txt (written by start-dsh.cmd).
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
        using (SplashForm form = new SplashForm(batch, port, baseDir))
        {
            Application.Run(form);
        }
    }

    private sealed class SplashForm : Form
    {
        private const int WindowWidth = 420;
        private const int WindowHeight = 320;
        private const int IconSide = 200;
        private const int BarWidth = 300;
        private const int BarHeight = 10;
        private const int BarTop = 286;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int ULW_ALPHA = 2;
        private const int AC_SRC_OVER = 0;
        private const int AC_SRC_ALPHA = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct PointI
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SizeI
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PointI pptDst, ref SizeI psize,
            IntPtr hdcSrc, ref PointI pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private readonly Process _batch;
        private readonly int _port;
        private readonly string _progressPath;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly DateTime _started = DateTime.Now;
        private readonly Image _icon;
        private readonly Font _lineFont;
        private readonly PointI _screenPos;
        private long _readPos;
        private string _line;
        private bool _failed;
        private bool _fadingOut;
        private DateTime _closeAt = DateTime.MinValue;
        private double _progress;
        private double _target;
        private double _alpha;
        private int _ticks;

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED;
                return cp;
            }
        }

        public SplashForm(Process batch, int port, string baseDir)
        {
            _batch = batch;
            _port = port;
            _progressPath = Path.Combine(baseDir, "logs", "progress.txt");
            _line = Decode(SPLASH_START_B64);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            ClientSize = new Size(WindowWidth, WindowHeight);

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            _screenPos.X = wa.Left + (wa.Width - WindowWidth) / 2;
            _screenPos.Y = wa.Top + (int)(wa.Height * 0.30);
            Location = new Point(_screenPos.X, _screenPos.Y);

            _icon = LoadSplashIcon(baseDir);
            _lineFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            MouseClick += delegate { Close(); };

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 50;
            _timer.Tick += OnTick;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Render();
            _timer.Start();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            /* the whole surface lives in the layered window bitmap */
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            /* the whole surface lives in the layered window bitmap */
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null) _timer.Dispose();
                if (_icon != null) _icon.Dispose();
                if (_lineFont != null) _lineFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private static Image LoadSplashIcon(string baseDir)
        {
            try
            {
                string icoPath = Path.Combine(baseDir, "assets", "deepseek-whale.ico");
                if (File.Exists(icoPath))
                {
                    using (Icon ico = new Icon(icoPath, new Size(256, 256)))
                    {
                        return ico.ToBitmap();
                    }
                }
            }
            catch { }
            try
            {
                using (Icon ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                {
                    return ico.ToBitmap();
                }
            }
            catch { }
            return null;
        }

        private void OnTick(object sender, EventArgs e)
        {
            _ticks++;
            if (_ticks % 4 == 0) ReadProgress();

            if (_fadingOut)
            {
                _alpha -= 0.09;
                if (_alpha <= 0.02)
                {
                    _timer.Stop();
                    Render();
                    Close();
                    return;
                }
            }
            else if (_alpha < 1.0)
            {
                _alpha = Math.Min(1.0, _alpha + 0.10);
            }

            if (!_failed && !_fadingOut)
            {
                if (GetListenerPid(_port) > 0)
                {
                    _line = Decode(SPLASH_READY_B64);
                    _target = 1.0;
                    if (_closeAt == DateTime.MinValue) _closeAt = DateTime.Now.AddMilliseconds(750);
                }
                else if (_batch != null && _batch.HasExited && (DateTime.Now - _started).TotalSeconds > 2)
                {
                    _failed = true;
                    _line = Decode(SPLASH_FAIL_B64);
                    _target = 0;
                }
            }
            if (_closeAt != DateTime.MinValue && DateTime.Now >= _closeAt) _fadingOut = true;

            if (_progress < _target) _progress = Math.Min(_target, _progress + 0.03);
            else if (_progress > _target) _progress = Math.Max(_target, _progress - 0.03);

            Render();
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
                        string[] lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < lines.Length; i++) ApplyLine(lines[i]);
                    }
                }
            }
            catch { /* the file may be rewritten mid-read */ }
        }

        private void ApplyLine(string raw)
        {
            string s = raw.Trim();
            if (s.Length == 0) return;
            if (s.StartsWith("["))
            {
                int close = s.IndexOf(']');
                if (close > 0) s = s.Substring(close + 1).Trim();
            }
            if (s.StartsWith("launcher:")) s = s.Substring(9).Trim();
            if (s.Length == 0) return;
            if (s.Length > 40) s = s.Substring(0, 39) + "...";
            _line = s;

            if (s.IndexOf("FAILED", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _failed = true;
                _target = 0;
            }
            else if (s.IndexOf("is up", StringComparison.OrdinalIgnoreCase) >= 0) _target = 1.0;
            else if (s.IndexOf("opening the browser", StringComparison.OrdinalIgnoreCase) >= 0) _target = 1.0;
            else if (s.IndexOf("already running", StringComparison.OrdinalIgnoreCase) >= 0) _target = 0.9;
            else if (s.IndexOf("starting dsh", StringComparison.OrdinalIgnoreCase) >= 0) _target = 0.35;
            else if (s.IndexOf("dsh=", StringComparison.OrdinalIgnoreCase) >= 0) _target = 0.2;
            else if (s.IndexOf("starting DSH", StringComparison.OrdinalIgnoreCase) >= 0) _target = 0.08;
            else if (s.IndexOf("waiting for port", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int open = s.IndexOf('(');
                int secs = 0;
                if (open > 0)
                {
                    string num = s.Substring(open + 1).Trim().TrimEnd(')').TrimEnd('s');
                    int.TryParse(num, out secs);
                }
                _target = Math.Min(0.9, 0.35 + secs * 0.02);
            }
        }

        private void Render()
        {
            using (Bitmap bmp = new Bitmap(WindowWidth, WindowHeight, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                    if (_icon != null)
                    {
                        g.DrawImage(_icon, new Rectangle((WindowWidth - IconSide) / 2, 8, IconSide, IconSide));
                    }

                    SizeF measured = g.MeasureString(_line, _lineFont);
                    float textX = (WindowWidth - measured.Width) / 2f;
                    float textY = 226f;
                    using (SolidBrush shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                    using (SolidBrush brush = new SolidBrush(_failed ? Color.FromArgb(250, 255, 150, 110) : Color.FromArgb(250, 245, 245, 250)))
                    {
                        g.DrawString(_line, _lineFont, shadow, textX + 1.4f, textY + 1.4f);
                        g.DrawString(_line, _lineFont, brush, textX, textY);
                    }

                    int barLeft = (WindowWidth - BarWidth) / 2;
                    using (GraphicsPath track = RoundedRect(barLeft, BarTop, BarWidth, BarHeight, BarHeight / 2f))
                    using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                    {
                        g.FillPath(trackBrush, track);
                    }

                    float fillWidth = (float)(BarWidth * _progress);
                    if (fillWidth > BarHeight)
                    {
                        using (GraphicsPath fill = RoundedRect(barLeft, BarTop, fillWidth, BarHeight, BarHeight / 2f))
                        using (SolidBrush fillBrush = new SolidBrush(_failed ? Color.FromArgb(240, 210, 90, 60) : Color.FromArgb(240, 38, 38, 44)))
                        {
                            g.FillPath(fillBrush, fill);
                        }
                    }
                }
                UpdateLayeredWindowSurface(bmp);
            }
        }

        private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            GraphicsPath path = new GraphicsPath();
            if (r > 0)
            {
                r = Math.Min(r, Math.Min(w, h) / 2f);
                path.AddArc(x, y, r * 2, r * 2, 180, 90);
                path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
                path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
                path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
                path.CloseFigure();
            }
            else
            {
                path.AddRectangle(new RectangleF(x, y, w, h));
            }
            return path;
        }

        private void UpdateLayeredWindowSurface(Bitmap bmp)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memDc, hBitmap);

                SizeI size = new SizeI();
                size.cx = bmp.Width;
                size.cy = bmp.Height;
                PointI src = new PointI();
                PointI dst = new PointI();
                dst.X = _screenPos.X;
                dst.Y = _screenPos.Y;
                BlendFunction blend = new BlendFunction();
                blend.BlendOp = AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(_alpha * 255)));
                blend.AlphaFormat = AC_SRC_ALPHA;

                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
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
