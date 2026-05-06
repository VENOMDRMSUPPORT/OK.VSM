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

namespace HyperVMManager.Models;

	public sealed class Ipv4PoolSettings
	{
		public string Ipv4RangeStart { get; set; } = "192.168.1.50";

		public string Ipv4RangeEnd { get; set; } = "192.168.1.80";

		public string DefaultGateway { get; set; } = "192.168.1.1";

		public int PrefixLength { get; set; } = 24;

		public string DnsServers { get; set; } = "192.168.1.1";

		/// <summary>Optional comma-separated IPv4 addresses in the pool to always treat as in use (e.g. offline guests with empty or non-standard Notes).</summary>
		public string ManualPoolExclusions { get; set; } = "";

		public bool HasValidStaticNetwork ()
		{
			int prefixLength = PrefixLength;
			if ((prefixLength < 1 || prefixLength > 32) ? true : false) {
				return false;
			}
			uint value;
			return TryParseIpv4 (DefaultGateway?.Trim () ?? "", out value);
		}

		public IEnumerable<string> EnumerateDnsServers ()
		{
			if (string.IsNullOrWhiteSpace (DnsServers)) {
				yield break;
			}
			string[] array = DnsServers.Split (',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			foreach (string part in array) {
				if (TryParseIpv4 (part, out var _)) {
					yield return part.Trim ();
				}
			}
		}

		public bool TryGetRange (out uint start, out uint end)
		{
			start = 0u;
			end = 0u;
			if (!TryParseIpv4 (Ipv4RangeStart, out var value) || !TryParseIpv4 (Ipv4RangeEnd, out var value2)) {
				return false;
			}
			if (value > value2) {
				uint num = value2;
				value2 = value;
				value = num;
			}
			start = value;
			end = value2;
			return true;
		}

		public static bool TryParseIpv4 (string s, out uint value)
		{
			value = 0u;
			if (!IPAddress.TryParse (s.Trim (), out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork) {
				return false;
			}
			byte[] addressBytes = address.GetAddressBytes ();
			value = (uint)((addressBytes [0] << 24) | (addressBytes [1] << 16) | (addressBytes [2] << 8) | addressBytes [3]);
			return true;
		}

		public static string FormatIpv4 (uint v)
		{
			return string.Join (".", new byte[4] {
				(byte)(v >> 24),
				(byte)(v >> 16),
				(byte)(v >> 8),
				(byte)v
			});
		}

		public bool ContainsAddress (string ipv4String)
		{
			if (!TryGetRange (out var start, out var end)) {
				return false;
			}
			if (!TryParseIpv4 (ipv4String, out var value)) {
				return false;
			}
			return value >= start && value <= end;
		}

		public void AddManualExclusionsTo (HashSet<string> target)
		{
			if (string.IsNullOrWhiteSpace (ManualPoolExclusions)) {
				return;
			}
			foreach (string part in ManualPoolExclusions.Split (',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
				string t = part.Trim ();
				if (TryParseIpv4 (t, out _) && ContainsAddress (t)) {
					target.Add (t);
				}
			}
		}
	}

