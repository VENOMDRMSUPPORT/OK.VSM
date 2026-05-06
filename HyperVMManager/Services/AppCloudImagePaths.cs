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

	public static class AppCloudImagePaths
	{
		public static string ResolveCloudImagesDirectory ()
		{
			string baseDirectory = AppContext.BaseDirectory;
			string fullPath = System.IO.Path.GetFullPath (System.IO.Path.Combine (baseDirectory, "CloudImages"));
			string fullPath2 = System.IO.Path.GetFullPath (System.IO.Path.Combine (baseDirectory, "..", "..", "..", "CloudImages"));
			if (Directory.Exists (fullPath)) {
				return fullPath;
			}
			if (Directory.Exists (fullPath2)) {
				return fullPath2;
			}
			return fullPath;
		}

		public static IReadOnlyList<string> EnumerateCloudBaseImages ()
		{
			try {
				string path = ResolveCloudImagesDirectory ();
				if (!Directory.Exists (path)) {
					return Array.Empty<string> ();
				}
				return Directory.GetFiles (path, "*.*", SearchOption.TopDirectoryOnly).Where (delegate(string f) {
					string extension = System.IO.Path.GetExtension (f);
					return extension.Equals (".vhdx", StringComparison.OrdinalIgnoreCase) || extension.Equals (".vhd", StringComparison.OrdinalIgnoreCase);
				}).OrderBy<string, string> ((string f) => System.IO.Path.GetFileName (f), StringComparer.OrdinalIgnoreCase)
					.ToArray ();
			} catch {
				return Array.Empty<string> ();
			}
		}
	}
