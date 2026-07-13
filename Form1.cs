using System.Diagnostics;
using System.Drawing.Printing;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization;

namespace ThermoPrinterTool;

public partial class Form1 : Form
{
    private const string GitHubOwner = "npakhomov";
    private const string GitHubRepository = "termoprinter-utility";
    private const string ReleaseAssetName = "ThermoPrinterTool.exe";

    private readonly ComboBox _modelBox = new();
    private readonly ComboBox _printerRoleBox = new();
    private readonly ComboBox _ethernetDhcpBox = new();
    private readonly ComboBox _printerProtocolBox = new();
    private readonly ComboBox _mediaTypeBox = new();
    private readonly TextBox _ethernetIpBox = new();
    private readonly TextBox _ethernetMaskBox = new();
    private readonly TextBox _ethernetGatewayBox = new();
    private readonly TextBox _ethernetMacBox = new();
    private readonly Label _usbStatusLabel = new();
    private readonly Label _freeIpStatusLabel = new();
    private readonly Label _networkStatusLabel = new();
    private readonly RichTextBox _logBox = new();
    private readonly ListView _networkList = new();
    private readonly ListView _printerList = new();

    private readonly Dictionary<string, PrinterModelInfo> _models = new()
    {
        ["ATOL DD340"] = new(
            "ATOL DD340",
            "https://drive.google.com/drive/folders/1foi7_d8zFb_SJ0kusThvRV0Wu9BMoIkt",
            "https://www.bartendersoftware.com/resources/printer-drivers/atol",
            "IP устройства обычно назначается через фирменную утилиту или веб-интерфейс принтера.",
            "197KUpI5jUbNWduTJHPkl0adJsrpUXmF4",
            "Driver_ATOL DD340.zip"),
        ["ATOL BP41"] = new(
            "ATOL BP41",
            "https://portkkm.ru/support/atol-bp41/programs/",
            "https://portkkm.ru/support/atol-bp41/programs/",
            "Точную смену IP лучше делать через утилиту производителя или поставщика.",
            null,
            null),
        ["TSC DA220"] = new(
            "TSC DA220",
            "https://www.bartendersoftware.com/resources/printer-drivers/tsc/tsc-da220",
            "https://www.tscprinters.com/EN/support",
            "Настройка Ethernet зависит от комплектации принтера и сетевого модуля.",
            null,
            null),
        ["BIXOLON XD3-40d"] = new(
            "BIXOLON XD3-40d",
            "https://www.bixolon.com/download_view.php?idx=40&s_key=Driver",
            "https://www.bixolon.com/download_view.php?idx=40&s_key=Utility",
            "Для сети смотри Network Connection Manual и утилиты на странице BIXOLON.",
            null,
            null)
    };

    public Form1()
    {
        InitializeComponent();
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FitWindowToScreen();
        BuildInterface();
        RefreshNetworkInfo();
        RefreshPrinters();
        SetStatus(_usbStatusLabel, "Не проверено", StatusKind.Neutral);
        SetStatus(_freeIpStatusLabel, "Не найден", StatusKind.Neutral);
        SetStatus(_networkStatusLabel, "Не проверено", StatusKind.Neutral);
        Shown += async (_, _) => await CheckForUpdatesAsync(false);
    }

    private void FitWindowToScreen()
    {
        var area = Screen.FromControl(this).WorkingArea;
        Width = Math.Min(1040, area.Width - 80);
        Height = Math.Min(760, area.Height - 80);
        MinimumSize = new Size(Math.Min(960, Width), Math.Min(700, Height));
        CenterToScreen();
    }

