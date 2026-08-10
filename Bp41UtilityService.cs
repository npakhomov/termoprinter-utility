using System.Diagnostics;
using System.IO.Compression;

namespace ThermoPrinterTool;

internal static class Bp41UtilityService
{
    private const string GoogleDriveFileId = "1ACTuf7Fs5d_uNUI6kA2H4_Bz4GUYq7FB";
    private const string ArchiveFileName = "Utility_ATOL_BP41.zip";
    private const string ExecutableFileName = "RTLabelPrinterTool.exe";

    public static string GetOrDownload(Action<string> log)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var targetDirectory = Path.Combine(documents, "Label_printer", "ATOL BP41");
        Directory.CreateDirectory(targetDirectory);

        var utilityPath = FindFile(targetDirectory, ExecutableFileName);
        if (!string.IsNullOrWhiteSpace(utilityPath))
        {
            log("Утилита ATOL BP41 уже скачана. Запускаю её.");
            return utilityPath;
        }

        var archivePath = Path.Combine(targetDirectory, ArchiveFileName);
        log("Скачиваю утилиту для ATOL BP41...");
        DownloadGoogleDriveFile(GoogleDriveFileId, archivePath);
        log("Распаковываю утилиту в папку Label_printer\\ATOL BP41...");
        ZipFile.ExtractToDirectory(archivePath, targetDirectory, true);

        utilityPath = FindFile(targetDirectory, ExecutableFileName);
        if (string.IsNullOrWhiteSpace(utilityPath))
            throw new FileNotFoundException($"В архиве не найден {ExecutableFileName}.");

        File.Delete(archivePath);
        return utilityPath;
    }

    public static void Run(string utilityPath)
    {
        Process.Start(new ProcessStartInfo(utilityPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(utilityPath) ?? Environment.CurrentDirectory
        });
    }

    private static string? FindFile(string directory, string fileName)
    {
        if (!Directory.Exists(directory))
            return null;

        return Directory.EnumerateFiles(directory, fileName, SearchOption.AllDirectories).FirstOrDefault();
    }

    private static void DownloadGoogleDriveFile(string fileId, string destination)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        client.Timeout = TimeSpan.FromMinutes(10);
        var url = "https://drive.usercontent.google.com/download?id=" +
            Uri.EscapeDataString(fileId) + "&export=download&confirm=t";

        using var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Google Drive не отдал файл напрямую.");

        var temporaryPath = destination + ".download";
        using (var input = response.Content.ReadAsStream())
        using (var output = File.Create(temporaryPath))
            input.CopyTo(output);

        if (new FileInfo(temporaryPath).Length == 0)
            throw new InvalidOperationException("Скачанный файл пустой.");

        File.Move(temporaryPath, destination, true);
    }
}
