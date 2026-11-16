namespace ThermoPrinterTool;

internal static class Dd340UtilityService
{
    public static string GetOrDownload(Action<string> log) => TrustedPackages.FindRequiredFile(
        TrustedPackages.Prepare(TrustedPackages.Dd340Utility, log), "HPRTPrinterUtility.exe");
}
