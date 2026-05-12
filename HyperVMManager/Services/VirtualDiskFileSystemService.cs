using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;

namespace HyperVMManager.Services;

public static class VirtualDiskFileSystemService
{
    public static async Task PrepareDirectoryForVirtualDisksAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Virtual disk directory is invalid.", nameof(directoryPath));
        }

        Directory.CreateDirectory(directoryPath);
        await RunNativeAsync("compact.exe", new[] { "/u", directoryPath }, "Clearing virtual disk directory compression", cancellationToken).ConfigureAwait(false);
        await RunNativeAsync("cipher.exe", new[] { "/d", directoryPath }, "Clearing virtual disk directory encryption", cancellationToken).ConfigureAwait(false);
    }

    public static async Task NormalizeMountableVirtualDiskAsync(
        string diskPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(diskPath) || !File.Exists(diskPath))
        {
            throw new FileNotFoundException("Virtual disk file was not found.", diskPath);
        }

        await RunNativeAsync("compact.exe", new[] { "/u", "/f", diskPath }, "Clearing virtual disk compression", cancellationToken).ConfigureAwait(false);
        await RunNativeAsync("cipher.exe", new[] { "/d", diskPath }, "Clearing virtual disk encryption", cancellationToken).ConfigureAwait(false);

        FileAttributes attributes = File.GetAttributes(diskPath);
        if ((attributes & FileAttributes.SparseFile) != 0)
        {
            await RunNativeAsync("fsutil.exe", new[] { "sparse", "setflag", diskPath, "0" }, "Clearing virtual disk sparse flag", cancellationToken).ConfigureAwait(false);
        }

        ValidateMountableAttributes(diskPath);
    }

    public static async Task ConvertVhdToVhdxAsync(
        string sourceVhdPath,
        string destinationVhdxPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceVhdPath) || !File.Exists(sourceVhdPath))
        {
            throw new FileNotFoundException("Source VHD file was not found.", sourceVhdPath);
        }

        if (string.IsNullOrWhiteSpace(destinationVhdxPath))
        {
            throw new ArgumentException("Destination VHDX path is invalid.", nameof(destinationVhdxPath));
        }

        string? destinationDirectory = Path.GetDirectoryName(destinationVhdxPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        await Task.Run(() => ConvertVhdToDynamicVhdx(sourceVhdPath, destinationVhdxPath, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public static async Task ResizeVhdAsync(
        string diskPath,
        ulong minimumSizeBytes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(diskPath) || !File.Exists(diskPath))
        {
            throw new FileNotFoundException("Virtual disk file was not found.", diskPath);
        }

        string script =
            "$ErrorActionPreference = 'Stop'\n"
            + "$path = " + QuotePowerShellLiteral(diskPath) + "\n"
            + "$size = " + minimumSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n"
            + "$vhd = Get-VHD -Path $path\n"
            + "if ($vhd.Size -lt $size) { Resize-VHD -Path $path -SizeBytes $size | Out-Null }\n";

        await RunPowerShellAsync(script, "Sizing virtual disk", cancellationToken).ConfigureAwait(false);
    }

    public static async Task PatchGrubForNoCloudAsync(
        string osVhdPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(osVhdPath) || !File.Exists(osVhdPath))
        {
            throw new FileNotFoundException("Virtual disk file was not found.", osVhdPath);
        }

        string pathLiteral = QuotePowerShellLiteral(osVhdPath);
        string script =
            "$ErrorActionPreference = 'Stop'\n"
            + BuildPowerShellVirtualDiskHelpers()
            + "Assert-VirtualDiskFileMountableForHyperV " + pathLiteral + "\n"
            + "$vhd = Mount-VHD -Path " + pathLiteral + " -PassThru\n"
            + "try {\n"
            + "  Start-Sleep -Milliseconds 1500\n"
            + "  $disk = $vhd | Get-Disk\n"
            + "  $efiPart = Get-Partition -DiskNumber $disk.Number | Where-Object { $_.GptType -eq '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}' } | Select-Object -First 1\n"
            + "  if (-not $efiPart) { return }\n"
            + "  if (-not $efiPart.DriveLetter) {\n"
            + "    $efiPart | Add-PartitionAccessPath -AssignDriveLetter -ErrorAction Stop\n"
            + "    $efiPart = Get-Partition -DiskNumber $disk.Number -PartitionNumber $efiPart.PartitionNumber\n"
            + "  }\n"
            + "  $efiRoot = $efiPart.DriveLetter.ToString() + ':\\\\'\n"
            + "  $grubCfg = Join-Path $efiRoot 'EFI\\\\ubuntu\\\\grub.cfg'\n"
            + "  if (-not (Test-Path $grubCfg)) { $grubCfg = (Get-ChildItem (Join-Path $efiRoot 'EFI') -Recurse -Filter 'grub.cfg' -ErrorAction SilentlyContinue | Select-Object -First 1).FullName }\n"
            + "  if (-not $grubCfg -or -not (Test-Path $grubCfg)) { return }\n"
            + "  $content = [System.IO.File]::ReadAllText($grubCfg)\n"
            + "  $match = [regex]::Match($content, 'search\\.fs_uuid\\s+(\\S+)')\n"
            + "  if (-not $match.Success) { return }\n"
            + "  $rootUuid = $match.Groups[1].Value\n"
            + "  $pfxMatch = [regex]::Match($content, \"set prefix=\\(\\`$root\\)'([^']+)'\")\n"
            + "  $pfx = if ($pfxMatch.Success) { $pfxMatch.Groups[1].Value } else { '/grub' }\n"
            + "  $newCfg = \"search.fs_uuid $rootUuid root`nset prefix=(`$root)'$pfx'`ninsmod linux`ninsmod gzio`nlinux (`$root)/vmlinuz root=LABEL=cloudimg-rootfs ro quiet splash ds=nocloud`ninitrd (`$root)/initrd.img`nboot`nconfigfile `$prefix/grub.cfg`n\"\n"
            + "  Copy-Item -LiteralPath $grubCfg -Destination ($grubCfg + '.orig') -Force\n"
            + "  [System.IO.File]::WriteAllText($grubCfg, $newCfg, [System.Text.UTF8Encoding]::new($false))\n"
            + "} finally {\n"
            + "  Dismount-VHD -Path " + pathLiteral + " -ErrorAction SilentlyContinue\n"
            + "}\n";

        await RunPowerShellAsync(script, "Patching GRUB for NoCloud boot", cancellationToken).ConfigureAwait(false);
    }

    public static async Task CreateDifferencingVhdAsync(
        string parentPath,
        string childPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentPath) || !File.Exists(parentPath))
        {
            throw new FileNotFoundException("Parent virtual disk file was not found.", parentPath);
        }

        if (string.IsNullOrWhiteSpace(childPath))
        {
            throw new ArgumentException("Child VHDX path is invalid.", nameof(childPath));
        }

        if (File.Exists(childPath))
        {
            throw new InvalidOperationException("Differencing OS disk already exists: " + childPath);
        }

        string? childDirectory = Path.GetDirectoryName(childPath);
        if (!string.IsNullOrWhiteSpace(childDirectory))
        {
            Directory.CreateDirectory(childDirectory);
            await PrepareDirectoryForVirtualDisksAsync(childDirectory, cancellationToken).ConfigureAwait(false);
        }

        File.SetAttributes(parentPath, File.GetAttributes(parentPath) | FileAttributes.ReadOnly);

        string script =
            "$ErrorActionPreference = 'Stop'\n"
            + "$parent = " + QuotePowerShellLiteral(parentPath) + "\n"
            + "$child = " + QuotePowerShellLiteral(childPath) + "\n"
            + "New-VHD -Path $child -ParentPath $parent -Differencing | Out-Null\n";

        await RunPowerShellAsync(script, "Creating differencing virtual disk", cancellationToken).ConfigureAwait(false);
        await NormalizeMountableVirtualDiskAsync(childPath, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateMountableAttributes(string diskPath)
    {
        FileAttributes attributes = File.GetAttributes(diskPath);
        string[] blocked = new[]
        {
            ((attributes & FileAttributes.Compressed) != 0) ? "compressed" : "",
            ((attributes & FileAttributes.Encrypted) != 0) ? "encrypted" : "",
            ((attributes & FileAttributes.SparseFile) != 0) ? "sparse" : ""
        }.Where(s => s.Length > 0).ToArray();

        if (blocked.Length > 0)
        {
            throw new InvalidOperationException(
                "Hyper-V cannot mount this virtual disk because the file is still "
                + string.Join(", ", blocked)
                + ". Move it to an uncompressed local NTFS folder and try again: "
                + diskPath);
        }
    }

    private static async Task RunNativeAsync(
        string fileName,
        string[] arguments,
        string action,
        CancellationToken cancellationToken)
    {
        using Process process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(action + " could not start: " + ex.Message, ex);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            throw;
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            string message = BuildNativeErrorMessage(stdout, stderr);
            throw new InvalidOperationException(action + " failed: " + message);
        }
    }

    private static string BuildNativeErrorMessage(string stdout, string stderr)
    {
        StringBuilder builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            builder.Append(stdout.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(stderr.Trim());
        }

        return builder.Length == 0 ? "The command did not return a message." : builder.ToString();
    }

    private static async Task RunPowerShellAsync(
        string script,
        string action,
        CancellationToken cancellationToken)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        using Process process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(action + " could not start: " + ex.Message, ex);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            throw;
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(action + " failed: " + BuildNativeErrorMessage(stdout, stderr));
        }
    }

    private static string QuotePowerShellLiteral(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }

    private static string BuildPowerShellVirtualDiskHelpers()
    {
        return """
function Invoke-NativeForVirtualDisk {
  param([string]$FileName, [string[]]$Arguments, [string]$Action)
  $output = (& $FileName @Arguments 2>&1 | Out-String).Trim()
  if ($LASTEXITCODE -ne 0) {
    $message = $output
    if ([string]::IsNullOrWhiteSpace($message)) { $message = 'The command did not return a message.' }
    throw "$Action failed: $message"
  }
}

function Assert-VirtualDiskFileMountableForHyperV {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Virtual disk not found: $Path" }
  $attrs = [System.IO.File]::GetAttributes($Path)
  $blocked = @()
  if (($attrs -band [System.IO.FileAttributes]::Compressed) -ne 0) { $blocked += 'compressed' }
  if (($attrs -band [System.IO.FileAttributes]::Encrypted) -ne 0) { $blocked += 'encrypted' }
  if (($attrs -band [System.IO.FileAttributes]::SparseFile) -ne 0) { $blocked += 'sparse' }
  if ($blocked.Count -gt 0) {
    throw "Hyper-V cannot mount '$Path' because it is still $($blocked -join ', '). Move it to an uncompressed local NTFS folder and try again."
  }
}

""";
    }

    private static void ConvertVhdToDynamicVhdx(string sourceVhdPath, string destinationVhdxPath, CancellationToken cancellationToken)
    {
        string tempDestination = destinationVhdxPath + ".partial";
        if (File.Exists(tempDestination))
        {
            File.Delete(tempDestination);
        }

        try
        {
            {
                using DiscUtils.Vhd.Disk sourceDisk = new DiscUtils.Vhd.Disk(sourceVhdPath, FileAccess.Read);
                using FileStream destinationStream = new FileStream(tempDestination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                using DiscUtils.Vhdx.Disk destinationDisk = DiscUtils.Vhdx.Disk.InitializeDynamic(destinationStream, Ownership.None, sourceDisk.Capacity);

                Stream source = sourceDisk.Content;
                Stream destination = destinationDisk.Content;
                byte[] buffer = new byte[4 * 1024 * 1024];
                long remaining = sourceDisk.Capacity;

                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int wanted = (int)Math.Min(buffer.Length, remaining);
                    int read = ReadExactlyUpTo(source, buffer, wanted);
                    if (read <= 0)
                    {
                        break;
                    }

                    if (!IsAllZero(buffer, read))
                    {
                        destination.Write(buffer, 0, read);
                    }
                    else
                    {
                        destination.Position += read;
                    }

                    remaining -= read;
                }

                destinationStream.Flush(true);
            }

            if (File.Exists(destinationVhdxPath))
            {
                File.Delete(destinationVhdxPath);
            }

            File.Move(tempDestination, destinationVhdxPath);
        }
        catch
        {
            try
            {
                if (File.Exists(tempDestination))
                {
                    File.Delete(tempDestination);
                }
            }
            catch
            {
            }

            throw;
        }
    }

    private static int ReadExactlyUpTo(Stream stream, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, total, count - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static bool IsAllZero(byte[] buffer, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (buffer[i] != 0)
            {
                return false;
            }
        }

        return true;
    }
}
