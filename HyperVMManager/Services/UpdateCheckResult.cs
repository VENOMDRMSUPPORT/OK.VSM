namespace HyperVMManager.Services;

public sealed class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; init; }

    public UpdateManifest? Manifest { get; init; }

    public string? Message { get; init; }
}
