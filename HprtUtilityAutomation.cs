using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace ThermoPrinterTool;

internal static class HprtUtilityAutomation
{
    private const uint WmCommand = 0x0111;
    private const uint WmSetText = 0x000C;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint BmClick = 0x00F5;
    private const uint CbGetCount = 0x0146;
    private const uint CbGetLbText = 0x0148;
    private const uint CbGetLbTextLen = 0x0149;
    private const uint CbSetCurSel = 0x014E;
    private const uint TcmGetItemCount = 0x1304;
    private const uint TcmGetItemRect = 0x130A;
    private const int CbnSelChange = 1;

    public static void OpenAndPrepare(
        string utilityPath,
        string ip,
        string mask,
        string gateway,
        bool printerConnectedByUsb,
        Action<string> log)
    {
        var process = GetOrStartUtility(utilityPath);
        var mainWindow = WaitForMainWindow(process, TimeSpan.FromSeconds(12));
        if (mainWindow == IntPtr.Zero)
            throw new InvalidOperationException("Окно HPRT Printer Utility не открылось.");

        if (!SelectComboItem(mainWindow, "HD200"))
            throw new InvalidOperationException("В утилите не найден профиль HD200.");

        Thread.Sleep(500);
        if (!SelectComboItem(mainWindow, "USB"))
            throw new InvalidOperationException("В утилите не найден интерфейс USB.");

        Thread.Sleep(700);
        log("В утилите выбраны профиль HD200 и подключение USB.");

        if (!printerConnectedByUsb)
        {
            DismissKnownPortError(process.Id, mainWindow);
            log("Термопринтер по USB не найден. Подключи его и снова нажми кнопку запуска утилиты.");
            BringWindowToFront(mainWindow);
            return;
        }

        if (HasBlockingDialog(process.Id, mainWindow))
        {
            log("Утилита не смогла открыть USB-порт принтера. Проверь кабель и питание.");
            BringWindowToFront(mainWindow);
            return;
        }

        if (!TryParseNetworkValues(ip, mask, gateway))
        {
            log("Сначала найди свободный IP, чтобы заполнить настройки Ethernet.");
            BringWindowToFront(mainWindow);
            return;
        }

        var requestButton = FindButton(mainWindow, "Запросить");
        if (requestButton == IntPtr.Zero)
            throw new InvalidOperationException("В утилите не найдена кнопка 'Запросить'.");

        log("Запрашиваю текущие настройки принтера по USB...");
        PostMessage(requestButton, BmClick, IntPtr.Zero, IntPtr.Zero);
        if (!WaitForRequest(process, mainWindow, requestButton, TimeSpan.FromSeconds(30)))
        {
            log("Не удалось прочитать настройки принтера. Проверь USB-подключение и сообщение утилиты.");
            BringWindowToFront(mainWindow);
            return;
        }

        if (!SelectEthernetTab(mainWindow))
            throw new InvalidOperationException("В утилите не найдена вкладка Ethernet.");

        Thread.Sleep(500);
        if (!SelectComboItem(mainWindow, "Выключено", "Выключен"))
            throw new InvalidOperationException("В разделе Ethernet не найден переключатель DHCP.");

        if (!FillEthernetFields(mainWindow, ip, mask, gateway))
            throw new InvalidOperationException("Не удалось определить поля IP, маски и шлюза.");

        log("Ethernet заполнен. Проверь значения в утилите и подтверди назначение вручную.");
        BringWindowToFront(mainWindow);
    }

    private static Process GetOrStartUtility(string utilityPath)
    {
        using var current = Process.GetCurrentProcess();
        foreach (var candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(utilityPath)))
        {
            using (candidate)
            {
                if (candidate.HasExited || candidate.MainWindowHandle == IntPtr.Zero)
                    continue;
                if (candidate.SessionId != current.SessionId)
                    continue;
                throw new InvalidOperationException("Закройте ранее запущенную HPRT Printer Utility и повторите запуск проверенной копии.");
            }
        }

