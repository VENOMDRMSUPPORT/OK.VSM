using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HyperVMManager.Models;
using HyperVMManager.Services;
using Microsoft.Win32;

namespace HyperVMManager.Dialogs;

	public partial class CreateVmDialog : Window
	{
		private readonly Ipv4PoolSettings _poolSettings;
		private string _resolvedSwitchName = "";
		private bool _passwordVisible;
		private HashSet<string> _existingVmNames = new (StringComparer.OrdinalIgnoreCase);
		private HashSet<string> _existingHostnames = new (StringComparer.OrdinalIgnoreCase);

		public CreateUbuntuCloudVmParameters? Result { get; private set; }

		public CreateVmDialog (string suggestedNextIpv4, string poolRangeDisplay, Ipv4PoolSettings poolSettings)
		{
			InitializeComponent ();
			_poolSettings = poolSettings ?? new Ipv4PoolSettings ();
			Title = AppBrand.DisplayName + " - Create Ubuntu VM";
			TxtSuggestedIp.Text = ((string.IsNullOrWhiteSpace (suggestedNextIpv4) || suggestedNextIpv4 == "\u2014") ? "\u2014 (refresh list)" : suggestedNextIpv4);
			TxtPoolRange.Text = (string.IsNullOrWhiteSpace (poolRangeDisplay) ? "Configure the pool under Network settings." : ("Pool: " + poolRangeDisplay));
			TxtVmName.TextChanged += (s, e) => {
				UpdateVhdPathHint (TxtVmName.Text.Trim ());
				UpdateHostname (TxtVmName.Text.Trim ());
			};
			TxtCustomDiskBaseFolder.TextChanged += (s, e) => UpdateCloudCacheHint ();
			UpdateVhdPathHint (null);
			UpdateHostname (null);
			UpdateCloudCacheHint ();
		}

		private void Window_Loaded (object sender, RoutedEventArgs e)
		{
			// Auto-resolve virtual switch (hidden from user)
			var (flag, readOnlyList, text) = VmControlService.ListVirtualSwitchNames ();
			if (flag && readOnlyList.Count > 0) {
				_resolvedSwitchName = readOnlyList[0];
			} else {
				if (!flag) {
					MessageBox.Show (string.IsNullOrWhiteSpace (text) ? "Could not list virtual switches." : text, "Hyper-V", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
				// No External switch found — offer to create one automatically
				TryAutoCreateExternalSwitch ();
			}

			// Load existing VMs for duplicate checking
			var (ok, existingVms, _) = VmControlService.ListExistingVms ();
			if (ok) {
				foreach (var vm in existingVms) {
					_existingVmNames.Add (vm.name);
					if (!string.IsNullOrWhiteSpace (vm.hostname)) {
						_existingHostnames.Add (vm.hostname);
					}
				}
			}

			CmbCloud.Items.Clear ();
			foreach (CloudImageCatalogItem item in CloudImageCatalog.List ()) {
				string probeDiskPath = BuildProbeOsVhdPathForSelectedLocation ();
				string suffix = CloudImageCacheService.IsVerifiedArchiveCached (item, probeDiskPath) ? " (ready in cache)" : " (download required)";
				CmbCloud.Items.Add (new ComboBoxItem {
					Content = item.DisplayName + suffix,
					ToolTip = item.ArchiveUri.ToString (),
					Tag = item
				});
			}
			if (CmbCloud.Items.Count > 0) {
				CmbCloud.SelectedIndex = 0;
			}
		}

		private void TryAutoCreateExternalSwitch ()
		{
			var (adaptersOk, adapters, _) = VmControlService.ListPhysicalNetAdapters ();
			if (!adaptersOk || adapters.Count == 0) {
				MessageBox.Show (
					"No External Hyper-V switch found and no active physical network adapter is available to create one.\n\nConnect a network adapter and retry.",
					"No network switch",
					MessageBoxButton.OK,
					MessageBoxImage.Exclamation);
				return;
			}

			string adapterList = string.Join ("\n", adapters.Select ((a, i) => $"  • {a}"));
			string adapterToUse = adapters[0];

			var answer = MessageBox.Show (
				$"No External Hyper-V switch was found.\n\nCreate one automatically using:\n{adapterList}\n\nClick Yes to create it now.",
				"Create External Switch",
				MessageBoxButton.YesNo,
				MessageBoxImage.Question,
				MessageBoxResult.Yes);

			if (answer != MessageBoxResult.Yes) {
				return;
			}

			var (ok, switchName, error) = VmControlService.CreateExternalSwitch (adapterToUse);
			if (!ok) {
				MessageBox.Show (
					"Could not create External switch:\n\n" + error + "\n\nCreate it manually in Hyper-V Manager → Virtual Switch Manager → New → External.",
					"Create External Switch",
					MessageBoxButton.OK,
					MessageBoxImage.Exclamation);
				return;
			}

			_resolvedSwitchName = switchName;
			MessageBox.Show (
				$"External switch \"{switchName}\" created successfully.",
				"Switch Ready",
				MessageBoxButton.OK,
				MessageBoxImage.Information);
		}

private static string ResolveVhdxBaseDirectory ()
	{
		// 1. استخدم آخر مسار استخدمه المستخدم (محفوظ في الإعدادات)
		var settings = AppUserSettings.Load ();
		if (!string.IsNullOrWhiteSpace (settings.LastUsedVmPath) && Directory.Exists (settings.LastUsedVmPath))
			return settings.LastUsedVmPath;

		// 2. ابحث عن أول درايف فيزيائي مناسب (ليس System Drive وليس USB)
		string? candidate = DiscoverFirstSuitableDrive ();
		if (candidate != null)
			return Path.Combine (candidate, "HyperVMManager", "vhdx");

		// 3. fallback: مجلد التشغيل
		string baseDir = AppContext.BaseDirectory;
		return Path.GetFullPath (Path.Combine (baseDir, "vhdx"));
	}

	/// <summary>
	/// يكتشف أول درايف فيزيائي مناسب ليكون مسار الـ VM الافتراضي.
	/// يتجنب System Drive (C:) ويتجنب الدرايفات الصغيرة أو USB.
	/// </summary>
	private static string? DiscoverFirstSuitableDrive ()
	{
		try {
			string systemRoot = Path.GetPathRoot (Environment.SystemDirectory) ?? "";

			foreach (DriveInfo drive in DriveInfo.GetDrives ()) {
				// تجاهل الدرايفات غير الجاهزة (CD-ROM، إلخ)
				if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Network)
					continue;

				// تجاهل درايف النظام (عادة C:)
				if (drive.RootDirectory.FullName.Equals (systemRoot, StringComparison.OrdinalIgnoreCase))
					continue;

				// تجاهل الدرايفات الصغيرة جداً (أقل من 20 GB) أو ممتلئة جداً
				if (drive.TotalSize < 20L * 1024 * 1024 * 1024)
					continue;

				// تأكد إن الدرايف جاهز للقراءة والكتابة
				if (drive.IsReady)
					return drive.RootDirectory.FullName.TrimEnd ('\\');
			}
		} catch {
			// تجاهل أي أخطاء في الاكتشاف
		}

		return null;
	}

	/// <summary>
	/// يحفظ المسار المستخدم في الإعدادات حتى الجلسة القادمة.
	/// </summary>
	private static void SaveLastUsedVmPath (string path)
	{
		try {
			var settings = AppUserSettings.Load ();
			settings.LastUsedVmPath = path;
			settings.Save ();
		} catch {
			// تجاهل أخطاء الحفظ — ليس مهماً جداً
		}
	}

private string ResolveSelectedVhdxBaseDirectory ()
	{
		if (RdoDiskCustom.IsChecked == true) {
			string customPath = TxtCustomDiskBaseFolder.Text.Trim ();
			if (!string.IsNullOrWhiteSpace (customPath))
				SaveLastUsedVmPath (customPath);
			return customPath;
		}

		// Auto mode: استخدم المنطق الذكي الجديد
		string autoPath = ResolveVhdxBaseDirectory ();
		// لا نحفظ هنا — SaveLastUsedVmPath يتُستدعى من CreateButton_Click عند النجاح
		return autoPath;
	}

		private string BuildProbeOsVhdPathForSelectedLocation ()
		{
			string vmName = TxtVmName.Text.Trim ();
			string safeName = SanitizeForPath (string.IsNullOrWhiteSpace (vmName) ? "sample-vm" : vmName);
			string baseDir = ResolveSelectedVhdxBaseDirectory ();
			if (string.IsNullOrWhiteSpace (baseDir)) {
				return System.IO.Path.Combine (CloudImageCacheService.CacheDirectory, safeName + ".vhdx");
			}
			return System.IO.Path.Combine (baseDir, safeName, safeName + ".vhdx");
		}

		private void UpdateCloudCacheHint ()
		{
			string probeDiskPath = BuildProbeOsVhdPathForSelectedLocation ();
			TxtCloudFolderHint.Text = "Cloud image cache: " + CloudImageCacheService.GetCacheDirectoryForVmDisk (probeDiskPath);
		}

		private void UpdateVhdPathHint (string? vmName)
		{
			if (string.IsNullOrWhiteSpace (vmName)) {
				TxtVhdPathHint.Text = "VM disk files will be auto-created in the vhdx/ folder.";
				return;
			}
			string safeName = SanitizeForPath (vmName);
			string baseDir = ResolveSelectedVhdxBaseDirectory ();
			string vmDir = string.IsNullOrWhiteSpace (baseDir) ? "" : System.IO.Path.Combine (baseDir, safeName);
			TxtVhdPathHint.Text = string.Format ("\U0001F4C1 {0}\n  \u2514\u2500 {1}.vhdx  (OS)\n  \u2514\u2500 {1}-cidata-seed.vhdx  (cloud-init)", vmDir, safeName);
		}

		private static string SanitizeForPath (string input)
		{
			if (string.IsNullOrWhiteSpace (input)) {
				return "";
			}
			return string.Join ("_", input.Split (System.IO.Path.GetInvalidFileNameChars ()));
		}

		private static bool TryValidateDiskLocation (string vmName, string vmDir, string osVhdPath, string seedVhdPath, out string message)
		{
			message = "";
			if (string.IsNullOrWhiteSpace (vmName)) {
				message = "Enter a VM name before choosing disk files.";
				return false;
			}
			if (string.IsNullOrWhiteSpace (vmDir)) {
				message = "Choose a custom disk base folder, or use Auto.";
				return false;
			}
			if (File.Exists (osVhdPath) || File.Exists (seedVhdPath)) {
				message = "Disk files already exist for this VM name. Choose another VM name or another disk folder.";
				return false;
			}
			try {
				Directory.CreateDirectory (vmDir);
				string probe = System.IO.Path.Combine (vmDir, ".venom-write-test-" + Guid.NewGuid ().ToString ("N") + ".tmp");
				File.WriteAllText (probe, "ok");
				File.Delete (probe);
				return true;
			} catch (Exception ex) {
				message = "The selected disk folder is not writable:\n" + ex.Message;
				return false;
			}
		}

		private void DiskLocationMode_Changed (object sender, RoutedEventArgs e)
		{
			if (CustomDiskLocationPanel == null) {
				return;
			}
			CustomDiskLocationPanel.Visibility = (RdoDiskCustom.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
			UpdateVhdPathHint (TxtVmName.Text.Trim ());
			UpdateCloudCacheHint ();
		}

		private void BrowseDiskFolder_Click (object sender, RoutedEventArgs e)
		{
			OpenFolderDialog dialog = new OpenFolderDialog {
				Title = "Choose base folder for VM disk files",
				Multiselect = false
			};
			if (dialog.ShowDialog (this) == true) {
				TxtCustomDiskBaseFolder.Text = dialog.FolderName;
				UpdateVhdPathHint (TxtVmName.Text.Trim ());
				UpdateCloudCacheHint ();
			}
		}

		private void UpdateHostname (string? vmName)
		{
			if (string.IsNullOrWhiteSpace (vmName)) {
				TxtHostname.Text = "";
				return;
			}
			// Lowercase, replace spaces and special chars with hyphens, collapse multiple hyphens
			var sb = new StringBuilder ();
			bool lastWasHyphen = true; // skip leading hyphens
			foreach (char ch in vmName.ToLowerInvariant ().Trim ()) {
				if (char.IsLetterOrDigit (ch)) {
					sb.Append (ch);
					lastWasHyphen = false;
				} else if (!lastWasHyphen) {
					sb.Append ('-');
					lastWasHyphen = true;
				}
			}
			// Remove trailing hyphen
			if (sb.Length > 0 && sb[sb.Length - 1] == '-') {
				sb.Length--;
			}
			if (sb.Length > 0) {
				sb.Append (".local");
			}
			TxtHostname.Text = sb.ToString ();
		}

		private void TogglePassword_Click (object sender, RoutedEventArgs e)
		{
			_passwordVisible = !_passwordVisible;
			if (_passwordVisible) {
				TxtAdminPasswordVisible.Text = TxtAdminPassword.Password;
				TxtAdminPasswordVisible.Visibility = Visibility.Visible;
				TxtAdminPassword.Visibility = Visibility.Collapsed;
				TxtEyeIcon.Text = "\uE8B6"; // EyeOff
			} else {
				TxtAdminPassword.Password = TxtAdminPasswordVisible.Text;
				TxtAdminPassword.Visibility = Visibility.Visible;
				TxtAdminPasswordVisible.Visibility = Visibility.Collapsed;
				TxtEyeIcon.Text = "\uE8B7"; // Eye
			}
		}

		private void GeneratePassword_Click (object sender, RoutedEventArgs e)
		{
			string password = GenerateRandomPassword (16);
			if (_passwordVisible) {
				TxtAdminPasswordVisible.Text = password;
			} else {
				TxtAdminPassword.Password = password;
			}
		}

		private static string GenerateRandomPassword (int length)
		{
			const string upper = "ABCDEFGHJKMNPQRSTUVWXYZ";
			const string lower = "abcdefghjkmnpqrstuvwxyz";
			const string digits = "23456789";
			const string special = "!@#$%&*?+=-";
			var all = upper + lower + digits + special;

			var bytes = RandomNumberGenerator.GetBytes (length);
			var sb = new StringBuilder (length);
			sb.Append (upper[bytes[0] % upper.Length]);
			sb.Append (lower[bytes[1] % lower.Length]);
			sb.Append (digits[bytes[2] % digits.Length]);
			sb.Append (special[bytes[3] % special.Length]);
			for (int i = 4; i < length; i++) {
				sb.Append (all[bytes[i] % all.Length]);
			}
			// Shuffle
			for (int i = sb.Length - 1; i > 0; i--) {
				int j = bytes[i % bytes.Length] % (i + 1);
				(sb[i], sb[j]) = (sb[j], sb[i]);
			}
			return sb.ToString ();
		}

		private string GetAdminPassword ()
		{
			return _passwordVisible ? TxtAdminPasswordVisible.Text : TxtAdminPassword.Password;
		}

		private void CreateButton_Click (object sender, RoutedEventArgs e)
		{
			string vmName = TxtVmName.Text.Trim ();
			if (vmName.Length == 0) {
				MessageBox.Show ("Enter a VM name.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			// Check duplicate VM name
			if (_existingVmNames.Contains (vmName)) {
				MessageBox.Show ("A VM with this name already exists.", "Duplicate VM name", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			// Check duplicate hostname
			string hostname = TxtHostname.Text.Trim ();
			if (!string.IsNullOrWhiteSpace (hostname) && _existingHostnames.Contains (hostname)) {
				MessageBox.Show (string.Format ("A VM with hostname \"{0}\" already exists.", hostname), "Duplicate hostname", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			// Sanitize VM name for folder path
			string safeName = SanitizeForPath (vmName);
			if (safeName.Length == 0) {
				MessageBox.Show ("VM name does not contain valid path characters.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}
			string selectedBaseDir = ResolveSelectedVhdxBaseDirectory ();
			string vmDir = System.IO.Path.Combine (selectedBaseDir, safeName);
			string osVhdPath = System.IO.Path.Combine (vmDir, safeName + ".vhdx");
			string seedVhdPath = System.IO.Path.Combine (vmDir, safeName + "-cidata-seed.vhdx");

			if (!TryValidateDiskLocation (safeName, vmDir, osVhdPath, seedVhdPath, out string diskLocationError)) {
				MessageBox.Show (diskLocationError, "Disk location", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			if (string.IsNullOrWhiteSpace (_resolvedSwitchName)) {
				MessageBox.Show ("No virtual switch found. Create an External Switch in Hyper-V Manager first.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			if (CmbCloud.Items.Count == 0) {
				MessageBox.Show ("No built-in Ubuntu cloud image catalog entries are available.", "No cloud image", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

CloudImageCatalogItem? cloudImage = null;
		if (CmbCloud.SelectedItem is ComboBoxItem { Tag: CloudImageCatalogItem tag }) {
			cloudImage = tag;
		}
		if (cloudImage == null) {
			MessageBox.Show ("Select an Ubuntu cloud image from the list.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}

		// تحديد إصدار Ubuntu بناءً على الصورة المختارة
		UbuntuInstallProfile selectedProfile = cloudImage.Id == CloudImageCatalog.Ubuntu2204AzureId
			? UbuntuInstallProfile.Ubuntu2204Lts
			: UbuntuInstallProfile.Ubuntu2404Lts;

		if (!int.TryParse (TxtMemGb.Text.Trim (), out var memGb) || memGb < 1 || memGb > 8) {
			MessageBox.Show ("Max memory must be between 1 and 8 GB.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}

		if (!int.TryParse (TxtCpu.Text.Trim (), out var cpuCount) || cpuCount < 1 || cpuCount > 8) {
			MessageBox.Show ("vCPU must be between 1 and 8.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}

		if (!int.TryParse (TxtDiskGb.Text.Trim (), out var diskGb) || diskGb < 8 || diskGb > 256) {
			MessageBox.Show ("Disk size must be between 8 and 256 GB.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (diskGb > 80 && MessageBox.Show ("This creates a dynamic virtual disk, but it can still grow physically as the guest writes data.\n\nContinue with " + diskGb + " GB?", "Disk size", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) {
			return;
		}

		if (string.IsNullOrWhiteSpace (_resolvedSwitchName)) {
			MessageBox.Show ("No virtual switch found. Create an External Switch in Hyper-V Manager first.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}

		var (switchOk, isExternal, switchType, switchError) = VmControlService.ValidateExternalSwitch (_resolvedSwitchName);
		if (!switchOk) {
			MessageBox.Show (string.IsNullOrWhiteSpace (switchError) ? "Could not validate the selected Hyper-V switch." : switchError, "Network switch", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (!isExternal) {
			MessageBox.Show ("Ubuntu cloud VMs require an External Hyper-V switch for reliable networking.\n\nSelected switch type: " + (string.IsNullOrWhiteSpace (switchType) ? "Unknown" : switchType) + ".", "Network switch", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}

		// Admin user: optional, default "venom"
		string adminUser = TxtAdminUser.Text.Trim ();
		if (string.IsNullOrWhiteSpace (adminUser)) {
			adminUser = "venom";
		}

		string adminPass = GetAdminPassword ();
		if (adminPass.Length == 0) {
			MessageBox.Show ("Enter an admin password (used for cloud-init, stored only on the seed disk).", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}

		bool useDhcpNetwork = false;
		string guestIpv4 = "";
		List<string> dnsServers = new List<string> ();
		string notes = "Guest IPv4: DHCP (" + _resolvedSwitchName + ")" + Environment.NewLine + selectedProfile.NotesSuffixCloud ();
		if (!useDhcpNetwork) {
			if (!_poolSettings.HasValidStaticNetwork ()) {
				MessageBox.Show ("Configure a valid default gateway and prefix (1\u201332) under Network settings (IPv4 pool) before creating a VM.", "Network settings", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			guestIpv4 = TxtSuggestedIp.Text.Trim ();
			if (guestIpv4.Length == 0 || guestIpv4 == "\u2014 (refresh list)") {
				MessageBox.Show ("Enter the guest IPv4 from the pool (use Refresh if the suggested address is missing).", "Guest IPv4", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			if (!Ipv4PoolSettings.TryParseIpv4 (guestIpv4, out var _)) {
				MessageBox.Show ("Enter a valid guest IPv4 address (e.g. 192.168.1.51).", "Guest IPv4", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			if (!_poolSettings.ContainsAddress (guestIpv4)) {
				MessageBox.Show (string.Format ("Guest IPv4 must be within the pool ({0} \u2013 {1}).", _poolSettings.Ipv4RangeStart, _poolSettings.Ipv4RangeEnd), "Guest IPv4", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			dnsServers = _poolSettings.EnumerateDnsServers ().ToList ();
			notes = "Static guest IPv4 (cloud-init): " + guestIpv4 + Environment.NewLine + selectedProfile.NotesSuffixCloud ();
		}

		Result = new CreateUbuntuCloudVmParameters {
			VmName = vmName,
			SwitchName = _resolvedSwitchName,
			CloudImageId = cloudImage.Id,
			OsVhdFullPath = System.IO.Path.GetFullPath (osVhdPath),
			SeedVhdFullPath = System.IO.Path.GetFullPath (seedVhdPath),
			VmDirectory = System.IO.Path.GetFullPath (vmDir),
			OsDiskSizeBytes = (ulong)((long)diskGb * 1024L * 1024 * 1024),
			MemoryStartupBytes = 1L * 1024L * 1024 * 1024,
			MemoryMinimumBytes = 1L * 1024L * 1024 * 1024,
			MemoryMaximumBytes = (long)memGb * 1024L * 1024 * 1024,
			ProcessorCount = cpuCount,
			AdminUsername = adminUser,
			AdminPassword = adminPass,
			Hostname = hostname,
			Profile = selectedProfile,
			Notes = notes,
			StaticGuestIpv4 = guestIpv4,
			DefaultGateway = _poolSettings.DefaultGateway.Trim (),
			PrefixLength = _poolSettings.PrefixLength,
			DnsServers = dnsServers
		};

		// حفظ مسار الـ VM المختار للإستخدام القادم
		SaveLastUsedVmPath (ResolveSelectedVhdxBaseDirectory ());

		base.DialogResult = true;
		}

		private void CancelButton_Click (object sender, RoutedEventArgs e)
		{
			base.DialogResult = false;
		}

		private void CloseButton_Click (object sender, RoutedEventArgs e)
		{
			base.DialogResult = false;
		}

		private void Header_MouseLeftButtonDown (object sender, MouseButtonEventArgs e)
		{
			if (e.LeftButton == MouseButtonState.Pressed) {
				DragMove ();
			}
		}
	}
