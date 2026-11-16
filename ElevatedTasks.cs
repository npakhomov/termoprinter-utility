using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace ThermoPrinterTool;

internal static class ElevatedTasks
{
    private static readonly string[] Roles = ["Принтер упаковки", "Принтер сроков годности", "Принтер напитков"];

    public static bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    internal static bool ValidArguments(string[] args) => args switch
    {
        ["--install-driver", "dd340" or "bp41"] => true,
        ["--configure-printer", var driver, var role, var ip] =>
            driver.Length is > 0 and <= 255 && !driver.Any(char.IsControl) && Roles.Contains(role) &&
            IPAddress.TryParse(ip, out var address) && address.AddressFamily == AddressFamily.InterNetwork &&
            address.GetAddressBytes()[0] is > 0 and < 224 && !IPAddress.IsLoopback(address),
        _ => false
    };

    public static int Handle(string[] args)
    {
        if (!ValidArguments(args) || !IsAdministrator)
            return 2;
        try
        {
            if (args[0] == "--install-driver")
                DriverInstallation.Install(args[1]);
            else
                ConfigurePrinter(args[1], args[2], args[3]);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Операция с принтером не выполнена", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    public static void Run(params string[] arguments)
    {
        if (!ValidArguments(arguments))
            throw new ArgumentException("Недопустимая операция администратора.");
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            // ArgumentList is escaped by .NET for ShellExecute.
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить операцию администратора.");
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Операция администратора завершилась с кодом {process.ExitCode}.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Запрос прав администратора отменен. Изменения не применены.", ex);
        }
    }

    public static void ConfigurePrinter(string driver, string role, string ip)
    {
        if (!ValidArguments(["--configure-printer", driver, role, ip]))
            throw new ArgumentException("Некорректные параметры принтера.");
        var port = "IP_" + IPAddress.Parse(ip);
        PowerShell($$"""
            Get-PrinterDriver -Name '{{Escape(driver)}}' -ErrorAction Stop | Out-Null
            $port = Get-PrinterPort -Name '{{port}}' -ErrorAction SilentlyContinue
            if ($null -eq $port) {
                Add-PrinterPort -Name '{{port}}' -PrinterHostAddress '{{IPAddress.Parse(ip)}}'
            } elseif ($port.PrinterHostAddress -ne '{{IPAddress.Parse(ip)}}') {
                throw 'Существующий TCP/IP-порт указывает на другой адрес.'
            }
            $printer = Get-Printer -Name '{{Escape(role)}}' -ErrorAction SilentlyContinue
            if ($null -eq $printer) {
                $onPort = Get-Printer | Where-Object { $_.PortName -eq '{{port}}' } | Select-Object -First 1
                if ($null -ne $onPort) {
                    Rename-Printer -Name $onPort.Name -NewName '{{Escape(role)}}'
                    Set-Printer -Name '{{Escape(role)}}' -DriverName '{{Escape(driver)}}' -PortName '{{port}}'
                } else {
                    Add-Printer -Name '{{Escape(role)}}' -DriverName '{{Escape(driver)}}' -PortName '{{port}}'
                }
            } else {
                Set-Printer -Name '{{Escape(role)}}' -DriverName '{{Escape(driver)}}' -PortName '{{port}}'
            }
            """);
    }

    internal static string PowerShell(string command)
    {
        var script = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop'; try { " + command +
            " } catch { [Console]::Error.WriteLine($_.Exception.Message); " +
            "if ($_.CategoryInfo.Category -eq 'PermissionDenied' -or $_.Exception.HResult -eq -2147024891) { exit 5 }; exit 1 }";
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        start.Environment.Remove("PSModulePath");
        using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить PowerShell.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Операция печати не завершилась за 60 секунд.");
        }
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        if (process.ExitCode == 5)
            throw new UnauthorizedAccessException(error.Result.Trim());
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error.Result.Trim());
        return output.Result;
    }

    internal static string Escape(string value) => value.Replace("'", "''");

    internal static string CreatePrivateDirectory()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ThermoPrinterTool-Support-" + Guid.NewGuid().ToString("N"));
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.SetOwner(administrators);
        foreach (var sid in new[] { administrators, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(root).Create(security);
        return root;
    }
}
