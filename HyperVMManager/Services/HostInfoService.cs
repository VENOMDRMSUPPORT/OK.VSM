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

	public static class HostInfoService
	{
		public static HostResourceSnapshot GetSnapshot ()
		{
			int num = Environment.ProcessorCount;
			ulong num2 = 0uL;
			ulong num3 = 0uL;
			try {
				ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher ("SELECT NumberOfLogicalProcessors FROM Win32_ComputerSystem");
				try {
					using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectSearcher.Get ().GetEnumerator ();
					if (managementObjectEnumerator.MoveNext ()) {
						ManagementObject managementObject = (ManagementObject)managementObjectEnumerator.Current;
						num = Convert.ToInt32 (managementObject ["NumberOfLogicalProcessors"]);
					}
				} finally {
					((IDisposable)managementObjectSearcher)?.Dispose ();
				}
			} catch {
			}
			try {
				ManagementObjectSearcher managementObjectSearcher2 = new ManagementObjectSearcher ("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
				try {
					using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator2 = managementObjectSearcher2.Get ().GetEnumerator ();
					if (managementObjectEnumerator2.MoveNext ()) {
						ManagementObject managementObject2 = (ManagementObject)managementObjectEnumerator2.Current;
						ulong num4 = Convert.ToUInt64 (managementObject2 ["TotalVisibleMemorySize"]);
						num2 = num4 * 1024;
					}
				} finally {
					((IDisposable)managementObjectSearcher2)?.Dispose ();
				}
			} catch {
			}
			try {
				ManagementObjectSearcher managementObjectSearcher3 = new ManagementObjectSearcher ("SELECT Size FROM Win32_LogicalDisk WHERE DriveType=3");
				try {
					foreach (ManagementObject item in managementObjectSearcher3.Get ()) {
						object obj3 = item ["Size"];
						if (obj3 != null) {
							num3 += Convert.ToUInt64 (obj3);
						}
					}
				} finally {
					((IDisposable)managementObjectSearcher3)?.Dispose ();
				}
			} catch {
			}
			if (num < 1) {
				num = 1;
			}
			if (num2 == 0) {
				num2 = 8589934592uL;
			}
			if (num3 == 0) {
				num3 = 549755813888uL;
			}
			return new HostResourceSnapshot {
				LogicalProcessors = num,
				TotalMemoryBytes = num2,
				TotalFixedDiskBytes = num3
			};
		}
	}
