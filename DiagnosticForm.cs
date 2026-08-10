using System.Diagnostics;
using System.Net.NetworkInformation;

namespace ThermoPrinterTool;

internal sealed class DiagnosticForm : Form
{
    private static readonly Color AccentColor = Color.FromArgb(31, 111, 235);
    private static readonly Color GoodColor = Color.FromArgb(20, 108, 67);
    private static readonly Color BadColor = Color.FromArgb(176, 42, 55);
    private static readonly Color NeutralColor = Color.FromArgb(82, 88, 96);

    private readonly Label _summaryLabel = new();
    private readonly ListView _printerList = new();
    private readonly RichTextBox _logBox = new();
    private readonly Button _checkButton = new();
    private readonly Button _restartDodoButton = new();
    private readonly Button _resetIpButton = new();
    private readonly Button _windowsSettingsButton = new();
    private readonly Button _backButton = new();
    private bool _isChecking;
    private bool _isRestartingDodo;
    private bool _isResettingIp;

    private bool IsBusy => _isChecking || _isRestartingDodo || _isResettingIp;

    public DiagnosticForm()
    {
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Text = "Диагностика проблем с подключенным принтером";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(800, 665);
        MinimumSize = new Size(720, 615);
        BackColor = Color.FromArgb(244, 246, 249);
        Font = new Font("Segoe UI", 9F);

        BuildInterface();
        Shown += async (_, _) => await CheckPrintersAsync();
        FormClosing += DiagnosticForm_FormClosing;
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 57));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "Диагностика принтеров",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 18F),
            ForeColor = Color.FromArgb(24, 31, 42),
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);

        ConfigureSummaryLabel();
        root.Controls.Add(_summaryLabel, 0, 1);

        _printerList.Dock = DockStyle.Fill;
        _printerList.View = View.Details;
        _printerList.FullRowSelect = true;
        _printerList.GridLines = false;
        _printerList.HideSelection = false;
        _printerList.BorderStyle = BorderStyle.FixedSingle;
        _printerList.BackColor = Color.White;
        _printerList.Columns.Add("Принтер", 235);
        _printerList.Columns.Add("Модель", 135);
        _printerList.Columns.Add("IP-адрес", 125);
        _printerList.Columns.Add("Результат", 210);
        _printerList.SelectedIndexChanged += (_, _) =>
        {
            var hasSelection = !IsBusy && _printerList.SelectedItems.Count > 0;
            _resetIpButton.Enabled = hasSelection;
            _windowsSettingsButton.Enabled = hasSelection;
        };
        root.Controls.Add(CreateGroup("Сетевые принтеры Windows", _printerList), 0, 2);

        _logBox.Dock = DockStyle.Fill;
        _logBox.ReadOnly = true;
        _logBox.BorderStyle = BorderStyle.None;
        _logBox.BackColor = Color.White;
        _logBox.Font = new Font("Consolas", 9F);
        root.Controls.Add(CreateGroup("Ход проверки", _logBox), 0, 3);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 4,
            Margin = new Padding(0, 12, 0, 0)
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(buttons, 0, 4);

        ConfigureButton(_restartDodoButton, "Перезапустить Dodo Label Printer", AccentColor, Color.White);
        _restartDodoButton.Margin = new Padding(0, 0, 6, 8);
        _restartDodoButton.Click += async (_, _) => await RestartDodoLabelPrinterAsync();
        buttons.Controls.Add(_restartDodoButton, 0, 0);
        buttons.SetColumnSpan(_restartDodoButton, 2);

        ConfigureButton(_resetIpButton, "Настроить IP заново", AccentColor, Color.White);
        _resetIpButton.Margin = new Padding(0, 0, 6, 8);
        _resetIpButton.Enabled = false;
        _resetIpButton.Click += async (_, _) => await ResetPrinterIpAsync();
        buttons.Controls.Add(_resetIpButton, 0, 1);
        buttons.SetColumnSpan(_resetIpButton, 2);

        ConfigureButton(_windowsSettingsButton, "Настройка принтера в Windows", AccentColor, Color.White);
        _windowsSettingsButton.Margin = new Padding(0, 0, 6, 8);
        _windowsSettingsButton.Enabled = false;
        _windowsSettingsButton.Click += (_, _) => OpenSelectedPrinterSettings();
        buttons.Controls.Add(_windowsSettingsButton, 0, 2);
        buttons.SetColumnSpan(_windowsSettingsButton, 2);

        ConfigureButton(_checkButton, "Проверить повторно", AccentColor, Color.White);
        _checkButton.Click += async (_, _) => await CheckPrintersAsync();
        buttons.Controls.Add(_checkButton, 0, 3);

        ConfigureButton(_backButton, "Назад", Color.FromArgb(238, 241, 245), Color.FromArgb(35, 45, 60));
        _backButton.FlatAppearance.BorderColor = Color.FromArgb(205, 213, 223);
        _backButton.FlatAppearance.BorderSize = 1;
        _backButton.Click += (_, _) => Close();
        buttons.Controls.Add(_backButton, 1, 3);
    }

    private void DiagnosticForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!IsBusy)
            return;

        e.Cancel = true;
        Log("Дождитесь завершения текущей операции.", Color.FromArgb(143, 92, 0));
    }

    private async Task RestartDodoLabelPrinterAsync()
    {
        if (IsBusy)
            return;

        _isRestartingDodo = true;
        SetOperationControlsEnabled(false);
        Log("Перезапускаю Dodo Label Printer...");

        try
        {
            var result = await Task.Run(RestartDodoLabelPrinter);
            Log($"Dodo Label Printer перезапущен. Завершено процессов: {result.StoppedProcesses}.", GoodColor);
        }
        catch (Exception ex)
        {
            Log("Не удалось перезапустить Dodo Label Printer: " + ex.Message, BadColor);
        }
        finally
        {
            _isRestartingDodo = false;
            SetOperationControlsEnabled(true);
        }
    }

    private async Task ResetPrinterIpAsync()
    {
        if (IsBusy || _printerList.SelectedItems.Count == 0 ||
            _printerList.SelectedItems[0].Tag is not PrinterDiagnosticResult selectedResult)
        {
            return;
        }

        var detectedModel = DetectPrinterModel(selectedResult.Printer);
        var isDd340 = detectedModel.Equals("ATOL DD340", StringComparison.OrdinalIgnoreCase);
        var isBp41 = detectedModel.Equals("ATOL BP41", StringComparison.OrdinalIgnoreCase);
        if (!isDd340 && !isBp41)
        {
            Log(
                $"Для принтера «{selectedResult.Printer.Name}» определена модель «{detectedModel}». " +
                "Автоматический запуск утилиты повторной настройки доступен для ATOL DD340 и ATOL BP41.",
                Color.FromArgb(143, 92, 0));
            return;
        }

        _isResettingIp = true;
        SetOperationControlsEnabled(false);
        try
        {
            var resolvedAddress = await ResolveIpv4AddressAsync(selectedResult.Printer.HostAddress);
            if (resolvedAddress is null)
                throw new InvalidOperationException("не удалось определить прежний IPv4-адрес принтера.");

            var printerBytes = System.Net.IPAddress.Parse(resolvedAddress).GetAddressBytes();
            var localNetwork = GetLocalIpv4Networks().FirstOrDefault(network =>
                IsSameNetwork(printerBytes, network.Address, network.Mask));
            if (localNetwork is null)
                throw new InvalidOperationException("прежний IP принтера находится не в текущей сети компьютера.");
            if (string.IsNullOrWhiteSpace(localNetwork.Gateway))
                throw new InvalidOperationException("у активной сети компьютера не найден основной шлюз.");

            Log($"Повторная настройка {detectedModel}. Использую прежний IP: {resolvedAddress}, " +
                $"маска: {localNetwork.MaskText}, шлюз: {localNetwork.Gateway}.");
            Log("Подключите настраиваемый принтер к компьютеру по USB.");

            if (isDd340)
            {
                Log("Открываю HPRT Printer Utility...");
                var utilityPath = await Task.Run(() => Dd340UtilityService.GetOrDownload(message => Log(message)));
                await Task.Run(() => HprtUtilityAutomation.OpenAndPrepare(
                    utilityPath,
                    resolvedAddress,
                    localNetwork.MaskText,
                    localNetwork.Gateway,
                    true,
                    message => Log(message)));
            }
            else
            {
                Log("Открываю RTLabelPrinterTool для ATOL BP41. Укажите в утилите показанные IP, маску и шлюз.");
                var utilityPath = await Task.Run(() => Bp41UtilityService.GetOrDownload(message => Log(message)));
                Bp41UtilityService.Run(utilityPath);
            }
        }
        catch (Exception ex)
        {
            Log("Не удалось подготовить повторную настройку IP: " + ex.Message, BadColor);
        }
        finally
        {
            _isResettingIp = false;
            SetOperationControlsEnabled(true);
        }
    }

    private void OpenSelectedPrinterSettings()
    {
        if (IsBusy || _printerList.SelectedItems.Count == 0 ||
            _printerList.SelectedItems[0].Tag is not PrinterDiagnosticResult selectedResult)
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo("rundll32.exe")
            {
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add("printui.dll,PrintUIEntry");
            startInfo.ArgumentList.Add("/e");
            startInfo.ArgumentList.Add("/n");
            startInfo.ArgumentList.Add(selectedResult.Printer.Name);
            _ = Process.Start(startInfo) ??
                throw new InvalidOperationException("Windows не смогла открыть настройки печати.");

            Log($"Открываю настройки печати Windows для принтера: {selectedResult.Printer.Name}.");
        }
        catch (Exception ex)
        {
            Log("Не удалось открыть настройки принтера Windows: " + ex.Message, BadColor);
        }
    }

    private static DodoRestartResult RestartDodoLabelPrinter()
    {
        const string processName = "Dodo.LabelPrinterDesktop.Windows";
        var executablePath = FindDodoLabelPrinterExecutable();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new FileNotFoundException(
                "программа не установлена. Установите Dodo Label Printer и повторите попытку.");
        }

        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var processes = Process.GetProcessesByName(processName)
            .Where(process => IsCurrentSessionProcess(process, currentSessionId))
            .ToArray();
        var stoppedProcesses = 0;
        var stopErrors = new List<string>();
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (process.HasExited)
                        continue;

                    process.Kill();
                    if (!process.WaitForExit(5000))
                    {
                        stopErrors.Add($"процесс {process.Id} не завершился за 5 секунд");
                        continue;
                    }
                    stoppedProcesses++;
                }
                catch (InvalidOperationException)
                {
                    // Another Dodo process may already have terminated this child process.
                }
                catch (Exception ex)
                {
                    stopErrors.Add($"процесс {process.Id}: {ex.Message}");
                }
            }
        }

        if (stopErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "не все старые процессы удалось завершить; новый экземпляр не запущен: " +
                string.Join("; ", stopErrors));
        }

        Thread.Sleep(700);
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Windows не смогла запустить программу.");

        var isRunning = WaitForDodoProcess(processName, currentSessionId, TimeSpan.FromSeconds(8));
        if (!isRunning)
            throw new InvalidOperationException("Windows запустила программу, но её процессы не появились за 8 секунд.");

        return new DodoRestartResult(stoppedProcesses);
    }

    private static bool WaitForDodoProcess(string processName, int sessionId, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                if (processes.Any(process => IsCurrentSessionProcess(process, sessionId)))
                    return true;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }

            Thread.Sleep(250);
        }

        return false;
    }

    private static bool IsCurrentSessionProcess(Process process, int sessionId)
    {
        try
        {
            return process.SessionId == sessionId;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindDodoLabelPrinterExecutable()
    {
        const string executableName = "Dodo.LabelPrinterDesktop.Windows.exe";
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Dodo Label Printer", executableName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Dodo Label Printer", executableName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Dodo Label Printer", executableName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private void SetOperationControlsEnabled(bool enabled)
    {
        if (IsDisposed || Disposing)
            return;

        _checkButton.Enabled = enabled;
        _restartDodoButton.Enabled = enabled;
        _resetIpButton.Enabled = enabled && _printerList.SelectedItems.Count > 0;
        _windowsSettingsButton.Enabled = enabled && _printerList.SelectedItems.Count > 0;
        _backButton.Enabled = enabled;
    }

    private void ConfigureSummaryLabel()
    {
        _summaryLabel.Text = "Подготовка к проверке";
        _summaryLabel.Dock = DockStyle.Fill;
        _summaryLabel.Height = 38;
        _summaryLabel.AutoSize = false;
        _summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        _summaryLabel.Padding = new Padding(12, 0, 12, 0);
        _summaryLabel.Font = new Font("Segoe UI Semibold", 9.5F);
        _summaryLabel.ForeColor = NeutralColor;
        _summaryLabel.BackColor = Color.FromArgb(238, 241, 245);
        _summaryLabel.Margin = new Padding(0, 0, 0, 10);
    }

    private static GroupBox CreateGroup(string title, Control child)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Font = new Font("Segoe UI Semibold", 9.5F),
            Margin = new Padding(0, 0, 0, 8)
        };
        child.Font = new Font("Segoe UI", child is RichTextBox ? 9F : 8.8F);
        group.Controls.Add(child);
        return group;
    }

    private static void ConfigureButton(Button button, string text, Color backColor, Color foreColor)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Height = 36;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.Font = new Font("Segoe UI Semibold", 9F);
        button.Margin = new Padding(0, 0, 6, 0);
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.BorderSize = 0;
    }

    private async Task CheckPrintersAsync()
    {
        if (IsBusy)
            return;

        _isChecking = true;
        SetOperationControlsEnabled(false);
        _printerList.Items.Clear();
        _logBox.Clear();
        SetSummary("Ищу сетевые принтеры Windows...", DiagnosticStatus.Neutral);
        Log("Получаю список принтеров с TCP/IP-портами...");

        try
        {
            var printers = await Task.Run(NetworkPrinterDiscovery.GetInstalledTcpIpPrinters);
            if (printers.Count == 0)
            {
                SetSummary("Сетевые принтеры не найдены", DiagnosticStatus.Warning);
                Log("В Windows не найдено принтеров, подключенных через TCP/IP-порт.");
                return;
            }

            Log($"Найдено сетевых принтеров: {printers.Count}.");
            var localNetworks = GetLocalIpv4Networks();
            var checks = printers.Select(printer => CheckPrinterAsync(printer, localNetworks)).ToArray();
            var results = await Task.WhenAll(checks);

            foreach (var result in results.OrderBy(x => x.Printer.Name, StringComparer.CurrentCultureIgnoreCase))
                AddResult(result);

            var preferredItem = _printerList.Items.Cast<ListViewItem>()
                .FirstOrDefault(item => item.Tag is PrinterDiagnosticResult result && !result.IsReachable) ??
                _printerList.Items.Cast<ListViewItem>().FirstOrDefault();
            if (preferredItem is not null)
            {
                preferredItem.Selected = true;
                preferredItem.Focused = true;
            }

            var connectedCount = results.Count(x => x.IsReachable);
            var differentNetworkCount = results.Count(x => x.IsSameSubnet == false);
            var unknownNetworkCount = results.Count(x => x.IsSameSubnet is null);
            var summaryStatus = differentNetworkCount > 0 || unknownNetworkCount > 0
                ? DiagnosticStatus.Warning
                : connectedCount == printers.Count
                ? DiagnosticStatus.Good
                : connectedCount == 0
                    ? DiagnosticStatus.Bad
                    : DiagnosticStatus.Warning;
            var networkSummary = differentNetworkCount > 0
                ? $". В другой сети: {differentNetworkCount}"
                : string.Empty;
            var unknownSummary = unknownNetworkCount > 0
                ? $". Сеть не определена: {unknownNetworkCount}"
                : string.Empty;
            SetSummary(
                $"Проверено: {printers.Count}. Отвечает на ping: {connectedCount}{networkSummary}{unknownSummary}",
                summaryStatus);
        }
        catch (Exception ex)
        {
            SetSummary("Не удалось выполнить диагностику", DiagnosticStatus.Bad);
            Log("Ошибка: " + ex.Message);
        }
        finally
        {
            _isChecking = false;
            SetOperationControlsEnabled(true);
        }
    }

    private async Task<PrinterDiagnosticResult> CheckPrinterAsync(
        NetworkPrinterInfo printer,
        IReadOnlyList<LocalIpv4Network> localNetworks)
    {
        Log($"Проверяю {printer.Name}, IP {printer.HostAddress}...");
        var resolvedAddress = await ResolveIpv4AddressAsync(printer.HostAddress);
        var isSameSubnet = resolvedAddress is null
            ? null
            : IsInSameSubnet(resolvedAddress, localNetworks);
        if (isSameSubnet != true)
            return new PrinterDiagnosticResult(printer, false, null, isSameSubnet);

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(resolvedAddress!, 1800);
            return new PrinterDiagnosticResult(
                printer,
                reply.Status == IPStatus.Success,
                reply.RoundtripTime,
                isSameSubnet);
        }
        catch
        {
            return new PrinterDiagnosticResult(printer, false, null, isSameSubnet);
        }
    }

    private void AddResult(PrinterDiagnosticResult result)
    {
        var resultText = result.IsSameSubnet is null
            ? "Сеть не определена"
            : result.IsSameSubnet == false ? "Разные сети"
            : result.IsReachable ? "Подключен в сети" : "Не обнаружен в сети";
        var item = new ListViewItem(result.Printer.Name);
        item.SubItems.Add(DetectPrinterModel(result.Printer));
        item.SubItems.Add(result.Printer.HostAddress);
        item.SubItems.Add(resultText);
        item.Tag = result;
        item.ForeColor = result.IsSameSubnet != true
            ? Color.FromArgb(143, 92, 0)
            : result.IsReachable ? GoodColor : BadColor;
        _printerList.Items.Add(item);

        if (result.IsSameSubnet is null)
        {
            Log(
                $"Не удалось определить IPv4-сеть принтера «{result.Printer.Name}» ({result.Printer.HostAddress}). " +
                "PING НЕ ВЫПОЛНЯЛСЯ. Проверьте IP-адрес принтера и сетевое подключение компьютера.",
                Color.FromArgb(143, 92, 0));
            return;
        }

        if (result.IsSameSubnet == false)
        {
            Log(
                $"ВНИМАНИЕ: КОМПЬЮТЕР И ПРИНТЕР «{result.Printer.Name}» ПОДКЛЮЧЕНЫ К РАЗНЫМ " +
                $"ЛОКАЛЬНЫМ СЕТЯМ. PING НЕ ВЫПОЛНЯЛСЯ. IP ПРИНТЕРА: {result.Printer.HostAddress}. " +
                "ПРОВЕРЬТЕ СЕТЕВОЕ ПОДКЛЮЧЕНИЕ.",
                Color.FromArgb(143, 92, 0));
            return;
        }

        if (result.IsReachable)
        {
            Log($"{result.Printer.Name}: Принтер подключен в сети ({result.RoundtripTime} мс).", GoodColor);
        }
        else
        {
            Log(
                $"{result.Printer.Name}: компьютер и принтер находятся в одной сети, но принтер не отвечает на ping. " +
                "Возможные причины: принтер не подключен к сети либо на принтере сбросился IP-адрес. " +
                "Проверьте сетевой кабель и при необходимости настройте IP-адрес принтера заново.",
                BadColor);
        }
    }

    private static IReadOnlyList<LocalIpv4Network> GetLocalIpv4Networks()
    {
        var result = new List<LocalIpv4Network>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var properties = networkInterface.GetIPProperties();
            var gateway = properties.GatewayAddresses
                .FirstOrDefault(item => item.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?.Address.ToString() ?? string.Empty;

            foreach (var address in properties.UnicastAddresses)
            {
                if (address.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
                    address.IPv4Mask is null ||
                    System.Net.IPAddress.IsLoopback(address.Address) ||
                    address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add(new LocalIpv4Network(
                    address.Address.GetAddressBytes(),
                    address.IPv4Mask.GetAddressBytes(),
                    address.IPv4Mask.ToString(),
                    gateway));
            }
        }

        return result;
    }

    private static string DetectPrinterModel(NetworkPrinterInfo printer)
    {
        var identity = $"{printer.Name} {printer.DriverName}";
        if (identity.Contains("HPRT HD200", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("ATOL DD340", StringComparison.OrdinalIgnoreCase))
        {
            return "ATOL DD340";
        }

        if (identity.Contains("ATOL BP41", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("Rongta RP4xx", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("Rongta RP4", StringComparison.OrdinalIgnoreCase))
            return "ATOL BP41";
        if (identity.Contains("TSC DA220", StringComparison.OrdinalIgnoreCase))
            return "TSC DA220";
        if (identity.Contains("BIXOLON XD3", StringComparison.OrdinalIgnoreCase))
            return "BIXOLON XD3-40d";

        return "Не определена";
    }

    private static bool? IsInSameSubnet(string printerAddress, IReadOnlyList<LocalIpv4Network> localNetworks)
    {
        if (!System.Net.IPAddress.TryParse(printerAddress, out var printerIp) ||
            printerIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            localNetworks.Count == 0)
        {
            return null;
        }

        var printerBytes = printerIp.GetAddressBytes();
        return localNetworks.Any(network => IsSameNetwork(printerBytes, network.Address, network.Mask));
    }

    private static async Task<string?> ResolveIpv4AddressAsync(string hostAddress)
    {
        if (System.Net.IPAddress.TryParse(hostAddress, out var parsedAddress))
        {
            return parsedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ? parsedAddress.ToString()
                : null;
        }

        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(hostAddress)
                .WaitAsync(TimeSpan.FromSeconds(3));
            return addresses.FirstOrDefault(address =>
                address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSameNetwork(byte[] firstAddress, byte[] secondAddress, byte[] mask)
    {
        if (firstAddress.Length != 4 || secondAddress.Length != 4 || mask.Length != 4)
            return false;

        for (var index = 0; index < 4; index++)
        {
            if ((firstAddress[index] & mask[index]) != (secondAddress[index] & mask[index]))
                return false;
        }

        return true;
    }

    private void SetSummary(string text, DiagnosticStatus status)
    {
        _summaryLabel.Text = text;
        _summaryLabel.ForeColor = status switch
        {
            DiagnosticStatus.Good => GoodColor,
            DiagnosticStatus.Warning => Color.FromArgb(143, 92, 0),
            DiagnosticStatus.Bad => BadColor,
            _ => NeutralColor
        };
        _summaryLabel.BackColor = status switch
        {
            DiagnosticStatus.Good => Color.FromArgb(218, 246, 226),
            DiagnosticStatus.Warning => Color.FromArgb(255, 243, 205),
            DiagnosticStatus.Bad => Color.FromArgb(255, 232, 235),
            _ => Color.FromArgb(238, 241, 245)
        };
    }

    private void Log(string message, Color? color = null)
    {
        if (InvokeRequired)
        {
            Invoke(() => Log(message, color));
            return;
        }

        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.SelectionLength = 0;
        _logBox.SelectionColor = color ?? Color.FromArgb(35, 45, 60);
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _logBox.SelectionColor = _logBox.ForeColor;
        _logBox.ScrollToCaret();
    }

    private sealed record PrinterDiagnosticResult(
        NetworkPrinterInfo Printer,
        bool IsReachable,
        long? RoundtripTime,
        bool? IsSameSubnet);

    private sealed record LocalIpv4Network(byte[] Address, byte[] Mask, string MaskText, string Gateway);

    private sealed record DodoRestartResult(int StoppedProcesses);

    private enum DiagnosticStatus
    {
        Neutral,
        Good,
        Warning,
        Bad
    }
}
