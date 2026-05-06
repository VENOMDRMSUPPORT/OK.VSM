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

	public static class Ipv4PoolAnalyzer
	{
		public static async Task<HashSet<string>> PingBusyAddressesAsync (Ipv4PoolSettings settings, CancellationToken ct = default(CancellationToken))
		{
			ConcurrentBag<string> busy = new ConcurrentBag<string> ();
			if (!settings.TryGetRange (out var lo, out var hi)) {
				return new HashSet<string> ();
			}
			int count = (int)(hi - lo + 1);
			if (count <= 0 || count > 512) {
				return new HashSet<string> ();
			}
			SemaphoreSlim sem = new SemaphoreSlim (16);
			try {
				List<Task> tasks = new List<Task> ();
				for (uint u = lo; u <= hi; u++) {
					string ip = Ipv4PoolSettings.FormatIpv4 (u);
					tasks.Add (Task.Run (async delegate {
						await sem.WaitAsync (ct).ConfigureAwait (continueOnCapturedContext: false);
						try {
							Ping ping = new Ping ();
							try {
								if ((await ping.SendPingAsync (ip, 400).ConfigureAwait (continueOnCapturedContext: false)).Status == IPStatus.Success) {
									busy.Add (ip);
								}
							} finally {
								((IDisposable)ping)?.Dispose ();
							}
						} catch {
						} finally {
							sem.Release ();
						}
					}, ct));
				}
				await Task.WhenAll (tasks).ConfigureAwait (continueOnCapturedContext: false);
				return new HashSet<string> (busy, StringComparer.Ordinal);
			} finally {
				if (sem != null) {
					((IDisposable)sem).Dispose ();
				}
			}
		}

		public static string? NextAvailable (Ipv4PoolSettings settings, HashSet<string> used)
		{
			if (!settings.TryGetRange (out var start, out var end)) {
				return null;
			}
			for (uint num = start; num <= end; num++) {
				string text = Ipv4PoolSettings.FormatIpv4 (num);
				if (!used.Contains (text)) {
					return text;
				}
			}
			return null;
		}

		public static IEnumerable<string> SplitIpv4Tokens (string? text)
		{
			if (string.IsNullOrWhiteSpace (text) || text == "—") {
				yield break;
			}
			string[] array = text.Split (',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			foreach (string part in array) {
				if (!part.Contains (':', StringComparison.Ordinal) && Ipv4PoolSettings.TryParseIpv4 (part, out var _)) {
					yield return part.Trim ();
				}
			}
		}
	}
