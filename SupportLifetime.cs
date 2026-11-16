using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace ThermoPrinterTool;

internal static class SupportLifetime
{
    public static bool Restarting { get; set; }

    internal static void CommitRestart(Process updater, Action closeApplication, Func<bool> closed)
    {
        Restarting = true;
        try
        {
            closeApplication();
            if (!closed())
                throw new InvalidOperationException("Закрытие приложения отменено. Обновление отложено.");
        }
        catch
        {
            Restarting = false;
            if (!updater.HasExited) { updater.Kill(); updater.WaitForExit(); }
            throw;
        }
    }

    internal static bool ShouldDelete(string executable, bool isSingleFile)
    {
        if (!isSingleFile || !File.Exists(executable))
            return false;
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(executable)!); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, "ThermoPrinterTool.csproj")))
                return false;
        }
        return true;
    }

    public static bool WillDeleteOnExit => Environment.ProcessPath is { } executable &&
        ShouldDelete(executable, string.IsNullOrEmpty(Assembly.GetExecutingAssembly().Location));

    public static void ScheduleDeletion()
    {
        if (Restarting || !WillDeleteOnExit)
            return;
        var executable = Environment.ProcessPath!;
        using var stream = File.OpenRead(executable);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        using var cleanup = StartHiddenPowerShell(BuildCleanupScript(Environment.ProcessId, executable, hash));
    }

    internal static string BuildCleanupScript(int processId, string executable, string hash) => $$"""
        $ErrorActionPreference = 'Stop'
        try {
            $running = [Diagnostics.Process]::GetProcessById({{processId}})
            if (-not $running.WaitForExit(120000)) { exit 1 }
            $running.Dispose()
        } catch [ArgumentException] { }
        $target = '{{executable.Replace("'", "''")}}'
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            if (-not [IO.File]::Exists($target)) { exit 0 }
            try {
                $stream = [IO.File]::OpenRead($target)
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
                finally { $stream.Dispose(); $sha.Dispose() }
                if ($actual -ne '{{hash}}') { exit 0 }
                [IO.File]::Delete($target)
                exit 0
            } catch { $lastError = $_.Exception.Message; [Threading.Thread]::Sleep(500) }
        }
        [Console]::Error.WriteLine($lastError)
        exit 1
        """;

    internal static Process StartHiddenPowerShell(string script, bool captureErrors = false)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = captureErrors
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        start.Environment.Remove("PSModulePath");
        return Process.Start(start) ?? throw new IOException("Не удалось запустить завершающую операцию.");
    }
}
