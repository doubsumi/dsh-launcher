// DeepSeek Harness Launcher - a tiny native launcher (.NET Framework 4.x).
// Doubles as the desktop shortcut target so the shortcut is a normal program.
//   (no args)           -> start DSH in a HIDDEN cmd window (log to file)
//   --visible / visible -> start DSH in a VISIBLE cmd window (kept open)
//
// Environment check: if dsh is not installed, ask (Windows popup) whether to
// install it via npm. Refuse -> exit. Agree -> open a cmd window and run the
// install command. It shells out to start-dsh.cmd (same folder) for launching.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal static class Program
{
    // base64-encoded UTF-8 Chinese strings (keeps this source file pure ASCII)
    private const string TITLE_B64 = "RGVlcFNlZWsgSGFybmVzcyDlkK/liqjlmag=";
    private const string MSG_NODSH_B64 = "5pyq5qOA5rWL5YiwIERlZXBTZWVrIEhhcm5lc3PvvIhkc2jvvInnjq/looPjgIINCg0K5piv5ZCm56uL5Y2z6Ieq5Yqo5LiL6L295bm25a6J6KOF77yfDQoNCuWwhuaJp+ihjO+8mm5wbSBpbnN0YWxsIC1nIEBkZWVwc2Vlay1haS9kc2gNCu+8iOmcgOimgeW3suWuieijhSBOb2RlLmpzIOS4jiBucG3vvIk=";

    [STAThread]
    private static int Main(string[] args)
    {
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

    // Locate the dsh launcher command (npm global bin on Windows).
    private static string FindDsh()
    {
        string npmDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
        string p = Path.Combine(npmDir, "dsh.cmd");
        if (File.Exists(p)) return p;

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
        return null;
    }

    private static string Decode(string b64)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
    }
}
