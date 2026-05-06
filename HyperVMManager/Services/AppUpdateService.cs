using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HyperVMManager.Services;

public static class AppUpdateService
{
    private static readonly HttpClient HttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public static async Task<UpdateCheckResult> CheckForUpdateAsync(Uri manifestUri, Version currentVersion, CancellationToken cancellationToken = default)
    {
        using var response = await HttpClient.GetAsync(manifestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (manifest == null)
        {
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                Message = "Could not parse update manifest."
            };
        }

        if (!Version.TryParse(manifest.Version, out var latestVersion))
        {
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                Message = "Invalid version in update manifest."
            };
        }

        var hasUpdate = latestVersion > currentVersion;
        return new UpdateCheckResult
        {
            IsUpdateAvailable = hasUpdate,
            Manifest = manifest,
            Message = hasUpdate ? "Update available." : "Already up to date."
        };
    }

    public static async Task<string> DownloadUpdateAsync(UpdateManifest manifest, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri))
        {
            throw new InvalidOperationException("Invalid downloadUrl in update manifest.");
        }

        Directory.CreateDirectory(destinationDirectory);

        var fileName = Path.GetFileName(downloadUri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "HyperVMManager-Setup.exe";
        }

        var destinationPath = Path.Combine(destinationDirectory, fileName);

        using var response = await HttpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = File.Create(destinationPath))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(manifest.Sha256) && !manifest.Sha256.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase))
        {
            var fileHash = ComputeSha256(destinationPath);
            if (!string.Equals(fileHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destinationPath);
                throw new InvalidOperationException("Downloaded update checksum mismatch.");
            }
        }

        return destinationPath;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }
}
