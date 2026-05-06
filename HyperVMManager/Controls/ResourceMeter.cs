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

namespace HyperVMManager.Controls;

	public partial class ResourceMeter : UserControl
	{
		public static readonly DependencyProperty TitleProperty = DependencyProperty.Register ("Title", typeof(string), typeof(ResourceMeter), new PropertyMetadata ("", OnDpChanged));

		public static readonly DependencyProperty PercentProperty = DependencyProperty.Register ("Percent", typeof(double), typeof(ResourceMeter), new PropertyMetadata (0.0, OnDpChanged));

		public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register ("Caption", typeof(string), typeof(ResourceMeter), new PropertyMetadata ("", OnDpChanged));

		public static readonly DependencyProperty MeterForegroundProperty = DependencyProperty.Register ("MeterForeground", typeof(Brush), typeof(ResourceMeter), new PropertyMetadata (new SolidColorBrush (Color.FromRgb (88, 166, byte.MaxValue)), OnDpChanged));

		public static readonly DependencyProperty IsAlertProperty = DependencyProperty.Register ("IsAlert", typeof(bool), typeof(ResourceMeter), new PropertyMetadata (false, OnDpChanged));

		private static readonly SolidColorBrush AlertFillBrush = new SolidColorBrush (Color.FromRgb (248, 81, 73));public string Title {
			get {
				return (string)GetValue (TitleProperty);
			}
			set {
				SetValue (TitleProperty, value);
			}
		}

		public double Percent {
			get {
				return (double)GetValue (PercentProperty);
			}
			set {
				SetValue (PercentProperty, value);
			}
		}

		public string Caption {
			get {
				return (string)GetValue (CaptionProperty);
			}
			set {
				SetValue (CaptionProperty, value);
			}
		}

		public Brush MeterForeground {
			get {
				return (Brush)GetValue (MeterForegroundProperty);
			}
			set {
				SetValue (MeterForegroundProperty, value);
			}
		}

		public bool IsAlert {
			get {
				return (bool)GetValue (IsAlertProperty);
			}
			set {
				SetValue (IsAlertProperty, value);
			}
		}

		public ResourceMeter ()
		{
			base.Focusable = false;
			InitializeComponent ();
			base.Loaded += delegate {
				Refresh ();
			};
		}

		private static void OnDpChanged (DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is ResourceMeter resourceMeter) {
				resourceMeter.Refresh ();
			}
		}

		private void Refresh ()
		{
			double value = (double.IsNaN (Percent) ? 0.0 : Math.Max (0.0, Math.Min (100.0, Percent)));
			PercentLabel.Text = $"{value:F0}%";
			CaptionLabel.Text = (string.IsNullOrWhiteSpace (Caption) ? "" : Caption);
			MeterBar.Value = value;
			MeterBar.Foreground = (IsAlert ? AlertFillBrush : MeterForeground);
		}


	}

