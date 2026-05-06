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

	public sealed class CreateUbuntuCloudVmParameters
	{
		public required string VmName { get; init; }

		public required string SwitchName { get; init; }

		public required string TemplateVhdPath { get; init; }

		public required string OsVhdFullPath { get; init; }

		public required string SeedVhdFullPath { get; init; }

		public string VmDirectory { get; init; } = "";

		public long MemoryStartupBytes { get; init; }

		public int ProcessorCount { get; init; }

		public ulong OsDiskSizeBytes { get; init; }

		public required string AdminUsername { get; init; }

		public required string AdminPassword { get; init; }

		public string Hostname { get; init; } = "";

		public UbuntuInstallProfile Profile { get; init; }

		public required string Notes { get; init; }

		public string StaticGuestIpv4 { get; init; } = "";

		public string DefaultGateway { get; init; } = "";

		public int PrefixLength { get; init; } = 24;

		public IReadOnlyList<string> DnsServers { get; init; } = Array.Empty<string> ();
	}

