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

namespace ThermoPrinterTool;

public partial class Form1 : Form
{
    private const string GitHubOwner = "npakhomov";
    private const string GitHubRepository = "termoprinter-utility";
    private const string ReleaseAssetName = "ThermoPrinterTool.exe";
    private const string Dd340UtilityGoogleDriveFileId = "13WUMg8DRlzyFaIg8dcE8pL-VFxigdjS7";
    private const string Dd340UtilityFileName = "Utility_ATOL DD340.zip";

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
           …16662 tokens truncated…{
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
