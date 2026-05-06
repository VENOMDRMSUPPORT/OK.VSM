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

	public partial class SemiCircularGauge : UserControl
	{
		public static readonly DependencyProperty TitleProperty = DependencyProperty.Register ("Title", typeof(string), typeof(SemiCircularGauge), new PropertyMetadata ("", OnVisualChanged));

		public static readonly DependencyProperty PercentProperty = DependencyProperty.Register ("Percent", typeof(double), typeof(SemiCircularGauge), new PropertyMetadata (0.0, OnVisualChanged));

		public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register ("Caption", typeof(string), typeof(SemiCircularGauge), new PropertyMetadata ("", OnVisualChanged));

		public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register ("AccentBrush", typeof(Brush), typeof(SemiCircularGauge), new PropertyMetadata (new SolidColorBrush (Color.FromRgb (88, 166, byte.MaxValue)), OnVisualChanged));

		public string Title {
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

		public Brush AccentBrush {
			get {
				return (Brush)GetValue (AccentBrushProperty);
			}
			set {
				SetValue (AccentBrushProperty, value);
			}
		}

		public SemiCircularGauge ()
		{
			base.Focusable = false;
			InitializeComponent ();
			base.Loaded += delegate {
				Redraw ();
			};
		}

		private static void OnVisualChanged (DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is SemiCircularGauge semiCircularGauge) {
				semiCircularGauge.Redraw ();
			}
		}

		private void Redraw ()
		{
			TitleText.Text = Title;
			TitleText.Visibility = (string.IsNullOrWhiteSpace (Title) ? Visibility.Collapsed : Visibility.Visible);
			CaptionText.Text = Caption ?? "";
			double num = (double.IsNaN (Percent) ? 0.0 : Math.Max (0.0, Math.Min (100.0, Percent)));
			PercentText.Text = $"{num:F0}%";
			ValuePath.Stroke = AccentBrush;
			TrackPath.Data = BuildArcGeometry (46.0, 48.0, 32.0, 1.0);
			ValuePath.Data = BuildArcGeometry (46.0, 48.0, 32.0, num / 100.0);
		}

		private static Geometry BuildArcGeometry (double cx, double cy, double r, double p)
		{
			p = Math.Max (0.0, Math.Min (1.0, p));
			if (p <= 0.0001) {
				return Geometry.Empty;
			}
			Point startPoint = DegToPoint (cx, cy, r, 180.0);
			Point point = DegToPoint (cx, cy, r, 180.0 * (1.0 - p));
			double num = 180.0 * p;
			bool isLargeArc = num > 180.0;
			PathFigure pathFigure = new PathFigure {
				StartPoint = startPoint,
				IsClosed = false
			};
			pathFigure.Segments.Add (new ArcSegment (point, new Size (r, r), 0.0, isLargeArc, SweepDirection.Counterclockwise, isStroked: true));
			return new PathGeometry (new PathFigure[1] { pathFigure });
		}

		private static Point DegToPoint (double cx, double cy, double r, double deg)
		{
			double num = deg * Math.PI / 180.0;
			return new Point (cx + r * Math.Cos (num), cy - r * Math.Sin (num));
		}


	}

