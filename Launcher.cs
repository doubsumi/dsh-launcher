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
            Process.Start(psi);
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

        // phase 2: kill when the page is closed (idle for the grace period)
        bool seenAnyConnection = false;
        int idleMs = 0;
        while (true)
        {
            Thread.Sleep(MONITOR_POLL_MS);

            listenerPid = GetListenerPid(port);
            if (listenerPid <= 0) return 0;            // dsh already stopped

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
                    if (IsNodeProcess(listenerPid)) KillProcess(listenerPid);
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
