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

	public partial class DeleteVmDialog : Window
	{


		public DeleteVmDialog (string vmName)
		{
			InitializeComponent ();
			VmNameRun.Text = vmName;
		}

		public static bool Show (Window owner, string vmName)
		{
			DeleteVmDialog deleteVmDialog = new DeleteVmDialog (vmName) {
				Owner = owner
			};
			return deleteVmDialog.ShowDialog () == true;
		}

		private void YesButton_Click (object sender, RoutedEventArgs e)
		{
			base.DialogResult = true;
		}

		private void NoButton_Click (object sender, RoutedEventArgs e)
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

