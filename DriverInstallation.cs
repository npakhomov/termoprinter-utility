using System.Diagnostics;

namespace ThermoPrinterTool;

internal static class DriverInstallation
{
    public static string DriverName(string model) => model switch
    {
        "dd340" => "HPRT HD200 - ZPL",
        "bp41" => "Rongta RP4xx Series",
        _ => throw new ArgumentException("Модель не поддерживает установку драйвера.")
    };

    public static void Install(string model)
    {
        if (!ElevatedTasks.IsAdministrator)
            throw new UnauthorizedAccessException("Для установки драйвера нужны права администратора.");
        var driver = DriverName(model);
        var root = ElevatedTasks.CreatePrivateDirectory();
        var temporaryPrinter = "ThermoPrinterTool Driver Setup " + Guid.NewGuid().ToString("N");
        try
        {
            CompleteInstallation(() =>
            {
                // Verify again inside an administrator-only directory, including over-the-shoulder UAC.
                var directory = TrustedPackages.Prepare(model == "dd340" ? TrustedPackages.Dd340Driver : TrustedPackages.Bp41Driver,
                    _ => { }, root);
                if (model == "dd340")
                {
                    var installer = TrustedPackages.FindRequiredFile(directory, "HPRT_2024.3_M-0.exe");
                    var extracted = Path.Combine(directory, "Seagull");
                    Directory.CreateDirectory(extracted);
                    Run(installer, ["/x", extracted]);
                    var wizard = TrustedPackages.FindRequiredFile(extracted, "DriverWizard.exe");
                    Run(wizard, ["install", "/name:" + temporaryPrinter, "/model:" + driver, "/port:LPT1:"]);
                }
                else
                {
                    var installer = TrustedPackages.FindRequiredFile(directory, "PrnInst.exe");
                    var inf = TrustedPackages.FindRequiredFile(directory, "RONGTAOEM.inf");
                    Run(installer, ["/DIR=" + Path.GetDirectoryName(inf), "/NOLICENCE",
                        "/INSTPRN=" + driver + "#LPT1:#" + temporaryPrinter]);
                }
                ElevatedTasks.PowerShell("Get-PrinterDriver -Name '" + ElevatedTasks.Escape(driver) + "' -ErrorAction Stop | Out-Null");
            }, () =>
            {
                try { ElevatedTasks.PowerShell("Remove-Printer -Name '" + temporaryPrinter + "' -ErrorAction SilentlyContinue"); }
                finally { Directory.Delete(root, recursive: true); }
            });
        }
        catch (InstallerStillRunningException ex)
        {
            // The vendor process may still use this directory or its temporary queue.
            throw new InstallerStillRunningException(ex.Message + "\nФайлы сохранены: " + root +
                "\nВременная очередь: " + temporaryPrinter +
                "\nНе повторяйте установку до завершения текущей. Очистку выполните после её завершения.", ex);
        }
    }

    internal static void CompleteInstallation(Action install, Action cleanup)
    {
        var cleanupAllowed = true;
        Exception? installationError = null;
        try { install(); }
        catch (InstallerStillRunningException)
        {
            cleanupAllowed = false;
            throw;
        }
        catch (Exception ex)
        {
            installationError = ex;
            throw;
        }
        finally
        {
            if (cleanupAllowed)
            {
                try
                {
                    cleanup();
                }
                catch when (installationError is not null) { /* Preserve the installation failure. */ }
            }
        }
    }

    internal static void Run(string executable, string[] arguments, int timeoutMilliseconds = 600000)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить установщик драйвера.");
        if (!process.WaitForExit(timeoutMilliseconds))
            throw new InstallerStillRunningException($"Установщик не завершился вовремя (PID {process.Id}). " +
                "Установка может продолжаться в Windows; процесс не прерывался, его файлы не удалены.");
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Установщик драйвера завершился с кодом {process.ExitCode}.");
    }

    internal sealed class InstallerStillRunningException(string message, Exception? inner = null) : TimeoutException(message, inner);
}
