using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using HyperVMManager.Commands;
using HyperVMManager.Dialogs;
using HyperVMManager.Models;
using HyperVMManager.Services;

namespace HyperVMManager.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
	private Ipv4PoolSettings _poolSettings = Ipv4PoolStore.Load();

	private bool _isLoading = true;

	private bool _isRefreshing;

	private string _statusText = "Loading virtual machines...";

	private int _runningCount;

	private int _stoppedCount;
		private int _otherCount;

	private VirtualMachine? _selectedVm;

	private bool _isDrawerNetworkLoading;

	private string _drawerIpv4 = "—";

	private string _drawerMac = "—";

	private string _drawerOsHint = "—";

	private string _drawerInstallHint = "";

	private string _drawerOsIconGlyph = "\ue756";

	private string _poolRangeDisplay = "";

	private string _nextAvailableIpDisplay = "—";

	private bool _isCreateVmBusy;

	private string _createVmProgressText = "";

	private bool _liveRefreshBusy;

	private readonly DispatcherTimer _liveRefreshTimer;

	public ObservableCollection<VirtualMachine> VirtualMachines { get; } = new ObservableCollection<VirtualMachine>();


	public bool IsLoading
	{
		get
		{
			return _isLoading;
		}
		set
		{
			_isLoading = value;
			OnPropertyChanged("IsLoading");
		}
	}

	public bool IsRefreshing
	{
		get
		{
			return _isRefreshing;
		}
		set
		{
			_isRefreshing = value;
			OnPropertyChanged("IsRefreshing");
		}
	}

	public string StatusText
	{
		get
		{
			return _statusText;
		}
		set
		{
			_statusText = value;
			OnPropertyChanged("StatusText");
		}
	}

	public int RunningCount
	{
		get
		{
			return _runningCount;
		}
		set
		{
			_runningCount = value;
			OnPropertyChanged("RunningCount");
		}
	}

	public int StoppedCount
	{
		get
		{
			return _stoppedCount;
		}
		set
		{
			_stoppedCount = value;
			OnPropertyChanged("StoppedCount");
		}
	}

		public int OtherCount
	{
		get
		{
			return _otherCount;
		}
		set
		{
			_otherCount = value;
			OnPropertyChanged("OtherCount");
		}
	}
