using System.Diagnostics;

namespace ThermoPrinterTool;

internal static class Bp41UtilityService
{
    public static string GetOrDownload(Action<string> log) => TrustedPackages.FindRequiredFile(
        TrustedPackages.Prepare(TrustedPackages.Bp41Utility, log), "RTLabelPrinterTool.exe");

    public static void Run(string utilityPath) => Process.Start(new ProcessStartInfo(utilityPath)
    {
        UseShellExecute = true,
        WorkingDirectory = Path.GetDirectoryName(utilityPath)!
    });
}
