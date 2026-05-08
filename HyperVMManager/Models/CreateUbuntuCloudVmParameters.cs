namespace HyperVMManager.Models;

public sealed class CreateUbuntuCloudVmParameters
{
	public required string VmName { get; init; }

	public required string SwitchName { get; init; }

	public required string CloudImageId { get; init; }

	public required string OsVhdFullPath { get; init; }

	public required string SeedVhdFullPath { get; init; }

	public string VmDirectory { get; init; } = "";

	public long MemoryStartupBytes { get; init; }

	public long MemoryMinimumBytes { get; init; }

	public long MemoryMaximumBytes { get; init; }

	public int ProcessorCount { get; init; }

	public ulong OsDiskSizeBytes { get; init; }

	public required string AdminUsername { get; init; }

	public required string AdminPassword { get; init; }

	public string Hostname { get; init; } = "";

	public UbuntuInstallProfile Profile { get; init; }

	public required string Notes { get; init; }

	public string StaticGuestIpv4 { get; init; } = "";

	public string DefaultGateway { get; init; } = "";

	public int PrefixLength { get; init; } = 24;

	public IReadOnlyList<string> DnsServers { get; init; } = Array.Empty<string>();
}
