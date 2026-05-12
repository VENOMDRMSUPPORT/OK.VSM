namespace HyperVMManager.Services;

public sealed class PreparedOsTemplate
{
    public required string TemplatePath { get; init; }

    public required string DiskFormat { get; init; }
}
