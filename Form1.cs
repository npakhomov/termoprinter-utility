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
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ThermoPrinterTool;

public partial class Form1 : Form
{
    private const string GitHubOwner = "npakhomov";
    private const string GitHubRepository = "termoprinter-utility";
    private const string ReleaseAssetName = "ThermoPrinterTool.exe";
    private const string ReleaseChecksumAssetName = ReleaseAssetName + ".sha256";
    private const string Dd340UtilityGoogleDriveFileId = "13WUMg8DRlzyFaIg8dcE8pL-VFxigdjS7";
    private const string Dd340UtilityFileName = "Utility_ATOL DD340.zip";
    private static readonly Regex ReleaseVersionPattern = new(
        @"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destinationIp, uint sourceIp, byte[] macAddress, ref int physicalAddressLength);

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
            "https://drive.google.com/drive/folders/1BHnFvJAW7yWbm7AaSs6dHX3nt0nNP6xQ",
            "https://drive.google.com/drive/folders/1BHnFvJAW7yWbm7AaSs6dHX3nt0nNP6xQ",
            "",
            "1So-KEVdDYpGN7B0O4PvLWVlEiFOy4hon",
            "Driver_ATOL_BP41.zip"),
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
        _modelBox.SelectedIndexChanged += (_, _) =>
        {
            var note = _models[_modelBox.Text].Note;
            if (!string.IsNullOrWhiteSpace(note))
                Log(note);
        };

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
        var panel = CreateStepPanel("2. Свободный IP и утилита", _freeIpStatusLabel);
        var layout = panel.Controls.OfType<TableLayoutPanel>().First();
        var buttons = GetButtonHost(panel);
        layout.Controls.Remove(buttons);

        var networkValues = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        networkValues.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        networkValues.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ConfigureNetworkResultBox(_ethernetIpBox);
        ConfigureNetworkResultBox(_ethernetMaskBox);
        ConfigureNetworkResultBox(_ethernetGatewayBox);
        AddRow(networkValues, 0, "Свободный IP", _ethernetIpBox);
        AddRow(networkValues, 1, "Маска", _ethernetMaskBox);
        AddRow(networkValues, 2, "Шлюз", _ethernetGatewayBox);
        layout.Controls.Add(networkValues);
        layout.Controls.Add(buttons);

