using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HyperVMManager.Services;

public static class CloudImageCacheService
{
    private static readonly HttpClient HttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    public static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        AppBrand.DisplayName,
        "CloudImages");

    public static string GetArchivePath(CloudImageCatalogItem image)
    {
        return Path.Combine(CacheDirectory, image.ArchiveFileName);
    }

    public static bool IsVerifiedArchiveCached(CloudImageCatalogItem image)
    {
        string archivePath = GetArchivePath(image);
        string shaPath = GetArchiveShaPath(image);
        if (!File.Exists(archivePath) || !File.Exists(shaPath))
        {
            return false;
        }

        string expected = File.ReadAllText(shaPath).Trim();
        return expected.Length == 64
            && string.Equals(ComputeSha256(archivePath), expected, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<VerifiedCachedArchive> EnsureVerifiedArchiveAsync(
        CloudImageCatalogItem image,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(CacheDirectory);
        string archivePath = GetArchivePath(image);
        string partialPath = archivePath + ".partial";

        progress?.Report("Checking Ubuntu cloud image cache...");
        string expectedSha = await GetExpectedSha256Async(image, cancellationToken).ConfigureAwait(false);

        if (File.Exists(archivePath))
        {
            progress?.Report("Verifying cached Ubuntu cloud image...");
            string existingSha = ComputeSha256(archivePath);
            if (string.Equals(existingSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(GetArchiveShaPath(image), expectedSha);
                return new VerifiedCachedArchive { Image = image, ArchivePath = archivePath, Sha256 = expectedSha };
            }

            QuarantineCorruptArchive(archivePath);
        }

        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        progress?.Report("Downloading Ubuntu cloud image...");
        using (var response = await HttpClient.GetAsync(image.ArchiveUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = File.Create(partialPath);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report("Verifying Ubuntu cloud image...");
        string downloadedSha = ComputeSha256(partialPath);
        if (!string.Equals(downloadedSha, expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            QuarantineCorruptArchive(partialPath);
            throw new InvalidOperationException("Downloaded Ubuntu cloud image checksum mismatch. The corrupt download was quarantined; retry the download.");
        }

        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }
        File.Move(partialPath, archivePath);
        File.WriteAllText(GetArchiveShaPath(image), expectedSha);
        return new VerifiedCachedArchive { Image = image, ArchivePath = archivePath, Sha256 = expectedSha };
    }

    public static async Task<ExtractedOsDisk> ExtractFinalOsDiskAsync(
        VerifiedCachedArchive archive,
        string finalDiskPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(finalDiskPath);
        if (!extension.Equals(".vhd", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".vhdx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Final OS disk path must end with .vhd or .vhdx.");
        }

        string? finalDir = Path.GetDirectoryName(finalDiskPath);
        if (string.IsNullOrWhiteSpace(finalDir))
        {
            throw new InvalidOperationException("Final OS disk directory is invalid.");
        }
        await VirtualDiskFileSystemService.PrepareDirectoryForVirtualDisksAsync(finalDir, cancellationToken).ConfigureAwait(false);

        if (File.Exists(finalDiskPath))
        {
            throw new InvalidOperationException("Final OS disk already exists: " + finalDiskPath);
        }

        string tempDir = Path.Combine(finalDir, ".extract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            progress?.Report("Extracting Ubuntu OS disk...");
            await RunTarExtractAsync(archive.ArchivePath, tempDir, cancellationToken).ConfigureAwait(false);

            string extracted = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories)
                .FirstOrDefault(p => p.EndsWith(".vhd", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("The Ubuntu archive did not contain a .vhd or .vhdx disk.");

            string extractedExt = Path.GetExtension(extracted);
            bool needsVhdToVhdxConversion = extractedExt.Equals(".vhd", StringComparison.OrdinalIgnoreCase)
                && extension.Equals(".vhdx", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(extractedExt, extension, StringComparison.OrdinalIgnoreCase) && !needsVhdToVhdxConversion)
            {
                throw new InvalidOperationException("Downloaded archive extracted " + extractedExt + ". The app will not rename disk formats; adjust the catalog to use the real disk extension.");
            }

            if (needsVhdToVhdxConversion)
            {
                progress?.Report("Converting Ubuntu OS disk to VHDX...");
                await VirtualDiskFileSystemService.ConvertVhdToVhdxAsync(extracted, finalDiskPath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                File.Move(extracted, finalDiskPath);
            }

            progress?.Report("Preparing Ubuntu OS disk for Hyper-V...");
            await VirtualDiskFileSystemService.NormalizeMountableVirtualDiskAsync(finalDiskPath, cancellationToken).ConfigureAwait(false);
            return new ExtractedOsDisk
            {
                DiskPath = finalDiskPath,
                DiskFormat = extension.ToLowerInvariant()
            };
        }
        finally
        {
            await DeleteDirectoryWithRetriesAsync(tempDir, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DeleteDirectoryWithRetriesAsync(string directoryPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        Exception? lastError = null;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(directoryPath, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                }

                Directory.Delete(directoryPath, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(500 + (attempt * 250)), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new IOException("Could not remove temporary extraction folder: " + directoryPath, lastError);
    }

    private static async Task RunTarExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken)
    {
        using Process process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "tar.exe",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-xzf");
        process.StartInfo.ArgumentList.Add(archivePath);
        process.StartInfo.ArgumentList.Add("-C");
        process.StartInfo.ArgumentList.Add(destinationDirectory);

        process.Start();
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            string message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException("Could not extract Ubuntu cloud image: " + message.Trim());
        }
    }

    private static async Task<string> GetExpectedSha256Async(CloudImageCatalogItem image, CancellationToken cancellationToken)
    {
        string sums = await HttpClient.GetStringAsync(image.Sha256SumsUri, cancellationToken).ConfigureAwait(false);
        string? hash = ParseSha256Sums(sums, image.ArchiveFileName);
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new InvalidOperationException("Could not find SHA256 for " + image.ArchiveFileName + ".");
        }
        return hash;
    }

    private static string GetArchiveShaPath(CloudImageCatalogItem image)
    {
        return GetArchivePath(image) + ".sha256";
    }

    private static string? ParseSha256Sums(string sums, string fileName)
    {
        foreach (string line in sums.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (!trimmed.EndsWith(fileName, StringComparison.Ordinal))
            {
                continue;
            }
            string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length == 64)
            {
                return parts[0];
            }
        }
        return null;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static void QuarantineCorruptArchive(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        string quarantinePath = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        if (File.Exists(quarantinePath))
        {
            File.Delete(quarantinePath);
        }
        File.Move(path, quarantinePath);
    }
}
