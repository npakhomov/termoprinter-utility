namespace ThermoPrinterTool;

internal sealed class StartForm : Form
{
    private static readonly Color AccentColor = Color.FromArgb(31, 111, 235);

    public StartForm()
    {
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Text = "ThermoPrinterTool";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 365);
        MinimumSize = new Size(620, 365);
        MaximizeBox = false;
        BackColor = Color.FromArgb(244, 246, 249);
        Font = new Font("Segoe UI", 9F);

        BuildInterface();
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(38, 34, 38, 34)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "Подключение принтера",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = Color.FromArgb(24, 31, 42),
            Margin = new Padding(0)
        }, 0, 0);

        var scenarioPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 30, 0, 0)
        };
        scenarioPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        scenarioPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.Controls.Add(scenarioPanel, 0, 1);

        scenarioPanel.Controls.Add(CreateScenarioButton(
            "Диагностика проблем с подключенным принтером",
            () => OpenScenario(() => new DiagnosticForm())), 0, 0);
        scenarioPanel.Controls.Add(CreateScenarioButton(
            "Настройка принтера с нуля",
            () => OpenScenario(() => new Form1())), 0, 1);

        root.Controls.Add(new Label
        {
            Text = SupportLifetime.WillDeleteOnExit
                ? "После завершения работы файл утилиты будет удален."
                : "Рабочая копия разработчика: автоудаление отключено.",
            AutoSize = true,
            ForeColor = Color.FromArgb(92, 101, 115),
            Margin = new Padding(0, 18, 0, 0)
        }, 0, 2);
    }

    private static Button CreateScenarioButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Height = 58,
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentColor,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Margin = new Padding(0, 0, 0, 10),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(25, 91, 192);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 78, 166);
        button.Click += (_, _) => action();
        return button;
    }

    private void OpenScenario(Func<Form> createForm)
    {
        Hide();
        try
        {
            using var form = createForm();
            form.ShowDialog(this);
        }
        finally
        {
            if (!IsDisposed)
            {
                Show();
                Activate();
            }
        }
    }
}
