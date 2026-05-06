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

	public static class UbuntuInstallProfileExtensions
	{
		public static string DisplayName (this UbuntuInstallProfile p)
		{
			if (1 == 0) {
			}
			string result = p switch {
				UbuntuInstallProfile.Ubuntu2204Lts => "Ubuntu 22.04 LTS", 
				UbuntuInstallProfile.Ubuntu2404Lts => "Ubuntu 24.04 LTS", 
				_ => "Linux", 
			};
			if (1 == 0) {
			}
			return result;
		}

		public static string NotesSuffix (this UbuntuInstallProfile p)
		{
			if (1 == 0) {
			}
			string result = p switch {
				UbuntuInstallProfile.Ubuntu2204Lts => "Install profile: Ubuntu 22.04 LTS — use the official 22.04 server ISO.", 
				UbuntuInstallProfile.Ubuntu2404Lts => "Install profile: Ubuntu 24.04 LTS — use the official 24.04 server ISO.", 
				_ => "", 
			};
			if (1 == 0) {
			}
			return result;
		}

		public static string NotesSuffixCloud (this UbuntuInstallProfile p)
		{
			if (1 == 0) {
			}
			string result = p switch {
				UbuntuInstallProfile.Ubuntu2204Lts => "Unattended: Ubuntu 22.04 cloud image + cloud-init (NoCloud).", 
				UbuntuInstallProfile.Ubuntu2404Lts => "Unattended: Ubuntu 24.04 cloud image + cloud-init (NoCloud).", 
				_ => "Unattended cloud-init.", 
			};
			if (1 == 0) {
			}
			return result;
		}
	}
