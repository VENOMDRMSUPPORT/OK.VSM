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

	public static class CloudInitSeedBuilder
	{
		public static string BuildMetaData (string instanceId)
		{
			return "instance-id: " + instanceId + "\nlocal-hostname: " + instanceId + "\ndsmode: local\n";
		}

		public static string BuildUserData (CreateUbuntuCloudVmParameters p)
		{
			string text = (string.IsNullOrWhiteSpace (p.Hostname) ? SanitizeHostname (p.VmName) : SanitizeHostname (p.Hostname));
			string text2 = p.AdminUsername.Trim ();
			if (text2.Length == 0) {
				text2 = "ubuntu";
			}
			string text3 = p.StaticGuestIpv4?.Trim () ?? "";
			string text4 = p.DefaultGateway?.Trim () ?? "";
			int prefixLength = p.PrefixLength;
			List<string> list = MergeDnsWithPublicFallbacks (p.DnsServers, text4);
			StringBuilder sb = new StringBuilder ();
			L ("#cloud-config");
			L ("hostname: " + text);
			L ("manage_etc_hosts: true");
			L ("package_update: false");
			L ("package_upgrade: false");
			L ("users:");
			L ("  - name: " + YamlQuote (text2));
			L ("    sudo: ALL=(ALL) NOPASSWD:ALL");
			L ("    groups: users, adm, sudo");
			L ("    shell: /bin/bash");
			L ("    lock_passwd: false");
			L ("    plain_text_passwd: " + YamlQuote (p.AdminPassword));
			L ("ssh_pwauth: true");
			L ("disable_root: false");
			L ("chpasswd:");
			L ("  list: |");
			L ("    " + text2 + ":" + p.AdminPassword);
			L ("    root:" + p.AdminPassword);
			L ("  expire: false");
			L ("write_files:");
			if (text3.Length > 0 && text4.Length > 0 && prefixLength >= 1 && prefixLength <= 32) {
				L ("  - path: /etc/cloud/cloud.cfg.d/99-disable-network-rendering.cfg");
			L ("    owner: root:root");
			L ("    permissions: '0644'");
			L ("    content: |");
			L ("      # Prevent cloud-init from re-rendering netplan on every boot.");
			L ("      # Networking is fully managed by 01-hypervm.yaml (written below).");
			L ("      network:");
			L ("        config: disabled");
			L ("  - path: /etc/netplan/01-hypervm.yaml");
				L ("    owner: root:root");
				L ("    permissions: '0600'");
				L ("    content: |");
				L ("      network:");
				L ("        version: 2");
				L ("        ethernets:");
				L ("          eth0:");
				L ("            dhcp4: false");
				L ("            addresses:");
				L ($"              - {text3}/{prefixLength}");
				L ("            routes:");
				L ("              - to: default");
				L ("                via: " + text4);
				L ("            nameservers:");
				L ("              addresses:");
				foreach (string item in list) {
					L ("                - " + item);
				}
			}
			L ("  - path: /etc/ssh/sshd_config.d/99-hypervm-ssh.conf");
			L ("    owner: root:root");
			L ("    permissions: '0644'");
			L ("    content: |");
			L ("      # HyperVM override (keep distro sshd_config, override auth policy only).");
			L ("      PermitRootLogin yes");
			L ("      MaxAuthTries 6");
			L ("      PubkeyAuthentication yes");
			L ("      PasswordAuthentication yes");
			L ("      KbdInteractiveAuthentication yes");
			L ("runcmd:");
			if (text3.Length > 0 && text4.Length > 0 && prefixLength >= 1 && prefixLength <= 32) {
				L ("  - rm -f /etc/netplan/50-cloud-init.yaml /etc/netplan/90-hotplug-azure.yaml");
				L ("  - netplan apply || true");
			}
			L ("  - bash -c 'rm -f /etc/ssh/sshd_config.d/50-cloud-init.conf /etc/ssh/sshd_config.d/60-cloudimg-settings.conf /etc/ssh/sshd_config.d/99-cloud-init.conf'");
			L ("  - bash -c " + ShellSingleQuote ("printf '%s\\n' " + ShellSingleQuote (text2 + ":" + p.AdminPassword) + " " + ShellSingleQuote ("root:" + p.AdminPassword) + " | chpasswd"));
			L ("  - systemctl daemon-reload || true");
			L ("  - systemctl unmask ssh.service ssh.socket || true");
			L ("  - systemctl enable ssh.service || true");
			L ("  - systemctl enable ssh.socket || true");
			L ("  - systemctl reload ssh || systemctl restart ssh || systemctl restart sshd || true");
			return sb.ToString ();
			void L (string s)
			{
				sb.Append (s).Append ('\n');
			}
		}

		public static string? BuildNetworkConfig (CreateUbuntuCloudVmParameters p)
		{
			string text = p.StaticGuestIpv4?.Trim () ?? "";
			string text2 = p.DefaultGateway?.Trim () ?? "";
			if (text.Length == 0 || text2.Length == 0) {
				return null;
			}
			if (!Ipv4PoolSettings.TryParseIpv4 (text, out var value) || !Ipv4PoolSettings.TryParseIpv4 (text2, out value)) {
				return null;
			}
			int prefixLength = p.PrefixLength;
			if ((prefixLength < 1 || prefixLength > 32) ? true : false) {
				return null;
			}
			StringBuilder sb = new StringBuilder ();
			L ("version: 2");
			L ("ethernets:");
			L ("  eth0:");
			L ("    dhcp4: false");
			L ("    addresses:");
			L ($"      - {text}/{prefixLength}");
			L ("    routes:");
			L ("      - to: default");
			L ("        via: " + text2);
			List<string> list = MergeDnsWithPublicFallbacks (p.DnsServers, text2);
			L ("    nameservers:");
			L ("      addresses:");
			foreach (string item in list) {
				L ("        - " + item);
			}
			return sb.ToString ();
			void L (string s)
			{
				sb.Append (s).Append ('\n');
			}
		}

		private static List<string> MergeDnsWithPublicFallbacks (IReadOnlyList<string>? userDns, string gateway)
		{
			HashSet<string> seen = new HashSet<string> ();
			List<string> list = new List<string> ();
			if (userDns != null && userDns.Count > 0) {
				foreach (string userDn in userDns) {
					Add (userDn);
				}
			}
			Add (gateway);
			Add ("8.8.8.8");
			Add ("1.1.1.1");
			return list;
			void Add (string ip)
			{
				string text = ip.Trim ();
				if (text.Length != 0 && Ipv4PoolSettings.TryParseIpv4 (text, out var _) && seen.Add (text)) {
					list.Add (text);
				}
			}
		}

		private static string SanitizeHostname (string name)
		{
			string text = name.Trim ().ToLowerInvariant ();
			if (text.Length == 0) {
				return "ubuntu-vm";
			}
			StringBuilder stringBuilder = new StringBuilder ();
			string text2 = text;
			foreach (char c in text2) {
				if (char.IsLetterOrDigit (c) || c == '-') {
					stringBuilder.Append (c);
				}
			}
			string text3 = stringBuilder.ToString ().Trim ('-');
			return (text3.Length > 0) ? text3 : "ubuntu-vm";
		}

		private static string YamlQuote (string s)
		{
			return "'" + s.Replace ("'", "''") + "'";
		}

		private static string ShellSingleQuote (string s)
		{
			return "'" + s.Replace ("'", "'\"'\"'") + "'";
		}
	}

