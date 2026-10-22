using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using TokenMojo.Core;

namespace TokenMojo;

internal sealed class WslDistros : IWslDistros
{
    // wsl.exe can stall while the WSL service starts or updates; a missed listing is retried next minute.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public IReadOnlyList<string> Running()
    {
        ProcessStartInfo startInfo = new("wsl.exe", "--list --running --quiet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            // wsl.exe writes UTF-16LE.
            StandardOutputEncoding = Encoding.Unicode,
        };
        try
        {
            using Process process = Process.Start(startInfo)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill(entireProcessTree: true);
                Trace.TraceWarning("TokenMojo: wsl.exe did not answer within {0}", Timeout);
                return [];
            }
            // Non-zero when no distro is running.
            return process.ExitCode == 0
                ? output.Result.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];
        }
        catch (Win32Exception)
        {
            // WSL is not installed.
            return [];
        }
    }
}
