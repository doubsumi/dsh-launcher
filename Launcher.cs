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
using System.Globalization;
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
    // Official DSH whale logo as an SVG path (viewBox 0 0 50 50, 4 subpaths,
    // cubic Bezier only) - rendered as a vector path so it stays crisp at any size.
    private const string WHALE_PATH =
        "M48.8354 10.0479C48.3232 9.79199 48.1025 10.2798 47.8032 10.5278C47.7007 10.6079 47.6143 10.7119 47.5273 10.8076C46.7793 11.624 45.9048 12.1597 44.7622 12.0957C43.0923 12 41.666 12.5356 40.4058 13.8398C40.1377 12.2319 39.2476 11.272 37.8926 10.6558C37.1836 10.3359 36.4668 10.0156 35.9702 9.31982C35.6235 8.82373 35.5293 8.27197 35.356 7.72754C35.2456 7.3999 35.1353 7.06396 34.76" +
        "51 7.00781C34.3633 6.94385 34.2056 7.2876 34.0479 7.57568C33.418 8.75195 33.1733 10.0479 33.1973 11.3599C33.2524 14.312 34.4736 16.6641 36.8999 18.3359C37.1758 18.5278 37.2466 18.7197 37.1597 19C36.9946 19.5757 36.7974 20.1357 36.624 20.7119C36.5137 21.0801 36.3486 21.1597 35.9624 21C34.6309 20.4321 33.481 19.5918 32.4644 18.5757C30.7393 16.8721 29.1792 14.9917 27.2334 13.52C26" +
        ".7764 13.1758 26.3193 12.856 25.8467 12.5518C23.8618 10.584 26.1069 8.96777 26.627 8.77588C27.1704 8.57568 26.8159 7.8877 25.0591 7.896C23.3022 7.90381 21.6953 8.50391 19.647 9.30371C19.3477 9.42383 19.0322 9.51172 18.7095 9.58398C16.8501 9.22363 14.9199 9.14355 12.9033 9.37598C9.10596 9.80762 6.07275 11.6396 3.84326 14.7681C1.16455 18.5278 0.53418 22.7998 1.30664 27.2559C2.117" +
        "68 31.9521 4.46582 35.8398 8.07373 38.8799C11.8159 42.0322 16.1255 43.5762 21.041 43.2803C24.0269 43.104 27.3516 42.6963 31.1016 39.4561C32.0469 39.936 33.0396 40.1279 34.686 40.272C35.9546 40.3921 37.1758 40.208 38.1211 40.0078C39.6021 39.688 39.4995 38.2881 38.9639 38.0322C34.623 35.9678 35.5762 36.8081 34.71 36.1279C36.9155 33.4639 40.2402 30.6958 41.54 21.728C41.6426 21.016" +
        "1 41.5557 20.5679 41.54 19.9917C41.5322 19.6396 41.6108 19.5039 42.0049 19.4639C43.0923 19.3359 44.1479 19.0317 45.1167 18.4878C47.9292 16.9199 49.064 14.3438 49.3315 11.2559C49.3711 10.7837 49.3237 10.2959 48.8354 10.0479ZM24.3262 37.8398C20.1196 34.4639 18.0791 33.3521 17.2358 33.3999C16.4482 33.4482 16.5898 34.3682 16.7632 34.9678C16.9443 35.5601 17.1812 35.9683 17.5117 36.4" +
        "878C17.7402 36.832 17.8979 37.3442 17.2832 37.728C15.9282 38.584 13.5728 37.4399 13.4624 37.3838C10.7207 35.7358 8.42822 33.5601 6.81348 30.584C5.25342 27.7197 4.34766 24.6479 4.19775 21.3677C4.1582 20.5757 4.38672 20.2959 5.15869 20.1519C6.17529 19.96 7.22314 19.9199 8.23926 20.0718C12.5327 20.7119 16.1885 22.6719 19.2529 25.7759C21.002 27.5439 22.3252 29.6558 23.6885 31.7202C" +
        "25.1377 33.9121 26.6978 36 28.6831 37.7119C29.3843 38.312 29.9434 38.7681 30.479 39.104C28.8643 39.2881 26.1699 39.3281 24.3262 37.8398ZM26.3433 24.6001C26.3433 24.248 26.6191 23.9678 26.9658 23.9678C27.0444 23.9678 27.1152 23.9839 27.1782 24.0078C27.2651 24.04 27.3438 24.0879 27.4067 24.1602C27.5171 24.272 27.5801 24.4321 27.5801 24.6001C27.5801 24.9521 27.3042 25.2319 26.9575" +
        " 25.2319C26.6108 25.2319 26.3433 24.9521 26.3433 24.6001ZM32.6064 27.8799C32.2046 28.0479 31.8027 28.1919 31.4165 28.208C30.8179 28.2397 30.1641 27.9922 29.8096 27.688C29.2583 27.2158 28.8643 26.9521 28.6987 26.1279C28.6279 25.7759 28.6675 25.2319 28.7305 24.9199C28.8721 24.248 28.7144 23.8159 28.2495 23.4238C27.8716 23.104 27.3911 23.0161 26.8633 23.0161C26.666 23.0161 26.4849" +
        " 22.9277 26.3511 22.856C26.1304 22.7441 25.9492 22.4639 26.1226 22.1201C26.1777 22.0078 26.4458 21.7358 26.5088 21.688C27.2256 21.272 28.0527 21.4077 28.8169 21.7197C29.5259 22.0161 30.0615 22.5601 30.834 23.3281C31.6216 24.2559 31.7632 24.5117 32.2124 25.208C32.5669 25.752 32.8901 26.312 33.1104 26.9521C33.2446 27.3521 33.0713 27.6802 32.6064 27.8799Z";

    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const uint MIB_TCP_STATE_LISTEN = 2;
    private const uint MIB_TCP_STATE_ESTAB = 5;
    private const int MONITOR_POLL_MS = 250;
    private const int MONITOR_STARTUP_WAIT_SECONDS = 300;

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
        private const int WindowHeight = 230;
        private const int IconSide = 150;
        private const int BarWidth = 300;
        private const int BarHeight = 10;
        private const int BarTop = 204;
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
        private readonly GraphicsPath _whale;
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

            _whale = BuildWhalePath((WindowWidth - IconSide) / 2f, 8f, IconSide);
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
                if (_whale != null) _whale.Dispose();
                if (_lineFont != null) _lineFont.Dispose();
            }
            base.Dispose(disposing);
        }

        // Render the official 50x50 SVG logo path as a GDI+ vector path: the
        // outline stays crisp at any size (no bitmap upscaling).
        private static GraphicsPath BuildWhalePath(float left, float top, float size)
        {
            GraphicsPath path = new GraphicsPath(FillMode.Alternate);
            float scale = size / 50f;
            string d = WHALE_PATH;
            int i = 0;
            char cmd = 'M';
            float cx = 0f, cy = 0f, sx = 0f, sy = 0f;
            while (i < d.Length)
            {
                char c = d[i];
                if (c == 'Z' || c == 'z')
                {
                    path.CloseFigure();
                    cx = sx;
                    cy = sy;
                    cmd = 'M';
                    i++;
                    continue;
                }
                if (char.IsLetter(c))
                {
                    cmd = c;
                    i++;
                    continue;
                }
                if (char.IsWhiteSpace(c) || c == ',')
                {
                    i++;
                    continue;
                }
                if (cmd == 'M')
                {
                    float x = ReadNumber(d, ref i);
                    float y = ReadNumber(d, ref i);
                    cx = x;
                    cy = y;
                    sx = x;
                    sy = y;
                    path.StartFigure();
                    cmd = 'L';   // implicit lineto for any following coordinate pairs
                }
                else if (cmd == 'C')
                {
                    float x1 = ReadNumber(d, ref i);
                    float y1 = ReadNumber(d, ref i);
                    float x2 = ReadNumber(d, ref i);
                    float y2 = ReadNumber(d, ref i);
                    float x = ReadNumber(d, ref i);
                    float y = ReadNumber(d, ref i);
                    path.AddBezier(left + cx * scale, top + cy * scale,
                        left + x1 * scale, top + y1 * scale,
                        left + x2 * scale, top + y2 * scale,
                        left + x * scale, top + y * scale);
                    cx = x;
                    cy = y;
                }
                else if (cmd == 'L')
                {
                    float x = ReadNumber(d, ref i);
                    float y = ReadNumber(d, ref i);
                    path.AddLine(left + cx * scale, top + cy * scale, left + x * scale, top + y * scale);
                    cx = x;
                    cy = y;
                }
                else
                {
                    i++;
                }
            }
            return path;
        }

        private static float ReadNumber(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == ',' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (i == start)
            {
                i++;
                return 0f;
            }
            float value;
            float.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return value;
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

                    if (_whale != null)
                    {
                        using (SolidBrush whaleBrush = new SolidBrush(Color.FromArgb(250, 12, 12, 16)))
                        {
                            g.FillPath(whaleBrush, _whale);
                        }
                    }

                    SizeF measured = g.MeasureString(_line, _lineFont);
                    float textX = (WindowWidth - measured.Width) / 2f;
                    float textY = 172f;
                    using (SolidBrush shadow = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                    using (SolidBrush brush = new SolidBrush(_failed ? Color.FromArgb(250, 200, 60, 40) : Color.FromArgb(250, 18, 18, 22)))
                    {
                        g.DrawString(_line, _lineFont, shadow, textX + 1.3f, textY + 1.3f);
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
                        using (SolidBrush fillBrush = new SolidBrush(Color.FromArgb(240, 38, 38, 44)))
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
        int graceSeconds = 3;
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
