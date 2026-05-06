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

namespace HyperVMManager.ViewModels;

	public class RelayCommand : ICommand
	{
		private readonly Func<Task> _execute;

		private bool _isExecuting;

		public event EventHandler? CanExecuteChanged {
			add {
				CommandManager.RequerySuggested += value;
			}
			remove {
				CommandManager.RequerySuggested -= value;
			}
		}

		public RelayCommand (Func<Task> execute)
		{
			_execute = execute;
		}

		public bool CanExecute (object? parameter)
		{
			return !_isExecuting;
		}

		public async void Execute (object? parameter)
		{
			if (_isExecuting) {
				return;
			}
			_isExecuting = true;
			CommandManager.InvalidateRequerySuggested ();
			try {
				await _execute ();
			} finally {
				_isExecuting = false;
				CommandManager.InvalidateRequerySuggested ();
			}
		}
	}
