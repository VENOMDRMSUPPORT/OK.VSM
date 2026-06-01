using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HyperVMManager.Services;

public static class CloudImageCacheService
{
    private static readonly HttpClient HttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    public static string CacheDirectory => GetAppInstallationCacheDirectory();

    private static string GetAppInstallationCacheDirectory()
    {
        // Get the directory where the app is installed (where the EXE is located)
        string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            string? installDir = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrWhiteSpace(installDir))
            {
                return Path.Combine(installDir, "CloudImages");
            }
        }
        
        // Fallback to app data if we can't determine install location
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppBrand.InternalAppDataFolder,
            "CloudImages");
    }

    public static string GetCacheDirectoryForVmDisk(string vmDiskPath)
    {
        // Always use the app's installation directory for cloud images cache
        // This ensures consistency regardless of where VM disks are stored
        return CacheDirectory;
    }

    public static string GetTemplateCacheDirectoryForVmDisk(string vmDiskPath)
    {
        return Path.Combine(GetCacheDirectoryForVmDisk(vmDiskPath), "Templates");
    }

    public static string GetArchivePath(CloudImageCatalogItem image, string? vmDiskPath = null)
    {
        return Path.Combine(GetCacheDirectoryForVmDisk(vmDiskPath ?? ""), image.ArchiveFileName);
    }

    public static bool IsVerifiedArchiveCached(CloudImageCatalogItem image, string? vmDiskPath = null)
    {
        foreach (string cacheRoot in EnumerateCandidateCacheDirectories(vmDiskPath))
        {
            string archivePath = Path.Combine(cacheRoot, image.ArchiveFileName);
            string shaPath = archivePath + ".sha256";
            if (!File.Exists(archivePath) || !File.Exists(shaPath))
            {
                continue;
            }

            string expected = File.ReadAllText(shaPath).Trim();
            if (expected.Length == 64
                && string.Equals(ComputeSha256(archivePath), expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<VerifiedCachedArchive> EnsureVerifiedArchiveAsync(
        CloudImageCatalogItem image,
        string targetVmDiskPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string cacheDirectory = GetCacheDirectoryForVmDisk(targetVmDiskPath);
        Directory.CreateDirectory(cacheDirectory);
        string archivePath = GetArchivePath(image, targetVmDiskPath);
        string partialPath = archivePath + ".partial";

        progress?.Report("Checking Ubuntu cloud image cache...");
        string expectedSha = await GetExpectedSha256Async(image, cancellationToken).ConfigureAwait(false);

        foreach (string candidateRoot in EnumerateCandidateCacheDirectories(targetVmDiskPath))
        {
            string candidateArchivePath = Path.Combine(candidateRoot, image.ArchiveFileName);
            if (!File.Exists(candidateArchivePath))
            {
                continue;
            }

            progress?.Report("Verifying cached Ubuntu cloud image...");
            string existingSha = ComputeSha256(candidateArchivePath);
            if (string.Equals(existingSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(candidateArchivePath, archivePath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(candidateArchivePath, archivePath, overwrite: true);
                }
                File.WriteAllText(GetArchiveShaPath(image, targetVmDiskPath), expectedSha);
                return new VerifiedCachedArchive { Image = image, ArchivePath = archivePath, Sha256 = expectedSha };
            }

            QuarantineCorruptArchive(candidateArchivePath);
        }

        // Resume download support - check if partial file exists
        long existingBytes = 0;
        if (File.Exists(partialPath))
        {
            existingBytes = new FileInfo(partialPath).Length;
            progress?.Report($"Resuming download... ({existingBytes / 1024 / 1024} MB already downloaded)");
        }
        else
        {
            progress?.Report("Downloading Ubuntu cloud image...");
        }

        // Create HTTP request with Range header for resume support
        using var request = new HttpRequestMessage(HttpMethod.Get, image.ArchiveUri);
        if (existingBytes > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
        }

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        
        // If server doesn't support range, start from beginning
        if (existingBytes > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            progress?.Report("Server doesn't support resume, restarting download...");
            File.Delete(partialPath);
            existingBytes = 0;
            request.Headers.Range = null;
            using var restartResponse = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            restartResponse.EnsureSuccessStatusCode();
            await DownloadToFileAsync(restartResponse, partialPath, existingBytes, progress, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            response.EnsureSuccessStatusCode();
            await DownloadToFileAsync(response, partialPath, existingBytes, progress, cancellationToken).ConfigureAwait(false);
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
        File.WriteAllText(GetArchiveShaPath(image, targetVmDiskPath), expectedSha);
        return new VerifiedCachedArchive { Image = image, ArchivePath = archivePath, Sha256 = expectedSha };
    }

    public static async Task<ExtractedOsDisk> ExtractFinalOsDiskAsync(
        VerifiedCachedArchive archive,
        string finalDiskPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await PrepareFinalOsDiskAsync(archive, finalDiskPath, progress, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<PreparedOsTemplate> EnsurePreparedBaseTemplateAsync(
        VerifiedCachedArchive archive,
        CloudImageCatalogItem image,
        string targetVmDiskPath,
        ulong requestedSizeBytes,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (requestedSizeBytes == 0)
        {
            throw new InvalidOperationException("Requested base template disk size must be greater than zero.");
        }

        string templateDirectory = GetTemplateCacheDirectoryForVmDisk(targetVmDiskPath);
        Directory.CreateDirectory(templateDirectory);

        string extension = image.FinalDiskExtension;
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".vhdx";
        }
        if (!extension.StartsWith(".", StringComparison.Ordinal))
        {
            extension = "." + extension;
        }

        string safeImageId = Regex.Replace(image.Id, @"[^A-Za-z0-9._-]+", "-").Trim('-');
        if (safeImageId.Length == 0)
        {
            safeImageId = "ubuntu-cloud-image";
        }

        string sizeSuffix = requestedSizeBytes.ToString() + "B";
        string templatePath = Path.Combine(templateDirectory, safeImageId + "-" + sizeSuffix + extension);
        if (File.Exists(templatePath))
        {
            File.SetAttributes(templatePath, File.GetAttributes(templatePath) | FileAttributes.ReadOnly);
            return new PreparedOsTemplate
            {
                TemplatePath = templatePath,
                DiskFormat = extension.ToLowerInvariant()
            };
        }

        string workingPath = Path.Combine(
            templateDirectory,
            Path.GetFileNameWithoutExtension(templatePath) + ".building" + extension);

        if (File.Exists(workingPath))
        {
            File.SetAttributes(workingPath, FileAttributes.Normal);
            File.Delete(workingPath);
        }

        try
        {
            progress?.Report("Preparing shared Ubuntu base template...");
            ExtractedOsDisk extracted = await PrepareFinalOsDiskAsync(archive, workingPath, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report("Sizing shared Ubuntu base template...");
            await VirtualDiskFileSystemService.ResizeVhdAsync(extracted.DiskPath, requestedSizeBytes, cancellationToken).ConfigureAwait(false);

            progress?.Report("Patching shared Ubuntu base template...");
            await VirtualDiskFileSystemService.PatchGrubForNoCloudAsync(extracted.DiskPath, cancellationToken).ConfigureAwait(false);

            File.SetAttributes(extracted.DiskPath, File.GetAttributes(extracted.DiskPath) | FileAttributes.ReadOnly);
            File.Move(extracted.DiskPath, templatePath);

            return new PreparedOsTemplate
            {
                TemplatePath = templatePath,
                DiskFormat = extension.ToLowerInvariant()
            };
        }
        catch
        {
            try
            {
                if (File.Exists(workingPath))
                {
                    File.SetAttributes(workingPath, FileAttributes.Normal);
                    File.Delete(workingPath);
                }
            }
            catch
            {
            }

            throw;
        }
    }

    private static async Task<ExtractedOsDisk> PrepareFinalOsDiskAsync(
        VerifiedCachedArchive archive,
        string finalDiskPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
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

    private static string GetArchiveShaPath(CloudImageCatalogItem image, string? vmDiskPath = null)
    {
        return GetArchivePath(image, vmDiskPath) + ".sha256";
    }

    private static string[] EnumerateCandidateCacheDirectories(string? vmDiskPath)
    {
        string preferred = GetCacheDirectoryForVmDisk(vmDiskPath ?? "");
        return new[] { preferred, CacheDirectory }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    private static async Task DownloadToFileAsync(
        HttpResponseMessage response, 
        string filePath, 
        long existingBytes,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        // Open file for append if resuming, or create new
        FileMode fileMode = existingBytes > 0 ? FileMode.Append : FileMode.Create;
        await using var target = new FileStream(filePath, fileMode, FileAccess.Write, FileShare.None);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        // Get total size for progress calculation
        long? totalBytes = response.Content.Headers.ContentLength;
        if (existingBytes > 0 && totalBytes.HasValue)
        {
            totalBytes += existingBytes; // Add already downloaded bytes
        }

        long totalRead = existingBytes;
        byte[] buffer = new byte[8192];
        DateTime lastProgressUpdate = DateTime.MinValue;

        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            totalRead += read;

            // Update progress every second
            if (progress != null && DateTime.Now - lastProgressUpdate > TimeSpan.FromSeconds(1))
            {
                lastProgressUpdate = DateTime.Now;
                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    double percent = (double)totalRead / totalBytes.Value * 100;
                    double downloadedMB = totalRead / 1024.0 / 1024.0;
                    double totalMB = totalBytes.Value / 1024.0 / 1024.0;
                    progress.Report($"Downloading... {percent:F1}% ({downloadedMB:F1} / {totalMB:F1} MB)");
                }
                else
                {
                    double downloadedMB = totalRead / 1024.0 / 1024.0;
                    progress.Report($"Downloading... {downloadedMB:F1} MB");
                }
            }
        }
    }
}