public VirtualMachine? SelectedVm
	{
		get
		{
			return _selectedVm;
		}
		set
		{
			if (_selectedVm != value)
			{
				_selectedVm = value;
				OnPropertyChanged("SelectedVm");
				OnPropertyChanged("HasSelectedVm");
			}
		}
	}

	public bool HasSelectedVm => SelectedVm != null;

	public bool IsDrawerNetworkLoading
	{
		get
		{
			return _isDrawerNetworkLoading;
		}
		set
		{
			_isDrawerNetworkLoading = value;
			OnPropertyChanged("IsDrawerNetworkLoading");
		}
	}

	public string DrawerIpv4
	{
		get
		{
			return _drawerIpv4;
		}
		set
		{
			_drawerIpv4 = value;
			OnPropertyChanged("DrawerIpv4");
		}
	}

	public string DrawerMac
	{
		get
		{
			return _drawerMac;
		}
		set
		{
			_drawerMac = value;
			OnPropertyChanged("DrawerMac");
		}
	}

	public string DrawerOsHint
	{
		get
		{
			return _drawerOsHint;
		}
		set
		{
			_drawerOsHint = value;
			OnPropertyChanged("DrawerOsHint");
		}
	}

	public string DrawerInstallHint
	{
		get
		{
			return _drawerInstallHint;
		}
		set
		{
			_drawerInstallHint = value;
			OnPropertyChanged("DrawerInstallHint");
			OnPropertyChanged("HasDrawerInstallHint");
		}
	}

	public bool HasDrawerInstallHint => !string.IsNullOrWhiteSpace(_drawerInstallHint);

	public string DrawerOsIconGlyph
	{
		get
		{
			return _drawerOsIconGlyph;
		}
		set
		{
			_drawerOsIconGlyph = value;
			OnPropertyChanged("DrawerOsIconGlyph");
		}
	}

	public string PoolRangeDisplay
	{
		get
		{
			return _poolRangeDisplay;
		}
		set
		{
			_poolRangeDisplay = value;
			OnPropertyChanged("PoolRangeDisplay");
		}
	}

	public string NextAvailableIpDisplay
	{
		get
		{
			return _nextAvailableIpDisplay;
		}
		set
		{
			_nextAvailableIpDisplay = value;
			OnPropertyChanged("NextAvailableIpDisplay");
		}
	}

	public bool IsCreateVmBusy
	{
		get
		{
			return _isCreateVmBusy;
		}
		set
		{
			_isCreateVmBusy = value;
			OnPropertyChanged("IsCreateVmBusy");
		}
	}

	public string CreateVmProgressText
	{
		get
		{
			return _createVmProgressText;
		}
		set
		{
			_createVmProgressText = value;
			OnPropertyChanged("CreateVmProgressText");
		}
	}

	public ICommand RefreshCommand { get; }

	public ICommand StartVmCommand { get; }

	public ICommand StopVmCommand { get; }

	public ICommand RestartVmCommand { get; }

	public ICommand DeleteVmCommand { get; }

	public ICommand ClearSelectionCommand { get; }

	public ICommand OpenVmConsoleCommand { get; }

	public ICommand CreateSnapshotCommand { get; }

	public ICommand OpenHyperVSettingsCommand { get; }

	public ICommand ViewMetricsCommand { get; }

	public ICommand CopyDrawerIpv4Command { get; }

	public ICommand CopyDrawerMacCommand { get; }

	public ICommand CopyDrawerOsVhdPathCommand { get; }

	public ICommand CopyDrawerSeedVhdPathCommand { get; }

	public ICommand OpenDrawerDiskFolderCommand { get; }

	public ICommand RemoveVirtualDvdCommand { get; }

	public ICommand OpenNetworkPoolSettingsCommand { get; }

	public ICommand OpenCreateUbuntuVmCommand { get; }

	public ICommand OpenHelpCommand { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	public MainViewModel()
	{
		ApplyPoolRangeLabels();
		RefreshCommand = new RelayCommand(async delegate
		{
			await LoadVMsAsync();
		});
		StartVmCommand = new AsyncRelayCommand<VirtualMachine>(OnStartVmAsync, (VirtualMachine? v) => v != null && v.EnabledState == 3);
		StopVmCommand = new AsyncRelayCommand<VirtualMachine>(OnStopVmAsync, (VirtualMachine? v) => v != null && v.EnabledState == 2);
		RestartVmCommand = new AsyncRelayCommand<VirtualMachine>(OnRestartVmAsync, (VirtualMachine? v) => v != null && v.EnabledState == 2);
		DeleteVmCommand = new AsyncRelayCommand<VirtualMachine>(OnDeleteVmAsync, (VirtualMachine? _) => true);
		ClearSelectionCommand = new DelegateCommand(delegate
		{
			SelectedVm = null;
		});
		OpenVmConsoleCommand = new DelegateCommand(OnOpenVmConsole);
		CreateSnapshotCommand = new RelayCommand(OnCreateSnapshotAsync);
		OpenHyperVSettingsCommand = new DelegateCommand(delegate
		{
			VmControlService.OpenHyperVManager();
			StatusText = "Opened Hyper-V Manager — select the VM, then Settings to edit CPU/RAM.";
		});
		ViewMetricsCommand = new DelegateCommand(delegate
		{
			StatusText = "Allocation vs host: use the main grid columns (CPU / Memory / Disk) and the meters below in this panel.";
		});
		CopyDrawerIpv4Command = new DelegateCommand(delegate
		{
			CopyIfValue(DrawerIpv4);
		});
		CopyDrawerMacCommand = new DelegateCommand(delegate
		{
			CopyIfValue(DrawerMac);
		});
		CopyDrawerOsVhdPathCommand = new DelegateCommand(delegate
		{
			CopyIfValue(SelectedVm?.OsVhdPath);
		});
		CopyDrawerSeedVhdPathCommand = new DelegateCommand(delegate
		{
			CopyIfValue(SelectedVm?.SeedVhdPath);
		});
		OpenDrawerDiskFolderCommand = new DelegateCommand(OpenSelectedVmDiskFolder);
		RemoveVirtualDvdCommand = new AsyncRelayCommand<VirtualMachine>(OnRemoveVirtualDvdAsync, (VirtualMachine? _) => true);
		OpenNetworkPoolSettingsCommand = new RelayCommand(async delegate
		{
			await OnOpenNetworkPoolSettingsAsync();
		});
		OpenCreateUbuntuVmCommand = new RelayCommand(async delegate
		{
			await OnOpenCreateUbuntuVmAsync();
		});
		OpenHelpCommand = new DelegateCommand(delegate
		{
			TutorialDialog.ShowFor(Application.Current.MainWindow, markSeen: false);
		});
		_liveRefreshTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(5.0)
		};
		_liveRefreshTimer.Tick += async delegate
		{
			await LiveRefreshTickAsync();
		};
		_liveRefreshTimer.Start();
		_ = LoadVMsAsync();
	}

	private async Task LiveRefreshTickAsync()
	{
		if (_liveRefreshBusy || IsLoading || IsCreateVmBusy || IsRefreshing || VirtualMachines.Count == 0)
		{
			return;
		}
		_liveRefreshBusy = true;
		try
		{
			List<VirtualMachine> fresh = await Task.Run(() => HyperVService.GetVirtualMachinesFast());
			await Application.Current.Dispatcher.InvokeAsync(delegate
			{
				MergeVmStates(fresh);
			});
			await RefreshPoolDisplayAsync();
		}
		catch
		{
		}
		finally
		{
			_liveRefreshBusy = false;
		}
	}

	private void MergeVmStates(List<VirtualMachine> fresh)
	{
		Dictionary<string, VirtualMachine> dictionary = fresh.ToDictionary<VirtualMachine, string>((VirtualMachine v) => v.Name, StringComparer.OrdinalIgnoreCase);
		for (int num = VirtualMachines.Count - 1; num >= 0; num--)
		{
			VirtualMachine virtualMachine = VirtualMachines[num];
			if (!dictionary.TryGetValue(virtualMachine.Name, out var value))
			{
				VirtualMachines.RemoveAt(num);
				if (SelectedVm?.Name == virtualMachine.Name)
				{
					SelectedVm = null;
				}
				VmProvisionStore.Clear(virtualMachine.Name);
				VmGuestIpCache.Remove(virtualMachine.Name);
			}
			else
			{
				virtualMachine.EnabledState = value.EnabledState;
				virtualMachine.Status = value.Status;
				virtualMachine.Uptime = value.Uptime;
				virtualMachine.IsRunning = value.IsRunning;
				virtualMachine.OsVhdPath = value.OsVhdPath;
				virtualMachine.SeedVhdPath = value.SeedVhdPath;
				virtualMachine.OsVhdActualSize = value.OsVhdActualSize;
				virtualMachine.SeedVhdActualSize = value.SeedVhdActualSize;
			}
		}
		foreach (VirtualMachine nv in fresh)
		{
			if (!VirtualMachines.Any((VirtualMachine v) => string.Equals(v.Name, nv.Name, StringComparison.OrdinalIgnoreCase)))
			{
				VirtualMachines.Add(nv);
			}
		}
		RunningCount = VirtualMachines.Count((VirtualMachine v) => v.EnabledState == 2);
		StoppedCount = VirtualMachines.Count((VirtualMachine v) => v.EnabledState == 3);
			OtherCount = VirtualMachines.Count - RunningCount - StoppedCount;
		string? selName = SelectedVm?.Name;
		if (selName != null)
		{
			SelectedVm = VirtualMachines.FirstOrDefault((VirtualMachine v) => v.Name == selName);
		}
	}

	private static void CopyIfValue(string? s)
	{
		if (string.IsNullOrWhiteSpace(s) || s == "—")
		{
			return;
		}
		try
		{
			Clipboard.SetText(s);
		}
		catch
		{
		}
	}

	private void ApplyPoolRangeLabels()
	{
		if (_poolSettings.TryGetRange(out var _, out var _))
		{
			PoolRangeDisplay = _poolSettings.Ipv4RangeStart + " – " + _poolSettings.Ipv4RangeEnd;
		}
		else
		{
			PoolRangeDisplay = "Invalid IPv4 pool (open settings)";
		}
	}

	private static string FormatGuestIpv4Cell(string? s)
	{
		if (!string.IsNullOrWhiteSpace(s))
		{
			return s;
		}
		return "—";
	}

	/// <summary>
	/// Priority: live neighbor/ARP → static IPv4 from Hyper-V VM Notes (cloud-init) → persisted cache → none.
	/// Notes persist while the VM is Off; ARP does not.
	/// </summary>
	private void ApplyGuestIpv4FromNetworkProbe(VirtualMachine vm, string? rawIpv4FromInspector, string? staticIpv4FromVmNotes)
	{
		string live = FormatGuestIpv4Cell(rawIpv4FromInspector);
		if (live != "\u2014")
		{
			vm.GuestIpv4 = live;
			foreach (string token in Ipv4PoolAnalyzer.SplitIpv4Tokens(live))
			{
				if (_poolSettings.ContainsAddress(token))
				{
					VmGuestIpCache.Remember(vm.Name, token, _poolSettings);
					VmInspectorService.PersistStaticIpv4InVmNotesAsync(vm.Name, token);
					break;
				}
			}
			return;
		}
		string fromNotes = (staticIpv4FromVmNotes ?? "").Trim();
		if (fromNotes.Length > 0 && _poolSettings.ContainsAddress(fromNotes))
		{
			vm.GuestIpv4 = fromNotes;
			VmGuestIpCache.Remember(vm.Name, fromNotes, _poolSettings);
			return;
		}
		if (VmGuestIpCache.TryGet(vm.Name, out var cached) && _poolSettings.ContainsAddress(cached))
		{
			vm.GuestIpv4 = cached;
			return;
		}
		vm.GuestIpv4 = live;
	}

	private static string StatusSublineForVm(VirtualMachine vm, VmInspectorService.VmNetworkRow? row)
	{
		if (row == null || vm.IsRunning)
		{
			return "";
		}
		if (!row.PendingIsoInstall)
		{
			return "";
		}
		return "ISO install: start the VM";
	}

	private void ApplyVmStatusDisplay(VirtualMachine vm, VmInspectorService.VmNetworkRow? row)
	{
		string guestIpv4Cell = (string.IsNullOrWhiteSpace(vm.GuestIpv4) ? "—" : vm.GuestIpv4);
		if (VmProvisionStore.IsInstallingDisplay(vm.Name, vm.IsRunning, guestIpv4Cell, out var pendingStartOnly))
		{
			vm.StatusDisplay = (pendingStartOnly ? "Pending start…" : "Installing…");
			vm.StatusSubline = (pendingStartOnly ? "Power on for unattended cloud-init." : "Cloud-init — waiting for guest IP…");
		}
		else
		{
			vm.StatusDisplay = vm.Status;
			vm.StatusSubline = StatusSublineForVm(vm, row);
		}
	}

	private void RecomputePoolDisplay(HashSet<string> pingBusy, IReadOnlyDictionary<string, string>? staticIpv4FromNotesByVm = null)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (VirtualMachine virtualMachine in VirtualMachines)
		{
			foreach (string item in Ipv4PoolAnalyzer.SplitIpv4Tokens(virtualMachine.GuestIpv4))
			{
				if (_poolSettings.ContainsAddress(item))
				{
					hashSet.Add(item);
				}
			}
		}
		if (staticIpv4FromNotesByVm != null)
		{
			foreach (KeyValuePair<string, string> kv in staticIpv4FromNotesByVm)
			{
				if (_poolSettings.ContainsAddress(kv.Value))
				{
					hashSet.Add(kv.Value);
				}
			}
		}
		VmGuestIpCache.AddAllCachedIpv4InPoolTo(_poolSettings, hashSet);
		_poolSettings.AddManualExclusionsTo(hashSet);
		foreach (string item2 in pingBusy)
		{
			if (_poolSettings.ContainsAddress(item2))
			{
				hashSet.Add(item2);
			}
		}
		VmProvisionStore.AddReservedPoolAddresses(_poolSettings, hashSet);
		string? text = Ipv4PoolAnalyzer.NextAvailable(_poolSettings, hashSet);
		NextAvailableIpDisplay = text ?? "—";
		ApplyPoolRangeLabels();
	}

	private async Task RefreshPoolDisplayAsync()
	{
		HashSet<string> pingBusy = await Ipv4PoolAnalyzer.PingBusyAddressesAsync(_poolSettings);
		(IReadOnlyList<VmInspectorService.VmNetworkRow> rows, IReadOnlyDictionary<string, string> notesMap) = await Task.Run(() =>
		{
			IReadOnlyList<VmInspectorService.VmNetworkRow> r = VmInspectorService.QueryAllVmNetworks();
			IReadOnlyDictionary<string, string> m = VmInspectorService.QueryVmNameToStaticIpv4FromNotes(_poolSettings);
			return (r, m);
		});
		await Application.Current.Dispatcher.InvokeAsync(delegate
		{
			foreach (VirtualMachine vm in VirtualMachines)
			{
				notesMap.TryGetValue(vm.Name, out var noteIp);
				VmInspectorService.VmNetworkRow? vmNetworkRow = rows.FirstOrDefault((VmInspectorService.VmNetworkRow r) => string.Equals(r.Name, vm.Name, StringComparison.OrdinalIgnoreCase));
				ApplyGuestIpv4FromNetworkProbe(vm, vmNetworkRow?.Ipv4, noteIp);
				ApplyVmStatusDisplay(vm, vmNetworkRow);
			}
			if (SelectedVm != null)
			{
				VmInspectorService.VmNetworkRow? vmNetworkRow2 = rows.FirstOrDefault((VmInspectorService.VmNetworkRow r) => string.Equals(r.Name, SelectedVm.Name, StringComparison.OrdinalIgnoreCase));
				DrawerIpv4 = SelectedVm.GuestIpv4;
				if (vmNetworkRow2 != null)
				{
					string text = VmInspectorService.SanitizeMac(vmNetworkRow2.Mac);
					DrawerMac = ((text.Length > 0) ? text : "—");
					DrawerInstallHint = ResolveDrawerInstallHint(SelectedVm, vmNetworkRow2);
				}
			}
			RecomputePoolDisplay(pingBusy, notesMap);
		});
	}

	private async Task OnOpenNetworkPoolSettingsAsync()
	{
		Window mainWindow = Application.Current.MainWindow;
		if (mainWindow != null)
		{
			Ipv4PoolSettings initial = new Ipv4PoolSettings
			{
				Ipv4RangeStart = _poolSettings.Ipv4RangeStart,
				Ipv4RangeEnd = _poolSettings.Ipv4RangeEnd,
				DefaultGateway = _poolSettings.DefaultGateway,
				PrefixLength = _poolSettings.PrefixLength,
				DnsServers = _poolSettings.DnsServers,
				ManualPoolExclusions = _poolSettings.ManualPoolExclusions
			};
			if (NetworkSettingsDialog.TryEdit(mainWindow, initial, out Ipv4PoolSettings saved))
			{
				_poolSettings = saved;
				Ipv4PoolStore.Save(_poolSettings);
				StatusText = "IPv4 pool saved. Scanning addresses…";
				await RefreshPoolDisplayAsync();
				StatusText = ((VirtualMachines.Count == 0) ? "No virtual machines found. Pool range updated." : $"Ready — {VirtualMachines.Count} Virtual Machine(s). Allocation vs host capacity.");
			}
		}
	}

	private static string ResolveDrawerInstallHint(VirtualMachine vm, VmInspectorService.VmNetworkRow sel)
	{
		if (VmProvisionStore.TryGet(vm.Name, out VmProvisionEntry? _))
		{
			if (!vm.IsRunning)
			{
				return "Start the VM for unattended first boot (cloud-init will configure the guest).";
			}
			if (!(FormatGuestIpv4Cell(sel.Ipv4) == "—"))
			{
				return "";
			}
			return "Cloud-init is running — guest IPv4 appears when the network is up.";
		}
		if (vm == null || vm.IsRunning || !sel.PendingIsoInstall)
		{
			return "";
		}
		return "Start the VM to boot the installer from the mounted ISO. MAC and guest IP appear after the virtual NIC is active.";
	}

	private async Task OnOpenCreateUbuntuVmAsync()
	{
		Window mainWindow = Application.Current.MainWindow;
		if (mainWindow == null)
		{
			return;
		}
		await RefreshPoolDisplayAsync();
		CreateVmDialog createVmDialog = new CreateVmDialog(NextAvailableIpDisplay, PoolRangeDisplay, _poolSettings)
		{
			Owner = mainWindow
		};
		if (!createVmDialog.ShowDialog().GetValueOrDefault() || createVmDialog.Result == null)
		{
			return;
		}
		IsCreateVmBusy = true;
		try
		{
			CreateVmProgressText = "Initializing...";
			StatusText = "Creating “" + createVmDialog.Result.VmName + "”…";
			Progress<string> progress = new Progress<string>(delegate(string step)
			{
				string step2 = step;
				Application.Current.Dispatcher.Invoke(() => CreateVmProgressText = step2);
			});
			CreateUbuntuCloudVmParameters p = createVmDialog.Result;
			CloudImageCatalogItem image = CloudImageCatalog.GetById(p.CloudImageId);
			VerifiedCachedArchive archive = await CloudImageCacheService.EnsureVerifiedArchiveAsync(image, progress);
			ExtractedOsDisk extractedDisk = await CloudImageCacheService.ExtractFinalOsDiskAsync(archive, p.OsVhdFullPath, progress);
			if (!string.Equals(extractedDisk.DiskPath, p.OsVhdFullPath, StringComparison.OrdinalIgnoreCase))
			{
				MessageBox.Show("Ubuntu OS disk was prepared at an unexpected path:\n" + extractedDisk.DiskPath, "Create VM", MessageBoxButton.OK, MessageBoxImage.Hand);
				StatusText = "Create VM failed.";
				return;
			}
			var (flag, messageBoxText) = await Task.Run(() => VmControlService.CreateUbuntuCloudVm(p, progress));
			if (!flag)
			{
				MessageBox.Show(messageBoxText, "Create VM", MessageBoxButton.OK, MessageBoxImage.Hand);
				StatusText = "Create VM failed.";
				return;
			}
			VmProvisionStore.MarkProvisioning(p.VmName, p.StaticGuestIpv4, vmDirectory: p.VmDirectory);
			if (!string.IsNullOrWhiteSpace(p.StaticGuestIpv4))
			{
				// Add entry to Windows hosts file when a static guest address is known.
				string hostsHostname = string.IsNullOrWhiteSpace(p.Hostname) ? p.VmName : p.Hostname.Trim();
				var (hostsOk, hostsMsg) = await Task.Run(() => HostsFileManager.AddOrUpdateEntry(p.StaticGuestIpv4, hostsHostname, p.VmName));
				if (!hostsOk)
				{
					MessageBox.Show("VM created but hosts file update failed:\n" + hostsMsg, "Hosts File", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
			}

			CreateVmProgressText = CreateVmProgressText = "Starting VM…";
			StatusText = "Starting “" + p.VmName + "”…";
			var (flag2, text) = await Task.Run(() => VmControlService.Start(p.VmName));
			if (!flag2)
			{
				MessageBox.Show("The VM was created, but automatic start failed:\n" + text + "\n\nStart it manually from the Action menu or the toolbar.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				StatusText = "Created “" + p.VmName + "” — start manually.";
				await AfterVmActionAsync();
			}
			else
			{
				StatusText = "Created and started “" + p.VmName + "” — cloud-init is running.";
				IsCreateVmBusy = false;
				await AfterVmActionAsync();
			}
		}
		catch (Exception ex)
		{
			string message = ex.Message;
			if (ex.InnerException != null && !string.IsNullOrWhiteSpace(ex.InnerException.Message))
			{
				message += "\n\n" + ex.InnerException.Message;
			}
			MessageBox.Show("Create VM failed:\n" + message, "Create VM", MessageBoxButton.OK, MessageBoxImage.Hand);
			StatusText = "Create VM failed.";
		}
		finally
		{
			IsCreateVmBusy = false;
		}
	}

	private static void ApplyOsIconGlyph(string hint, MainViewModel vm)
	{
		string text = hint.ToLowerInvariant();
		if (text.Contains("windows", StringComparison.Ordinal))
		{
			vm.DrawerOsIconGlyph = "\ue782";
		}
		else if (text.Contains("ubuntu", StringComparison.Ordinal) || text.Contains("linux", StringComparison.Ordinal) || text.Contains("debian", StringComparison.Ordinal) || text.Contains("fedora", StringComparison.Ordinal))
		{
			vm.DrawerOsIconGlyph = "\ue756";
		}
		else
		{
			vm.DrawerOsIconGlyph = "\ue756";
		}
	}

	public void OnVmRowSelected(VirtualMachine? vm)
	{
		SelectedVm = vm;
		if (vm == null)
		{
			DrawerInstallHint = "";
		}
		else
		{
			_ = LoadDrawerDetailsAsync();
		}
	}

	private async Task LoadDrawerDetailsAsync()
	{
		if (SelectedVm == null)
		{
			return;
		}
		string name = SelectedVm.Name;
		IsDrawerNetworkLoading = true;
		DrawerInstallHint = "";
		try
		{
			VmInspectorService.VmExtendedInfo info = await Task.Run(() => VmInspectorService.QueryExtendedInfo(name));
			await Application.Current.Dispatcher.InvokeAsync(delegate
			{
				if (!(SelectedVm?.Name != name))
				{
					string? probeIp = (string.IsNullOrWhiteSpace(info.Ipv4) ? null : info.Ipv4.Trim());
					if (string.IsNullOrWhiteSpace(probeIp) && VmGuestIpCache.TryGet(name, out var cachedIp) && _poolSettings.ContainsAddress(cachedIp))
					{
						DrawerIpv4 = cachedIp;
					}
					else
					{
						DrawerIpv4 = string.IsNullOrWhiteSpace(probeIp) ? "—" : probeIp;
					}
					string text = VmInspectorService.SanitizeMac(info.Mac);
					DrawerMac = ((text.Length > 0) ? text : "—");
					DrawerOsHint = (string.IsNullOrWhiteSpace(info.OsHint) ? "—" : info.OsHint);
					DrawerInstallHint = (string.IsNullOrWhiteSpace(info.InstallHint) ? "" : info.InstallHint.Trim());
					SelectedVm.OsVhdPath = info.OsVhdPath;
					SelectedVm.SeedVhdPath = info.SeedVhdPath;
					SelectedVm.OsVhdActualSize = info.OsVhdActualSize;
					SelectedVm.SeedVhdActualSize = info.SeedVhdActualSize;
					ApplyOsIconGlyph(info.OsHint ?? "", this);
				}
			});
		}
		finally
		{
			await Application.Current.Dispatcher.InvokeAsync(() => IsDrawerNetworkLoading = false);
		}
	}

	private void OnOpenVmConsole()
	{
		if (SelectedVm != null)
		{
			var (flag, messageBoxText) = VmControlService.OpenVmConsole(SelectedVm.Name);
			if (!flag)
			{
				MessageBox.Show(messageBoxText, "Open Console", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
			else
			{
				StatusText = "Opened VM Connection for “" + SelectedVm.Name + "”.";
			}
		}
	}

	private void OpenSelectedVmDiskFolder()
	{
		string? path = SelectedVm?.OsVhdPath;
		if (string.IsNullOrWhiteSpace(path) || path == "—" || path == "â€”")
		{
			MessageBox.Show("No VHDX path is available for the selected VM yet.", "Disk location", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}

		string? folder = System.IO.Path.GetDirectoryName(path);
		if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder))
		{
			MessageBox.Show("The disk folder no longer exists:\n" + (folder ?? path), "Disk location", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}

		try
		{
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
			{
				FileName = folder,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show("Could not open the disk folder:\n" + ex.Message, "Disk location", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async Task OnCreateSnapshotAsync()
	{
		if (SelectedVm != null)
		{
			StatusText = "Creating checkpoint for “" + SelectedVm.Name + "”…";
			var (flag, messageBoxText) = await Task.Run(() => VmControlService.CreateCheckpoint(SelectedVm.Name));
			if (!flag)
			{
				MessageBox.Show(messageBoxText, "Create Snapshot", MessageBoxButton.OK, MessageBoxImage.Hand);
				StatusText = "Checkpoint failed.";
			}
			else
			{
				StatusText = "Checkpoint created for “" + SelectedVm.Name + "”.";
				await AfterVmActionAsync();
			}
		}
	}

	private async Task LoadVMsAsync()
	{
		IsLoading = true;
		IsRefreshing = true;
		StatusText = "Scanning virtual machines...";
		try
		{
			await FetchAndApplyAsync(clearInitialOverlay: true);
		}
		catch (UnauthorizedAccessException)
		{
			StatusText = "Access denied. Please run as Administrator.";
			MessageBox.Show("This application requires Administrator privileges to access Hyper-V.\n\nPlease right-click and select 'Run as Administrator'.", "Administrator Required", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		catch (ManagementException ex2)
		{
			StatusText = "Hyper-V is not available on this system.";
			MessageBox.Show("Could not connect to Hyper-V:\n" + ex2.Message + "\n\nMake sure Hyper-V is installed and enabled.", "Hyper-V Not Available", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		catch (Exception ex3)
		{
			StatusText = "Error: " + ex3.Message;
			MessageBox.Show(ex3.Message, "Hyper-V Manager", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		finally
		{
			IsRefreshing = false;
			IsLoading = false;
			if (VirtualMachines.Count == 0)
			{
				IsLoading = false;
			}
		}
	}

	private async Task FetchAndApplyAsync(bool clearInitialOverlay = false)
	{
		List<VirtualMachine> vms = await Task.Run(() => HyperVService.GetVirtualMachinesFast());
		await Application.Current.Dispatcher.InvokeAsync(delegate
		{
			string? previousName = SelectedVm?.Name;
			VirtualMachines.Clear();
			RunningCount = 0;
			StoppedCount = 0;
			OtherCount = 0;
			foreach (VirtualMachine item in vms)
			{
				VirtualMachines.Add(item);
				if (item.EnabledState == 2)
				{
					int runningCount = RunningCount;
					RunningCount = runningCount + 1;
				}
				else if (item.EnabledState == 3)
				{
					int runningCount = StoppedCount;
					StoppedCount = runningCount + 1;
				}
			}
			if (previousName != null)
			{
				SelectedVm = VirtualMachines.FirstOrDefault((VirtualMachine v) => v.Name == previousName);
			}
		});
		if (clearInitialOverlay)
		{
			IsLoading = false;
		}
		StatusText = $"Found {vms.Count} VM(s). Loading details...";
		if (vms.Count > 0)
		{
			VmBulkDetails details = await Task.Run(() => HyperVService.QueryVmResourceDetails(vms));
			Application.Current.Dispatcher.Invoke(delegate
			{
				HyperVService.ApplyVmResourceDetails(vms, details);
			});
			StatusText = $"Ready — {vms.Count} Virtual Machine(s). Allocation vs host capacity.";
		}
		else
		{
			StatusText = "No virtual machines found. Make sure Hyper-V is enabled.";
		}
		await RefreshPoolDisplayAsync();
	}

	private async Task AfterVmActionAsync()
	{
		IsRefreshing = true;
		try
		{
			await FetchAndApplyAsync();
		}
		catch (Exception ex)
		{
			StatusText = "Error: " + ex.Message;
			MessageBox.Show(ex.Message, "Hyper-V Manager", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		finally
		{
			IsRefreshing = false;
		}
	}

	private async Task OnStartVmAsync(VirtualMachine? vm)
	{
		VirtualMachine? vm2 = vm;
		if (vm2 != null)
		{
			StatusText = "Starting “" + vm2.Name + "”…";
			var (flag, messageBoxText) = await Task.Run(() => VmControlService.Start(vm2.Name));
			if (!flag)
			{
				MessageBox.Show(messageBoxText, "Start VM", MessageBoxButton.OK, MessageBoxImage.Hand);
				StatusText = "Start failed.";
			}
			else
			{
				await AfterVmActionAsync();
				await RefreshDrawerIfShowingAsync(vm2.Name);
			}
		}
	}

	private async Task OnStopVmAsync(VirtualMachine? vm)
	{
		VirtualMachine? vm2 = vm;
		if (vm2 != null)
		{
			StatusText = "Stopping “" + vm2.Name + "”…";
			var (flag, messageBoxText) = await Task.Run(() => VmControlService.Stop(vm2.Name));
			if (!flag)
			{
				MessageBox.Show(messageBoxText, "Stop VM", MessageBoxButton.OK, MessageBoxImage.Hand);
				StatusText = "Stop failed.";
			}
			else
			{
				await AfterVmActionAsync();
				await RefreshDrawerIfShowingAsync(vm2.Name);
			}
		}
	}

	private async Task OnRestartVmAsync(VirtualMachine? vm)
	{
		VirtualMachine? vm2 = vm;
		if (vm2 != null)
		{
			StatusText = "Restarting “" + vm2.Name + "”…";
			var (flag, messageBoxText) = await Task.Run(() => VmControlService.Restart(vm2.Name));
			if (!flag)
			{
				MessageBox.Show(messageBoxText, "Restart VM", MessageBoxButton.OK, MessageBoxImage.Hand);
				StatusText = "Restart failed.";
			}
			else
			{
				await AfterVmActionAsync();
				await RefreshDrawerIfShowingAsync(vm2.Name);
			}
		}
	}

	private async Task OnRemoveVirtualDvdAsync(VirtualMachine? vm)
	{
		VirtualMachine? vm2 = vm;
		if (vm2 != null && Application.Current.MainWindow != null && MessageBox.Show("Hyper-V often adds an empty virtual DVD drive. Linux may hang at boot with /dev/sr0 (Can't lookup blockdev).\n\nThe VM will be turned off if it is running, then the virtual DVD drive will be removed. Start the VM again after this.\n\nContinue?", "Remove virtual DVD", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
		{
			StatusText = "Removing virtual DVD from “" + vm2.Name + "”…";
			var (flag, messageBoxText) = await Task.Run(() => VmControlService.RemoveVirtualDvdDrives(vm2.Name));
			if (!flag)
			{
				MessageBox.Show(messageBoxText, "Remove virtual DVD", MessageBoxButton.OK, MessageBoxImage.Hand);
				StatusText = "Remove virtual DVD failed.";
			}
			else
			{
				MessageBox.Show("Virtual DVD removed. Start the VM again — the guest should pass the previous /dev/sr0 hang.", "Remove virtual DVD", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				StatusText = "Virtual DVD removed for “" + vm2.Name + "”.";
				await AfterVmActionAsync();
			}
		}
	}

	private async Task OnDeleteVmAsync(VirtualMachine? vm)
	{
		if (vm == null)
		{
			return;
		}
		Window mainWindow = Application.Current.MainWindow;
		if (mainWindow != null && DeleteVmDialog.Show(mainWindow, vm.Name))
		{
			StatusText = "Stopping and deleting \"" + vm.Name + "\"...";
			List<string> diskPaths = new List<string>();
			if (!string.IsNullOrWhiteSpace(vm.OsVhdPath) && vm.OsVhdPath != "—" && vm.OsVhdPath != "â€”")
			{
				diskPaths.Add(vm.OsVhdPath);
			}
			if (!string.IsNullOrWhiteSpace(vm.SeedVhdPath))
			{
				diskPaths.Add(vm.SeedVhdPath);
			}
			if (diskPaths.Count == 0)
			{
				Dictionary<string, VmDiskInfo> diskInfo = await Task.Run(() => HyperVService.QueryVmDiskInfoViaPowerShell());
				if (diskInfo.TryGetValue(vm.Name, out var info))
				{
					if (!string.IsNullOrWhiteSpace(info.OsVhdPath)) diskPaths.Add(info.OsVhdPath);
					if (!string.IsNullOrWhiteSpace(info.SeedVhdPath)) diskPaths.Add(info.SeedVhdPath);
				}
			}
			var (ok, msg) = await Task.Run(() => VmControlService.Delete(vm.Name));
			if (!ok)
			{
				MessageBox.Show(msg, "Delete VM", MessageBoxButton.OK, MessageBoxImage.Hand);
				StatusText = "Delete failed.";
				return;
			}
			string vmDir = VmProvisionStore.GetVmDirectory(vm.Name);
						// Remove entry from Windows hosts file
			var (hOk, hMsg) = await Task.Run(() => HostsFileManager.RemoveEntry(vm.Name));

			StatusText = "Removing disk files for \"" + vm.Name + "\"...";
			var (ok2, msg2) = await Task.Run(() => VmControlService.DeleteVmStorage(vm.Name, vmDir, diskPaths));
			if (!ok2)
			{
				MessageBox.Show("VM deleted from Hyper-V but disk files could not be removed:\n" + msg2, "Delete VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
			VmProvisionStore.Clear(vm.Name);
			VmGuestIpCache.Remove(vm.Name);
			SelectedVm = null;
			await AfterVmActionAsync();
		}
	}

	private static string ResolveVmDirectory(string vmName)
	{
		string baseDir = AppContext.BaseDirectory;
		string devPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "vhdx", vmName));
		if (System.IO.Directory.Exists(devPath))
		{
			return devPath;
		}
		return System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "vhdx", vmName));
	}
		private async Task RefreshDrawerIfShowingAsync(string vmName)
	{
		if (SelectedVm != null && !(SelectedVm.Name != vmName))
		{
			await LoadDrawerDetailsAsync();
		}
	}

	protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
