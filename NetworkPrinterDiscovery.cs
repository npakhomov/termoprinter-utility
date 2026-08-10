using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace ThermoPrinterTool;

internal static class NetworkPrinterDiscovery
{
    public static IReadOnlyList<NetworkPrinterInfo> GetInstalledTcpIpPrinters()
    {
        try
        {
            return GetInstalledTcpIpPrintersFromRegistry();
        }
        catch (Exception registryException)
        {
            try
            {
                return GetInstalledTcpIpPrintersFromPowerShell();
            }
            catch (Exception powerShellException)
            {
                throw new InvalidOperationException(
                    $"Windows не разрешила получить список сетевых принтеров. " +
                    $"Реестр: {registryException.Message} PowerShell: {powerShellException.Message}");
            }
        }
    }

    private static IReadOnlyList<NetworkPrinterInfo> GetInstalledTcpIpPrintersFromRegistry()
    {
        const string portsPath = @"SYSTEM\CurrentControlSet\Control\Print\Monitors\Standard TCP/IP Port\Ports";
        const string printersPath = @"SYSTEM\CurrentControlSet\Control\Print\Printers";

        using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var portsKey = localMachine.OpenSubKey(portsPath, false) ??
            throw new InvalidOperationException("раздел TCP/IP-портов не найден.");
        using var printersKey = localMachine.OpenSubKey(printersPath, false) ??
            throw new InvalidOperationException("раздел принтеров не найден.");

        var ports = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var portName in portsKey.GetSubKeyNames())
        {
            using var portKey = portsKey.OpenSubKey(portName, false);
            var hostAddress = portKey?.GetValue("HostName") as string;
            if (string.IsNullOrWhiteSpace(hostAddress))
                hostAddress = portKey?.GetValue("IPAddress") as string;
            if (!string.IsNullOrWhiteSpace(hostAddress))
                ports[portName] = hostAddress.Trim();
        }

        var result = new List<NetworkPrinterInfo>();
        foreach (var printerName in printersKey.GetSubKeyNames())
        {
            using var printerKey = printersKey.OpenSubKey(printerName, false);
            var configuredPorts = (printerKey?.GetValue("Port") as string ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var portName in configuredPorts)
            {
                if (ports.TryGetValue(portName, out var hostAddress))
                {
                    var driverName = printerKey?.GetValue("Printer Driver") as string ?? string.Empty;
                    result.Add(new NetworkPrinterInfo(printerName, portName, hostAddress, driverName));
                }
            }
        }

        return Normalize(result);
    }

    private static IReadOnlyList<NetworkPrinterInfo> GetInstalledTcpIpPrintersFromPowerShell()
    {
        const string command = """
            $ports = @{}
            Get-CimInstance Win32_TCPIPPrinterPort -ErrorAction Stop | ForEach-Object {
                $ports[[string]$_.Name] = [string]$_.HostAddress
            }
            $printers = @(Get-CimInstance Win32_Printer -ErrorAction Stop | ForEach-Object {
                $portName = [string]$_.PortName
                if ($ports.ContainsKey($portName)) {
                    [PSCustomObject]@{
                        Name = [string]$_.Name
                        PortName = $portName
                        HostAddress = [string]$ports[$portName]
                        DriverName = [string]$_.DriverName
                    }
                }
            })
            ConvertTo-Json -InputObject @($printers) -Compress
            """;

        var json = RunPowerShell(command);
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<NetworkPrinterInfo>();

        var printers = JsonSerializer.Deserialize<List<NetworkPrinterInfo>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        return Normalize(printers);
    }

    private static IReadOnlyList<NetworkPrinterInfo> Normalize(IEnumerable<NetworkPrinterInfo> printers)
    {
        return printers
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) &&
                !string.IsNullOrWhiteSpace(x.HostAddress))
            .DistinctBy(x => $"{x.Name}\u001f{x.HostAddress}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string RunPowerShell(string command)
    {
        var utf8Command = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
            "$OutputEncoding = [System.Text.Encoding]::UTF8; " + command;
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(utf8Command));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand " + encodedCommand,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Не удалось запустить PowerShell.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000))
        {
            process.Kill(true);
            throw new TimeoutException("Windows слишком долго формирует список принтеров.");
        }

        Task.WhenAll(outputTask, errorTask).GetAwaiter().GetResult();
        var output = outputTask.Result;
        var error = errorTask.Result;

        if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
        {
            var cleanError = CleanPowerShellOutput(error).Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(cleanError)
                ? $"PowerShell завершился с кодом {process.ExitCode}."
                : cleanError);
        }

        return CleanPowerShellOutput(output).Trim();
    }

    private static string CleanPowerShellOutput(string text)
    {
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line =>
            {
                var trimmed = line.TrimStart();
                return !trimmed.StartsWith("#< CLIXML", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("<Objs", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("<Obj ", StringComparison.OrdinalIgnoreCase);
            });
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record NetworkPrinterInfo(string Name, string PortName, string HostAddress, string DriverName);
