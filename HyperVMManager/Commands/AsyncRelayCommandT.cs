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

namespace HyperVMManager.Commands;

	public sealed class AsyncRelayCommand<T> : ICommand where T : class
	{
		private readonly Func<T?, Task> _execute;

		private readonly Predicate<T?>? _canExecute;

		private bool _busy;

		public event EventHandler? CanExecuteChanged {
			add {
				CommandManager.RequerySuggested += value;
			}
			remove {
				CommandManager.RequerySuggested -= value;
			}
		}

		public AsyncRelayCommand (Func<T?, Task> execute, Predicate<T?>? canExecute = null)
		{
			_execute = execute;
			_canExecute = canExecute;
		}

		public bool CanExecute (object? parameter)
		{
			if (_busy) {
				return false;
			}
			return parameter is T obj && (_canExecute?.Invoke (obj) ?? true);
		}

		public async void Execute (object? parameter)
		{
			if (!(parameter is T t)) {
				return;
			}
			_busy = true;
			CommandManager.InvalidateRequerySuggested ();
			try {
				await _execute (t);
			} finally {
				_busy = false;
				CommandManager.InvalidateRequerySuggested ();
			}
		}
	}
