namespace HyperVMManager.Services;

public sealed class VerifiedCachedArchive
{
    public required CloudImageCatalogItem Image { get; init; }

    public required string ArchivePath { get; init; }

    public required string Sha256 { get; init; }
}
