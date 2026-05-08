namespace HyperVMManager.Services;

public sealed class ExtractedOsDisk
{
    public required string DiskPath { get; init; }

    public required string DiskFormat { get; init; }
}
