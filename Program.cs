namespace ThermoPrinterTool;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        if (args.Length > 0)
            return ElevatedTasks.Handle(args);
        try
        {
            Application.Run(new StartForm());
        }
        finally
        {
            try { SupportLifetime.ScheduleDeletion(); }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось запланировать удаление утилиты: " + ex.Message +
                    "\nУдалите ThermoPrinterTool.exe вручную.", "Завершение работы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        return 0;
    }    
}