        var process = Process.Start(new ProcessStartInfo(utilityPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(utilityPath) ?? Environment.CurrentDirectory
        });

        return process ?? throw new InvalidOperationException("Не удалось запустить HPRT Printer Utility.");
    }

    private static IntPtr WaitForMainWindow(Process process, TimeSpan timeout)
    {
        try
        {
            process.WaitForInputIdle((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue));
        }
        catch
        {
            // Some Delphi builds do not report input-idle reliably.
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
                return process.MainWindowHandle;
            Thread.Sleep(200);
        }

        return IntPtr.Zero;
    }

    private static bool SelectComboItem(IntPtr mainWindow, params string[] acceptedValues)
    {
        foreach (var combo in GetDescendants(mainWindow).Where(IsComboBox))
        {
            var items = GetComboItems(combo);
            var match = items.FirstOrDefault(x => acceptedValues.Any(value =>
                x.Text.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                x.Text.StartsWith(value, StringComparison.OrdinalIgnoreCase)));
            if (match is null)
                continue;

            SendMessage(combo, CbSetCurSel, (IntPtr)match.Index, IntPtr.Zero);
            var controlId = GetDlgCtrlID(combo) & 0xFFFF;
            var command = (CbnSelChange << 16) | controlId;
            PostMessage(GetParent(combo), WmCommand, (IntPtr)command, combo);
            return true;
        }

        return false;
    }

    private static List<ComboItem> GetComboItems(IntPtr combo)
    {
        var count = (int)SendMessage(combo, CbGetCount, IntPtr.Zero, IntPtr.Zero);
        var result = new List<ComboItem>();
        if (count <= 0 || count > 1000)
            return result;

        for (var index = 0; index < count; index++)
        {
            var length = (int)SendMessage(combo, CbGetLbTextLen, (IntPtr)index, IntPtr.Zero);
            if (length < 0 || length > 4096)
                continue;

            var text = new StringBuilder(length + 2);
            SendMessage(combo, CbGetLbText, (IntPtr)index, text);
            result.Add(new ComboItem(index, text.ToString()));
        }

        return result;
    }

    private static IntPtr FindButton(IntPtr mainWindow, string caption)
    {
        return GetDescendants(mainWindow)
            .FirstOrDefault(handle =>
                IsButton(handle) && GetText(handle).Equals(caption, StringComparison.OrdinalIgnoreCase));
    }

    private static bool WaitForRequest(Process process, IntPtr mainWindow, IntPtr button, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var minimumWait = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        var wasDisabled = false;

        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited || HasBlockingDialog(process.Id, mainWindow))
                return false;

            var enabled = IsWindowEnabled(button);
            wasDisabled |= !enabled;
            if (DateTime.UtcNow >= minimumWait && (wasDisabled ? enabled : true))
                return true;

            Thread.Sleep(250);
        }

        return false;
    }

    private static bool HasBlockingDialog(int processId, IntPtr mainWindow)
    {
        var found = false;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var ownerProcessId);
            if (ownerProcessId == processId && handle != mainWindow && IsWindowVisible(handle))
            {
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static void DismissKnownPortError(int processId, IntPtr mainWindow)
    {
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var ownerProcessId);
            if (ownerProcessId != processId || handle == mainWindow || !IsWindowVisible(handle))
                return true;

            var text = GetText(handle);
            if (!text.Contains("ошибка", StringComparison.OrdinalIgnoreCase) &&
                !text.Contains("error", StringComparison.OrdinalIgnoreCase))
                return true;

            var okButton = FindButton(handle, "ОК");
            if (okButton == IntPtr.Zero)
                okButton = FindButton(handle, "OK");
            if (okButton != IntPtr.Zero)
                PostMessage(okButton, BmClick, IntPtr.Zero, IntPtr.Zero);
            return false;
        }, IntPtr.Zero);
    }

    private static bool SelectEthernetTab(IntPtr mainWindow)
    {
        foreach (var tab in GetDescendants(mainWindow))
        {
            var count = (int)SendMessage(tab, TcmGetItemCount, IntPtr.Zero, IntPtr.Zero);
            if (count < 4 || count > 20)
                continue;

            var rect = new Rect();
            if (SendMessage(tab, TcmGetItemRect, (IntPtr)3, ref rect) == IntPtr.Zero)
                continue;

            var x = (rect.Left + rect.Right) / 2;
            var y = (rect.Top + rect.Bottom) / 2;
            var coordinates = (IntPtr)((y << 16) | (x & 0xFFFF));
            PostMessage(tab, WmLButtonDown, (IntPtr)1, coordinates);
            PostMessage(tab, WmLButtonUp, IntPtr.Zero, coordinates);
            return true;
        }

        return false;
    }

    private static bool FillEthernetFields(IntPtr mainWindow, string ip, string mask, string gateway)
    {
        GetWindowRect(mainWindow, out var mainRect);
        var edits = GetDescendants(mainWindow)
            .Where(handle => IsEdit(handle) && IsWindowVisible(handle) && IsWindowEnabled(handle))
            .Select(handle => new WindowPosition(handle, GetRect(handle)))
            .Where(x => x.Rect.Left - mainRect.Left > 180)
            .Where(x => x.Rect.Top - mainRect.Top is > 20 and < 240)
            .OrderBy(x => x.Rect.Top)
            .ThenBy(x => x.Rect.Left)
            .ToList();

        var rows = GroupRows(edits)
            .Where(row => row.Count >= 4)
            .Take(3)
            .ToList();
        if (rows.Count != 3)
            return false;

        var values = new[] { ip, mask, gateway };
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var octets = values[rowIndex].Split('.');
            if (octets.Length != 4)
                return false;

            var controls = rows[rowIndex].OrderBy(x => x.Rect.Left).Take(4).ToArray();
            for (var column = 0; column < 4; column++)
                SendMessage(controls[column].Handle, WmSetText, IntPtr.Zero, octets[column]);
        }

        return true;
    }

    private static List<List<WindowPosition>> GroupRows(List<WindowPosition> controls)
    {
        var rows = new List<List<WindowPosition>>();
        foreach (var control in controls)
        {
            var row = rows.FirstOrDefault(x => Math.Abs(x[0].Rect.Top - control.Rect.Top) <= 5);
            if (row is null)
            {
                row = new List<WindowPosition>();
                rows.Add(row);
            }
            row.Add(control);
        }
        return rows.OrderBy(x => x[0].Rect.Top).ToList();
    }

    private static bool TryParseNetworkValues(string ip, string mask, string gateway) =>
        IPAddress.TryParse(ip, out _) &&
        IPAddress.TryParse(mask, out _) &&
        IPAddress.TryParse(gateway, out _);

    private static List<IntPtr> GetDescendants(IntPtr parent)
    {
        var result = new List<IntPtr>();
        EnumChildWindows(parent, (handle, _) =>
        {
            result.Add(handle);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static bool IsComboBox(IntPtr handle) => GetClass(handle).Contains("ComboBox", StringComparison.OrdinalIgnoreCase);
    private static bool IsButton(IntPtr handle) => GetClass(handle).Contains("Button", StringComparison.OrdinalIgnoreCase);
    private static bool IsEdit(IntPtr handle) => GetClass(handle).Contains("Edit", StringComparison.OrdinalIgnoreCase);

    private static string GetClass(IntPtr handle)
    {
        var value = new StringBuilder(128);
        GetClassName(handle, value, value.Capacity);
        return value.ToString();
    }

    private static string GetText(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        var value = new StringBuilder(length + 2);
        GetWindowText(handle, value, value.Capacity);
        return value.ToString();
    }

    private static Rect GetRect(IntPtr handle)
    {
        GetWindowRect(handle, out var rect);
        return rect;
    }

    private static void BringWindowToFront(IntPtr handle)
    {
        ShowWindow(handle, 9);
        SetForegroundWindow(handle);
    }

    private sealed record ComboItem(int Index, string Text);
    private sealed record WindowPosition(IntPtr Handle, Rect Rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder className, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out int processId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, string lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, ref Rect lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
}
