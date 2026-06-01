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

	public static class HyperVService
	{
		private const string WmiScope = "\\\\.\\root\\virtualization\\v2";

		public static List<VirtualMachine> GetVirtualMachinesFast ()
		{
			List<VirtualMachine> list = new List<VirtualMachine> ();
			ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher ("\\\\.\\root\\virtualization\\v2", "SELECT ElementName, EnabledState, Name, OnTimeInMilliseconds FROM Msvm_ComputerSystem WHERE Caption = 'Virtual Machine'");
			try {
				foreach (ManagementObject item2 in managementObjectSearcher.Get ()) {
					ushort num = Convert.ToUInt16 (item2 ["EnabledState"]);
					VirtualMachine item = new VirtualMachine {
						Name = (item2 ["ElementName"]?.ToString () ?? "Unknown"),
						EnabledState = num,
						WmiName = (item2 ["Name"]?.ToString () ?? ""),
						Status = GetStatusText (num),
						Uptime = GetUptime (item2),
						IsRunning = (num == 2)
					};
					list.Add (item);
				}
				return list;
			} finally {
				((IDisposable)managementObjectSearcher)?.Dispose ();
			}
		}

		public static VmBulkDetails QueryVmResourceDetails (IReadOnlyList<VirtualMachine> vms)
		{
			HostResourceSnapshot snapshot = HostInfoService.GetSnapshot ();
			Dictionary<string, int> dictionary = new Dictionary<string, int> ();
			Dictionary<string, ulong> dictionary2 = new Dictionary<string, ulong> ();
			Dictionary<string, ulong> dictionary3 = new Dictionary<string, ulong> ();
			try {
				ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher ("\\\\.\\root\\virtualization\\v2", "SELECT VirtualQuantity, InstanceID FROM Msvm_ProcessorSettingData");
				try {
					foreach (ManagementObject item in managementObjectSearcher.Get ()) {
						string text = item ["InstanceID"]?.ToString () ?? "";
						int value = Convert.ToInt32 (item ["VirtualQuantity"]);
						foreach (VirtualMachine vm in vms) {
							if (text.Contains (vm.WmiName, StringComparison.OrdinalIgnoreCase)) {
								dictionary [vm.WmiName] = value;
								break;
							}
						}
					}
				} finally {
					((IDisposable)managementObjectSearcher)?.Dispose ();
				}
			} catch {
			}
			try {
				Dictionary<string, ulong> dictionary4 = QueryMemoryStartupViaPowerShell ();
				foreach (VirtualMachine vm2 in vms) {
					if (dictionary4.TryGetValue (vm2.Name, out var value2) && value2 != 0) {
						dictionary2 [vm2.WmiName] = value2;
					}
				}
			} catch {
			}
			try {
				ManagementObjectSearcher managementObjectSearcher2 = new ManagementObjectSearcher ("\\\\.\\root\\virtualization\\v2", "SELECT Limit, InstanceID FROM Msvm_StorageAllocationSettingData");
				try {
					foreach (ManagementObject item2 in managementObjectSearcher2.Get ()) {
						string text2 = item2 ["InstanceID"]?.ToString () ?? "";
						ulong num = Convert.ToUInt64 (item2 ["Limit"]);
						foreach (VirtualMachine vm3 in vms) {
							if (text2.Contains (vm3.WmiName, StringComparison.OrdinalIgnoreCase)) {
								if (!dictionary3.ContainsKey (vm3.WmiName)) {
									dictionary3 [vm3.WmiName] = 0uL;
								}
								dictionary3 [vm3.WmiName] += num;
							}
						}
					}
				} finally {
					((IDisposable)managementObjectSearcher2)?.Dispose ();
				}
			} catch {
			}
			Dictionary<string, VmDiskInfo> diskInfoByName = QueryVmDiskInfoViaPowerShell ();
			return new VmBulkDetails {
				CpuByVm = dictionary,
				MemoryBytesByVm = dictionary2,
				DiskBytesByVm = dictionary3,
				DiskInfoByVmName = diskInfoByName,
				Host = snapshot
			};
		}

		public static void ApplyVmResourceDetails (IReadOnlyList<VirtualMachine> vms, VmBulkDetails details)
		{
			HostResourceSnapshot host = details.Host;
			foreach (VirtualMachine vm in vms) {
				int valueOrDefault = details.CpuByVm.GetValueOrDefault (vm.WmiName, 0);
				details.MemoryBytesByVm.TryGetValue (vm.WmiName, out var value);
				details.DiskBytesByVm.TryGetValue (vm.WmiName, out var value2);
				if (value2 < 1048576) {
					value2 = 0uL;
				}
				vm.CpuCount = valueOrDefault;
				vm.Memory = ((value != 0) ? FormatSize (value) : "N/A");
				vm.DiskSize = ((value2 != 0) ? FormatSize (value2) : "—");
				vm.CpuGaugePercent = ((host.LogicalProcessors > 0) ? Math.Min (100.0, (double)valueOrDefault * 100.0 / (double)host.LogicalProcessors) : 0.0);
				vm.CpuGaugeCaption = $"{valueOrDefault} / {host.LogicalProcessors} vCPU";
				vm.MemoryGaugePercent = ((host.TotalMemoryBytes != 0 && value != 0) ? Math.Min (100.0, (double)value * 100.0 / (double)host.TotalMemoryBytes) : 0.0);
				vm.MemoryGaugeCaption = ((value != 0) ? (FormatSize (value) + " / " + FormatSize (host.TotalMemoryBytes)) : "N/A");
				vm.MemoryAllocationCritical = vm.IsRunning && host.TotalMemoryBytes != 0 && value != 0 && vm.MemoryGaugePercent >= 90.0;
				vm.DiskAllocationMeaningful = value2 >= 1048576;
				vm.DiskGaugePercent = ((vm.DiskAllocationMeaningful && host.TotalFixedDiskBytes != 0) ? Math.Min (100.0, (double)value2 * 100.0 / (double)host.TotalFixedDiskBytes) : 0.0);
				vm.DiskGaugeCaption = (vm.DiskAllocationMeaningful ? (FormatSize (value2) + " / " + FormatSize (host.TotalFixedDiskBytes)) : "No provisioned VHD total (< 1 MB)");
				vm.ShowDiskGauge = vm.IsRunning && vm.DiskAllocationMeaningful;
				if (details.DiskInfoByVmName.TryGetValue (vm.Name, out var diskInfo)) {
					vm.OsVhdPath = diskInfo.OsVhdPath;
					vm.SeedVhdPath = diskInfo.SeedVhdPath;
					vm.OsVhdActualSize = diskInfo.OsVhdActualSize;
					vm.SeedVhdActualSize = diskInfo.SeedVhdActualSize;
					vm.OsVhdParentPath = diskInfo.OsVhdParentPath;
					vm.OsVhdParentActualSize = diskInfo.OsVhdParentActualSize;
					vm.OsVhdType = diskInfo.OsVhdType;
				}
				vm.DetailsLoaded = true;
			}
		}

		public static Dictionary<string, VmDiskInfo> QueryVmDiskInfoViaPowerShell ()
		{
			Dictionary<string, VmDiskInfo> dictionary = new Dictionary<string, VmDiskInfo> (StringComparer.OrdinalIgnoreCase);
			try {
				string s = "$ErrorActionPreference = 'SilentlyContinue'\r\n" +
					"function Format-Size([long]$bytes) {\r\n" +
					"  if ($bytes -le 0) { return '' }\r\n" +
					"  $units = @('B','KB','MB','GB','TB')\r\n" +
					"  $n = [double]$bytes; $i = 0\r\n" +
					"  while ($n -ge 1024 -and $i -lt $units.Length - 1) { $n = $n / 1024; $i++ }\r\n" +
					"  return ('{0:N1} {1}' -f $n, $units[$i])\r\n" +
					"}\r\n" +
					"$bucket = [System.Collections.Generic.List[object]]::new()\r\n" +
					"Get-VM -ErrorAction SilentlyContinue | ForEach-Object {\r\n" +
					"  $vmName = [string]$_.Name\r\n" +
					"  $paths = @(Get-VMHardDiskDrive -VMName $vmName -ErrorAction SilentlyContinue | Where-Object { $_.Path } | ForEach-Object { [string]$_.Path })\r\n" +
					"  $seed = ($paths | Where-Object { $_ -match '(?i)(cidata|seed)' } | Select-Object -First 1)\r\n" +
					"  $os = ($paths | Where-Object { $_ -and $_ -ne $seed } | Select-Object -First 1)\r\n" +
					"  if (-not $os -and $paths.Count -gt 0) { $os = $paths[0] }\r\n" +
					"  $osBytes = if ($os -and (Test-Path -LiteralPath $os)) { (Get-Item -LiteralPath $os).Length } else { 0 }\r\n" +
					"  $seedBytes = if ($seed -and (Test-Path -LiteralPath $seed)) { (Get-Item -LiteralPath $seed).Length } else { 0 }\r\n" +
					"  $parent = ''\r\n" +
					"  $parentBytes = 0\r\n" +
					"  if ($os -and (Test-Path -LiteralPath $os)) {\r\n" +
					"    try {\r\n" +
					"      $vhd = Get-VHD -Path $os -ErrorAction Stop\r\n" +
					"      if ($vhd.ParentPath) {\r\n" +
					"        $parent = [string]$vhd.ParentPath\r\n" +
					"        if (Test-Path -LiteralPath $parent) { $parentBytes = (Get-Item -LiteralPath $parent).Length }\r\n" +
					"      }\r\n" +
					"    } catch { }\r\n" +
					"  }\r\n" +
					"  $vhdType = ''\r\n" +
					"  if ($os -and (Test-Path -LiteralPath $os)) {\r\n" +
					"    try { $vhdInfo = Get-VHD -Path $os -ErrorAction Stop; $vhdType = [string]$vhdInfo.VhdType } catch { }\r\n" +
					"  }\r\n" +
					"  $bucket.Add([PSCustomObject]@{ Name = $vmName; Os = [string]$os; Seed = [string]$seed; OsSize = (Format-Size $osBytes); SeedSize = (Format-Size $seedBytes); Parent = [string]$parent; ParentSize = (Format-Size $parentBytes); VhdType = $vhdType })\r\n" +
					"}\r\n" +
					"$bucket | ConvertTo-Json -Compress -Depth 5\r\n";
				string text = Convert.ToBase64String (Encoding.Unicode.GetBytes (s));
				ProcessStartInfo startInfo = new ProcessStartInfo {
					FileName = "powershell.exe",
					Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + text,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true
				};
				using Process? process = Process.Start (startInfo);
				if (process == null) {
					return dictionary;
				}
				string text2 = process.StandardOutput.ReadToEnd ();
				process.WaitForExit (30000);
				if (process.ExitCode != 0 || string.IsNullOrWhiteSpace (text2)) {
					return dictionary;
				}
				using JsonDocument jsonDocument = JsonDocument.Parse (text2.Trim ());
				JsonElement rootElement = jsonDocument.RootElement;
				void AddDiskInfo (JsonElement el)
				{
					string name = el.TryGetProperty ("Name", out var nameEl) ? (nameEl.GetString () ?? "") : "";
					if (string.IsNullOrWhiteSpace (name)) {
						return;
					}
					dictionary [name] = new VmDiskInfo {
						OsVhdPath = el.TryGetProperty ("Os", out var osEl) ? (osEl.GetString () ?? "") : "",
						SeedVhdPath = el.TryGetProperty ("Seed", out var seedEl) ? (seedEl.GetString () ?? "") : "",
						OsVhdActualSize = el.TryGetProperty ("OsSize", out var osSizeEl) ? (osSizeEl.GetString () ?? "") : "",
						SeedVhdActualSize = el.TryGetProperty ("SeedSize", out var seedSizeEl) ? (seedSizeEl.GetString () ?? "") : "",
						OsVhdParentPath = el.TryGetProperty ("Parent", out var parentEl) ? (parentEl.GetString () ?? "") : "",
						OsVhdParentActualSize = el.TryGetProperty ("ParentSize", out var parentSizeEl) ? (parentSizeEl.GetString () ?? "") : "",
						OsVhdType = el.TryGetProperty ("VhdType", out var vhdTypeEl) ? (vhdTypeEl.GetString () ?? "") : ""
						};
				}
				if (rootElement.ValueKind == JsonValueKind.Array) {
					foreach (JsonElement item in rootElement.EnumerateArray ()) {
						AddDiskInfo (item);
					}
				} else if (rootElement.ValueKind == JsonValueKind.Object) {
					AddDiskInfo (rootElement);
				}
			} catch {
			}
			return dictionary;
		}

		private static Dictionary<string, ulong> QueryMemoryStartupViaPowerShell ()
		{
			Dictionary<string, ulong> dictionary = new Dictionary<string, ulong> (StringComparer.OrdinalIgnoreCase);
			try {
				string s = "$ErrorActionPreference = 'SilentlyContinue'\r\nGet-VM | ForEach-Object {\r\n  $mem = (Get-VMMemory -VMName $_.Name).Startup\r\n  [PSCustomObject]@{ N = $_.Name; B = [long]$mem }\r\n} | ConvertTo-Json -Compress -Depth 4\r\n";
				string text = Convert.ToBase64String (Encoding.Unicode.GetBytes (s));
				ProcessStartInfo startInfo = new ProcessStartInfo {
					FileName = "powershell.exe",
					Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + text,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true
				};
				using Process? process = Process.Start (startInfo);
				if (process == null) {
					return dictionary;
				}
				string text2 = process.StandardOutput.ReadToEnd ();
				process.WaitForExit (30000);
				if (process.ExitCode != 0 || string.IsNullOrWhiteSpace (text2)) {
					return dictionary;
				}
				using JsonDocument jsonDocument = JsonDocument.Parse (text2.Trim ());
				JsonElement rootElement = jsonDocument.RootElement;
				if (rootElement.ValueKind == JsonValueKind.Array) {
					foreach (JsonElement item in rootElement.EnumerateArray ()) {
						TryAddMemEntry (item, dictionary);
					}
				} else {
					TryAddMemEntry (rootElement, dictionary);
				}
			} catch {
			}
			return dictionary;
		}

		private static void TryAddMemEntry (JsonElement el, Dictionary<string, ulong> dict)
		{
			JsonElement value;
			string text = (el.TryGetProperty ("N", out value) ? (value.GetString () ?? "") : "");
			if (text.Length != 0) {
				JsonElement value2;
				ulong num = (ulong)(el.TryGetProperty ("B", out value2) ? Math.Max (0L, value2.GetInt64 ()) : 0);
				if (num != 0) {
					dict [text] = num;
				}
			}
		}

		private static string GetStatusText (ushort state)
		{
			if (1 == 0) {
			}
			string result = state switch {
				0 => "Unknown", 
				2 => "Running", 
				3 => "Off", 
				32768 => "Paused", 
				32769 => "Saved", 
				32770 => "Starting", 
				32771 => "Stopping", 
				32773 => "Saving", 
				32774 => "Resuming", 
				_ => $"Other ({state})", 
			};
			if (1 == 0) {
			}
			return result;
		}

		private static string GetUptime (ManagementObject vm)
		{
			try {
				object obj = vm ["OnTimeInMilliseconds"];
				if (obj != null) {
					ulong num = Convert.ToUInt64 (obj);
					if (num != 0) {
						TimeSpan timeSpan = TimeSpan.FromMilliseconds (num);
						List<string> list = new List<string> ();
						if (timeSpan.Days > 0) {
							list.Add ($"{timeSpan.Days}d");
						}
						if (timeSpan.Hours > 0) {
							list.Add ($"{timeSpan.Hours}h");
						}
						if (timeSpan.Minutes > 0) {
							list.Add ($"{timeSpan.Minutes}m");
						}
						if (list.Count == 0) {
							list.Add ($"{timeSpan.Seconds}s");
						}
						return string.Join (" ", list);
					}
				}
			} catch {
			}
			return "N/A";
		}

		private static string FormatSize (ulong bytes)
		{
			if (bytes == 0) {
				return "N/A";
			}
			string[] array = new string[5] { "B", "KB", "MB", "GB", "TB" };
			double num = bytes;
			int num2 = 0;
			while (num >= 1024.0 && num2 < array.Length - 1) {
				num /= 1024.0;
				num2++;
			}
			return $"{num:F1} {array [num2]}";
		}

		/// <summary>
		/// Scans all known VM storage directories for VHDX folders that are not
		/// linked to any registered Hyper-V virtual machine (orphaned disks).
		/// </summary>
		public static List<OrphanedVhdxInfo> ScanOrphanedVhdx ()
		{
			var result = new List<OrphanedVhdxInfo> ();
			try {
				// Collect all registered VHDX paths from Hyper-V
				var registeredPaths = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
				try {
					using var searcher = new ManagementObjectSearcher (
						"SELECT HardDrivePath FROM Msvm_ResourceAllocationSettingData WHERE ResourceSubType = 'Microsoft:Hyper-V:Virtual Hard Disk'");
					foreach (ManagementObject obj in searcher.Get ()) {
						string? path = obj ["HardDrivePath"] as string;
						if (!string.IsNullOrWhiteSpace (path)) {
							registeredPaths.Add (System.IO.Path.GetFullPath (path));
						}
					}
				} catch {
				}
				// Also check HardDiskImagePath from VM settings
				try {
					using var searcher2 = new ManagementObjectSearcher (
						"SELECT HardDiskImagePath FROM Msvm_VirtualSystemSettingData WHERE VirtualSystemType = 'Microsoft:Hyper-V:Virtual Machine'");
					foreach (ManagementObject obj in searcher2.Get ()) {
						string? path = obj ["HardDiskImagePath"] as string;
						if (!string.IsNullOrWhiteSpace (path)) {
							registeredPaths.Add (System.IO.Path.GetFullPath (path));
						}
					}
				} catch {
				}

				// Scan all fixed + removable drives for "vhdx" subdirectories
				foreach (var drive in DriveInfo.GetDrives ()) {
					if (!drive.IsReady) continue;
					if (drive.DriveType == DriveType.CDRom || drive.DriveType == DriveType.Network) continue;
					string vhdxRoot = System.IO.Path.Combine (drive.Name, "vhdx");
					if (!Directory.Exists (vhdxRoot)) continue;

					foreach (string folder in Directory.GetDirectories (vhdxRoot)) {
						string folderName = System.IO.Path.GetFileName (folder);
						// Find VHDX files inside this folder
						string[] vhdxFiles;
						try {
							vhdxFiles = Directory.GetFiles (folder, "*.vhdx", SearchOption.AllDirectories);
						} catch { continue; }

						foreach (string vhdxFile in vhdxFiles) {
							string fullPath = System.IO.Path.GetFullPath (vhdxFile);
							if (registeredPaths.Contains (fullPath)) continue;

							// This VHDX is not registered to any VM — it's orphaned
							try {
								var fi = new FileInfo (vhdxFile);
								var entry = new OrphanedVhdxInfo {
									FolderName = folderName,
									VhdxPath = fullPath,
									SizeBytes = fi.Length,
									SizeDisplay = $"{fi.Length / (1024.0 * 1024.0 * 1024.0):F1} GB",
									ActualSizeDisplay = $"{fi.Length / (1024.0 * 1024.0 * 1024.0):F1} GB",
								};
								result.Add (entry);
							} catch {
							}
						}
					}
				}
			} catch {
			}
			return result;
		}

		/// <summary>
		/// Deletes an orphaned VHDX file and its parent folder if empty.
		/// </summary>
		public static (bool ok, string message) DeleteOrphanedVhdx (string vhdxPath)
		{
			try {
				string fullPath = System.IO.Path.GetFullPath (vhdxPath);
				if (!File.Exists (fullPath)) return (false, "File not found.");
				File.Delete (fullPath);
				string? folder = System.IO.Path.GetDirectoryName (fullPath);
				if (folder != null && Directory.Exists (folder)) {
					try {
						if (Directory.GetFileSystemEntries (folder).Length == 0) {
							Directory.Delete (folder);
						}
					} catch {
					}
				}
				return (true, "Deleted.");
			} catch (Exception ex) {
				return (false, ex.Message);
			}
		}
	}

