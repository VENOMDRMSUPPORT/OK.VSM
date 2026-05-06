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

	public static class VmProvisionStore
	{
		private static readonly ConcurrentDictionary<string, VmProvisionEntry> Cache;

		private static readonly JsonSerializerOptions JsonOpts;

		private static string FilePath => System.IO.Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.ApplicationData), "HyperVMManager", "vm-provision.json");

		public static TimeSpan MaxProvisionDuration { get; }

		static VmProvisionStore ()
		{
			Cache = new ConcurrentDictionary<string, VmProvisionEntry> (StringComparer.OrdinalIgnoreCase);
			JsonOpts = new JsonSerializerOptions {
				WriteIndented = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
			};
			MaxProvisionDuration = TimeSpan.FromHours (1.0);
			TryLoad ();
		}

		private static void TryLoad ()
		{
			try {
				string filePath = FilePath;
				if (!File.Exists (filePath)) {
					return;
				}
				string json = File.ReadAllText (filePath);
				Dictionary<string, VmProvisionEntry>? dictionary = JsonSerializer.Deserialize<Dictionary<string, VmProvisionEntry>> (json, JsonOpts);
				if (dictionary == null) {
					return;
				}
				foreach (KeyValuePair<string, VmProvisionEntry> item in dictionary) {
					Cache [item.Key] = item.Value;
				}
			} catch {
			}
		}

		private static void Save ()
		{
			try {
				string? directoryName = System.IO.Path.GetDirectoryName (FilePath);
				if (!string.IsNullOrEmpty (directoryName) && !Directory.Exists (directoryName)) {
					Directory.CreateDirectory (directoryName);
				}
				Dictionary<string, VmProvisionEntry> value = Cache.ToDictionary<KeyValuePair<string, VmProvisionEntry>, string, VmProvisionEntry> ((KeyValuePair<string, VmProvisionEntry> k) => k.Key, (KeyValuePair<string, VmProvisionEntry> v) => v.Value, StringComparer.OrdinalIgnoreCase);
				File.WriteAllText (FilePath, JsonSerializer.Serialize (value, JsonOpts));
			} catch {
			}
		}

		public static void MarkProvisioning (string vmName, string? reservedIpv4 = null, VmProvisionPhase phase = VmProvisionPhase.CloudInit, string? vmDirectory = null)
		{
			if (!string.IsNullOrWhiteSpace (vmName)) {
				Cache [vmName.Trim ()] = new VmProvisionEntry {
					Phase = phase,
					StartedUtc = DateTime.UtcNow,
					ReservedIpv4 = (string.IsNullOrWhiteSpace (reservedIpv4) ? null : reservedIpv4.Trim ()),
					VmDirectory = (string.IsNullOrWhiteSpace (vmDirectory) ? "" : vmDirectory)
				};
				Save ();
			}
		}

		public static void AddReservedPoolAddresses (Ipv4PoolSettings pool, HashSet<string> used)
		{
			foreach (VmProvisionEntry value in Cache.Values) {
				string? text = value?.ReservedIpv4;
				if (!string.IsNullOrWhiteSpace (text)) {
					string text2 = text.Trim ();
					if (pool.ContainsAddress (text2)) {
						used.Add (text2);
					}
				}
			}
		}

		public static void Clear (string vmName)
		{
			if (!string.IsNullOrWhiteSpace (vmName)) {
				Cache.TryRemove (vmName.Trim (), out VmProvisionEntry? _);
				Save ();
			}
		}

		public static string GetVmDirectory (string vmName)
		{
			if (TryGet (vmName, out VmProvisionEntry? entry) && entry != null && !string.IsNullOrWhiteSpace (entry.VmDirectory)) {
				return entry.VmDirectory;
			}
			return string.Empty;
		}

		public static bool TryGet (string vmName, out VmProvisionEntry? entry)
		{
			entry = null;
			if (string.IsNullOrWhiteSpace (vmName)) {
				return false;
			}
			return Cache.TryGetValue (vmName.Trim (), out entry);
		}

		public static bool IsInstallingDisplay (string vmName, bool vmRunning, string guestIpv4Cell, out bool pendingStartOnly)
		{
			pendingStartOnly = false;
			if (!TryGet (vmName, out VmProvisionEntry? entry) || entry == null) {
				return false;
			}
			if (DateTime.UtcNow - entry.StartedUtc > MaxProvisionDuration) {
				Clear (vmName);
				return false;
			}
			if (guestIpv4Cell.Length > 0 && guestIpv4Cell != "—") {
				Clear (vmName);
				return false;
			}
			if (!vmRunning) {
				pendingStartOnly = true;
				return true;
			}
			return true;
		}
	}

