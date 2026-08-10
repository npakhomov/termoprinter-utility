using System.IO.Compression;

namespace ThermoPrinterTool;

internal static class Dd340UtilityService
{
    private const string GoogleDriveFileId = "13WUMg8DRlzyFaIg8dcE8pL-VFxigdjS7";
    private const string ArchiveFileName = "Utility_ATOL DD340.zip";
    private const string ExecutableFileName = "HPRTPrinterUtility.exe";

    public static string GetOrDownload(Action<string> log)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var targetDirectory = Path.Combine(documents, "Label_printer");
        Directory.CreateDirectory(targetDirectory);

        var developmentUtility = Path.Combine(documents, "HPRTPrinterUtility", ExecutableFileName);
        var utilityPath = FindFile(targetDirectory, ExecutableFileName) ??
            (File.Exists(developmentUtility) ? developmentUtility : null);
        if (!string.IsNullOrWhiteSpace(utilityPath))
        {
            log("Утилита ATOL DD340 уже скачана. Запускаю её.");
            return utilityPath;
        }

        var archivePath = Path.Combine(targetDirectory, ArchiveFileName);
        log("Скачиваю утилиту для ATOL DD340...");
        DownloadGoogleDriveFile(GoogleDriveFileId, archivePath);
        log("Распаковываю утилиту в папку Label_printer...");
        ZipFile.ExtractToDirectory(archivePath, targetDirectory, true);

        utilityPath = FindFile(targetDirectory, ExecutableFileName);
        if (string.IsNullOrWhiteSpace(utilityPath))
            throw new FileNotFoundException("В архиве не найден HPRTPrinterUtility.exe.");

        File.Delete(archivePath);
        return utilityPath;
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
