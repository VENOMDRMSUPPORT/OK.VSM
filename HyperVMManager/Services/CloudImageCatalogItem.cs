using System;

namespace HyperVMManager.Services;

public sealed class CloudImageCatalogItem
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required Uri ArchiveUri { get; init; }

    public required Uri Sha256SumsUri { get; init; }

    public required string ArchiveFileName { get; init; }

    public required string FinalDiskExtension { get; init; }
}
