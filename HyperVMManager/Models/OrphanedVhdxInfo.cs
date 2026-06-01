using System;

namespace HyperVMManager.Models;

/// <summary>
/// Represents a VHDX file on disk that is not linked to any registered Hyper-V VM.
/// </summary>
public class OrphanedVhdxInfo
{
	public string FolderName { get; set; } = "";
	public string VhdxPath { get; set; } = "";
	public long SizeBytes { get; set; }
	public string SizeDisplay { get; set; } = "";
	public string ActualSizeDisplay { get; set; } = "";
}
