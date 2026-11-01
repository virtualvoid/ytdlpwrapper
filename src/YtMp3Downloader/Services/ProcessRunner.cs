using System.Diagnostics;
using System.Text;

namespace YtMp3Downloader.Services;

public static class ProcessRunner
{
    /// <summary>
    /// Spustí proces a po riadkoch posiela stdout/stderr do <paramref name="onLine"/> (druhý parameter = je to stderr).
    /// Pri zrušení ukončí celý strom procesov a vyhodí <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<int> RunAsync(
        string exe,
        IEnumerable<string> args,
        Action<string, bool> onLine,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data, false); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data, true); };

        ct.ThrowIfCancellationRequested();
        p.Start();
        p.StandardInput.Close();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var reg = ct.Register(() =>
        {
            try { if (!p.HasExited) p.Kill(true); } catch { /* proces už skončil */ }
        });

        await p.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        return p.ExitCode;
    }
}
