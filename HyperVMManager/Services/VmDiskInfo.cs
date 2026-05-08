namespace HyperVMManager.Services;

public sealed class VmDiskInfo
{
    public string OsVhdPath { get; init; } = "";

    public string SeedVhdPath { get; init; } = "";

    public string OsVhdActualSize { get; init; } = "";

    public string SeedVhdActualSize { get; init; } = "";

    public string AllDiskPathsDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SeedVhdPath))
            {
                return OsVhdPath;
            }

            return OsVhdPath + "\n" + SeedVhdPath;
        }
    }
}
