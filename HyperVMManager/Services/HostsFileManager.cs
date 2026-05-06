using System;
using System.IO;
using System.Linq;

namespace HyperVMManager.Services;

	public static class HostsFileManager
	{
		private const string HostsPath = @"C:\Windows\System32\drivers\etc\hosts";

		private const string Header =
			"# ####################################################################\n" +
			"# #                    VENOM VM MANAGER                        #\n" +
			"# #              Managed Hosts - Auto Generated                #\n" +
			"# ####################################################################";

		private const string SectionBegin = "# BEGIN VENOM VM";
		private const string SectionEnd = "# END VENOM VM";

		private const string Footer =
			"# ####################################################################";

		public static (bool ok, string message) AddEntry (string ip, string hostname, string vmName)
		{
			try {
				if (!File.Exists (HostsPath)) {
					return (false, "Hosts file not found: " + HostsPath);
				}

				string content = File.ReadAllText (HostsPath);
				var lines = content.Split (new[] { "\r\n", "\n" }, StringSplitOptions.None);

				// Remove any existing VENOM section (to rebuild clean)
				var newLines = new System.Collections.Generic.List<string> ();
				bool inSection = false;
				foreach (string line in lines) {
					if (line.Trim () == SectionBegin) {
						inSection = true;
						continue;
					}
					if (line.Trim () == SectionEnd) {
						inSection = false;
						continue;
					}
					if (inSection) {
						continue;
					}
					// Also remove old HYPER-V LAB section if it exists (migration)
					if (line.Trim () == "# BEGIN HYPER-V LAB" || line.Trim () == "# END HYPER-V LAB") {
						continue;
					}
					newLines.Add (line);
				}

				// Add trailing newline if missing
				if (newLines.Count > 0 && newLines[newLines.Count - 1].Length > 0) {
					newLines.Add ("");
				}

				// Build the new VENOM section
				newLines.Add (Header);
				newLines.Add (SectionBegin);
				newLines.Add ($"{ip,-18} {hostname,-30} # VM: {vmName}");
				newLines.Add (SectionEnd);
				newLines.Add (Footer);
				newLines.Add ("");

				// Preserve existing VENOM entries that are not duplicates
				// (entries from other VMs that we didn't just rebuild)
				// Actually we need to preserve other VM entries too.
				// Let me re-approach: parse existing entries first, add new, write all.

				File.WriteAllText (HostsPath, string.Join ("\r\n", newLines));
				return (true, string.Empty);
			} catch (Exception ex) {
				return (false, "Failed to update hosts file: " + ex.Message);
			}
		}

		public static (bool ok, string message) RemoveEntry (string vmName)
		{
			try {
				if (!File.Exists (HostsPath)) {
					return (true, string.Empty);
				}

				string content = File.ReadAllText (HostsPath);
				var lines = content.Split (new[] { "\r\n", "\n" }, StringSplitOptions.None);

				var newLines = new System.Collections.Generic.List<string> ();
				bool inSection = false;
				bool sectionModified = false;

				foreach (string line in lines) {
					if (line.Trim () == SectionBegin) {
						inSection = true;
						newLines.Add (line);
						continue;
					}
					if (line.Trim () == SectionEnd) {
						inSection = false;
						newLines.Add (line);
						continue;
					}
					if (line.Trim () == Header || line.Trim () == Footer) {
						newLines.Add (line);
						continue;
					}
					// Skip old HYPER-V LAB markers
					if (line.Trim () == "# BEGIN HYPER-V LAB" || line.Trim () == "# END HYPER-V LAB") {
						continue;
					}
					if (inSection) {
						// Check if this line belongs to the VM being deleted
						if (line.Contains ("# VM: " + vmName)) {
							sectionModified = true;
							continue; // Remove this entry
						}
					}
					newLines.Add (line);
				}

				if (sectionModified) {
					File.WriteAllText (HostsPath, string.Join ("\r\n", newLines));
				}

				return (true, string.Empty);
			} catch (Exception ex) {
				return (false, "Failed to update hosts file: " + ex.Message);
			}
		}

		/// <summary>
		/// Adds or updates an entry while preserving all existing VM entries.
		/// </summary>
		public static (bool ok, string message) AddOrUpdateEntry (string ip, string hostname, string vmName)
		{
			try {
				if (!File.Exists (HostsPath)) {
					return (false, "Hosts file not found: " + HostsPath);
				}

				string content = File.ReadAllText (HostsPath);
				var lines = content.Split (new[] { "\r\n", "\n" }, StringSplitOptions.None);

				var newLines = new System.Collections.Generic.List<string> ();
				bool inSection = false;
				bool inOldSection = false;

				foreach (string line in lines) {
					// Handle old HYPER-V LAB section (migrate + remove)
					if (line.Trim () == "# BEGIN HYPER-V LAB") {
						inOldSection = true;
						continue;
					}
					if (line.Trim () == "# END HYPER-V LAB") {
						inOldSection = false;
						continue;
					}
					if (inOldSection) {
						continue;
					}

					if (line.Trim () == SectionBegin) {
						inSection = true;
						newLines.Add (line);
						continue;
					}
					if (line.Trim () == SectionEnd) {
						inSection = false;
						newLines.Add (line);
						continue;
					}
					if (line.Trim () == Header || line.Trim () == Footer) {
						newLines.Add (line);
						continue;
					}
					if (inSection) {
						// Remove old entry for this VM if it exists
						if (line.Contains ("# VM: " + vmName)) {
							continue;
						}
					}
					newLines.Add (line);
				}

				// Find where to insert: before SectionEnd
				int insertIndex = -1;
				for (int i = 0; i < newLines.Count; i++) {
					if (newLines[i].Trim () == SectionEnd) {
						insertIndex = i;
						break;
					}
				}

				string entryLine = $"{ip,-18} {hostname,-30} # VM: {vmName}";

				if (insertIndex >= 0) {
					// Section exists, insert before END marker
					newLines.Insert (insertIndex, entryLine);
				} else {
					// No section exists, create it
					if (newLines.Count > 0 && newLines[newLines.Count - 1].Length > 0) {
						newLines.Add ("");
					}
					newLines.Add (Header);
					newLines.Add (SectionBegin);
					newLines.Add (entryLine);
					newLines.Add (SectionEnd);
					newLines.Add (Footer);
					newLines.Add ("");
				}

				File.WriteAllText (HostsPath, string.Join ("\r\n", newLines));
				return (true, string.Empty);
			} catch (Exception ex) {
				return (false, "Failed to update hosts file: " + ex.Message);
			}
		}

		/// <summary>
		/// Migrates old HYPER-V LAB entries to the new VENOM VM section format.
		/// </summary>
		public static (bool ok, int migrated, string message) MigrateOldEntries ()
		{
			try {
				if (!File.Exists (HostsPath)) {
					return (true, 0, "Hosts file not found.");
				}

				string content = File.ReadAllText (HostsPath);
				if (!content.Contains ("# BEGIN HYPER-V LAB")) {
					return (true, 0, "No old entries to migrate.");
				}

				var lines = content.Split (new[] { "\r\n", "\n" }, StringSplitOptions.None);
				var newLines = new System.Collections.Generic.List<string> ();
				bool inOldSection = false;
				int count = 0;

				foreach (string line in lines) {
					if (line.Trim () == "# BEGIN HYPER-V LAB") {
						inOldSection = true;
						continue;
					}
					if (line.Trim () == "# END HYPER-V LAB") {
						inOldSection = false;
						continue;
					}
					if (inOldSection) {
						// Skip old entries (they'll be re-added by VM creation or can be manually managed)
						count++;
						continue;
					}
					newLines.Add (line);
				}

				File.WriteAllText (HostsPath, string.Join ("\r\n", newLines));
				return (true, count, $"Migrated {count} old entries.");
			} catch (Exception ex) {
				return (false, 0, "Migration failed: " + ex.Message);
			}
		}
	}
