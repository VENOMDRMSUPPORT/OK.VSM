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

	public static class VmInspectorService
	{
		public sealed class VmExtendedInfo
		{
			public string Mac { get; init; } = "—";

			public string Ipv4 { get; init; } = "—";

			public string Ipv6 { get; init; } = "—";

			public string OsHint { get; init; } = "—";

			public string InstallHint { get; init; } = "";

			public string OsVhdPath { get; init; } = "";

			public string SeedVhdPath { get; init; } = "";

			public string OsVhdActualSize { get; init; } = "";

			public string SeedVhdActualSize { get; init; } = "";
		}

		public sealed class VmNetworkRow
		{
			public string Name { get; init; } = "";

			public string Ipv4 { get; init; } = "";

			public string Ipv6 { get; init; } = "";

			public string Mac { get; init; } = "";

			public bool PendingIsoInstall { get; init; }
		}

		public const string IsoInstallPendingHint = "Start the VM to boot the installer from the mounted ISO. MAC and guest IP appear after the virtual NIC is active.";

		public const string CloudInitPendingHint = "Start the VM for unattended first boot (cloud-init will configure the guest).";

		public const string CloudInitRunningHint = "Cloud-init is running — guest IPv4 appears when the network is up.";

		private static string EscapeSingleQuoted (string s)
		{
			return s.Replace ("'", "''");
		}

		private static readonly Regex NotesIpv4TokenRegex = new Regex (
			@"\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d{1,2})\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d{1,2})\b",
			RegexOptions.CultureInvariant);

		/// <summary>Resolves a guest static IP from VM Notes: strict cloud-init line first, then any in-pool IPv4 (skips gateway/DNS literals).</summary>
		public static string TryResolveStaticGuestIpv4FromVmNotes (string? notes, Ipv4PoolSettings pool)
		{
			if (string.IsNullOrWhiteSpace (notes)) {
				return "";
			}
			string strict = TryParseCloudInitStaticIpv4FromNotes (notes);
			if (strict.Length > 0 && pool.ContainsAddress (strict)) {
				return strict;
			}
			string gw = (pool.DefaultGateway ?? "").Trim ();
			HashSet<string> skip = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
			if (gw.Length > 0 && Ipv4PoolSettings.TryParseIpv4 (gw, out _)) {
				skip.Add (gw);
			}
			foreach (string d in pool.EnumerateDnsServers ()) {
				skip.Add (d);
			}
			foreach (Match m in NotesIpv4TokenRegex.Matches (notes)) {
				string ip = m.Value;
				if (!pool.ContainsAddress (ip) || skip.Contains (ip)) {
					continue;
				}
				return ip;
			}
			return "";
		}

		public static string TryParseCloudInitStaticIpv4FromNotes (string? notes)
		{
			if (string.IsNullOrWhiteSpace (notes)) {
				return "";
			}
			// Format from Create VM: "Static guest IPv4 (cloud-init): 192.168.1.50" (allow extra whitespace / line breaks)
			const string pattern = @"Static\s+guest\s+IPv4\s*\(\s*cloud-init\s*\)\s*:\s*(\d{1,3}(?:\.\d{1,3}){3})";
			Match match = Regex.Match (notes, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
			if (match.Success) {
				return match.Groups [1].Value;
			}
			foreach (string line in notes.Split (new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
				match = Regex.Match (line.Trim (), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
				if (match.Success) {
					return match.Groups [1].Value;
				}
			}
			return "";
		}

		/// <summary>
		/// Reads Hyper-V VM Notes (persists across Off state) and returns static cloud-init IPv4 per VM.
		/// Used so pool allocation does not reuse an IP still assigned in guest config while the VM is stopped.
		/// </summary>
		public static IReadOnlyDictionary<string, string> QueryVmNameToStaticIpv4FromNotes (Ipv4PoolSettings pool)
		{
			const string script = "$ErrorActionPreference = 'SilentlyContinue'\r\n" +
				"$bucket = [System.Collections.Generic.List[object]]::new()\r\n" +
				"Get-VM -ErrorAction SilentlyContinue | ForEach-Object {\r\n" +
				"  [void]$bucket.Add([PSCustomObject]@{ Name = [string]$_.Name; Notes = [string]$_.Notes })\r\n" +
				"}\r\n" +
				"$bucket | ConvertTo-Json -Compress -Depth 5\r\n";
			var (flag, text) = RunScript (script);
			Dictionary<string, string> dict = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);
			if (!flag || string.IsNullOrWhiteSpace (text)) {
				return dict;
			}
			try {
				using JsonDocument jsonDocument = JsonDocument.Parse (text.Trim ());
				JsonElement rootElement = jsonDocument.RootElement;
				void AddFromElement (JsonElement el)
				{
					if (!el.TryGetProperty ("Name", out JsonElement nameEl)) {
						return;
					}
					string name = nameEl.GetString ()?.Trim () ?? "";
					if (name.Length == 0) {
						return;
					}
					string notes = el.TryGetProperty ("Notes", out JsonElement notesEl) ? (notesEl.GetString () ?? "") : "";
					string ip = TryResolveStaticGuestIpv4FromVmNotes (notes, pool);
					if (ip.Length > 0) {
						dict [name] = ip;
					}
				}
				if (rootElement.ValueKind == JsonValueKind.Array) {
					foreach (JsonElement item in rootElement.EnumerateArray ()) {
						AddFromElement (item);
					}
				} else if (rootElement.ValueKind == JsonValueKind.Object) {
					AddFromElement (rootElement);
				}
			} catch {
			}
			return dict;
		}

		internal static string SanitizeMac (string? raw)
		{
			if (string.IsNullOrWhiteSpace (raw)) {
				return "";
			}
			string text = raw.Replace (":", "").Replace ("-", "").Trim ();
			if (text.Length < 12) {
				return "";
			}
			if (text.All ((char c) => c == '0')) {
				return "";
			}
			return raw.Trim ();
		}

		public static IReadOnlyList<VmNetworkRow> QueryAllVmNetworks ()
		{
			string script = "$ErrorActionPreference = 'Continue'\r\nfunction Normalize-Mac([string]$m) {\r\n  if (-not $m) { return '' }\r\n  return ([string]$m -replace '[:-]','').ToLower()\r\n}\r\nfunction Test-ZeroMac([string]$m) {\r\n  if (-not $m) { return $true }\r\n  $s = ([string]$m -replace '[:-]','')\r\n  if ($s.Length -lt 12) { return $true }\r\n  return ($s -cmatch '^0{12}$')\r\n}\r\n$neighborByMac = @{}\r\nGet-NetNeighbor -AddressFamily IPv4 -ErrorAction SilentlyContinue | ForEach-Object {\r\n  $ip = $_.IPAddress\r\n  if (-not $ip) { return }\r\n  $ip = [string]$ip\r\n  if ($ip -match ':') { return }\r\n  if (-not $_.LinkLayerAddress) { return }\r\n  if (Test-ZeroMac ([string]$_.LinkLayerAddress)) { return }\r\n  $k = Normalize-Mac ([string]$_.LinkLayerAddress)\r\n  if (-not $k) { return }\r\n  if (-not $neighborByMac.ContainsKey($k)) { $neighborByMac[$k] = $ip }\r\n}\r\ntry {\r\n  $arpLines = @(arp -a 2>$null)\r\n  foreach ($line in $arpLines) {\r\n    if ($line -notmatch '(\\d+\\.\\d+\\.\\d+\\.\\d+)\\s+([0-9a-fA-F]{2}(-[0-9a-fA-F]{2}){5})') { continue }\r\n    $aip = $Matches[1]\r\n    $k = Normalize-Mac ($Matches[2])\r\n    if (-not $k) { continue }\r\n    if ($k -cmatch '^0{12}$') { continue }\r\n    if (-not $neighborByMac.ContainsKey($k)) { $neighborByMac[$k] = $aip }\r\n  }\r\n} catch { }\r\n$bucket = [System.Collections.Generic.List[object]]::new()\r\nGet-VM -ErrorAction SilentlyContinue | ForEach-Object {\r\n  $vmName = $_.Name\r\n  $vmNotes = [string]$_.Notes\r\n  $vmState = [string]$_.State\r\n  $ipv4 = [System.Collections.Generic.List[string]]::new()\r\n  $ipv6 = [System.Collections.Generic.List[string]]::new()\r\n  $macList = [System.Collections.Generic.List[string]]::new()\r\n  $macFirst = ''\r\n  Get-VMNetworkAdapter -VMName $vmName -ErrorAction SilentlyContinue | ForEach-Object {\r\n    $ma = [string]$_.MacAddress\r\n    if ($ma -and -not (Test-ZeroMac $ma)) {\r\n      [void]$macList.Add($ma)\r\n      if (-not $macFirst) { $macFirst = $ma }\r\n    }\r\n    foreach ($ip in @($_.IPAddresses)) {\r\n      if (-not $ip) { continue }\r\n      $s = [string]$ip\r\n      if ($s -match ':') { if (-not $ipv6.Contains($s)) { $ipv6.Add($s) } }\r\n      else { if (-not $ipv4.Contains($s)) { $ipv4.Add($s) } }\r\n    }\r\n  }\r\n  foreach ($m in $macList) {\r\n    $k = Normalize-Mac $m\r\n    if (-not $k -or ($k -cmatch '^0{12}$')) { continue }\r\n    if ($neighborByMac.ContainsKey($k)) {\r\n      $s = [string]$neighborByMac[$k]\r\n      if (-not $ipv4.Contains($s)) { [void]$ipv4.Add($s) }\r\n    }\r\n  }\r\n  if (($ipv4.Count -eq 0) -and $vmNotes -and ($vmNotes -match 'Static guest IPv4 \\(cloud-init\\):\\s*(\\d+\\.\\d+\\.\\d+\\.\\d+)')) {\r\n    [void]$ipv4.Add([string]$Matches[1])\r\n  }\r\n  $dvdMounted = $false\r\n  Get-VMDvdDrive -VMName $vmName -ErrorAction SilentlyContinue | ForEach-Object {\r\n    if ($_.Path -and [string]$_.Path -ne '') { $dvdMounted = $true }\r\n  }\r\n  $pendingIsoInstall = ($vmState -eq 'Off') -and $dvdMounted\r\n  $bucket.Add([PSCustomObject]@{\r\n    Name = $vmName\r\n    Ipv4 = ($ipv4 -join ', ')\r\n    Ipv6 = ($ipv6 -join ', ')\r\n    Mac = $macFirst\r\n    PendingIsoInstall = $pendingIsoInstall\r\n  })\r\n}\r\n$bucket | ConvertTo-Json -Compress -Depth 6\r\n";
			var (flag, text) = RunScript (script);
			if (!flag || string.IsNullOrWhiteSpace (text)) {
				return Array.Empty<VmNetworkRow> ();
			}
			try {
				using JsonDocument jsonDocument = JsonDocument.Parse (text.Trim ());
				return ParseNetworkRows (jsonDocument.RootElement);
			} catch {
				return Array.Empty<VmNetworkRow> ();
			}
		}

		private static IReadOnlyList<VmNetworkRow> ParseNetworkRows (JsonElement root)
		{
			List<VmNetworkRow> list = new List<VmNetworkRow> ();
			if (root.ValueKind == JsonValueKind.Array) {
				foreach (JsonElement item in root.EnumerateArray ()) {
					TryAddRow (item, list);
				}
			} else {
				TryAddRow (root, list);
			}
			return list;
		}

		private static void TryAddRow (JsonElement el, List<VmNetworkRow> list)
		{
			JsonElement value;
			string text = (el.TryGetProperty ("Name", out value) ? (value.GetString () ?? "") : "");
			if (text.Length != 0) {
				JsonElement value2;
				string ipv = (el.TryGetProperty ("Ipv4", out value2) ? (value2.GetString () ?? "") : "");
				JsonElement value3;
				string ipv2 = (el.TryGetProperty ("Ipv6", out value3) ? (value3.GetString () ?? "") : "");
				JsonElement value4;
				string mac = SanitizeMac (el.TryGetProperty ("Mac", out value4) ? value4.GetString () : null);
				JsonElement value5;
				bool pendingIsoInstall = el.TryGetProperty ("PendingIsoInstall", out value5) && (value5.ValueKind == JsonValueKind.True || (value5.ValueKind == JsonValueKind.String && string.Equals (value5.GetString (), "true", StringComparison.OrdinalIgnoreCase)));
				list.Add (new VmNetworkRow {
					Name = text,
					Ipv4 = ipv,
					Ipv6 = ipv2,
					Mac = mac,
					PendingIsoInstall = pendingIsoInstall
				});
			}
		}

		public static VmExtendedInfo QueryExtendedInfo (string vmName)
		{
			string text = EscapeSingleQuoted (vmName);
			string script = "$ErrorActionPreference = 'Stop'\r\nfunction Normalize-Mac([string]$m) {\r\n  if (-not $m) { return '' }\r\n  return ([string]$m -replace '[:-]','').ToLower()\r\n}\r\nfunction Test-ZeroMac([string]$m) {\r\n  if (-not $m) { return $true }\r\n  $s = ([string]$m -replace '[:-]','')\r\n  if ($s.Length -lt 12) { return $true }\r\n  return ($s -cmatch '^0{12}$')\r\n}\r\ntry {\r\n  $vm = Get-VM -Name '" + text + "' -ErrorAction Stop\r\n  $notes = [string]$vm.Notes\r\n  $gen = [int]$vm.Generation\r\n  $osHint = if ($notes) { $notes } else { \"Generation $gen — set guest OS in VM Settings or use Create VM (Ubuntu 22/24)\" }\r\n  $neighborByMac = @{}\r\n  Get-NetNeighbor -AddressFamily IPv4 -ErrorAction SilentlyContinue | ForEach-Object {\r\n    $ip = $_.IPAddress\r\n    if (-not $ip) { return }\r\n    $ip = [string]$ip\r\n    if ($ip -match ':') { return }\r\n    if (-not $_.LinkLayerAddress) { return }\r\n    if (Test-ZeroMac ([string]$_.LinkLayerAddress)) { return }\r\n    $k = Normalize-Mac ([string]$_.LinkLayerAddress)\r\n    if (-not $k) { return }\r\n    if (-not $neighborByMac.ContainsKey($k)) { $neighborByMac[$k] = $ip }\r\n  }\r\n  try {\r\n    $arpLines = @(arp -a 2>$null)\r\n    foreach ($line in $arpLines) {\r\n      if ($line -notmatch '(\\d+\\.\\d+\\.\\d+\\.\\d+)\\s+([0-9a-fA-F]{2}(-[0-9a-fA-F]{2}){5})') { continue }\r\n      $aip = $Matches[1]\r\n      $k = Normalize-Mac ($Matches[2])\r\n      if (-not $k -or ($k -cmatch '^0{12}$')) { continue }\r\n      if (-not $neighborByMac.ContainsKey($k)) { $neighborByMac[$k] = $aip }\r\n    }\r\n  } catch { }\r\n  $ipv4 = [System.Collections.Generic.List[string]]::new()\r\n  $ipv6 = [System.Collections.Generic.List[string]]::new()\r\n  $macList = [System.Collections.Generic.List[string]]::new()\r\n  $macFirst = ''\r\n  Get-VMNetworkAdapter -VMName $vm.Name -ErrorAction SilentlyContinue | ForEach-Object {\r\n    $ma = [string]$_.MacAddress\r\n    if ($ma -and -not (Test-ZeroMac $ma)) {\r\n      [void]$macList.Add($ma)\r\n      if (-not $macFirst) { $macFirst = $ma }\r\n    }\r\n    foreach ($ip in @($_.IPAddresses)) {\r\n      if (-not $ip) { continue }\r\n      $s = [string]$ip\r\n      if ($s -match ':') { if (-not $ipv6.Contains($s)) { $ipv6.Add($s) } }\r\n      else { if (-not $ipv4.Contains($s)) { $ipv4.Add($s) } }\r\n    }\r\n  }\r\n  foreach ($m in $macList) {\r\n    $k = Normalize-Mac $m\r\n    if (-not $k -or ($k -cmatch '^0{12}$')) { continue }\r\n    if ($neighborByMac.ContainsKey($k)) {\r\n      $s = [string]$neighborByMac[$k]\r\n      if (-not $ipv4.Contains($s)) { [void]$ipv4.Add($s) }\r\n    }\r\n  }\r\n  $dvdMounted = $false\r\n  Get-VMDvdDrive -VMName $vm.Name -ErrorAction SilentlyContinue | ForEach-Object {\r\n    if ($_.Path -and [string]$_.Path -ne '') { $dvdMounted = $true }\r\n  }\r\n  $installHint = ''\r\n  if (($vm.State -eq 'Off') -and $dvdMounted) {\r\n    $installHint = '" + "Start the VM to boot the installer from the mounted ISO. MAC and guest IP appear after the virtual NIC is active.".Replace ("'", "''") + "'\r\n  }\r\n  $h = @{\r\n    Mac = $macFirst\r\n    Ipv4 = ($ipv4 -join ', ')\r\n    Ipv6 = ($ipv6 -join ', ')\r\n    OsHint = $osHint\r\n    InstallHint = $installHint\r\n  }\r\n  $h | ConvertTo-Json -Compress\r\n} catch {\r\n  @{ Mac = ''; Ipv4 = ''; Ipv6 = ''; OsHint = ($_.Exception.Message); InstallHint = '' } | ConvertTo-Json -Compress\r\n  exit 1\r\n}\r\n";
			var (flag, text2) = RunScript (script);
			if (!flag || string.IsNullOrWhiteSpace (text2)) {
				return new VmExtendedInfo {
					OsHint = (flag ? "No data returned." : HumanizePs (text2)),
					Mac = "—",
					Ipv4 = "—",
					Ipv6 = "—",
					InstallHint = ""
				};
			}
			try {
				using JsonDocument jsonDocument = JsonDocument.Parse (text2.Trim ());
				JsonElement rootElement = jsonDocument.RootElement;
				JsonElement value;
				string? raw = (rootElement.TryGetProperty ("Mac", out value) ? value.GetString () : null);
				string text3 = SanitizeMac (raw);
				string mac = ((text3 != null && text3.Length > 0) ? text3 : "—");
				JsonElement value2;
				string text4 = (rootElement.TryGetProperty ("OsHint", out value2) ? (value2.GetString () ?? "—") : "—");
				object obj;
				if (rootElement.TryGetProperty ("Ipv4", out var value3)) {
					string? text5 = value3.GetString ();
					if (text5 != null && text5.Length > 0) {
						obj = text5;
						goto IL_0179;
					}
				}
				obj = "—";
				goto IL_0179;
				IL_0179:
				string text6 = (string)obj;
				if (string.IsNullOrWhiteSpace (text6) || text6 == "—") {
					string text7 = TryParseCloudInitStaticIpv4FromNotes (text4);
					if (text7.Length > 0) {
						text6 = text7;
					}
				}
				object obj2;
				if (rootElement.TryGetProperty ("Ipv6", out var value4)) {
					string? text8 = value4.GetString ();
					if (text8 != null && text8.Length > 0) {
						obj2 = text8;
						goto IL_01e8;
					}
				}
				obj2 = "—";
				goto IL_01e8;
				IL_01e8:
				string ipv = (string)obj2;
				JsonElement value5;
				string installHint = (rootElement.TryGetProperty ("InstallHint", out value5) ? (value5.GetString () ?? "") : "");
				HyperVService.QueryVmDiskInfoViaPowerShell ().TryGetValue (vmName, out var diskInfo);
				return new VmExtendedInfo {
					Mac = mac,
					Ipv4 = text6,
					Ipv6 = ipv,
					OsHint = text4,
					InstallHint = installHint,
					OsVhdPath = diskInfo?.OsVhdPath ?? "",
					SeedVhdPath = diskInfo?.SeedVhdPath ?? "",
					OsVhdActualSize = diskInfo?.OsVhdActualSize ?? "",
					SeedVhdActualSize = diskInfo?.SeedVhdActualSize ?? ""
				};
			} catch {
				return new VmExtendedInfo {
					OsHint = "Could not parse network details."
				};
			}
		}

		private static string HumanizePs (string raw)
		{
			if (string.IsNullOrWhiteSpace (raw)) {
				return "Unknown PowerShell error.";
			}
			string text = raw.Trim ();
			if (!text.Contains ("<Objs", StringComparison.Ordinal)) {
				return (text.Length > 400) ? (text.Substring (0, 400) + "…") : text;
			}
			StringBuilder stringBuilder = new StringBuilder ();
			foreach (Match item in Regex.Matches (text, "<S[^>]*>([^<]+)</S>")) {
				string text2 = item.Groups [1].Value.Trim ();
				if (text2.Length >= 4) {
					if (stringBuilder.Length > 0) {
						stringBuilder.Append (' ');
					}
					stringBuilder.Append (text2);
				}
			}
			return (stringBuilder.Length > 0) ? stringBuilder.ToString () : "PowerShell error.";
		}

		/// <summary>
		/// Persists a discovered guest IP into Hyper-V VM Notes so pool allocation
		/// finds it even when the VM is Off (ARP/neighbors are empty).
		/// Runs fire-and-forget on a background thread.
		/// </summary>
		public static void PersistStaticIpv4InVmNotesAsync (string vmName, string ipv4)
		{
			if (string.IsNullOrWhiteSpace (vmName) || string.IsNullOrWhiteSpace (ipv4)) {
				return;
			}
			string safeName = EscapeSingleQuoted (vmName.Trim ());
			string safeIp   = ipv4.Trim ();
			string tag = "Static guest IPv4 (cloud-init): " + safeIp;

			string script =
				"$ErrorActionPreference = 'Stop'\r\n" +
				"$vm = Get-VM -Name '" + safeName + "'\r\n" +
				"$existing = [string]$vm.Notes\r\n" +
				"if ($existing -match 'Static guest IPv4 \\(cloud-init\\):\\s*" + safeIp + "') { return }\r\n" +
				"$cleaned = ($existing -replace 'Static guest IPv4 \\(cloud-init\\):\\s*[\\d\\.]+', '').Trim()\r\n" +
				"$newNotes = if ($cleaned.Length -gt 0) { $cleaned + \"`n\" + '" + tag + "' } else { '" + tag + "' }\r\n" +
				"Set-VM -Name '" + safeName + "' -Notes $newNotes\r\n";

			Task.Run (() => RunScript (script));
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
				using Process? process = Process.Start (startInfo);
				if (process == null) {
					return (ok: false, message: "Could not start PowerShell.");
				}
				string text2 = process.StandardError.ReadToEnd ();
				string text3 = process.StandardOutput.ReadToEnd ();
				process.WaitForExit (120000);
				if (process.ExitCode != 0) {
					return (ok: false, message: string.IsNullOrWhiteSpace (text3) ? text2 : text3);
				}
				return (ok: true, message: text3.Trim ());
			} catch (Exception ex) {
				return (ok: false, message: ex.Message);
			}
		}
	}