        buttons.Controls.Add(CreateButton("Найти свободный IP", FindFreeIp));
        buttons.Controls.Add(CreateButton("Скачать или запустить утилиту", DownloadOrRunUtility));
        return panel;
    }

    private static void ConfigureNetworkResultBox(TextBox box)
    {
        box.ReadOnly = true;
        box.BackColor = Color.FromArgb(248, 250, 252);
        box.ForeColor = Color.FromArgb(35, 45, 60);
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    private Control CreateStep3Panel()
    {
        var panel = CreateStepPanel("3. Ping, драйвер и TCP/IP порт", _networkStatusLabel);
        var buttons = GetButtonHost(panel);
        buttons.Controls.Add(CreateButton("Проверить ping", CheckNetworkConnection));
        buttons.Controls.Add(CreateButton("Скачать и установить драйвер", DownloadDriver));
        buttons.Controls.Add(CreateButton("Создать TCP/IP порт", CheckNetworkAndCreatePort));
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

            var checksumAsset = latestRelease.Assets
                .FirstOrDefault(x => x.Name.Equals(ReleaseChecksumAssetName, StringComparison.OrdinalIgnoreCase));
            if (checksumAsset is null || string.IsNullOrWhiteSpace(checksumAsset.BrowserDownloadUrl))
            {
                Log($"Найдена версия {latestVersion}, но контрольная сумма {ReleaseChecksumAssetName} отсутствует. Обновление отменено.");
                return;
            }

            Log($"Найдена новая версия {latestVersion}. Скачиваю обновление...");
            var updatePath = await DownloadUpdateAssetAsync(
                asset.BrowserDownloadUrl,
                checksumAsset.BrowserDownloadUrl,
                latestVersion);
            Log("Обновление скачано и проверено. Программа сейчас перезапустится.");
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
            .Where(x => ReleaseVersionPattern.IsMatch(NormalizeVersion(x.TagName)))
            .OrderByDescending(x => ParseVersionForCompare(NormalizeVersion(x.TagName)))
            .FirstOrDefault();
    }

    private static async Task<string> DownloadUpdateAssetAsync(string url, string checksumUrl, string version)
    {
        EnsureGitHubReleaseAssetUrl(url);
        EnsureGitHubReleaseAssetUrl(checksumUrl);

        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ThermoPrinterTool");
        client.Timeout = TimeSpan.FromMinutes(5);

        var expectedHash = await DownloadExpectedSha256Async(client, checksumUrl);

        var updateDirectory = Path.Combine(Path.GetTempPath(), "ThermoPrinterTool", "updates");
        Directory.CreateDirectory(updateDirectory);
        var updatePath = Path.Combine(updateDirectory, $"ThermoPrinterTool-{version}.exe");
        var tempPath = updatePath + ".download";

        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(tempPath))
            {
                await input.CopyToAsync(output);
            }

            if (new FileInfo(tempPath).Length == 0)
                throw new InvalidOperationException("Скачанный файл обновления пустой.");

            await using var updateStream = File.OpenRead(tempPath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(updateStream));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Контрольная сумма обновления не совпадает. Файл не будет запущен.");

            File.Move(tempPath, updatePath, true);
            return updatePath;
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Preserve the original download or validation error.
            }
            throw;
        }
    }

    private static async Task<string> DownloadExpectedSha256Async(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 1024)
            throw new InvalidOperationException("Файл контрольной суммы обновления имеет неверный размер.");

        var content = await response.Content.ReadAsStringAsync();
        if (content.Length > 1024)
            throw new InvalidOperationException("Файл контрольной суммы обновления имеет неверный размер.");
        var expectedHash = content.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (expectedHash is null || expectedHash.Length != 64 || expectedHash.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("Файл контрольной суммы обновления имеет неверный формат.");

        return expectedHash;
    }

    private static void EnsureGitHubReleaseAssetUrl(string url)
    {
        var expectedPathPrefix = $"/{GitHubOwner}/{GitHubRepository}/releases/download/";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(expectedPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("GitHub вернул недопустимый адрес файла обновления.");
        }

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

        BeginInvoke(Application.Exit);
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
            return;
        }

        var usbDevices = GetUsbDeviceRows();
        var modelUsbDevice = usbDevices.FirstOrDefault(IsLikelyUsbDeviceForSelectedModel);
        if (modelUsbDevice is not null)
        {
            SetStatus(_usbStatusLabel, "USB найден", StatusKind.Warning);
            Log("Физическое USB-устройство найдено, но принтер Windows с подходящим драйвером не найден.");
            Log("Скорее всего нужно установить драйвер выбранной модели.");
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
        Log($"Свободный IP для принтера: {candidate}. Маска и шлюз показаны во втором шаге.");
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
        var currentNetworkIp = GetReachablePrinterIpForSdk();

        if (!string.IsNullOrWhiteSpace(currentNetworkIp) &&
            !currentNetworkIp.Equals(ip, StringComparison.OrdinalIgnoreCase))
        {
            Log($"Текущий адрес принтера в Windows: {currentNetworkIp}. После подключения ему будет назначен {ip}.");
        }

        try
        {
            var model = GetHprtSdkPrinterModel();
            var portSetting = HprtSdk.SetEthernet(model, dhcpEnabled, ip, mask, gateway, currentNetworkIp);
            Log($"Все Ethernet-команды переданы в принтер через {FormatSdkPort(portSetting)}.");
            VerifyEthernetSettingsAfterApply(model, portSetting, dhcpEnabled, ip, mask, gateway);
        }
        catch (Exception ex)
        {
            Log("Не удалось отправить Ethernet-настройки в принтер: " + ex.Message);
        }
    }

    private void VerifyEthernetSettingsAfterApply(
        string model,
        string portSetting,
        bool dhcpEnabled,
        string ip,
        string mask,
        string gateway)
    {
        if (!portSetting.Equals("USB", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(_networkStatusLabel, "Ожидает ping", StatusKind.Warning);
            Log("Настройки переданы по сети. Их применение нужно подтвердить после перезагрузки принтера кнопкой 'Проверить ping'.");
            return;
        }

        try
        {
            var actual = HprtSdk.TryReadEthernet(model, null);
            var matches = actual.DhcpEnabled == dhcpEnabled &&
                (dhcpEnabled ||
                    (Ipv4Equals(actual.Ip, ip) &&
                     Ipv4Equals(actual.Mask, mask) &&
                     Ipv4Equals(actual.Gateway, gateway)));

            if (matches)
            {
                Log("Ethernet-настройки подтверждены повторным чтением по USB.");
            }
            else
            {
                Log("Команды переданы, но повторное чтение пока не подтвердило новые значения. Возможно, принтер применит их после перезагрузки.");
            }
        }
        catch (Exception ex)
        {
            Log("Команды переданы, но автоматически перечитать настройки не удалось: " + ex.Message);
        }

        SetStatus(_networkStatusLabel, "Ожидает ping", StatusKind.Warning);
        Log("Перезагрузи принтер, подключи его к сети и нажми 'Проверить ping' для окончательной проверки.");
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
        var printerPortOpen = IsTcpPortOpen(ip, 9100, 2000);
        var hasMacAddress = TryGetArpMac(IPAddress.Parse(ip), out _);

        if (printerPortOpen && hasMacAddress)
        {
            SetStatus(_networkStatusLabel, "Подключен", StatusKind.Good);
            Log($"Принтер по сети отвечает, {reply.RoundtripTime} ms.");
        }
        else
        {
            SetStatus(_networkStatusLabel, "Не подключен", StatusKind.Bad);
            Log("Принтер по сети не отвечает.");
        }
    }

    private void CheckNetworkAndCreatePort()
    {
        var ip = GetPrinterIp();
        if (ip is null)
            return;

        Log($"Проверяю принтер перед созданием TCP/IP-порта: {ip}...");
        var hasMacAddress = TryGetArpMac(IPAddress.Parse(ip), out _);
        if (!hasMacAddress || !IsTcpPortOpen(ip, 9100, 2000))
        {
            SetStatus(_networkStatusLabel, "Не подключен", StatusKind.Bad);
            Log("Принтер по сети не отвечает. TCP/IP-порт Windows не создан.");
            Log("Сначала назначь IP в утилите, отключи USB и подключи принтер к сети.");
            return;
        }

        SetStatus(_networkStatusLabel, "Подключен", StatusKind.Good);
        CreateTcpIpPort();
    }

    private static bool IsTcpPortOpen(string ip, int port, int timeoutMilliseconds)
    {
        try
        {
            using var client = new TcpClient();
            using var cancellation = new CancellationTokenSource(timeoutMilliseconds);
            client.ConnectAsync(ip, port, cancellation.Token).AsTask().GetAwaiter().GetResult();
            return client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private static bool TryGetArpMac(IPAddress address, out string macAddress)
    {
        macAddress = "";
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = new byte[6];
        var length = bytes.Length;
        var destination = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
        var result = SendARP(destination, 0, bytes, ref length);
        if (result != 0 || length <= 0)
            return false;

        macAddress = string.Join("-", bytes.Take(length).Select(x => x.ToString("X2")));
        return !macAddress.Equals("00-00-00-00-00-00", StringComparison.OrdinalIgnoreCase);
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

    private static List<TcpIpPrinterPortRow> GetTcpIpPrinterPortRows()
    {
        var output = RunPowerShell("(Get-CimInstance Win32_TCPIPPrinterPort | Select-Object Name,HostAddress | ConvertTo-Csv -NoTypeInformation)");
        return ParseCsv(output)
            .Skip(1)
            .Select(fields => new TcpIpPrinterPortRow(
                fields.ElementAtOrDefault(0) ?? "",
                fields.ElementAtOrDefault(1) ?? ""))
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

            if (!TryGetArpMac(candidate, out _))
                return candidate;
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

    private void DownloadOrRunUtility()
    {
        var model = _models[GetSelectedModel()];
        try
        {
            if (model.Name.Equals("ATOL DD340", StringComparison.OrdinalIgnoreCase))
            {
                var utilityPath = Dd340UtilityService.GetOrDownload(Log);
                Log("Утилита ATOL DD340 готова. Открываю и подготавливаю настройки.");
                OpenAndPrepareHprtUtility(utilityPath);
                return;
            }

            if (model.Name.Equals("ATOL BP41", StringComparison.OrdinalIgnoreCase))
            {
                var utilityPath = Bp41UtilityService.GetOrDownload(Log);
                Log("Открываю RTLabelPrinterTool для ATOL BP41.");
                Bp41UtilityService.Run(utilityPath);
                return;
            }

            Log("Автоматическое скачивание утилиты для выбранной модели пока не настроено.");
            OpenUrl(model.UtilityUrl);
        }
        catch (Exception ex)
        {
            Log("Не удалось скачать или запустить утилиту: " + ex.Message);
            Log("Открываю папку Google Drive для ручного скачивания.");
            OpenUrl(model.UtilityUrl);
        }
    }

    private void OpenAndPrepareHprtUtility(string utilityPath)
    {
        var ip = GetText(_ethernetIpBox);
        var mask = GetText(_ethernetMaskBox);
        var gateway = GetText(_ethernetGatewayBox);
        var usbDevices = GetUsbDeviceRows();
        var printerConnectedByUsb = usbDevices.Any(IsLikelyUsbDeviceForSelectedModel) ||
            usbDevices.Any(IsGenericUsbPrinterDevice);

        HprtUtilityAutomation.OpenAndPrepare(
            utilityPath,
            ip,
            mask,
            gateway,
            printerConnectedByUsb,
            Log);
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

        var targetDirectory = GetLabelPrinterDirectory();
        var destination = Path.Combine(targetDirectory, model.DriverFileName);
        var extractDirectory = Path.Combine(targetDirectory, Path.GetFileNameWithoutExtension(model.DriverFileName));

        try
        {
            if (!Directory.Exists(extractDirectory) ||
                !Directory.EnumerateFiles(extractDirectory, "*", SearchOption.AllDirectories).Any())
            {
                Log("Скачиваю драйвер для " + model.Name + "...");
                Log("Папка сохранения: " + targetDirectory);
                DownloadGoogleDriveFile(model.DriverGoogleDriveFileId, destination);
                Directory.CreateDirectory(extractDirectory);
                Log("Распаковываю драйвер...");
                ZipFile.ExtractToDirectory(destination, extractDirectory, true);
                File.Delete(destination);
            }
            else
            {
                Log("Файлы драйвера уже скачаны.");
            }

            InstallSelectedDriverSilently(extractDirectory);
        }
        catch (Exception ex)
        {
            Log("Не удалось скачать или установить драйвер: " + ex.Message);
        }
    }

    private static string GetLabelPrinterDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var directory = Path.Combine(documents, "Label_printer");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string? FindFile(string directory, string fileName)
    {
        if (!Directory.Exists(directory))
            return null;

        return Directory
            .EnumerateFiles(directory, fileName, SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static void RunFile(string filePath)
    {
        Process.Start(new ProcessStartInfo(filePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory
        });
    }

    private void InstallSelectedDriverSilently(string packageDirectory)
    {
        if (IsAtolDd340Selected())
        {
            InstallDd340DriverSilently(packageDirectory);
            return;
        }

        if (IsAtolBp41Selected())
        {
            InstallBp41DriverSilently(packageDirectory);
            return;
        }

        Log("Тихая установка для выбранной модели пока не настроена.");
    }

    private void InstallDd340DriverSilently(string packageDirectory)
    {

        const string driverName = "HPRT HD200 - ZPL";
        if (GetPrinterDriverNames().Any(x => x.Equals(driverName, StringComparison.OrdinalIgnoreCase)))
        {
            Log("Драйвер HPRT HD200 - ZPL уже установлен.");
            RefreshPrinters();
            return;
        }

        if (!IsRunningAsAdministrator())
        {
            Log("Для тихой установки драйвера запусти программу от имени администратора.");
            return;
        }

        var driverWizard = PrepareDriverWizard(packageDirectory);
        if (string.IsNullOrWhiteSpace(driverWizard))
        {
            Log("Не удалось подготовить DriverWizard.exe для тихой установки.");
            return;
        }

        var temporaryPrinterName = "ThermoPrinterTool Driver Setup " + Environment.ProcessId;
        var driverInstalled = false;
        try
        {
            Log("Устанавливаю драйвер HPRT HD200 - ZPL...");
            var arguments = $"install /name:\"{temporaryPrinterName}\" /model:\"{driverName}\" /port:\"LPT1:\"";
            var exitCode = RunProcess(driverWizard, arguments, Path.GetDirectoryName(driverWizard));
            if (exitCode != 0)
            {
                Log($"Не удалось тихо установить драйвер. Код DriverWizard: {exitCode}.");
                return;
            }

            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (GetPrinterDriverNames().Any(x => x.Equals(driverName, StringComparison.OrdinalIgnoreCase)))
                {
                    driverInstalled = true;
                    break;
                }

                Thread.Sleep(500);
            }
        }
        finally
        {
            RemoveTemporaryPrinter(temporaryPrinterName);
        }

        if (driverInstalled)
        {
            Log("Драйвер HPRT HD200 - ZPL установлен.");
            RefreshPrinters();
        }
        else
        {
            Log("DriverWizard завершился без ошибки, но драйвер HPRT HD200 - ZPL не найден в Windows.");
        }
    }

    private void InstallBp41DriverSilently(string packageDirectory)
    {
        const string driverName = "Rongta RP4xx Series";
        if (GetPrinterDriverNames().Any(x => x.Equals(driverName, StringComparison.OrdinalIgnoreCase)))
        {
            Log("Драйвер Rongta RP4xx Series уже установлен.");
            RefreshPrinters();
            return;
        }

        if (!IsRunningAsAdministrator())
        {
            Log("Для тихой установки драйвера запусти программу от имени администратора.");
            return;
        }

        var installer = FindFile(packageDirectory, "PrnInst.exe");
        var infPath = FindFile(packageDirectory, "RONGTAOEM.inf");
        if (string.IsNullOrWhiteSpace(installer) || string.IsNullOrWhiteSpace(infPath))
        {
            Log("В пакете ATOL BP41 не найдены PrnInst.exe или RONGTAOEM.inf.");
            return;
        }

        var temporaryPrinterName = "ThermoPrinterTool BP41 Driver Setup " + Environment.ProcessId;
        var driverInstalled = false;
        try
        {
            Log("Устанавливаю драйвер Rongta RP4xx Series без открытия мастера...");
            var driverDirectory = Path.GetDirectoryName(infPath)!;
            var arguments = $"\"/DIR={driverDirectory}\" /NOLICENCE \"/INSTPRN={driverName}#LPT1:#{temporaryPrinterName}\"";
            var exitCode = RunProcess(installer, arguments, Path.GetDirectoryName(installer));
            if (exitCode != 0)
            {
                Log($"Не удалось тихо установить драйвер. Код PrnInst: {exitCode}.");
                return;
            }

            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (GetPrinterDriverNames().Any(x => x.Equals(driverName, StringComparison.OrdinalIgnoreCase)))
                {
                    driverInstalled = true;
                    break;
                }

                Thread.Sleep(500);
            }
        }
        finally
        {
            RemoveTemporaryPrinter(temporaryPrinterName);
        }

        if (driverInstalled)
        {
            Log("Драйвер Rongta RP4xx Series установлен.");
            RefreshPrinters();
        }
        else
        {
            Log("PrnInst завершился без ошибки, но драйвер Rongta RP4xx Series не найден в Windows.");
        }
    }

    private string? PrepareDriverWizard(string packageDirectory)
    {
        var driverWizard = FindDriverWizard(packageDirectory) ?? FindInstalledSeagullDriverWizard();
        if (!string.IsNullOrWhiteSpace(driverWizard))
            return driverWizard;

        var packageInstaller = Directory
            .EnumerateFiles(packageDirectory, "*.exe", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).Equals("DriverWizard.exe", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => Path.GetFileName(path).Contains("HPRT", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(packageInstaller))
            return null;

        var extractedDirectory = Path.Combine(packageDirectory, "Seagull");
        if (!TryExtractSeagullPackage(packageInstaller, extractedDirectory))
            return null;

        return FindDriverWizard(extractedDirectory) ?? FindInstalledSeagullDriverWizard();
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

    private bool TryExtractSeagullPackage(string packageInstallerPath, string extractedDirectory)
    {
        try
        {
            Directory.CreateDirectory(extractedDirectory);
            var exitCode = RunProcess(packageInstallerPath, $"/x \"{extractedDirectory}\"", Path.GetDirectoryName(packageInstallerPath));
            Log(exitCode == 0
                ? "Пакет драйвера распакован для тихой установки."
                : $"Не удалось тихо распаковать пакет драйвера, код {exitCode}.");
            return exitCode == 0;
        }
        catch (Exception ex)
        {
            Log("Не удалось подготовить тихую установку: " + ex.Message);
            return false;
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RemoveTemporaryPrinter(string printerName)
    {
        RunPowerShell("Remove-Printer -Name '" + EscapePowerShell(printerName) + "' -ErrorAction SilentlyContinue");
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
            Log(IsAtolDd340Selected()
                ? "Драйвер HPRT HD200 - ZPL не установлен. Сначала нажми 'Скачать и установить драйвер'."
                : "Не удалось создать принтер Windows: установи драйвер выбранной модели и повтори создание порта.");
            return;
        }

        var script = "$printer = Get-Printer -Name '" + EscapePowerShell(roleName) + "' -ErrorAction SilentlyContinue; " +
            "$printerOnPort = Get-Printer | Where-Object { $_.PortName -eq '" + EscapePowerShell(portName) + "' } | Select-Object -First 1; " +
            "if ($null -eq $printer) { " +
            "if ($null -ne $printerOnPort) { " +
            "Rename-Printer -Name $printerOnPort.Name -NewName '" + EscapePowerShell(roleName) + "'; " +
            "Set-Printer -Name '" + EscapePowerShell(roleName) + "' -DriverName '" + EscapePowerShell(driverName) + "' -PortName '" + EscapePowerShell(portName) + "' " +
            "} else { " +
            "Add-Printer -Name '" + EscapePowerShell(roleName) + "' -DriverName '" + EscapePowerShell(driverName) + "' -PortName '" + EscapePowerShell(portName) + "' " +
            "} " +
            "} else { " +
            "Set-Printer -Name '" + EscapePowerShell(roleName) + "' -DriverName '" + EscapePowerShell(driverName) + "' -PortName '" + EscapePowerShell(portName) + "' " +
            "}";
        var output = RunPowerShell(script);
        Log(string.IsNullOrWhiteSpace(output)
            ? $"Принтер Windows готов: {roleName}, драйвер {driverName}, порт {portName}."
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
        var installedDrivers = GetPrinterDriverNames();
        if (IsAtolDd340Selected())
        {
            return installedDrivers.FirstOrDefault(x =>
                    x.Equals("HPRT HD200 - ZPL", StringComparison.OrdinalIgnoreCase)) ??
                installedDrivers.FirstOrDefault(x =>
                    x.Contains("HPRT HD200", StringComparison.OrdinalIgnoreCase) &&
                    x.Contains("ZPL", StringComparison.OrdinalIgnoreCase));
        }

        if (IsAtolBp41Selected())
        {
            return installedDrivers.FirstOrDefault(x =>
                    x.Equals("Rongta RP4xx Series", StringComparison.OrdinalIgnoreCase)) ??
                installedDrivers.FirstOrDefault(x =>
                    x.Contains("Rongta RP4", StringComparison.OrdinalIgnoreCase));
        }

        var printers = GetPrinterRows();
        var printer = FindBestInstalledPrinterForModel(printers);
        if (!string.IsNullOrWhiteSpace(printer?.Driver))
            return printer.Driver;

        return installedDrivers.FirstOrDefault(IsLikelyDriverForSelectedModel);
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
            "ATOL DD340" => new[] { "DD340", "HD200" },
            "ATOL BP41" => new[] { "BP41", "RP4xx", "Rongta RP4" },
            "TSC DA220" => new[] { "DA220" },
            "BIXOLON XD3-40d" => new[] { "XD3-40", "XD3" },
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
        if (!TryParseIpv4(ip, out _))
        {
            Log("Сначала нажми 'Найти свободный IP'.");
            return null;
        }
        return ip;
    }

    private string? GetReachablePrinterIpForSdk()
    {
        var candidates = new List<string>();
        var printers = GetPrinterRows();
        var roleName = GetSelectedRole();
        var matchingPrinters = printers
            .Where(x => x.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase) ||
                IsConfiguredDd340Printer(x))
            .OrderByDescending(x => x.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var tcpIpPorts = GetTcpIpPrinterPortRows()
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().HostAddress, StringComparer.OrdinalIgnoreCase);

        foreach (var printer in matchingPrinters)
        {
            if (tcpIpPorts.TryGetValue(printer.Port, out var hostAddress))
                AddIpv4Candidate(candidates, hostAddress);

            AddIpv4Candidate(candidates, printer.Port);
            if (printer.Port.StartsWith("IP_", StringComparison.OrdinalIgnoreCase))
                AddIpv4Candidate(candidates, printer.Port[3..]);
        }

        AddIpv4Candidate(candidates, GetText(_ethernetIpBox));

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(IsIpv4Reachable);
    }

    private static bool IsConfiguredDd340Printer(PrinterRow printer)
    {
        var text = printer.Name + " " + printer.Driver;
        return text.Contains("DD340", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("HD200", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddIpv4Candidate(List<string> candidates, string value)
    {
        if (TryParseIpv4(value, out var address))
            candidates.Add(address.ToString());
    }

    private static bool IsIpv4Reachable(string ip)
    {
        if (!TryParseIpv4(ip, out _))
            return false;

        try
        {
            using var ping = new Ping();
            var reply = ping.Send(ip, 700);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static bool Ipv4Equals(string left, string right) =>
        TryParseIpv4(left, out var leftAddress) &&
        TryParseIpv4(right, out var rightAddress) &&
        leftAddress.Equals(rightAddress);

    private static bool TryParseIpv4(string value, out IPAddress address)
    {
        var success = IPAddress.TryParse(value, out var parsed) &&
            parsed.AddressFamily == AddressFamily.InterNetwork;
        address = success ? parsed! : IPAddress.None;
        return success;
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

    private bool IsAtolBp41Selected() => GetSelectedModel().Equals("ATOL BP41", StringComparison.OrdinalIgnoreCase);

    private string GetHprtSdkPrinterModel() => IsAtolDd340Selected() ? "HD200" : GetSelectedModel();

    private static string GetHprtSdkPath() => "ESC_SDK.dll";

    private bool ValidateEthernetAddress(string value, string label)
    {
        if (TryParseIpv4(value, out _))
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

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(8000))
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // The process may have exited between the timeout and Kill.
            }

            Task.WhenAll(outputTask, errorTask).GetAwaiter().GetResult();
            return "PowerShell не завершил операцию за 8 секунд.";
        }

        Task.WhenAll(outputTask, errorTask).GetAwaiter().GetResult();
        var output = outputTask.Result;
        var error = errorTask.Result;
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

    private sealed record TcpIpPrinterPortRow(string Name, string HostAddress);

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
        private const string HprtIoDllPath = "hprtio.dll";
        private const int LangId = 0;
        private const string UsbPort = "USB";
        private const string ResourcePrefix = "HPRT.";
        private const CharSet SdkCharSet = CharSet.Unicode;
        private static readonly object NativeLock = new();
        private static string? _nativeDirectory;

        private sealed class PartialHprtWriteException : InvalidOperationException
        {
            public PartialHprtWriteException(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }

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

        [DllImport(HprtIoDllPath, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern int hprt_io_port_creator(out IntPtr port, string uri);

        [DllImport(HprtIoDllPath, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int hprt_io_port_destroy(IntPtr port);

        [DllImport(HprtIoDllPath, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int hprt_io_open(IntPtr port);

        [DllImport(HprtIoDllPath, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int hprt_io_close(IntPtr port);

        [DllImport(HprtIoDllPath, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int hprt_io_write(IntPtr port, byte[] buffer, int offset, int length, out int written);

        public static string SetEthernet(string model, bool dhcpEnabled, string ip, string mask, string gateway, string? networkIp)
        {
            EnsureX86();
            Exception? directError = null;
            try
            {
                return SetEthernetDirect(dhcpEnabled, ip, mask, gateway, networkIp);
            }
            catch (PartialHprtWriteException)
            {
                throw;
            }
            catch (Exception ex)
            {
                directError = ex;
            }

            try
            {
                var mode = dhcpEnabled ? 1 : 0;
                return WithOpenAutoPrinter(model, networkIp, printer =>
                {
                    var result = SetEthernetInfo(printer, mode, ip, mask, gateway);
                    ThrowIfFailed(result, "назначить Ethernet-настройки");
                });
            }
            catch (Exception sdkError)
            {
                throw new InvalidOperationException(
                    $"Прямая команда HPRT: {directError?.Message} | ESC SDK: {sdkError.Message}", sdkError);
            }
        }

        private static string SetEthernetDirect(bool dhcpEnabled, string ip, string mask, string gateway, string? networkIp)
        {
            var errors = new List<string>();
            foreach (var portSetting in GetPortCandidates(networkIp))
            {
                var commandsWritten = 0;
                try
                {
                    WithOpenDirectPort(portSetting, port =>
                    {
                        var commands = BuildEthernetCommands(portSetting, dhcpEnabled, ip, mask, gateway);

                        foreach (var command in commands)
                        {
                            WriteDirect(port, command);
                            commandsWritten++;
                            Thread.Sleep(80);
                        }
                    });
                    return portSetting;
                }
                catch (Exception ex)
                {
                    if (commandsWritten > 0)
                    {
                        throw new PartialHprtWriteException(
                            $"Передано команд: {commandsWritten} из 4 через {portSetting}. " +
                            "Повторная отправка другим способом остановлена, чтобы не получить смешанные настройки. " +
                            "Перезагрузи принтер, прочитай текущие значения и повтори назначение.", ex);
                    }

                    errors.Add($"{portSetting}: {ex.Message}");
                }
            }

            throw new InvalidOperationException("Не удалось отправить прямые команды HPRT. " + string.Join(" | ", errors));
        }

        private static IReadOnlyList<byte[]> BuildEthernetCommands(
            string portSetting,
            bool dhcpEnabled,
            string ip,
            string mask,
            string gateway)
        {
            if (!portSetting.StartsWith("NET,", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    HprtEthernetProtocol.BuildDhcp(dhcpEnabled),
                    HprtEthernetProtocol.BuildIp(ip),
                    HprtEthernetProtocol.BuildMask(mask),
                    HprtEthernetProtocol.BuildGateway(gateway)
                };
            }

            return dhcpEnabled
                ? new[]
                {
                    HprtEthernetProtocol.BuildMask(mask),
                    HprtEthernetProtocol.BuildGateway(gateway),
                    HprtEthernetProtocol.BuildIp(ip),
                    HprtEthernetProtocol.BuildDhcp(true)
                }
                : new[]
                {
                    HprtEthernetProtocol.BuildMask(mask),
                    HprtEthernetProtocol.BuildGateway(gateway),
                    HprtEthernetProtocol.BuildDhcp(false),
                    HprtEthernetProtocol.BuildIp(ip)
                };
        }

        private static void WithOpenDirectPort(string portSetting, Action<IntPtr> action)
        {
            var port = IntPtr.Zero;
            var opened = false;
            try
            {
                var createResult = hprt_io_port_creator(out port, portSetting);
                ThrowIfHprtIoFailed(createResult, $"создать порт {portSetting}");
                if (port == IntPtr.Zero)
                    throw new InvalidOperationException("hprtio.dll вернула пустой объект порта.");

                var openResult = hprt_io_open(port);
                ThrowIfHprtIoFailed(openResult, $"открыть порт {portSetting}");
                opened = true;
                action(port);
            }
            finally
            {
                if (opened)
                    hprt_io_close(port);
                if (port != IntPtr.Zero)
                    hprt_io_port_destroy(port);
            }
        }

        private static void WriteDirect(IntPtr port, byte[] command)
        {
            var result = hprt_io_write(port, command, 0, command.Length, out var written);
            ThrowIfHprtIoFailed(result, "записать команду в принтер");
            if (written != command.Length)
                throw new IOException($"hprtio.dll записала {written} из {command.Length} байт.");
        }

        private static void ThrowIfHprtIoFailed(int result, string action)
        {
            if (result == 0)
                return;

            throw new InvalidOperationException($"Не удалось {action}. Код hprtio: {result}.");
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
            if (!libraryName.Equals(DllPath, StringComparison.OrdinalIgnoreCase) &&
                !libraryName.Equals(HprtIoDllPath, StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;

            var nativeDirectory = EnsureNativeLibrariesExtracted();
            var libraryPath = Path.Combine(nativeDirectory, libraryName);
            return NativeLibrary.Load(libraryPath);
        }

        private static string EnsureNativeLibrariesExtracted()
        {
            lock (NativeLock)
            {
                if (!string.IsNullOrWhiteSpace(_nativeDirectory) &&
                    File.Exists(Path.Combine(_nativeDirectory, DllPath)) &&
                    File.Exists(Path.Combine(_nativeDirectory, HprtIoDllPath)))
                {
                    return _nativeDirectory;
                }

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

                    if (ResourceMatchesFile(resource, targetPath))
                        continue;

                    resource.Position = 0;
                    using var output = File.Create(targetPath);
                    resource.CopyTo(output);
                }

                var sdkPath = Path.Combine(targetDirectory, DllPath);
                if (!File.Exists(sdkPath))
                    throw new FileNotFoundException("ESC_SDK.dll не найден среди распакованных HPRT DLL.", sdkPath);

                var hprtIoPath = Path.Combine(targetDirectory, HprtIoDllPath);
                if (!File.Exists(hprtIoPath))
                    throw new FileNotFoundException("hprtio.dll не найден среди распакованных HPRT DLL.", hprtIoPath);

                _nativeDirectory = targetDirectory;
                return targetDirectory;
            }
        }

        private static bool ResourceMatchesFile(Stream resource, string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            resource.Position = 0;
            var resourceHash = SHA256.HashData(resource);
            resource.Position = 0;

            using var file = File.OpenRead(filePath);
            var fileHash = SHA256.HashData(file);
            return resourceHash.AsSpan().SequenceEqual(fileHash);
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
