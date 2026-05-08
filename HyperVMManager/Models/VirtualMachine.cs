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

	public class VirtualMachine : INotifyPropertyChanged
	{
		private bool _isRunning;

		private string _status = string.Empty;

		private string _statusSubline = "";

		private string _statusDisplay = "";

		private int _cpuCount;

		private string _memory = "Loading...";

		private string _diskSize = "Loading...";

		private string _uptime = string.Empty;

		private string _guestIpv4 = "—";

		private bool _detailsLoaded;

		private double _cpuGaugePercent;

		private string _cpuGaugeCaption = "—";

		private double _memoryGaugePercent;

		private string _memoryGaugeCaption = "—";

		private bool _memoryAllocationCritical;

		private double _diskGaugePercent;

		private string _diskGaugeCaption = "—";

		private bool _diskAllocationMeaningful = true;

		private bool _showDiskGauge;

		private bool _isInstalling;

		private string _osVhdPath = "—";

		private string _seedVhdPath = "";

		private string _osVhdActualSize = "";

		private string _seedVhdActualSize = "";

		public string Name { get; set; } = string.Empty;

		public ushort EnabledState { get; set; }

		public string WmiName { get; set; } = string.Empty;

		public bool IsRunning {
			get {
				return _isRunning;
			}
			set {
				_isRunning = value;
				OnPropertyChanged ("IsRunning");
			}
		}

		public string Status {
			get {
				return _status;
			}
			set {
				_status = value;
				OnPropertyChanged ("Status");
			}
		}

		public string StatusSubline {
			get {
				return _statusSubline;
			}
			set {
				_statusSubline = value;
				OnPropertyChanged ("StatusSubline");
				OnPropertyChanged ("HasStatusSubline");
			}
		}

		public bool HasStatusSubline => !string.IsNullOrWhiteSpace (_statusSubline);

		public string StatusDisplay {
			get {
				return string.IsNullOrEmpty (_statusDisplay) ? _status : _statusDisplay;
			}
			set {
				_statusDisplay = value ?? "";
				OnPropertyChanged ("StatusDisplay");
			}
		}

		public int CpuCount {
			get {
				return _cpuCount;
			}
			set {
				_cpuCount = value;
				OnPropertyChanged ("CpuCount");
			}
		}

		public string Memory {
			get {
				return _memory;
			}
			set {
				_memory = value;
				OnPropertyChanged ("Memory");
			}
		}

		public string DiskSize {
			get {
				return _diskSize;
			}
			set {
				_diskSize = value;
				OnPropertyChanged ("DiskSize");
			}
		}

		public string Uptime {
			get {
				return _uptime;
			}
			set {
				_uptime = value;
				OnPropertyChanged ("Uptime");
			}
		}

		public string GuestIpv4 {
			get {
				return _guestIpv4;
			}
			set {
				_guestIpv4 = value;
				OnPropertyChanged ("GuestIpv4");
			}
		}

		public bool DetailsLoaded {
			get {
				return _detailsLoaded;
			}
			set {
				_detailsLoaded = value;
				OnPropertyChanged ("DetailsLoaded");
			}
		}

		public double CpuGaugePercent {
			get {
				return _cpuGaugePercent;
			}
			set {
				_cpuGaugePercent = value;
				OnPropertyChanged ("CpuGaugePercent");
			}
		}

		public string CpuGaugeCaption {
			get {
				return _cpuGaugeCaption;
			}
			set {
				_cpuGaugeCaption = value;
				OnPropertyChanged ("CpuGaugeCaption");
			}
		}

		public double MemoryGaugePercent {
			get {
				return _memoryGaugePercent;
			}
			set {
				_memoryGaugePercent = value;
				OnPropertyChanged ("MemoryGaugePercent");
			}
		}

		public string MemoryGaugeCaption {
			get {
				return _memoryGaugeCaption;
			}
			set {
				_memoryGaugeCaption = value;
				OnPropertyChanged ("MemoryGaugeCaption");
			}
		}

		public bool MemoryAllocationCritical {
			get {
				return _memoryAllocationCritical;
			}
			set {
				_memoryAllocationCritical = value;
				OnPropertyChanged ("MemoryAllocationCritical");
			}
		}

		public double DiskGaugePercent {
			get {
				return _diskGaugePercent;
			}
			set {
				_diskGaugePercent = value;
				OnPropertyChanged ("DiskGaugePercent");
			}
		}

		public string DiskGaugeCaption {
			get {
				return _diskGaugeCaption;
			}
			set {
				_diskGaugeCaption = value;
				OnPropertyChanged ("DiskGaugeCaption");
			}
		}

		public bool DiskAllocationMeaningful {
			get {
				return _diskAllocationMeaningful;
			}
			set {
				_diskAllocationMeaningful = value;
				OnPropertyChanged ("DiskAllocationMeaningful");
			}
		}

		public bool ShowDiskGauge {
			get {
				return _showDiskGauge;
			}
			set {
				_showDiskGauge = value;
				OnPropertyChanged ("ShowDiskGauge");
			}
		}

		public bool IsInstalling {
			get {
				return _isInstalling;
			}
			set {
				_isInstalling = value;
				OnPropertyChanged ("IsInstalling");
			}
		}

		public string OsVhdPath {
			get {
				return _osVhdPath;
			}
			set {
				_osVhdPath = string.IsNullOrWhiteSpace (value) ? "—" : value;
				OnPropertyChanged ("OsVhdPath");
				OnPropertyChanged ("HasOsVhdPath");
			}
		}

		public bool HasOsVhdPath => !string.IsNullOrWhiteSpace (_osVhdPath) && _osVhdPath != "—";

		public string SeedVhdPath {
			get {
				return _seedVhdPath;
			}
			set {
				_seedVhdPath = value ?? "";
				OnPropertyChanged ("SeedVhdPath");
				OnPropertyChanged ("HasSeedVhdPath");
			}
		}

		public bool HasSeedVhdPath => !string.IsNullOrWhiteSpace (_seedVhdPath);

		public string OsVhdActualSize {
			get {
				return _osVhdActualSize;
			}
			set {
				_osVhdActualSize = value ?? "";
				OnPropertyChanged ("OsVhdActualSize");
				OnPropertyChanged ("HasOsVhdActualSize");
			}
		}

		public bool HasOsVhdActualSize => !string.IsNullOrWhiteSpace (_osVhdActualSize);

		public string SeedVhdActualSize {
			get {
				return _seedVhdActualSize;
			}
			set {
				_seedVhdActualSize = value ?? "";
				OnPropertyChanged ("SeedVhdActualSize");
				OnPropertyChanged ("HasSeedVhdActualSize");
			}
		}

		public bool HasSeedVhdActualSize => !string.IsNullOrWhiteSpace (_seedVhdActualSize);

		public event PropertyChangedEventHandler? PropertyChanged;

		protected void OnPropertyChanged ([CallerMemberName] string? propertyName = null)
		{
			this.PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (propertyName));
		}
	}

