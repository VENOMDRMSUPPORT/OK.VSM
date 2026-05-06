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
			TxtSuggestedIp.Text = ((string.IsNullOrWhiteSpace (suggestedNextIpv4) || suggestedNextIpv4 == "\u2014") ? "\u2014 (refresh list)" : suggestedNextIpv4);
			TxtPoolRange.Text = (string.IsNullOrWhiteSpace (poolRangeDisplay) ? "Configure the pool under Network settings." : ("Pool: " + poolRangeDisplay));
			TxtCloudFolderHint.Text = "Cloud base images folder: " + AppCloudImagePaths.ResolveCloudImagesDirectory ();
			TxtVmName.TextChanged += (s, e) => {
				UpdateVhdPathHint (TxtVmName.Text.Trim ());
				UpdateHostname (TxtVmName.Text.Trim ());
			};
			UpdateVhdPathHint (null);
			UpdateHostname (null);
		}

		private void Window_Loaded (object sender, RoutedEventArgs e)
		{
			// Auto-resolve virtual switch (hidden from user)
			var (flag, readOnlyList, text) = VmControlService.ListVirtualSwitchNames ();
			if (flag && readOnlyList.Count > 0) {
				_resolvedSwitchName = readOnlyList[0];
			} else if (!flag) {
				MessageBox.Show (string.IsNullOrWhiteSpace (text) ? "Could not list virtual switches." : text, "Hyper-V", MessageBoxButton.OK, MessageBoxImage.Exclamation);
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
			foreach (string item in AppCloudImagePaths.EnumerateCloudBaseImages ()) {
				CmbCloud.Items.Add (new ComboBoxItem {
					Content = System.IO.Path.GetFileNameWithoutExtension (item),
					ToolTip = item,
					Tag = item
				});
			}
			if (CmbCloud.Items.Count > 0) {
				CmbCloud.SelectedIndex = 0;
			}
		}

		private static string ResolveVhdxBaseDirectory ()
		{
			string baseDir = AppContext.BaseDirectory;
			string devPath = System.IO.Path.GetFullPath (System.IO.Path.Combine (baseDir, "..", "..", "..", "vhdx"));
			if (Directory.Exists (devPath)) {
				return devPath;
			}
			string prodPath = System.IO.Path.GetFullPath (System.IO.Path.Combine (baseDir, "vhdx"));
			return prodPath;
		}

		private void UpdateVhdPathHint (string? vmName)
		{
			if (string.IsNullOrWhiteSpace (vmName)) {
				TxtVhdPathHint.Text = "VM disk files will be auto-created in the vhdx/ folder.";
				return;
			}
			string safeName = SanitizeForPath (vmName);
			string vmDir = System.IO.Path.Combine (ResolveVhdxBaseDirectory (), safeName);
			TxtVhdPathHint.Text = string.Format ("\U0001F4C1 {0}\n  \u2514\u2500 {1}.vhdx  (OS)\n  \u2514\u2500 {1}-cidata-seed.vhdx  (cloud-init)", vmDir, safeName);
		}

		private static string SanitizeForPath (string input)
		{
			if (string.IsNullOrWhiteSpace (input)) {
				return "";
			}
			return string.Join ("_", input.Split (System.IO.Path.GetInvalidFileNameChars ()));
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
			string vmDir = System.IO.Path.Combine (ResolveVhdxBaseDirectory (), safeName);
			string osVhdPath = System.IO.Path.Combine (vmDir, safeName + ".vhdx");
			string seedVhdPath = System.IO.Path.Combine (vmDir, safeName + "-cidata-seed.vhdx");

			if (string.IsNullOrWhiteSpace (_resolvedSwitchName)) {
				MessageBox.Show ("No virtual switch found. Create an External Switch in Hyper-V Manager first.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			if (CmbCloud.Items.Count == 0) {
				MessageBox.Show ("Add one or more .vhd / .vhdx cloud base files under:\r\n" + AppCloudImagePaths.ResolveCloudImagesDirectory (), "No cloud base image", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			string? templatePath = null;
			if (CmbCloud.SelectedItem is ComboBoxItem { Tag: string tag }) {
				templatePath = tag;
			}
			if (string.IsNullOrWhiteSpace (templatePath) || !File.Exists (templatePath)) {
				MessageBox.Show ("Select a cloud base image from the list.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			if (!int.TryParse (TxtMemGb.Text.Trim (), out var memGb) || memGb < 1 || memGb > 512) {
				MessageBox.Show ("Memory must be between 1 and 512 GB.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			if (!int.TryParse (TxtCpu.Text.Trim (), out var cpuCount) || cpuCount < 1 || cpuCount > 256) {
				MessageBox.Show ("vCPU must be between 1 and 256.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			if (!int.TryParse (TxtDiskGb.Text.Trim (), out var diskGb) || diskGb < 8 || diskGb > 65536) {
				MessageBox.Show ("Disk size must be between 8 and 65536 GB.", "Create VM", MessageBoxButton.OK, MessageBoxImage.Exclamation);
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

			if (!_poolSettings.HasValidStaticNetwork ()) {
				MessageBox.Show ("Configure a valid default gateway and prefix (1\u201332) under Network settings (IPv4 pool) before creating a VM.", "Network settings", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}

			string guestIpv4 = TxtSuggestedIp.Text.Trim ();
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

			List<string> dnsServers = _poolSettings.EnumerateDnsServers ().ToList ();
			string notes = "Static guest IPv4 (cloud-init): " + guestIpv4 + "\r\n" + UbuntuInstallProfile.Ubuntu2404Lts.NotesSuffixCloud ();

			Result = new CreateUbuntuCloudVmParameters {
				VmName = vmName,
				SwitchName = _resolvedSwitchName,
				TemplateVhdPath = templatePath,
				OsVhdFullPath = System.IO.Path.GetFullPath (osVhdPath),
				SeedVhdFullPath = System.IO.Path.GetFullPath (seedVhdPath),
				VmDirectory = System.IO.Path.GetFullPath (vmDir),
				OsDiskSizeBytes = (ulong)((long)diskGb * 1024L * 1024 * 1024),
				MemoryStartupBytes = (long)memGb * 1024L * 1024 * 1024,
				ProcessorCount = cpuCount,
				AdminUsername = adminUser,
				AdminPassword = adminPass,
				Hostname = hostname,
				Profile = UbuntuInstallProfile.Ubuntu2404Lts,
				Notes = notes,
				StaticGuestIpv4 = guestIpv4,
				DefaultGateway = _poolSettings.DefaultGateway.Trim (),
				PrefixLength = _poolSettings.PrefixLength,
				DnsServers = dnsServers
			};
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
