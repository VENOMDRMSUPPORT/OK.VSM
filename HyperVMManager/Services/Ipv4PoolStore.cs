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

	public static class Ipv4PoolStore
	{
		private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions {
			WriteIndented = true
		};

		public static string FilePath => System.IO.Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.ApplicationData), "HyperVMManager", "ipv4-pool.json");

		public static Ipv4PoolSettings Load ()
		{
			try {
				string filePath = FilePath;
				if (!File.Exists (filePath)) {
					return new Ipv4PoolSettings ();
				}
				string json = File.ReadAllText (filePath);
				Ipv4PoolSettings? ipv4PoolSettings = JsonSerializer.Deserialize<Ipv4PoolSettings> (json);
				return ipv4PoolSettings ?? new Ipv4PoolSettings ();
			} catch {
				return new Ipv4PoolSettings ();
			}
		}

		public static void Save (Ipv4PoolSettings settings)
		{
			string? directoryName = System.IO.Path.GetDirectoryName (FilePath);
			if (!string.IsNullOrEmpty (directoryName)) {
				Directory.CreateDirectory (directoryName);
			}
			File.WriteAllText (FilePath, JsonSerializer.Serialize (settings, JsonOpts));
		}
	}

