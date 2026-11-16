using System.IO.Compression;
using System.Security.Cryptography;

namespace ThermoPrinterTool;

internal sealed record TrustedPackage(string Id, string DriveId, string Sha256);

internal static class TrustedPackages
{
    internal static readonly TrustedPackage Dd340Utility = new("dd340-utility",
        "13WUMg8DRlzyFaIg8dcE8pL-VFxigdjS7", "3C74682A41F2C2FBE291D0CDB629E8E59AFD8F9CFADCB5A19DF720A82E5EDF43");
    internal static readonly TrustedPackage Bp41Utility = new("bp41-utility",
        "1ACTuf7Fs5d_uNUI6kA2H4_Bz4GUYq7FB", "C3CAE49F8222064A6FE19398C93999598F918E7A05F26F3B4F1A1DAB23B51DD5");
    internal static readonly TrustedPackage Dd340Driver = new("dd340-driver",
        "197KUpI5jUbNWduTJHPkl0adJsrpUXmF4", "16EA1096BA4E7B21FC199D474E7A3DE26677CBC59154629449E5C53E30E8A6DC");
    internal static readonly TrustedPackage Bp41Driver = new("bp41-driver",
        "1So-KEVdDYpGN7B0O4PvLWVlEiFOy4hon", "8DF4CB4BB34ED83E257B76A43DCC3C098362578B66609AE2FA31F5F20597579F");

    private static readonly object Gate = new();

    public static string Prepare(TrustedPackage package, Action<string> log, string? privateRoot = null)
    {
        lock (Gate)
        {
            var cache = privateRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ThermoPrinterTool", "Packages");
            Directory.CreateDirectory(cache);
            var archivePath = Path.Combine(cache, package.Id + ".zip");
            if (!File.Exists(archivePath))
                Download(package, archivePath, log);

            // Hold the validated archive open while extracting it.
            using var archiveFile = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            VerifyHash(archiveFile, package.Sha256);
            archiveFile.Position = 0;
            log($"SHA-256 архива {package.Id} проверен.");

            var directory = Path.Combine(privateRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Label_printer", "Verified"),
                package.Id + "-" + Guid.NewGuid().ToString("N"));
            ExtractVerifiedArchive(archiveFile, directory);
            log("Проверенный пакет распакован: " + directory);
            return directory;
        }
    }

    internal static void VerifyHash(Stream stream, string expectedHash)
    {
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 архива не совпадает с закрепленным в программе. " +
                "Распаковка и запуск отменены. Удалите поврежденный архив из кэша или обратитесь в поддержку.");
    }

    internal static void ExtractVerifiedArchive(Stream stream, string directory)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalSize = 0;
        if (archive.Entries.Count > 10000)
            throw new InvalidDataException("В архиве слишком много файлов.");

        // Validate every entry before writing any files.
        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('\\', '/');
            if (relative.StartsWith('/') || relative.Contains(':') ||
                relative.Split('/').Any(part => part is ".." or "." || part.EndsWith(' ') || part.EndsWith('.')))
                throw new InvalidDataException("Недопустимый путь в архиве.");
            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !paths.Add(target))
                throw new InvalidDataException("Повторяющийся путь или выход за пределы каталога распаковки.");
            totalSize = checked(totalSize + entry.Length);
            if (totalSize > 2L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Слишком большой распакованный пакет.");
        }

        Directory.CreateDirectory(directory);
        archive.ExtractToDirectory(directory);
    }

    internal static string FindRequiredFile(string directory, string name) =>
        Directory.EnumerateFiles(directory, name, SearchOption.AllDirectories).FirstOrDefault()
        ?? throw new FileNotFoundException($"В проверенном пакете не найден {name}.");

    private static void Download(TrustedPackage package, string destination, Action<string> log)
    {
        log("Скачиваю проверяемый пакет: " + package.Id);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var url = "https://drive.usercontent.google.com/download?id=" +
            Uri.EscapeDataString(package.DriveId) + "&export=download&confirm=t";
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true)
                throw new InvalidDataException("Google Drive вернул страницу вместо архива.");
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using var input = response.Content.ReadAsStream();
                var buffer = new byte[81920];
                long received = 0;
                int count;
                while ((count = input.Read(buffer)) > 0)
                {
                    received += count;
                    if (received > 128L * 1024 * 1024)
                        throw new InvalidDataException("Скачиваемый архив превышает допустимый размер.");
                    output.Write(buffer, 0, count);
                }
                output.Position = 0;
                VerifyHash(output, package.Sha256);
            }
            File.Move(temporary, destination);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
