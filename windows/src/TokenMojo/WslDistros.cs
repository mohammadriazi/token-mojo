using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using TokenMojo.Core;

namespace TokenMojo;

internal sealed class WslDistros : IWslDistros
{
    public IReadOnlyList<string> Running()
    {
        ProcessStartInfo startInfo = new("wsl.exe", "--list --running --quiet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // wsl.exe writes UTF-16LE.
            StandardOutputEncoding = Encoding.Unicode,
        };
        try
        {
            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            // Non-zero when no distro is running.
            return process.ExitCode == 0
                ? output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];
        }
        catch (Win32Exception)
        {
            // WSL is not installed.
            return [];
        }
    }
}