    private void BuildInterface()
    {
        Text = "Пошаговое подключение термопринтера";
        BackColor = Color.FromArgb(244, 246, 249);
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 450));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var leftHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(0, 0, 8, 0)
        };
        leftHost.HorizontalScroll.Enabled = false;
        leftHost.HorizontalScroll.Visible = false;
        root.Controls.Add(leftHost, 0, 0);

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 8,
            AutoSize = true,
            Width = 430,
            Padding = new Padding(0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftHost.Controls.Add(left);

        left.Controls.Add(new Label
        {
            Text = "Подключение принтера",
            Font = new Font("Segoe UI Semibold", 16F),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        });

        left.Controls.Add(CreateSettingsPanel());
        left.Controls.Add(CreateEthernetPanel());
        left.Controls.Add(CreatePrinterSettingsPanel());
        left.Controls.Add(CreateStep1Panel());
        left.Controls.Add(CreateStep2Panel());
        left.Controls.Add(CreateStep3Panel());
        left.Controls.Add(CreateToolsPanel());

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 37));
        root.Controls.Add(right, 1, 0);

        _networkList.Dock = DockStyle.Fill;
        _networkList.View = View.Details;
        _networkList.FullRowSelect = true;
        _networkList.Columns.Add("Адаптер", 145);
        _networkList.Columns.Add("IPv4", 105);
        _networkList.Columns.Add("Маска", 105);
        _networkList.Columns.Add("Шлюз", 105);
        _networkList.Columns.Add("Статус", 70);
        right.Controls.Add(CreateGroup("Сеть компьютера", _networkList), 0, 0);

        _printerList.Dock = DockStyle.Fill;
        _printerList.View = View.Details;
        _printerList.FullRowSelect = true;
        _printerList.Columns.Add("Принтер", 150);
        _printerList.Columns.Add("Порт", 100);
        _printerList.Columns.Add("Драйвер", 175);
        _printerList.Columns.Add("Статус", 70);
        right.Controls.Add(CreateGroup("Принтеры Windows", _printerList), 0, 1);

        _logBox.Dock = DockStyle.Fill;
        _logBox.BorderStyle = BorderStyle.None;
        _logBox.BackColor = Color.White;
        _logBox.Font = new Font("Consolas", 9F);
        right.Controls.Add(CreateGroup("Лог", _logBox), 0, 2);
    }

    private Control CreateSettingsPanel()
    {
        var panel = CreatePanel();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10),
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(layout);

        _modelBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modelBox.Items.AddRange(_models.Keys.Cast<object>().ToArray());
        _modelBox.SelectedIndex = 0;
        _modelBox.SelectedIndexChanged += (_, _) => Log(_models[_modelBox.Text].Note);

        _printerRoleBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _printerRoleBox.Items.AddRange(new object[]
        {
            "Принтер упаковки",
            "Принтер сроков годности",
            "Принтер напитков"
        });
        _printerRoleBox.SelectedIndex = 0;

        AddRow(layout, 0, "Модель", _modelBox);
        AddRow(layout, 1, "Назначение", _printerRoleBox);
        return panel;
    }

    private Control CreateEthernetPanel()
    {
        var panel = CreatePanel();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(10),
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = "Настройки сети",
            Font = new Font("Segoe UI Semibold", 10F),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);

        _ethernetDhcpBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _ethernetDhcpBox.Items.AddRange(new object[] { "Выключено", "Включено" });
        _ethernetDhcpBox.SelectedIndex = 0;

        _ethernetIpBox.PlaceholderText = "192.168.1.200";
        _ethernetMaskBox.PlaceholderText = "255.255.255.0";
        _ethernetGatewayBox.PlaceholderText = "192.168.1.254";
        _ethernetMacBox.PlaceholderText = "Будет прочитан из принтера";
        _ethernetMacBox.ReadOnly = true;
        _ethernetMacBox.BackColor = Color.FromArgb(247, 248, 250);

        AddRow(layout, 1, "DHCP", _ethernetDhcpBox);
        AddRow(layout, 2, "IP-адрес", _ethernetIpBox);
        AddRow(layout, 3, "Маска подсети", _ethernetMaskBox);
        AddRow(layout, 4, "Шлюз", _ethernetGatewayBox);
        AddRow(layout, 5, "MAC-адрес", _ethernetMacBox);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 0)
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.Controls.Add(CreateCompactButton("Заполнить", FillEthernetFromCurrentNetwork), 0, 0);
        buttons.Controls.Add(CreateCompactButton("Назначить настройки", ApplyEthernetSettings), 1, 0);
        layout.Controls.Add(buttons, 1, 6);

        return panel;
    }

    private Control CreatePrinterSettingsPanel()
    {
        var panel = CreatePanel();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(10),
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = "Настройки принтера",
            Font = new Font("Segoe UI Semibold", 10F),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);

        _printerProtocolBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _printerProtocolBox.Items.AddRange(new object[]
        {
            new PrinterSettingOption("AUTO", "AUTO"),
            new PrinterSettingOption("TSPL", "TSPL"),
            new PrinterSettingOption("ZPL", "ZPL")
        });
        _printerProtocolBox.SelectedIndex = 0;

        _mediaTypeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _mediaTypeBox.Items.AddRange(new object[]
        {
            new PrinterSettingOption("Этикетка с промежутком", "gap"),
            new PrinterSettingOption("Черная метка", "mark"),
            new PrinterSettingOption("Непрерывная лента", "continuous")
        });
        _mediaTypeBox.SelectedIndex = 0;

        AddRow(layout, 1, "Протокол", _printerProtocolBox);
        AddRow(layout, 2, "Носитель", _mediaTypeBox);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 0)
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.Controls.Add(CreateCompactButton("Прочитать", ReadPrinterSettings), 0, 0);
        buttons.Controls.Add(CreateCompactButton("Назначить", ApplyPrinterSettings), 1, 0);
        layout.Controls.Add(buttons, 1, 3);

        return panel;
    }

    private Control CreateStep1Panel()
    {
        var panel = CreateStepPanel("1. Подключение принтера по USB", _usbStatusLabel);
        var buttons = GetButtonHost(panel);
        buttons.Controls.Add(CreateButton("Проверить USB подключение", CheckUsbConnection));
        return panel;
    }

    private Control CreateStep2Panel()
    {
        var panel = CreateStepPanel("2. Найти и назначить IP", _freeIpStatusLabel);
        var buttons = GetButtonHost(panel);
        buttons.Controls.Add(CreateButton("Найти свободный IP", FindFreeIp));
        return panel;
    }

    private Control CreateStep3Panel()
    {
        var panel = CreateStepPanel("3. Ping и установка драйвера", _networkStatusLabel);
        var buttons = GetButtonHost(panel);
        buttons.Controls.Add(CreateButton("Проверить ping", CheckNetworkConnection));
        buttons.Controls.Add(CreateButton("Создать TCP/IP порт", CheckNetworkAndCreatePort));
        buttons.Controls.Add(CreateButton("Скачать драйвер", DownloadDriver));
        return panel;
    }

    private Control CreateToolsPanel()
    {
        var panel = CreatePanel();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(10),
            AutoSize = true
        };
        panel.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = "Дополнительно",
            Font = new Font("Segoe UI Semibold", 10F),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        });
        layout.Controls.Add(CreateButton("Скачать Dodo Label Printer", DownloadDodoLabelPrinter));
        layout.Controls.Add(CreateButton("Проверить обновления", () => CheckForUpdatesAsync(true).GetAwaiter().GetResult()));
        layout.Controls.Add(CreateButton("Открыть настройки принтера Windows", OpenWindowsPrinterSettings));
        layout.Controls.Add(CreateButton("Обновить IP сети", RefreshNetworkInfo));
        layout.Controls.Add(CreateButton("Очистить лог", () => _logBox.Clear()));
        return panel;
    }

    private Panel CreateStepPanel(string title, Label statusLabel)
    {
        var panel = CreatePanel();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(10),
            AutoSize = true
        };
        panel.Controls.Add(layout);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
        header.Controls.Add(new Label
        {
            Text = title,
            Font = new Font("Segoe UI Semibold", 9.5F),
            AutoSize = false,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Height = 26,
            Margin = new Padding(0, 0, 10, 0)
        }, 0, 0);
        header.Controls.Add(statusLabel, 1, 0);

        statusLabel.Width = 126;
        statusLabel.Height = 24;
        statusLabel.Dock = DockStyle.Right;
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(header);

        var buttonHost = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Tag = "buttons",
            Margin = new Padding(0, 2, 0, 0)
        };
        layout.Controls.Add(buttonHost);
        return panel;
    }

    private static TableLayoutPanel GetButtonHost(Control panel)
    {
        var layout = panel.Controls.OfType<TableLayoutPanel>().First();
        return layout.Controls.OfType<TableLayoutPanel>().First(x => Equals(x.Tag, "buttons"));
    }

    private Button CreateButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 30,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, 0, 5),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(31, 111, 235),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 8.5F)
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(25, 91, 192);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 78, 166);
        button.Click += async (_, _) => await RunAction(action);
        return button;
    }

    private Button CreateCompactButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 28,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, 5, 5),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(238, 241, 245),
            ForeColor = Color.FromArgb(35, 45, 60),
            Font = new Font("Segoe UI Semibold", 8.3F)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(205, 213, 223);
        button.FlatAppearance.BorderSize = 1;
        button.Click += async (_, _) => await RunAction(action);
        return button;
    }

    private static Panel CreatePanel() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        BackColor = Color.White,
        Padding = new Padding(1),
        Margin = new Padding(0, 0, 0, 8)
    };

    private static GroupBox CreateGroup(string title, Control child)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Font = new Font("Segoe UI Semibold", 10F)
        };
        child.Font = new Font("Segoe UI", child is RichTextBox ? 9F : 8.5F);
        group.Controls.Add(child);
        return group;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        control.Dock = DockStyle.Top;
        control.Margin = new Padding(0, 0, 0, 6);
        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Margin = new Padding(0, 4, 8, 6)
        }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private async Task RunAction(Action action)
    {
        try
        {
            await Task.Run(action);
        }
        catch (Exception ex)
        {
            Log("Ошибка: " + ex.Message);
        }
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (GitHubOwner.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            if (manual)
                Log("Автообновление не настроено: укажи GitHub owner/repo в коде приложения.");
            return;
        }

        try
        {
            if (manual)
                Log("Проверяю обновления...");

            var currentVersion = GetCurrentAppVersion();
            var latestRelease = await GetLatestGitHubReleaseAsync();
            if (latestRelease is null)
            {
                if (manual)
                    Log("Обновления не найдены.");
                return;
            }

            var latestVersion = NormalizeVersion(latestRelease.TagName);
            if (!IsNewerVersion(latestVersion, currentVersion))
            {
                if (manual)
                    Log($"Установлена актуальная версия: {currentVersion}.");
                return;
            }

            var asset = latestRelease.Assets
                .FirstOrDefault(x => x.Name.Equals(ReleaseAssetName, StringComparison.OrdinalIgnoreCase));
            if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            {
                Log($"Найдена версия {latestVersion}, но файл {ReleaseAssetName} в релизе отсутствует.");
                return;
            }

            Log($"Найдена новая версия {latestVersion}. Скачиваю обновление...");
            var updatePath = await DownloadUpdateAssetAsync(asset.BrowserDownloadUrl, latestVersion);
            Log("Обновление скачано. Программа сейчас перезапустится.");
            StartSelfUpdate(updatePath);
        }
        catch (Exception ex)
        {
            if (manual)
                Log("Не удалось проверить обновления: " + ex.Message);
        }
    }

    private static string GetCurrentAppVersion()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);

        return NormalizeVersion(version ?? "0.0.0");
    }

    private static async Task<GitHubRelease?> GetLatestGitHubReleaseAsync()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ThermoPrinterTool");
        client.Timeout = TimeSpan.FromSeconds(20);

        var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepository}/releases";
        var releases = await client.GetFromJsonAsync<List<GitHubRelease>>(url);
        return releases?
            .Where(x => !x.Draft && !string.IsNullOrWhiteSpace(x.TagName))
            .OrderByDescending(x => ParseVersionForCompare(NormalizeVersion(x.TagName)))
            .FirstOrDefault();
    }

    private static async Task<string> DownloadUpdateAssetAsync(string url, string version)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ThermoPrinterTool");
        client.Timeout = TimeSpan.FromMinutes(5);

        var updateDirectory = Path.Combine(Path.GetTempPath(), "ThermoPrinterTool", "updates");
        Directory.CreateDirectory(updateDirectory);
        var updatePath = Path.Combine(updateDirectory, $"ThermoPrinterTool-{version}.exe");
        var tempPath = updatePath + ".download";

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = File.Create(tempPath))
        {
            await input.CopyToAsync(output);
        }

        if (new FileInfo(tempPath).Length == 0)
            throw new InvalidOperationException("Скачанный файл обновления пустой.");

        File.Move(tempPath, updatePath, true);
        return updatePath;
    }

    private void StartSelfUpdate(string updatePath)
    {
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
            throw new InvalidOperationException("Не удалось определить путь текущего exe.");

        var scriptPath = Path.Combine(Path.GetTempPath(), "ThermoPrinterTool", "update.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);

        var currentProcessId = Environment.ProcessId;
        var script = new StringBuilder()
            .AppendLine("$ErrorActionPreference = 'Stop'")
            .AppendLine($"$processId = {currentProcessId}")
            .AppendLine($"$source = '{EscapePowerShell(updatePath)}'")
            .AppendLine($"$target = '{EscapePowerShell(currentPath)}'")
            .AppendLine("Wait-Process -Id $processId -ErrorAction SilentlyContinue")
            .AppendLine("Start-Sleep -Milliseconds 500")
            .AppendLine("Copy-Item -LiteralPath $source -Destination $target -Force")
            .AppendLine("Start-Process -FilePath $target")
            .ToString();
        File.WriteAllText(scriptPath, script, Encoding.UTF8);

        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        BeginInvoke(() => Close());
    }

    private static string NormalizeVersion(string version)
    {
        version = version.Trim();
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            version = version[1..];

        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }

    private static bool IsNewerVersion(string candidate, string current) =>
        ParseVersionForCompare(candidate).CompareTo(ParseVersionForCompare(current)) > 0;

    private static Version ParseVersionForCompare(string version)
    {
        var numeric = version.Split('-', 2)[0];
        var parts = numeric.Split('.');
        var major = parts.Length > 0 && int.TryParse(parts[0], out var parsedMajor) ? parsedMajor : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var parsedMinor) ? parsedMinor : 0;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var parsedPatch) ? parsedPatch : 0;
        return new Version(major, minor, patch);
    }

    private void RefreshNetworkInfo()
    {
        InvokeIfNeeded(() =>
        {
            _networkList.Items.Clear();
            Log("Проверяю сетевые адаптеры...");
        });

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var props = adapter.GetIPProperties();
            var unicast = props.UnicastAddresses
                .FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork);
            if (unicast is null)
                continue;

            var gateway = props.GatewayAddresses
                .FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "";

            var row = new ListViewItem(adapter.Name);
            row.SubItems.Add(unicast.Address.ToString());
            row.SubItems.Add(unicast.IPv4Mask?.ToString() ?? "");
            row.SubItems.Add(gateway);
            row.SubItems.Add(adapter.OperationalStatus.ToString());

            InvokeIfNeeded(() => _networkList.Items.Add(row));
        }

        Log("Сеть обновлена.");
    }

    private void RefreshPrinters()
    {
        InvokeIfNeeded(() =>
        {
            _printerList.Items.Clear();
            Log("Проверяю установленные принтеры Windows...");
        });

        var printerRows = GetPrinterRows();
        if (printerRows.Count == 0)
        {
            foreach (string printer in PrinterSettings.InstalledPrinters)
                printerRows.Add(new PrinterRow(printer, "", "", "Установлен"));
        }

        InvokeIfNeeded(() =>
        {
            foreach (var printer in printerRows)
            {
                var row = new ListViewItem(printer.Name);
                row.SubItems.Add(printer.Port);
                row.SubItems.Add(printer.Driver);
                row.SubItems.Add(printer.Status);
                _printerList.Items.Add(row);
            }
        });

        var usbCount = printerRows.Count(IsUsbPrinter);
        Log(printerRows.Count == 0
            ? "Принтеры не найдены."
            : $"Найдено принтеров: {printerRows.Count}. Похоже на USB: {usbCount}.");
    }

    private void CheckUsbConnection()
    {
        var printerRows = GetPrinterRows();
        var modelUsbPrinter = printerRows.FirstOrDefault(x =>
            IsUsbPrinter(x) && (IsLikelyDriverForSelectedModel(x.Name) || IsLikelyDriverForSelectedModel(x.Driver)));
        var anyUsbPrinter = printerRows.FirstOrDefault(IsUsbPrinter);

        RefreshPrinters();

        if (modelUsbPrinter is not null)
        {
            SetStatus(_usbStatusLabel, "Подключен", StatusKind.Good);
            Log($"Найден нужный USB-принтер Windows: {modelUsbPrinter.Name}, порт {modelUsbPrinter.Port}, драйвер {modelUsbPrinter.Driver}.");
            TryReadEthernetSettingsAfterUsbCheck();
            return;
        }

        var usbDevices = GetUsbDeviceRows();
        var modelUsbDevice = usbDevices.FirstOrDefault(IsLikelyUsbDeviceForSelectedModel);
        if (modelUsbDevice is not null)
        {
            SetStatus(_usbStatusLabel, "USB найден", StatusKind.Warning);
            Log("Физическое USB-устройство найдено, но принтер Windows с подходящим драйвером не найден.");
            Log("Скорее всего нужно установить драйвер выбранной модели.");
            TryReadEthernetSettingsAfterUsbCheck();
            return;
        }

        var genericPrinterDevice = usbDevices.FirstOrDefault(IsGenericUsbPrinterDevice);
        if (genericPrinterDevice is not null)
        {
            SetStatus(_usbStatusLabel, "USB найден", StatusKind.Warning);
            Log("Найдено USB-устройство печати, но модель не удалось определить без драйвера.");
            Log("Если это нужный термопринтер, установи драйвер и повтори проверку.");
            return;
        }

        if (anyUsbPrinter is not null)
        {
            SetStatus(_usbStatusLabel, "Не найден", StatusKind.Bad);
            Log($"USB-принтер есть, но он не похож на выбранную модель {GetSelectedModel()}: {anyUsbPrinter.Name}, драйвер {anyUsbPrinter.Driver}.");
            return;
        }

        if (usbDevices.Count == 0)
        {
            SetStatus(_usbStatusLabel, "Не подключен", StatusKind.Bad);
            Log("USB-устройства не найдены. Проверь кабель и питание принтера.");
            return;
        }

        SetStatus(_usbStatusLabel, "Не найден", StatusKind.Bad);
        Log($"Подключенные USB-устройства есть, но среди них не найден {GetSelectedModel()}.");
        Log("Проверь выбранную модель, USB-кабель и питание принтера.");
    }

    private void FindFreeIp()
    {
        var network = GetPrimaryNetwork();
        if (network is null)
        {
            SetStatus(_freeIpStatusLabel, "Сеть не найдена", StatusKind.Bad);
            Log("Не удалось определить активную IPv4 сеть.");
            return;
        }

        Log($"Выбрана сеть: {network.AdapterName}, IP {network.Address}, маска {network.Mask}, шлюз {network.Gateway}.");
        Log("Ищу свободный IP внутри реального диапазона этой сети.");
        var candidate = FindFirstNotPinging(network);
        if (candidate is null)
        {
            SetStatus(_freeIpStatusLabel, "Не найден", StatusKind.Bad);
            Log("Свободный IP не найден в выбранной сети. Проверь, что активен обычный LAN/Wi-Fi адаптер, а не VPN/виртуальная сеть.");
            return;
        }

        InvokeIfNeeded(() => _ethernetIpBox.Text = candidate.ToString());
        FillEthernetFromCurrentNetwork();
        SetStatus(_freeIpStatusLabel, "Найден", StatusKind.Good);
        Log($"Свободный IP для принтера: {candidate}. Адрес подставлен в раздел Ethernet.");
    }

    private void FillEthernetFromCurrentNetwork()
    {
        var network = GetPrimaryNetwork();
        if (network is null)
        {
            Log("Не удалось заполнить Ethernet: активная IPv4 сеть не найдена.");
            return;
        }

        InvokeIfNeeded(() =>
        {
            _ethernetMaskBox.Text = network.Mask?.ToString() ?? "";
            _ethernetGatewayBox.Text = network.Gateway?.ToString() ?? "";
        });

        Log($"Ethernet заполнен из сети компьютера: маска {network.Mask}, шлюз {network.Gateway}.");
    }

    private void TryReadEthernetSettingsAfterUsbCheck()
    {
        if (!IsAtolDd340Selected())
            return;

        try
        {
            var result = HprtSdk.TryReadEthernet(GetHprtSdkPrinterModel(), GetReachablePrinterIpForSdk());
            if (!result.Success)
                return;

            InvokeIfNeeded(() =>
            {
                _ethernetDhcpBox.SelectedIndex = result.DhcpEnabled ? 1 : 0;
                _ethernetIpBox.Text = result.Ip;
                _ethernetMaskBox.Text = result.Mask;
                _ethernetGatewayBox.Text = result.Gateway;
                _ethernetMacBox.Text = result.Mac;
            });
            Log($"Текущие Ethernet-настройки принтера прочитаны через {FormatSdkPort(result.PortSetting)}.");
        }
        catch (Exception ex)
        {
            Log("Не удалось прочитать Ethernet-настройки принтера: " + ex.Message);
        }
    }

    private void ApplyEthernetSettings()
    {
        if (!IsAtolDd340Selected())
        {
            Log("Автонастройка Ethernet пока подготовлена только для ATOL DD340.");
            return;
        }

        var ip = GetText(_ethernetIpBox);
        var mask = GetText(_ethernetMaskBox);
        var gateway = GetText(_ethernetGatewayBox);
        if (!ValidateEthernetAddress(ip, "IP-адрес") ||
            !ValidateEthernetAddress(mask, "Маска подсети") ||
            !ValidateEthernetAddress(gateway, "Шлюз"))
        {
            return;
        }

        var dhcpEnabled = GetText(_ethernetDhcpBox).Equals("Включено", StringComparison.OrdinalIgnoreCase);

        try
        {
            var portSetting = HprtSdk.SetEthernet(GetHprtSdkPrinterModel(), dhcpEnabled, ip, mask, gateway, GetReachablePrinterIpForSdk());
            Log($"Ethernet-настройки отправлены в принтер через {FormatSdkPort(portSetting)}. Перезагрузи принтер, затем подключи его к сети и проверь ping.");
        }
        catch (Exception ex)
        {
            Log("Не удалось отправить Ethernet-настройки в принтер: " + ex.Message);
        }
    }

    private void ReadPrinterSettings()
    {
        if (!IsAtolDd340Selected())
        {
            Log("Чтение настроек принтера пока подготовлено только для ATOL DD340.");
            return;
        }

        try
        {
            var portSetting = HprtSdk.CheckAutoReady(GetHprtSdkPrinterModel(), GetReachablePrinterIpForSdk());
            Log($"Связь с принтером есть через {FormatSdkPort(portSetting)}. Чтение протокола и носителя требует проверки точных HPRT-команд на живом DD340.");
            Log("Пока можно назначить значения через кнопку 'Назначить'. Если принтер вернет ошибку, она появится здесь в логе.");
        }
        catch (Exception ex)
        {
            Log("Не удалось подготовить чтение настроек принтера: " + ex.Message);
        }
    }

    private void ApplyPrinterSettings()
    {
        if (!IsAtolDd340Selected())
        {
            Log("Назначение настроек принтера пока подготовлено только для ATOL DD340.");
            return;
        }

        var protocol = GetSelectedOptionCode(_printerProtocolBox);
        var mediaType = GetSelectedOptionCode(_mediaTypeBox);
        var commands = BuildPrinterSettingsCommands(protocol, mediaType);
        if (commands.Count == 0)
        {
            Log("Не удалось подготовить команды для выбранных настроек принтера.");
            return;
        }

        try
        {
            var portSetting = HprtSdk.SendCommands(GetHprtSdkPrinterModel(), commands, GetReachablePrinterIpForSdk());
            Log($"Настройки принтера отправлены через {FormatSdkPort(portSetting)}: протокол {protocol}, носитель {GetText(_mediaTypeBox)}.");
        }
        catch (Exception ex)
        {
            Log("Не удалось назначить настройки принтера: " + ex.Message);
        }
    }

    private static List<string> BuildPrinterSettingsCommands(string protocol, string mediaType)
    {
        var commands = new List<string>();
        var useZpl = protocol.Equals("ZPL", StringComparison.OrdinalIgnoreCase);

        if (useZpl)
        {
            commands.Add(mediaType switch
            {
                "mark" => "^XA^MNM^JUS^XZ",
                "continuous" => "^XA^MNN^JUS^XZ",
                _ => "^XA^MNW^JUS^XZ"
            });
            return commands;
        }

        commands.Add(mediaType switch
        {
            "mark" => "BLINEDETECT\r\n",
            "continuous" => "GAP 0,0\r\n",
            _ => "GAPDETECT\r\n"
        });

        return commands;
    }

    private void CheckNetworkConnection()
    {
        var ip = GetPrinterIp();
        if (ip is null)
            return;

        using var ping = new Ping();
        Log($"Проверяю подключение по сети: {ip}...");
        var reply = ping.Send(ip, 2500);
        if (reply.Status == IPStatus.Success)
        {
            SetStatus(_networkStatusLabel, "Подключен", StatusKind.Good);
            Log($"Сеть OK: принтер отвечает, {reply.RoundtripTime} ms.");
        }
        else
        {
            SetStatus(_networkStatusLabel, "Не подключен", StatusKind.Bad);
            Log($"Принтер по сети не отвечает: {reply.Status}.");
        }
    }

    private void CheckNetworkAndCreatePort()
    {
        var ip = GetPrinterIp();
        if (ip is null)
            return;

        using var ping = new Ping();
        Log($"Проверяю ping перед созданием принтера: {ip}...");
        var reply = ping.Send(ip, 2500);
        if (reply.Status != IPStatus.Success)
        {
            SetStatus(_networkStatusLabel, "Не подключен", StatusKind.Bad);
            Log("Принтер по сети не отвечает. Сначала назначь IP в утилите, отключи USB и подключи принтер к сети.");
            return;
        }

        SetStatus(_networkStatusLabel, "Подключен", StatusKind.Good);
        CreateTcpIpPort();
    }

    private static List<PrinterRow> GetPrinterRows()
    {
        var output = RunPowerShell("(Get-CimInstance Win32_Printer | Select-Object Name,PortName,DriverName,PrinterStatus | ConvertTo-Csv -NoTypeInformation)");
        return ParseCsv(output)
            .Skip(1)
            .Select(fields => new PrinterRow(
                fields.ElementAtOrDefault(0) ?? "",
                fields.ElementAtOrDefault(1) ?? "",
                fields.ElementAtOrDefault(2) ?? "",
                PrinterStatusToText(fields.ElementAtOrDefault(3))))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Where(x => !IsPowerShellMetadataRow(x.Name))
            .ToList();
    }

    private static bool IsPowerShellMetadataRow(string value)
    {
        var text = value.Trim();
        return text.StartsWith("#< CLIXML", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("<Objs", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("<Obj ", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> GetPrinterDriverNames()
    {
        var output = RunPowerShell("(Get-PrinterDriver | Select-Object Name | ConvertTo-Csv -NoTypeInformation)");
        return ParseCsv(output)
            .Skip(1)
            .Select(fields => fields.ElementAtOrDefault(0) ?? "")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !IsPowerShellMetadataRow(x))
            .ToList();
    }

    private static List<UsbDeviceRow> GetUsbDeviceRows()
    {
        var output = RunPowerShell("(Get-CimInstance Win32_PnPEntity | Where-Object { $_.PNPDeviceID -like 'USB*' } | Select-Object Name,Manufacturer,PNPDeviceID,Status | ConvertTo-Csv -NoTypeInformation)");
        return ParseCsv(output)
            .Skip(1)
            .Select(fields => new UsbDeviceRow(
                fields.ElementAtOrDefault(0) ?? "",
                fields.ElementAtOrDefault(1) ?? "",
                fields.ElementAtOrDefault(2) ?? "",
                fields.ElementAtOrDefault(3) ?? ""))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.PnpDeviceId))
            .Where(x => !IsPowerShellMetadataRow(x.Name))
            .ToList();
    }

    private static bool IsUsbPrinter(PrinterRow printer) =>
        printer.Port.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
        printer.Name.Contains("USB", StringComparison.OrdinalIgnoreCase);

    private bool IsLikelyUsbDeviceForSelectedModel(UsbDeviceRow device)
    {
        var text = $"{device.Name} {device.Manufacturer} {device.PnpDeviceId}";
        return IsLikelyDriverForSelectedModel(text);
    }

    private static bool IsGenericUsbPrinterDevice(UsbDeviceRow device)
    {
        var text = $"{device.Name} {device.Manufacturer} {device.PnpDeviceId}";
        return text.Contains("USB Printing Support", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("USBPRINT", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Printer", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Печать", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Принтер", StringComparison.OrdinalIgnoreCase);
    }

    private void LogUsbDevice(UsbDeviceRow device)
    {
        Log($"USB: {device.Name}; производитель: {device.Manufacturer}; статус: {device.Status}; ID: {device.PnpDeviceId}");
    }

    private NetworkInfo? GetPrimaryNetwork()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(adapter =>
            {
                var props = adapter.GetIPProperties();
                var address = props.UnicastAddresses.FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork);
                var gateway = props.GatewayAddresses.FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
                return address is null ? null : new NetworkInfo(adapter.Name, address.Address, address.IPv4Mask, gateway);
            })
            .Where(x => x is not null && x.Mask is not null)
            .OrderByDescending(x => x!.Gateway is not null)
            .ThenBy(x => IsVirtualAdapterName(x!.AdapterName))
            .ThenByDescending(x => IsPrivateAddress(x!.Address))
            .ThenByDescending(x => x!.Mask?.ToString() == "255.255.255.0")
            .ThenByDescending(x => CountUsableAddresses(x!) > 10)
            .FirstOrDefault();
    }

    private static IPAddress? FindFirstNotPinging(NetworkInfo network)
    {
        var gateway = network.Gateway?.ToString();
        var local = network.Address.ToString();

        foreach (var candidate in EnumerateCandidateAddresses(network).Take(512))
        {
            var text = candidate.ToString();
            if (text == gateway || text == local)
                continue;

            using var ping = new Ping();
            try
            {
                var reply = ping.Send(candidate, 180);
                if (reply.Status != IPStatus.Success)
                    return candidate;
            }
            catch
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<IPAddress> EnumerateCandidateAddresses(NetworkInfo network)
    {
        if (network.Mask is null)
            yield break;

        var ip = ToUInt32(network.Address);
        var mask = ToUInt32(network.Mask);
        var networkAddress = ip & mask;
        var broadcastAddress = networkAddress | ~mask;

        if (broadcastAddress <= networkAddress + 1)
            yield break;

        var first = networkAddress + 1;
        var last = broadcastAddress - 1;
        var baseBytes = network.Address.GetAddressBytes();

        foreach (var octet in Enumerable.Range(200, 55).Concat(Enumerable.Range(50, 150)))
        {
            var bytes = (byte[])baseBytes.Clone();
            bytes[3] = (byte)octet;
            var preferred = ToUInt32(new IPAddress(bytes));
            if (preferred >= first && preferred <= last)
                yield return FromUInt32(preferred);
        }

        for (var value = first; value <= last; value++)
        {
            yield return FromUInt32(value);
            if (value == uint.MaxValue)
                yield break;
        }
    }

    private static uint CountUsableAddresses(NetworkInfo network)
    {
        if (network.Mask is null)
            return 0;

        var ip = ToUInt32(network.Address);
        var mask = ToUInt32(network.Mask);
        var networkAddress = ip & mask;
        var broadcastAddress = networkAddress | ~mask;
        return broadcastAddress <= networkAddress + 1 ? 0 : broadcastAddress - networkAddress - 1;
    }

    private static bool IsVirtualAdapterName(string name)
    {
        var markers = new[] { "virtual", "vethernet", "hyper-v", "vmware", "virtualbox", "docker", "wsl", "vpn", "tap", "loopback" };
        return markers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            bytes[0] == 192 && bytes[1] == 168 ||
            bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31;
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value) => new(new[]
    {
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value
    });

    private void OpenPrinterWeb()
    {
        var ip = GetPrinterIp();
        if (ip is null)
            return;

        OpenUrl("http://" + ip);
        Log("Открываю веб-интерфейс принтера: http://" + ip);
    }

    private void OpenDriverPage()
    {
        var model = _models[GetSelectedModel()];
        OpenUrl(model.DriverUrl);
        Log("Открываю страницу драйвера для " + model.Name);
    }

    private void DownloadDodoLabelPrinter()
    {
        const string url = "https://labelprinter.dodois.io/app/windows/download";
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var destination = Path.Combine(documents, "DodoLabelPrinterSetup.exe");
        Directory.CreateDirectory(documents);

        try
        {
            Log("Скачиваю Dodo Label Printer...");
            DownloadFile(url, destination);
            Log("Dodo Label Printer скачан: " + destination);
            Log("Запускаю установщик Dodo Label Printer.");
            Process.Start(new ProcessStartInfo(destination)
            {
                UseShellExecute = true,
                WorkingDirectory = documents
            });
        }
        catch (Exception ex)
        {
            Log("Не удалось скачать или запустить Dodo Label Printer: " + ex.Message);
            Log("Открываю страницу скачивания.");
            OpenUrl(url);
        }
    }

    private void DownloadDriver()
    {
        var model = _models[GetSelectedModel()];
        if (string.IsNullOrWhiteSpace(model.DriverGoogleDriveFileId) ||
            string.IsNullOrWhiteSpace(model.DriverFileName))
        {
            Log("Для этой модели автоматическое скачивание драйвера пока не настроено.");
            OpenDriverPage();
            return;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var destination = Path.Combine(documents, model.DriverFileName);
        var extractDirectory = Path.Combine(documents, Path.GetFileNameWithoutExtension(model.DriverFileName));
        Directory.CreateDirectory(documents);

        try
        {
            if (!File.Exists(destination) || new FileInfo(destination).Length == 0)
            {
                Log("Скачиваю драйвер для " + model.Name + "...");
                Log("Путь сохранения: " + destination);
                DownloadGoogleDriveFile(model.DriverGoogleDriveFileId, destination);
                Log("Драйвер скачан: " + destination);
            }
            else
            {
                Log("Архив драйвера уже скачан: " + destination);
            }

            var installerPath = ExtractDriverInstaller(destination, extractDirectory);
            if (!TryInstallDriverSilently(installerPath, out var installerWasOpened))
            {
                if (!installerWasOpened)
                    RunDriverInstaller(installerPath);

                Log("Создаю TCP/IP порт Windows по IP из раздела Ethernet.");
                CreateTcpIpPort();
            }
        }
        catch (Exception ex)
        {
            Log("Не удалось выполнить установку драйвера: " + ex.Message);
            Log("Открываю папку Google Drive, можно скачать файл вручную.");
            OpenUrl(model.DriverUrl);
        }
    }

    private string ExtractDriverInstaller(string archivePath, string extractDirectory)
    {
        Directory.CreateDirectory(extractDirectory);

        var installer = Directory
            .EnumerateFiles(extractDirectory, "*.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(installer))
        {
            Log("Установщик драйвера уже распакован: " + installer);
            return installer;
        }

        Log("Распаковываю архив драйвера...");
        ZipFile.ExtractToDirectory(archivePath, extractDirectory, true);

        installer = Directory
            .EnumerateFiles(extractDirectory, "*.exe", SearchOption.AllDirectories)
            .OrderByDescending(x => Path.GetFileName(x).Contains("HPRT", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(installer))
            throw new FileNotFoundException("В архиве драйвера не найден exe-установщик.");

        Log("Установщик найден: " + installer);
        return installer;
    }

    private bool TryInstallDriverSilently(string packageInstallerPath, out bool installerWasOpened)
    {
        installerWasOpened = false;
        var ip = GetPrinterIp();
        if (ip is null)
            return false;

        var packageDirectory = Path.GetDirectoryName(packageInstallerPath) ?? Environment.CurrentDirectory;
        var driverWizard = FindDriverWizard(packageDirectory) ?? FindInstalledSeagullDriverWizard();
        if (string.IsNullOrWhiteSpace(driverWizard))
        {
            var extractedDirectory = Path.Combine(packageDirectory, "Extracted");
            TryExtractSeagullPackage(packageInstallerPath, extractedDirectory);
            driverWizard = FindDriverWizard(extractedDirectory) ?? FindInstalledSeagullDriverWizard();
        }

        if (string.IsNullOrWhiteSpace(driverWizard))
        {
            Log("Распакованные файлы Seagull не найдены. Открою распаковщик драйвера.");
            RunDriverInstaller(packageInstallerPath);
            installerWasOpened = true;
            driverWizard = FindDriverWizard(packageDirectory) ?? FindInstalledSeagullDriverWizard();
        }

        if (string.IsNullOrWhiteSpace(driverWizard))
        {
            Log("Тихая установка недоступна: DriverWizard.exe не найден после распаковки.");
            return false;
        }

        var portName = CreateTcpIpPortOnly();
        if (string.IsNullOrWhiteSpace(portName))
            return false;

        var roleName = GetSelectedRole();
        foreach (var modelName in GetSilentDriverModelCandidates())
        {
            Log($"Пробую тихо установить драйвер: {modelName}.");
            var args = $"install /name:\"{roleName}\" /model:\"{modelName}\" /port:\"{portName}\"";
            var exitCode = RunElevatedProcess(driverWizard, args, Path.GetDirectoryName(driverWizard));
            if (exitCode == 0)
            {
                Log("Драйвер установлен и принтер создан через DriverWizard.");
                RefreshPrinters();
                return true;
            }

            Log($"DriverWizard вернул код {exitCode} для модели {modelName}.");
        }

        Log("Тихая установка не сработала, открою обычный установщик.");
        return false;
    }

    private static string? FindDriverWizard(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        return Directory
            .EnumerateFiles(directory, "DriverWizard.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static string? FindInstalledSeagullDriverWizard()
    {
        var candidates = new[]
        {
            @"C:\Seagull\HPRT\2024.3 M-0",
            @"C:\Seagull\HPRT",
            @"C:\Seagull"
        };

        return candidates
            .Select(FindDriverWizard)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
    }

    private void TryExtractSeagullPackage(string packageInstallerPath, string extractedDirectory)
    {
        try
        {
            Directory.CreateDirectory(extractedDirectory);
            var exitCode = RunProcess(packageInstallerPath, $"/x \"{extractedDirectory}\"", Path.GetDirectoryName(packageInstallerPath));
            Log(exitCode == 0
                ? "Пакет драйвера распакован для тихой установки."
                : $"Не удалось тихо распаковать пакет драйвера, код {exitCode}.");
        }
        catch (Exception ex)
        {
            Log("Не удалось подготовить тихую установку: " + ex.Message);
        }
    }

    private IEnumerable<string> GetSilentDriverModelCandidates()
    {
        return GetSelectedModel() switch
        {
            "ATOL DD340" => new[] { "HPRT HD200", "HD200", "ATOL DD340" },
            _ => new[] { GetSelectedModel() }
        };
    }

    private void RunDriverInstaller(string installerPath)
    {
        Log("Запускаю установщик драйвера. Если Windows спросит разрешение, нажми Да.");
        using var process = Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Environment.CurrentDirectory
        });

        if (process is null)
            throw new InvalidOperationException("Не удалось запустить установщик драйвера.");

        process.WaitForExit();
        Log("Установщик драйвера закрыт.");
    }

    private static void DownloadGoogleDriveFile(string fileId, string destination)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        client.Timeout = TimeSpan.FromMinutes(10);

        var url = "https://drive.usercontent.google.com/download?id=" +
            Uri.EscapeDataString(fileId) +
            "&export=download&confirm=t";

        using var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Google Drive не отдал файл напрямую.");

        var tempFile = destination + ".download";
        using (var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
        using (var output = File.Create(tempFile))
        {
            input.CopyTo(output);
        }

        if (new FileInfo(tempFile).Length == 0)
            throw new InvalidOperationException("Скачанный файл пустой.");

        File.Move(tempFile, destination, true);
    }

    private static void DownloadFile(string url, string destination)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        client.Timeout = TimeSpan.FromMinutes(10);

        using var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var tempFile = destination + ".download";
        using (var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
        using (var output = File.Create(tempFile))
        {
            input.CopyTo(output);
        }

        if (new FileInfo(tempFile).Length == 0)
            throw new InvalidOperationException("Скачанный файл пустой.");

        File.Move(tempFile, destination, true);
    }

    private void OpenWindowsPrinterSettings()
    {
        var roleName = GetSelectedRole();
        var printer = GetPrinterRows()
            .FirstOrDefault(x => x.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));

        if (printer is null)
        {
            Log($"Принтер Windows '{roleName}' не найден. Открываю общий список принтеров.");
            Process.Start(new ProcessStartInfo("control", "printers") { UseShellExecute = true });
            return;
        }

        Process.Start(new ProcessStartInfo("rundll32.exe", $"printui.dll,PrintUIEntry /e /n \"{printer.Name}\"")
        {
            UseShellExecute = true
        });
        Log($"Открываю настройки печати Windows для принтера: {printer.Name}.");
    }

    private void CreateTcpIpPort()
    {
        var portName = CreateTcpIpPortOnly();
        if (string.IsNullOrWhiteSpace(portName))
            return;

        CreateWindowsPrinterFromSelectedDriver(portName);
        RefreshPrinters();
    }

    private string? CreateTcpIpPortOnly()
    {
        var ip = GetPrinterIp();
        if (ip is null)
            return null;

        var portName = "IP_" + ip;
        var script = $"Add-PrinterPort -Name '{EscapePowerShell(portName)}' -PrinterHostAddress '{EscapePowerShell(ip)}'";
        var output = RunPowerShell(script);
        Log(string.IsNullOrWhiteSpace(output)
            ? $"TCP/IP порт Windows создан или уже существует: {portName} -> {ip}"
            : output.Trim());

        return portName;
    }

    private void CreateWindowsPrinterFromSelectedDriver(string portName)
    {
        var roleName = GetSelectedRole();
        var driverName = FindBestDriverName();

        if (string.IsNullOrWhiteSpace(driverName))
        {
            Log("Не удалось создать принтер Windows: установи драйвер для выбранной модели, затем повтори создание порта.");
            return;
        }

        var script = "$printer = Get-Printer -Name '" + EscapePowerShell(roleName) + "' -ErrorAction SilentlyContinue; " +
            "$printerOnPort = Get-Printer | Where-Object { $_.PortName -eq '" + EscapePowerShell(portName) + "' } | Select-Object -First 1; " +
            "if ($null -eq $printer) { " +
            "if ($null -ne $printerOnPort) { " +
            "Rename-Printer -Name $printerOnPort.Name -NewName '" + EscapePowerShell(roleName) + "'; " +
            "Set-Printer -Name '" + EscapePowerShell(roleName) + "' -PortName '" + EscapePowerShell(portName) + "' " +
            "} else { " +
            "Add-Printer -Name '" + EscapePowerShell(roleName) + "' -DriverName '" + EscapePowerShell(driverName) + "' -PortName '" + EscapePowerShell(portName) + "' " +
            "} " +
            "} else { " +
            "Set-Printer -Name '" + EscapePowerShell(roleName) + "' -PortName '" + EscapePowerShell(portName) + "' " +
            "}";
        var output = RunPowerShell(script);
        Log(string.IsNullOrWhiteSpace(output)
            ? $"Принтер Windows готов: {roleName}"
            : output.Trim());
    }

    private void PrintTestPage()
    {
        var printerName = FindPrinterForTest();
        if (string.IsNullOrWhiteSpace(printerName))
        {
            Log("Не найден принтер для тестовой печати. Сначала создай сетевой принтер или установи драйвер.");
            return;
        }

        using var document = new PrintDocument();
        document.PrinterSettings.PrinterName = printerName;
        document.DocumentName = "ThermoPrinterTool test";
        document.PrintPage += (_, e) =>
        {
            using var font = new Font("Arial", 10);
            var text = $"TEST OK\n{GetSelectedModel()}\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}\nIP: {GetText(_ethernetIpBox)}";
            e.Graphics?.DrawString(text, font, Brushes.Black, new RectangleF(8, 8, 360, 220));
        };
        document.Print();
        Log("Тестовая страница отправлена на принтер: " + printerName);
    }

    private string? FindPrinterForTest()
    {
        var roleName = GetSelectedRole();
        var printers = GetPrinterRows();
        return printers.FirstOrDefault(x => x.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase))?.Name ??
            FindBestInstalledPrinterForModel(printers)?.Name;
    }

    private string? FindBestDriverName()
    {
        var printers = GetPrinterRows();
        var printer = FindBestInstalledPrinterForModel(printers);
        if (!string.IsNullOrWhiteSpace(printer?.Driver))
            return printer.Driver;

        return GetPrinterDriverNames()
            .FirstOrDefault(IsLikelyDriverForSelectedModel);
    }

    private PrinterRow? FindBestInstalledPrinterForModel(List<PrinterRow> printers)
    {
        return printers.FirstOrDefault(x => x.Name.Equals(GetSelectedRole(), StringComparison.OrdinalIgnoreCase)) ??
            printers.FirstOrDefault(x => IsLikelyDriverForSelectedModel(x.Driver) || IsLikelyDriverForSelectedModel(x.Name)) ??
            printers.FirstOrDefault(x => !IsMicrosoftVirtualPrinter(x));
    }

    private bool IsLikelyDriverForSelectedModel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var model = GetSelectedModel();
        var keywords = model switch
        {
            "ATOL DD340" => new[] { "ATOL", "DD340", "Seagull", "HPRT", "HD200" },
            "ATOL BP41" => new[] { "ATOL", "BP41" },
            "TSC DA220" => new[] { "TSC", "DA220", "Seagull" },
            "BIXOLON XD3-40d" => new[] { "BIXOLON", "XD3", "40" },
            _ => model.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        };

        return keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMicrosoftVirtualPrinter(PrinterRow printer)
    {
        var text = printer.Name + " " + printer.Driver + " " + printer.Port;
        return text.Contains("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Microsoft XPS", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("OneNote", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Fax", StringComparison.OrdinalIgnoreCase);
    }

    private string? GetPrinterIp()
    {
        var ip = GetText(_ethernetIpBox);
        if (!IPAddress.TryParse(ip, out _))
        {
            Log("Укажи IP-адрес в разделе Ethernet или нажми 'Найти свободный IP'.");
            return null;
        }
        return ip;
    }

    private string? GetReachablePrinterIpForSdk()
    {
        var ip = GetText(_ethernetIpBox);
        if (!IPAddress.TryParse(ip, out _))
            return null;

        try
        {
            using var ping = new Ping();
            var reply = ping.Send(ip, 700);
            return reply.Status == IPStatus.Success ? ip : null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatSdkPort(string portSetting)
    {
        if (portSetting.Equals("USB", StringComparison.OrdinalIgnoreCase))
            return "USB";

        if (portSetting.StartsWith("NET,", StringComparison.OrdinalIgnoreCase))
        {
            var parts = portSetting.Split(',');
            if (parts.Length >= 2)
                return $"сеть {parts[1]}";
        }

        return portSetting;
    }

    private string GetSelectedModel() => InvokeRequired ? (string)Invoke(GetSelectedModel) : _modelBox.Text;

    private string GetSelectedRole() => InvokeRequired ? (string)Invoke(GetSelectedRole) : _printerRoleBox.Text;

    private bool IsAtolDd340Selected() => GetSelectedModel().Equals("ATOL DD340", StringComparison.OrdinalIgnoreCase);

    private string GetHprtSdkPrinterModel() => IsAtolDd340Selected() ? "HD200" : GetSelectedModel();

    private static string GetHprtSdkPath() => "ESC_SDK.dll";

    private bool ValidateEthernetAddress(string value, string label)
    {
        if (IPAddress.TryParse(value, out _))
            return true;

        Log($"{label}: укажи корректный IPv4 адрес.");
        return false;
    }

    private static string GetText(Control control) => control.InvokeRequired
        ? (string)control.Invoke(() => control.Text.Trim())
        : control.Text.Trim();

    private static string GetSelectedOptionCode(ComboBox comboBox)
    {
        object? selected = comboBox.InvokeRequired
            ? comboBox.Invoke(() => comboBox.SelectedItem)
            : comboBox.SelectedItem;

        return selected is PrinterSettingOption option ? option.Code : comboBox.Text.Trim();
    }

    private void CopyTextToClipboard(string text)
    {
        InvokeIfNeeded(() =>
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                Log("Не удалось скопировать в буфер обмена: " + ex.Message);
            }
        });
    }

    private void SetStatus(Label label, string text, StatusKind kind)
    {
        InvokeIfNeeded(() =>
        {
            label.Text = text;
            label.AutoSize = false;
            label.Font = new Font("Segoe UI Semibold", 8.5F);
            label.Padding = new Padding(6, 2, 6, 2);
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.ForeColor = kind switch
            {
                StatusKind.Good => Color.FromArgb(20, 108, 67),
                StatusKind.Warning => Color.FromArgb(143, 92, 0),
                StatusKind.Bad => Color.FromArgb(176, 42, 55),
                _ => Color.FromArgb(82, 88, 96)
            };
            label.BackColor = kind switch
            {
                StatusKind.Good => Color.FromArgb(218, 246, 226),
                StatusKind.Warning => Color.FromArgb(255, 243, 205),
                StatusKind.Bad => Color.FromArgb(255, 232, 235),
                _ => Color.FromArgb(238, 241, 245)
            };
        });
    }

    private void Log(string message)
    {
        InvokeIfNeeded(() =>
        {
            _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            _logBox.ScrollToCaret();
        });
    }

    private void InvokeIfNeeded(Action action)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
            Invoke(action);
        else
            action();
    }

    private static string RunPowerShell(string command)
    {
        var utf8Command = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
            "$OutputEncoding = [System.Text.Encoding]::UTF8; " +
            command;
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

        using var process = Process.Start(startInfo);
        if (process is null)
            return "Не удалось запустить PowerShell.";

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(8000);
        output = RemovePowerShellXmlNoise(output);
        error = RemovePowerShellXmlNoise(error);
        return string.IsNullOrWhiteSpace(error) ? output : output + Environment.NewLine + error;
    }

    private static int RunProcess(string fileName, string arguments, string? workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        });

        if (process is null)
            return -1;

        process.WaitForExit();
        return process.ExitCode;
    }

    private static int RunElevatedProcess(string fileName, string arguments, string? workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        });

        if (process is null)
            return -1;

        process.WaitForExit();
        return process.ExitCode;
    }

    private static string RemovePowerShellXmlNoise(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line =>
            {
                var trimmed = line.TrimStart();
                return !trimmed.StartsWith("#< CLIXML", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("<Objs", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("<Obj ", StringComparison.OrdinalIgnoreCase);
            });
        return string.Join(Environment.NewLine, lines);
    }

    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            var quoted = false;

            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = !quoted;
                }
                else if (ch == ',' && !quoted)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }

            fields.Add(current.ToString());
            rows.Add(fields.ToArray());
        }

        return rows;
    }

    private static string PrinterStatusToText(string? status) => status switch
    {
        "1" => "Other",
        "2" => "Unknown",
        "3" => "Idle",
        "4" => "Printing",
        "5" => "Warmup",
        "6" => "Stopped",
        "7" => "Offline",
        _ => status ?? ""
    };

    private static string EscapePowerShell(string value) => value.Replace("'", "''");

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private enum StatusKind
    {
        Neutral,
        Good,
        Warning,
        Bad
    }

    private sealed record PrinterModelInfo(
        string Name,
        string DriverUrl,
        string UtilityUrl,
        string Note,
        string? DriverGoogleDriveFileId,
        string? DriverFileName);

    private sealed record PrinterRow(string Name, string Port, string Driver, string Status);

    private sealed record UsbDeviceRow(string Name, string Manufacturer, string PnpDeviceId, string Status);

    private sealed record NetworkInfo(string AdapterName, IPAddress Address, IPAddress? Mask, IPAddress? Gateway);

    private sealed record HprtEthernetResult(bool Success, bool DhcpEnabled, string Ip, string Mask, string Gateway, string Mac, string PortSetting);

    private sealed record PrinterSettingOption(string Text, string Code)
    {
        public override string ToString() => Text;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }

    private static class HprtSdk
    {
        private const string DllPath = "ESC_SDK.dll";
        private const int LangId = 0;
        private const string UsbPort = "USB";
        private const string ResourcePrefix = "HPRT.";
        private const CharSet SdkCharSet = CharSet.Unicode;
        private static readonly object NativeLock = new();
        private static string? _nativeDirectory;

        static HprtSdk()
        {
            NativeLibrary.SetDllImportResolver(typeof(HprtSdk).Assembly, ResolveNativeLibrary);
        }

        [DllImport(DllPath, CharSet = SdkCharSet)]
        private static extern int FormatError(int errorNo, int langId, byte[] buffer, int pos, int bufferSize);

        [DllImport(DllPath, CharSet = SdkCharSet)]
        private static extern int PrinterCreator(ref IntPtr printer, string model);

        [DllImport(DllPath)]
        private static extern int PrinterDestroy(IntPtr printer);

        [DllImport(DllPath, CharSet = SdkCharSet)]
        private static extern int PortOpen(IntPtr printer, string portSetting);

        [DllImport(DllPath)]
        private static extern int PortClose(IntPtr printer);

        [DllImport(DllPath, CharSet = SdkCharSet)]
        private static extern int SetEthernetInfo(IntPtr printer, int mode, string ip, string ipMask, string gateway);

        [DllImport(DllPath, CharSet = SdkCharSet)]
        private static extern int GetEthernetInfo(
            IntPtr printer,
            ref int mode,
            StringBuilder ip,
            StringBuilder ipMask,
            StringBuilder gateway,
            StringBuilder mac);

        [DllImport(DllPath, CharSet = SdkCharSet)]
        private static extern int SendCommand(IntPtr printer, string command);

        public static string SetEthernet(string model, bool dhcpEnabled, string ip, string mask, string gateway, string? networkIp)
        {
            EnsureX86();
            var mode = dhcpEnabled ? 1 : 0;
            return WithOpenAutoPrinter(model, networkIp, printer =>
            {
                var result = SetEthernetInfo(printer, mode, ip, mask, gateway);
                ThrowIfFailed(result, "назначить Ethernet-настройки");
            });
        }

        public static HprtEthernetResult TryReadEthernet(string model, string? networkIp)
        {
            EnsureX86();

            var mode = 0;
            var ip = new StringBuilder(64);
            var mask = new StringBuilder(64);
            var gateway = new StringBuilder(64);
            var mac = new StringBuilder(96);

            var portSetting = WithOpenAutoPrinter(model, networkIp, printer =>
            {
                var result = GetEthernetInfo(printer, ref mode, ip, mask, gateway, mac);
                ThrowIfFailed(result, "прочитать Ethernet-настройки");
            });

            return new HprtEthernetResult(
                true,
                mode != 0,
                ip.ToString().TrimEnd('\0'),
                mask.ToString().TrimEnd('\0'),
                gateway.ToString().TrimEnd('\0'),
                mac.ToString().TrimEnd('\0'),
                portSetting);
        }

        public static string CheckAutoReady(string model, string? networkIp)
        {
            EnsureX86();
            return WithOpenAutoPrinter(model, networkIp, _ => { });
        }

        public static string SendCommands(string model, IEnumerable<string> commands, string? networkIp)
        {
            EnsureX86();
            return WithOpenAutoPrinter(model, networkIp, printer =>
            {
                foreach (var command in commands)
                {
                    var result = SendCommand(printer, command);
                    ThrowIfFailed(result, "отправить команду настройки принтера");
                }
            });
        }

        private static string WithOpenAutoPrinter(string model, string? networkIp, Action<IntPtr> action)
        {
            var errors = new List<string>();
            foreach (var portSetting in GetPortCandidates(networkIp))
            {
                try
                {
                    WithOpenPrinter(model, portSetting, action);
                    return portSetting;
                }
                catch (Exception ex)
                {
                    errors.Add($"{portSetting}: {ex.Message}");
                }
            }

            throw new InvalidOperationException("Не удалось подключиться к принтеру автоматически. " + string.Join(" | ", errors));
        }

        private static IEnumerable<string> GetPortCandidates(string? networkIp)
        {
            yield return UsbPort;

            if (!string.IsNullOrWhiteSpace(networkIp))
                yield return $"NET,{networkIp},9100";
        }

        private static void WithOpenPrinter(string model, string portSetting, Action<IntPtr> action)
        {
            var printer = IntPtr.Zero;
            var created = false;
            var opened = false;

            try
            {
                var createResult = PrinterCreator(ref printer, model);
                ThrowIfFailed(createResult, "создать объект принтера");
                if (printer == IntPtr.Zero)
                    throw new InvalidOperationException("SDK вернул пустой объект принтера.");

                created = true;

                var openResult = PortOpen(printer, portSetting);
                ThrowIfFailed(openResult, $"открыть порт принтера {portSetting}");
                opened = true;

                action(printer);
            }
            finally
            {
                if (opened)
                    PortClose(printer);

                if (created)
                    PrinterDestroy(printer);
            }
        }

        private static void EnsureX86()
        {
            if (!Environment.Is64BitProcess)
                return;

            throw new InvalidOperationException("HPRT ESC_SDK.dll 32-битная. Запусти x86-версию программы из папки publish\\single-x86.");
        }

        private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!libraryName.Equals(DllPath, StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;

            var nativeDirectory = EnsureNativeLibrariesExtracted();
            var sdkPath = Path.Combine(nativeDirectory, DllPath);
            return NativeLibrary.Load(sdkPath);
        }

        private static string EnsureNativeLibrariesExtracted()
        {
            lock (NativeLock)
            {
                if (!string.IsNullOrWhiteSpace(_nativeDirectory) && File.Exists(Path.Combine(_nativeDirectory, DllPath)))
                    return _nativeDirectory;

                var version = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion ?? "dev";
                var safeVersion = string.Concat(version.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' ? ch : '_'));
                var targetDirectory = Path.Combine(Path.GetTempPath(), "ThermoPrinterTool", "native", "hprt", safeVersion);
                Directory.CreateDirectory(targetDirectory);

                var assembly = typeof(HprtSdk).Assembly;
                var resources = assembly.GetManifestResourceNames()
                    .Where(name => name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (resources.Count == 0)
                    throw new FileNotFoundException("HPRT DLL не найдены внутри сборки.");

                foreach (var resourceName in resources)
                {
                    var fileName = resourceName[ResourcePrefix.Length..];
                    var targetPath = Path.Combine(targetDirectory, fileName);
                    using var resource = assembly.GetManifestResourceStream(resourceName);
                    if (resource is null)
                        continue;

                    if (File.Exists(targetPath) && new FileInfo(targetPath).Length == resource.Length)
                        continue;

                    using var output = File.Create(targetPath);
                    resource.CopyTo(output);
                }

                var sdkPath = Path.Combine(targetDirectory, DllPath);
                if (!File.Exists(sdkPath))
                    throw new FileNotFoundException("ESC_SDK.dll не найден среди распакованных HPRT DLL.", sdkPath);

                _nativeDirectory = targetDirectory;
                return targetDirectory;
            }
        }

        private static void ThrowIfFailed(int result, string action)
        {
            if (result == 0)
                return;

            throw new InvalidOperationException($"Не удалось {action}. Код SDK: {result}. {FormatSdkError(result)}");
        }

        private static string FormatSdkError(int errorNo)
        {
            try
            {
                var buffer = new byte[512];
                FormatError(errorNo, LangId, buffer, 0, buffer.Length);
                return Encoding.Default.GetString(buffer).TrimEnd('\0').Trim();
            }
            catch
            {
                return "";
            }
        }
    }
}
