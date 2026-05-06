using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HyperVMManager.Models;

namespace HyperVMManager.Services;

/// <summary>
/// Persists the last guest IPv4 seen for each VM (while in pool range).
/// When a VM is off, ARP/neighbors no longer show the address — we still treat that IP as reserved for pool allocation.
/// </summary>
public static class VmGuestIpCache
{
	private static readonly ConcurrentDictionary<string, string> ByVm =
		new (StringComparer.OrdinalIgnoreCase);

	private static readonly JsonSerializerOptions JsonOpts = new () {
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private static readonly string FilePath = Path.Combine (
		Environment.GetFolderPath (Environment.SpecialFolder.ApplicationData),
		"HyperVMManager",
		"vm-guest-ipv4-cache.json");

	static VmGuestIpCache ()
	{
		TryLoad ();
	}

	private static void TryLoad ()
	{
		try {
			if (!File.Exists (FilePath)) {
				return;
			}
			string json = File.ReadAllText (FilePath);
			Dictionary<string, string>? d = JsonSerializer.Deserialize<Dictionary<string, string>> (json, JsonOpts);
			if (d == null) {
				return;
			}
			foreach (KeyValuePair<string, string> kv in d) {
				if (!string.IsNullOrWhiteSpace (kv.Key) && !string.IsNullOrWhiteSpace (kv.Value)) {
					ByVm [kv.Key.Trim ()] = kv.Value.Trim ();
				}
			}
		} catch {
		}
	}

	private static void Save ()
	{
		try {
			string? dir = Path.GetDirectoryName (FilePath);
			if (!string.IsNullOrEmpty (dir) && !Directory.Exists (dir)) {
				Directory.CreateDirectory (dir);
			}
			File.WriteAllText (FilePath, JsonSerializer.Serialize (new Dictionary<string, string> (ByVm), JsonOpts));
		} catch {
		}
	}

	public static void Remember (string vmName, string ipv4, Ipv4PoolSettings pool)
	{
		if (string.IsNullOrWhiteSpace (vmName) || string.IsNullOrWhiteSpace (ipv4)) {
			return;
		}
		string t = ipv4.Trim ();
		if (!pool.ContainsAddress (t)) {
			return;
		}
		ByVm [vmName.Trim ()] = t;
		Save ();
	}

	public static bool TryGet (string vmName, out string ipv4)
	{
		ipv4 = "";
		if (string.IsNullOrWhiteSpace (vmName)) {
			return false;
		}
		return ByVm.TryGetValue (vmName.Trim (), out ipv4!) && !string.IsNullOrWhiteSpace (ipv4);
	}

	public static void Remove (string vmName)
	{
		if (string.IsNullOrWhiteSpace (vmName)) {
			return;
		}
		if (ByVm.TryRemove (vmName.Trim (), out _)) {
			Save ();
		}
	}

	/// <summary>Every remembered in-pool guest IP is treated as reserved for allocation (covers offline VMs after we saw them once).</summary>
	public static void AddAllCachedIpv4InPoolTo (Ipv4PoolSettings pool, HashSet<string> target)
	{
		foreach (KeyValuePair<string, string> kv in ByVm) {
			string ip = kv.Value.Trim ();
			if (pool.ContainsAddress (ip)) {
				target.Add (ip);
			}
		}
	}
}
