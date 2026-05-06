using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using HyperVMManager.Commands;
using HyperVMManager.Controls;
using HyperVMManager.Dialogs;
using HyperVMManager.Models;
using HyperVMManager.Services;
using HyperVMManager.ViewModels;
using Microsoft.Win32;

namespace HyperVMManager.Services;

	public static class VmControlService
	{
		private static readonly string DiagLogPath = System.IO.Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.Desktop), "HyperVMManager-diag.log");

		private static string EscapeSingleQuoted (string s)
		{
			return s.Replace ("'", "''");
		}

		private static string VmLiteral (string name)
		{
			return "'" + EscapeSingleQuoted (name) + "'";
		}

		private static string HumanizePowerShellOutput (string raw)
		{
			if (string.IsNullOrWhiteSpace (raw)) {
				return "The command did not return a message.";
			}
			string text = raw.Trim ();
			if (!text.Contains ("<Objs", StringComparison.Ordinal) && !text.Contains ("#< CLIXML", StringComparison.Ordinal)) {
				return (text.Length > 900) ? (text.Substring (0, 900) + "…") : text;
			}
			StringBuilder stringBuilder = new StringBuilder ();
			foreach (Match item in Regex.Matches (text, "<S[^>]*>([^<]+)</S>")) {
				string text2 = item.Groups [1].Value.Trim ();
				if (text2.Length >= 2 && !text2.Contains ("xmlns", StringComparison.Ordinal) && !text2.StartsWith ("#", StringComparison.Ordinal)) {
					if (stringBuilder.Length > 0) {
						stringBuilder.Append (' ');
					}
					stringBuilder.Append (text2);
				}
			}
			string text3 = stringBuilder.ToString ().Trim ();
			if (text3.Length > 0) {
				return (text3.Length > 500) ? (text3.Substring (0, 500) + "…") : text3;
			}
			string input = Regex.Replace (text, "<[^>]+>", " ");
			input = Regex.Replace (input, "\\s+", " ").Trim ();
			if (input.Length > 0) {
				string text4 = ((input.Length > 600) ? (input.Substring (0, 600) + "…") : input);
				if (ContainsAccessHint (input)) {
					return text4 + "\n\nIf this mentions access or permission, close the app and run it as Administrator.";
				}
				return text4;
			}
			return "The operation failed (Hyper-V / PowerShell). Close the app and run it as Administrator (Hyper-V requires elevation).";
		}

		private static bool ContainsAccessHint (string s)
		{
			return s.Contains ("access", StringComparison.OrdinalIgnoreCase) || s.Contains ("denied", StringComparison.OrdinalIgnoreCase) || s.Contains ("permission", StringComparison.OrdinalIgnoreCase) || s.Contains ("privilege", StringComparison.OrdinalIgnoreCase) || s.Contains ("authorized", StringComparison.OrdinalIgnoreCase);
		}

		private static void DiagLog (string text)
		{
			try {
				File.AppendAllText (DiagLogPath, $"[{DateTime.Now:HH:mm:ss}] {text}\r\n");
			} catch {
			}
		}

		private static (bool ok, string message) RunScript (string script)
		{
			try {
				string text = Convert.ToBase64String (Encoding.Unicode.GetBytes (script));
				ProcessStartInfo startInfo = new ProcessStartInfo {
					FileName = "powershell.exe",
					Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + text,
					UseShellExecute = false,
					RedirectStandardError = true,
					RedirectStandardOutput = true,
					CreateNoWindow = true
				};
				DiagLog ("RUN>> " + script.Replace ("\r\n", " | ").Replace ("\n", " | "));
				using Process? process = Process.Start (startInfo);
				if (process == null) {
					return (ok: false, message: "Could not start PowerShell.");
				}
				string text2 = process.StandardError.ReadToEnd ();
				string text3 = process.StandardOutput.ReadToEnd ();
				process.WaitForExit (120000);
				DiagLog ($"EXIT={process.ExitCode} STDOUT[{text3.Length}]={text3.Trim ()} STDERR[{text2.Length}]={text2.Trim ()}");
				if (process.ExitCode != 0) {
					string raw = (string.IsNullOrWhiteSpace (text3) ? text2 : (text3 + "\n" + text2));
					return (ok: false, message: HumanizePowerShellOutput (raw));
				}
				return (ok: true, message: text3.Trim ());
			} catch (Exception ex) {
				DiagLog ($"EXCEPTION: {ex}");
				return (ok: false, message: ex.Message);
			}
		}

		private static (bool ok, string message) RunScriptTryCatch (string innerScriptBody)
		{
			string script = "$ErrorActionPreference = 'Stop'\r\ntry {\r\n" + innerScriptBody.Trim () + "\r\n} catch {\r\n  Write-Output ($_.Exception.Message)\r\n  if ($_.InvocationInfo.PositionMessage) { Write-Output ($_.InvocationInfo.PositionMessage) }\r\n  exit 1\r\n}\r\n";
			return RunScript (script);
		}

		public static (bool ok, string message) Start (string vmName)
		{
			return RunScriptTryCatch ("Start-VM -Name " + VmLiteral (vmName));
		}

		public static (bool ok, string message) Stop (string vmName)
		{
			return RunScriptTryCatch ("Stop-VM -Name " + VmLiteral (vmName) + " -TurnOff");
		}

		public static (bool ok, string message) Restart (string vmName)
		{
			return RunScriptTryCatch ("Restart-VM -Name " + VmLiteral (vmName) + " -Force");
		}

		public static (bool ok, string message) Delete (string vmName)
		{
			string value = VmLiteral (vmName);
			string innerScriptBody = "$vm = Get-VM -Name " + value + " -ErrorAction Stop\r\n" +
				"# Step 1: Stop if running\r\n" +
				"if ($vm.State -eq 'Running') { Stop-VM -VM $vm -TurnOff -Force }\r\n" +
				"# Step 2: Remove all disk attachments from VM\r\n" +
				"Get-VMHardDiskDrive -VMName " + value + " | ForEach-Object { Remove-VMHardDiskDrive -VMHardDiskDrive $_ -ErrorAction SilentlyContinue }\r\n" +
				"# Step 3: Remove VM from Hyper-V\r\n" +
				"Remove-VM -VM $vm -Force\r\n";
			return RunScriptTryCatch (innerScriptBody);
		}

		public static (bool ok, string message) DeleteVmFiles (string vmDirectory)
		{
			if (string.IsNullOrWhiteSpace (vmDirectory) || !Directory.Exists (vmDirectory)) {
				return (ok: true, message: string.Empty);
			}
			string innerScriptBody = "$dir = '" + EscapeSingleQuoted (vmDirectory) + "'\r\n" +
				"# Dismount any mounted VHDs in the directory\r\n" +
				"Get-ChildItem -LiteralPath $dir -Filter '*.vhdx' -ErrorAction SilentlyContinue | ForEach-Object { Dismount-VHD -Path $_.FullName -ErrorAction SilentlyContinue }\r\n" +
				"Start-Sleep -Milliseconds 500\r\n" +
				"# Remove the entire VM directory\r\n" +
				"Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction Stop\r\n";
			return RunScriptTryCatch (innerScriptBody);
		}

		public static (bool ok, string message) RemoveVirtualDvdDrives (string vmName)
		{
			string value = VmLiteral (vmName);
			string innerScriptBody = $"$vm = Get-VM -Name {value} -ErrorAction Stop\r\nif ($vm.State -eq 'Running') {{ Stop-VM -VM $vm -TurnOff -Force }}\r\nGet-VMDvdDrive -VMName {value} -ErrorAction SilentlyContinue | Remove-VMDvdDrive -ErrorAction SilentlyContinue\r\n";
			return RunScriptTryCatch (innerScriptBody);
		}

		public static (bool ok, string message) OpenVmConsole (string vmName)
		{
			try {
				string folderPath = Environment.GetFolderPath (Environment.SpecialFolder.System);
				string text = System.IO.Path.Combine (folderPath, "vmconnect.exe");
				if (!File.Exists (text)) {
					return (ok: false, message: "vmconnect.exe was not found (Hyper-V tools missing).");
				}
				ProcessStartInfo processStartInfo = new ProcessStartInfo {
					FileName = text,
					UseShellExecute = true
				};
				processStartInfo.ArgumentList.Add ("localhost");
				processStartInfo.ArgumentList.Add (vmName);
				Process.Start (processStartInfo);
				return (ok: true, message: string.Empty);
			} catch (Exception ex) {
				return (ok: false, message: ex.Message);
			}
		}

		public static (bool ok, string message) CreateCheckpoint (string vmName)
		{
			string value = VmLiteral (vmName);
			string s = $"UI-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
			string value2 = "'" + EscapeSingleQuoted (s) + "'";
			string innerScriptBody = $"Checkpoint-VM -VMName {value} -SnapshotName {value2}\r\n";
			return RunScriptTryCatch (innerScriptBody);
		}

		public static void OpenHyperVManager ()
		{
			try {
				Process.Start (new ProcessStartInfo {
					FileName = "virtmgmt.msc",
					UseShellExecute = true
				});
			} catch {
			}
		}

		public static (bool ok, IReadOnlyList<(string name, string hostname)> vms, string message) ListExistingVms ()
		{
			string script = "$ErrorActionPreference = 'Continue'\r\n" +
				"$bucket = [System.Collections.Generic.List[object]]::new()\r\n" +
				"Get-VM -ErrorAction SilentlyContinue | ForEach-Object {\r\n" +
				"  $name = [string]$_.Name\r\n" +
				"  $notes = [string]$_.Notes\r\n" +
				"  $hostname = ''\r\n" +
				"  if ($notes -match 'hostname:\\s*(\\S+)') { $hostname = $Matches[1] }\r\n" +
				"  $bucket.Add([PSCustomObject]@{ Name = $name; Hostname = $hostname })\r\n" +
				"}\r\n" +
				"$bucket | ConvertTo-Json -Compress\r\n";
			var (flag, text) = RunScript (script);
			if (!flag) {
				return (ok: false, vms: Array.Empty<(string, string)> (), message: HumanizePowerShellOutput (text));
			}
			var list = new List<(string name, string hostname)> ();
			try {
				using var doc = JsonDocument.Parse (text);
				foreach (JsonElement el in doc.RootElement.EnumerateArray ()) {
					string name = el.TryGetProperty ("Name", out var n) ? (n.GetString () ?? "") : "";
					string host = el.TryGetProperty ("Hostname", out var h) ? (h.GetString () ?? "") : "";
					if (!string.IsNullOrWhiteSpace (name)) {
						list.Add ((name, host));
					}
				}
			} catch {
			}
			return (ok: true, vms: list, message: string.Empty);
		}

		public static (bool ok, IReadOnlyList<string> names, string message) ListVirtualSwitchNames ()
		{
			string script = "$ErrorActionPreference = 'SilentlyContinue'\r\n@(Get-VMSwitch -SwitchType External | Sort-Object Name | ForEach-Object { [string]$_.Name }) | ConvertTo-Json -Compress\r\n";
			var (flag, text) = RunScript (script);
			if (!flag) {
				return (ok: false, names: Array.Empty<string> (), message: HumanizePowerShellOutput (text));
			}
			return (ok: true, names: ParseJsonStringArray (text), message: string.Empty);
		}

		private static IReadOnlyList<string> ParseJsonStringArray (string json)
		{
			if (string.IsNullOrWhiteSpace (json)) {
				return Array.Empty<string> ();
			}
			try {
				using JsonDocument jsonDocument = JsonDocument.Parse (json.Trim ());
				JsonElement rootElement = jsonDocument.RootElement;
				if (rootElement.ValueKind == JsonValueKind.Array) {
					return (from e in rootElement.EnumerateArray ()
						select e.GetString () ?? "" into s
						where s.Length > 0
						select s).ToList ();
				}
				if (rootElement.ValueKind == JsonValueKind.String) {
					string? text = rootElement.GetString ();
					return string.IsNullOrEmpty (text) ? ((IReadOnlyList<string>)Array.Empty<string> ()) : ((IReadOnlyList<string>)new string[1] { text });
				}
			} catch {
			}
			return Array.Empty<string> ();
		}

		public static (bool ok, string message) CreateUbuntuInstallVm (CreateVmParameters p, IProgress<string>? progress = null)
		{
			string value = VmLiteral (p.VmName);
			string value2 = VmLiteral (p.SwitchName);
			string text = "'" + EscapeSingleQuoted (p.VhdFullPath) + "'";
			string value3 = "'" + EscapeSingleQuoted (p.IsoPath) + "'";
			string value4 = "'" + EscapeSingleQuoted (p.Notes) + "'";
			long memoryStartupBytes = p.MemoryStartupBytes;
			int processorCount = p.ProcessorCount;
			ulong vhdSizeBytes = p.VhdSizeBytes;
			(string, string)[] array = new(string, string)[5] {
				("Preparing VHD directory…", "$dir = Split-Path -LiteralPath " + text + "\r\nif ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }\r\n"),
				("Creating virtual machine…", $"New-VM -Name {value} -MemoryStartupBytes {memoryStartupBytes} -Generation 2 -NewVHDPath {text} -NewVHDSizeBytes {vhdSizeBytes} -SwitchName {value2}\r\n"),
				("Configuring processor…", $"Set-VMProcessor -VMName {value} -Count {processorCount}\r\n"),
				("Attaching ISO and setting boot order…", $"Add-VMDvdDrive -VMName {value} -Path {value3}\r\n$dvd = Get-VMDvdDrive -VMName {value} | Select-Object -First 1\r\nif (-not $dvd) {{ throw 'No DVD drive after Add-VMDvdDrive' }}\r\nSet-VMFirmware -VMName {value} -SecureBootTemplate MicrosoftUEFICertificateAuthority\r\nSet-VMFirmware -VMName {value} -FirstBootDevice $dvd\r\n"),
				("Applying VM notes…", $"Set-VM -VMName {value} -Notes {value4}\r\n")
			};
			for (int i = 0; i < array.Length; i++) {
				progress?.Report (array [i].Item1);
				var (flag, item) = RunScriptTryCatch (array [i].Item2);
				if (!flag) {
					return (ok: false, message: item);
				}
			}
			progress?.Report ("Done");
			return (ok: true, message: string.Empty);
		}

		public static (bool ok, string message) CreateUbuntuCloudVm (CreateUbuntuCloudVmParameters p, IProgress<string>? progress = null)
		{
			string value = VmLiteral (p.VmName);
			string value2 = VmLiteral (p.SwitchName);
			string text = "'" + EscapeSingleQuoted (p.OsVhdFullPath) + "'";
			string value3 = "'" + EscapeSingleQuoted (p.TemplateVhdPath) + "'";
			string text2 = "'" + EscapeSingleQuoted (p.SeedVhdFullPath) + "'";
			string value4 = "'" + EscapeSingleQuoted (p.Notes) + "'";
			long memoryStartupBytes = p.MemoryStartupBytes;
			int processorCount = p.ProcessorCount;
			ulong osDiskSizeBytes = p.OsDiskSizeBytes;
			string s = CloudInitSeedBuilder.BuildUserData (p);
			string s2 = CloudInitSeedBuilder.BuildMetaData (string.IsNullOrWhiteSpace (p.Hostname) ? p.VmName : p.Hostname.Trim ());
			string? text3 = CloudInitSeedBuilder.BuildNetworkConfig (p);
			string udB = Convert.ToBase64String (Encoding.UTF8.GetBytes (s));
			string mdB = Convert.ToBase64String (Encoding.UTF8.GetBytes (s2));
			string ncB = ((text3 != null) ? Convert.ToBase64String (Encoding.UTF8.GetBytes (text3)) : "");
			(string, string)[] array = new(string, string)[8] {
				("Preparing directories…", $"$os = {text}\r\n$dir = Split-Path -LiteralPath $os\r\nif ($dir -and -not (Test-Path -LiteralPath $dir)) {{ New-Item -ItemType Directory -Path $dir -Force | Out-Null }}\r\n$sd = {text2}\r\n$sdir = Split-Path -LiteralPath $sd\r\nif ($sdir -and -not (Test-Path -LiteralPath $sdir)) {{ New-Item -ItemType Directory -Path $sdir -Force | Out-Null }}\r\n"),
				("Copying cloud base disk…", $"if (-not (Test-Path -LiteralPath {value3})) {{ throw 'Template VHD not found.' }}\r\nCopy-Item -LiteralPath {value3} -Destination {text} -Force\r\n"),
				("Sizing OS disk…", $"$v = Get-VHD -Path {text}\r\nif ($v.Size -lt {osDiskSizeBytes}) {{ Resize-VHD -Path {text} -SizeBytes {osDiskSizeBytes} }}\r\n"),
				("Patching GRUB for NoCloud datasource…", BuildGrubPatchScript (text)),
				("Building cloud-init seed disk…", BuildSeedDiskScript (udB, mdB, ncB, text2)),
				("Creating virtual machine…", $"New-VM -Name {value} -MemoryStartupBytes {memoryStartupBytes} -Generation 2 -VHDPath {text} -SwitchName {value2}\r\n"),
				("Configuring processor and disks…", $"Set-VMProcessor -VMName {value} -Count {processorCount}\r\nAdd-VMHardDiskDrive -VMName {value} -Path {text2}\r\n$expectedOs = [System.IO.Path]::GetFullPath({text})\r\n$osHdd = @(Get-VMHardDiskDrive -VMName {value} | Where-Object {{ $_.Path -and ([System.IO.Path]::GetFullPath($_.Path) -ieq $expectedOs) }})[0]\r\nif (-not $osHdd) {{ throw 'UEFI: could not find the OS VHD for FirstBootDevice (path mismatch after attaching seed disk).' }}\r\nSet-VMFirmware -VMName {value} -SecureBootTemplate MicrosoftUEFICertificateAuthority\r\nSet-VMFirmware -VMName {value} -FirstBootDevice $osHdd\r\nGet-VMDvdDrive -VMName {value} -ErrorAction SilentlyContinue | Remove-VMDvdDrive -ErrorAction SilentlyContinue\r\nif (@(Get-VMDvdDrive -VMName {value} -ErrorAction SilentlyContinue).Count -gt 0) {{ throw 'Virtual DVD drive could not be removed; Linux may hang on /dev/sr0.' }}\r\n"),
				("Applying VM notes and disabling auto-checkpoints…", $"Set-VM -VMName {value} -Notes {value4}\r\nSet-VM -VMName {value} -AutomaticCheckpointsEnabled $false\r\n")
			};
			for (int i = 0; i < array.Length; i++) {
				progress?.Report (array [i].Item1);
				var (flag, item) = RunScriptTryCatch (array [i].Item2);
				if (!flag) {
					return (ok: false, message: item);
				}
			}
			progress?.Report ("Done");
			return (ok: true, message: string.Empty);
		}

		private static string BuildGrubPatchScript (string osVhdPsSingleQuoted)
		{
			return "$vhd = Mount-VHD -Path " + osVhdPsSingleQuoted + " -PassThru\r\ntry {\r\n  Start-Sleep -Milliseconds 1500\r\n  $disk = $vhd | Get-Disk\r\n  $efiPart = Get-Partition -DiskNumber $disk.Number | Where-Object { $_.GptType -eq '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}' } | Select-Object -First 1\r\n  if (-not $efiPart) { Write-Output 'No EFI partition found — skipping GRUB patch.'; return }\r\n  if (-not $efiPart.DriveLetter) {\r\n    $efiPart | Add-PartitionAccessPath -AssignDriveLetter -ErrorAction Stop\r\n    $efiPart = Get-Partition -DiskNumber $disk.Number -PartitionNumber $efiPart.PartitionNumber\r\n  }\r\n  $efiRoot = $efiPart.DriveLetter.ToString() + ':\\'\r\n  $grubCfg = Join-Path $efiRoot 'EFI\\ubuntu\\grub.cfg'\r\n  if (-not (Test-Path $grubCfg)) { $grubCfg = (Get-ChildItem (Join-Path $efiRoot 'EFI') -Recurse -Filter 'grub.cfg' -ErrorAction SilentlyContinue | Select-Object -First 1).FullName }\r\n  if (-not $grubCfg -or -not (Test-Path $grubCfg)) { Write-Output 'grub.cfg not found on EFI — skipping patch.'; return }\r\n  $content = [System.IO.File]::ReadAllText($grubCfg)\r\n  Write-Output \"ORIGINAL_GRUB_CFG>>>$content<<<\"\r\n  $match = [regex]::Match($content, 'search\\.fs_uuid\\s+(\\S+)')\r\n  if (-not $match.Success) { Write-Output 'Could not extract root UUID from grub.cfg — skipping patch.'; return }\r\n  $rootUuid = $match.Groups[1].Value\r\n  $pfxMatch = [regex]::Match($content, \"set prefix=\\(\\`$root\\)'([^']+)'\")\r\n  $pfx = if ($pfxMatch.Success) { $pfxMatch.Groups[1].Value } else { '/grub' }\r\n  $newCfg = \"search.fs_uuid $rootUuid root`nset prefix=(`$root)'$pfx'`ninsmod linux`ninsmod gzio`nlinux (`$root)/vmlinuz root=LABEL=cloudimg-rootfs ro quiet splash ds=nocloud`ninitrd (`$root)/initrd.img`nboot`nconfigfile `$prefix/grub.cfg`n\"\r\n  Copy-Item -LiteralPath $grubCfg -Destination ($grubCfg + '.orig') -Force\r\n  [System.IO.File]::WriteAllText($grubCfg, $newCfg, [System.Text.UTF8Encoding]::new($false))\r\n} finally {\r\n  Dismount-VHD -Path " + osVhdPsSingleQuoted + " -ErrorAction SilentlyContinue\r\n}\r\n";
		}

		private static string BuildSeedDiskScript (string udB64, string mdB64, string ncB64, string seedPathPsSingleQuoted)
		{
			return "$seed = " + seedPathPsSingleQuoted + "\r\nif (Test-Path -LiteralPath $seed) {\r\n  Dismount-VHD -Path $seed -ErrorAction SilentlyContinue\r\n  $seedFull = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $seed).Path)\r\n  foreach ($vm in @(Get-VM -ErrorAction SilentlyContinue)) {\r\n    $hds = @(Get-VMHardDiskDrive -VMName $vm.Name -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ([System.IO.Path]::GetFullPath($_.Path) -ieq $seedFull) })\r\n    foreach ($hd in $hds) {\r\n      try { $hd | Remove-VMHardDiskDrive -ErrorAction Stop }\r\n      catch { Stop-VM -Name $vm.Name -TurnOff -Force -ErrorAction SilentlyContinue; $hd | Remove-VMHardDiskDrive -ErrorAction SilentlyContinue }\r\n    }\r\n  }\r\n  Dismount-VHD -Path $seed -ErrorAction SilentlyContinue\r\n  for ($i = 0; $i -lt 10; $i++) {\r\n    if (-not (Test-Path -LiteralPath $seed)) { break }\r\n    try { Remove-Item -LiteralPath $seed -Force -ErrorAction Stop; break } catch { Start-Sleep -Milliseconds 400 }\r\n  }\r\n  if (Test-Path -LiteralPath $seed) { throw 'Cannot replace seed VHD. Remove the CIDATA disk from the VM in Hyper-V Manager, or delete the old VM, then retry.' }\r\n}\r\n" + $"New-VHD -Path $seed -SizeBytes {536870912} -Dynamic | Out-Null\r\n" + "$ud = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('" + udB64 + "'))\r\n$md = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('" + mdB64 + "'))\r\n$vhd = Mount-VHD -Path $seed -PassThru\r\n$disk = $vhd | Get-Disk\r\nInitialize-Disk -InputObject $disk -PartitionStyle MBR\r\n$part = New-Partition -DiskNumber $disk.Number -UseMaximumSize -AssignDriveLetter\r\nFormat-Volume -Partition $part -FileSystem FAT32 -NewFileSystemLabel CIDATA -Confirm:$false | Out-Null\r\n$p2 = Get-Partition -DiskNumber $disk.Number | Where-Object { $_.DriveLetter -ne 0 } | Select-Object -First 1\r\n$root = $p2.DriveLetter.ToString() + ':\\'\r\n[System.IO.File]::WriteAllText((Join-Path $root 'user-data'), $ud, [System.Text.UTF8Encoding]::new($false))\r\n[System.IO.File]::WriteAllText((Join-Path $root 'meta-data'), $md, [System.Text.UTF8Encoding]::new($false))\r\n" + ((ncB64.Length > 0) ? ("$nc = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('" + ncB64 + "'))\r\n[System.IO.File]::WriteAllText((Join-Path $root 'network-config'), $nc, [System.Text.UTF8Encoding]::new($false))\r\n") : "") + "Dismount-VHD -Path $seed\r\n";
		}
	}

