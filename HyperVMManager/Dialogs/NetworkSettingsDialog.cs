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

namespace HyperVMManager.Dialogs;

	public partial class NetworkSettingsDialog : Window
	{






		public Ipv4PoolSettings? EditedSettings { get; private set; }

		public NetworkSettingsDialog (Ipv4PoolSettings initial)
		{
			InitializeComponent ();
			TxtStart.Text = initial.Ipv4RangeStart;
			TxtEnd.Text = initial.Ipv4RangeEnd;
			TxtGateway.Text = initial.DefaultGateway;
			TxtPrefix.Text = initial.PrefixLength.ToString ();
			TxtDns.Text = initial.DnsServers;
			TxtManualExclusions.Text = initial.ManualPoolExclusions;
		}

		public static bool TryEdit (Window owner, Ipv4PoolSettings initial, out Ipv4PoolSettings saved)
		{
			saved = initial;
			NetworkSettingsDialog networkSettingsDialog = new NetworkSettingsDialog (initial) {
				Owner = owner
			};
			if (networkSettingsDialog.ShowDialog () != true || networkSettingsDialog.EditedSettings == null) {
				return false;
			}
			saved = networkSettingsDialog.EditedSettings;
			return true;
		}

		private void SaveButton_Click (object sender, RoutedEventArgs e)
		{
			Ipv4PoolSettings ipv4PoolSettings = new Ipv4PoolSettings {
				Ipv4RangeStart = TxtStart.Text.Trim (),
				Ipv4RangeEnd = TxtEnd.Text.Trim (),
				DefaultGateway = TxtGateway.Text.Trim (),
				DnsServers = TxtDns.Text.Trim (),
				ManualPoolExclusions = TxtManualExclusions.Text.Trim ()
			};
			if (!ipv4PoolSettings.TryGetRange (out var _, out var end)) {
				MessageBox.Show ("Enter two valid IPv4 addresses (e.g. 192.168.1.50 and 192.168.1.80).", "Invalid range", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}
			int result;
			bool flag = !int.TryParse (TxtPrefix.Text.Trim (), out result);
			bool flag2 = flag;
			if (!flag2) {
				bool flag3 = ((result < 1 || result > 32) ? true : false);
				flag2 = flag3;
			}
			if (flag2) {
				MessageBox.Show ("Prefix length must be a number between 1 and 32 (e.g. 24 for /24).", "Invalid prefix", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}
			ipv4PoolSettings.PrefixLength = result;
			if (!Ipv4PoolSettings.TryParseIpv4 (ipv4PoolSettings.DefaultGateway, out end)) {
				MessageBox.Show ("Enter a valid default gateway IPv4 (e.g. 192.168.1.1).", "Invalid gateway", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}
			string[] array = ipv4PoolSettings.DnsServers.Split (',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			foreach (string text in array) {
				if (!Ipv4PoolSettings.TryParseIpv4 (text, out end)) {
					MessageBox.Show ("DNS entries must be comma-separated IPv4 addresses. Invalid: " + text, "Invalid DNS", MessageBoxButton.OK, MessageBoxImage.Exclamation);
					return;
				}
			}
			string[] array2 = ipv4PoolSettings.ManualPoolExclusions.Split (',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			foreach (string text2 in array2) {
				if (!Ipv4PoolSettings.TryParseIpv4 (text2, out end)) {
					MessageBox.Show ("Manual exclusions must be comma-separated IPv4 addresses. Invalid: " + text2, "Invalid exclusion", MessageBoxButton.OK, MessageBoxImage.Exclamation);
					return;
				}
				if (!ipv4PoolSettings.ContainsAddress (text2.Trim ())) {
					MessageBox.Show ("Each manual exclusion must fall inside the pool range (" + ipv4PoolSettings.Ipv4RangeStart + " – " + ipv4PoolSettings.Ipv4RangeEnd + "). Invalid: " + text2.Trim (), "Exclusion out of range", MessageBoxButton.OK, MessageBoxImage.Exclamation);
					return;
				}
			}
			EditedSettings = ipv4PoolSettings;
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


