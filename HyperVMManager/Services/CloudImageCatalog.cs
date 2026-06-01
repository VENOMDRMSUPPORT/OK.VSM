using System;
using System.Collections.Generic;
using System.Linq;

namespace HyperVMManager.Services;

public static class CloudImageCatalog
{
    public const string Ubuntu2204AzureId = "ubuntu-22.04-server-cloudimg-amd64-azure";
    public const string Ubuntu2404AzureId = "ubuntu-24.04-server-cloudimg-amd64-azure";

    // GitHub releases base URL - update this with your actual repo
    private const string GitHubReleasesBaseUrl = "https://github.com/VENOMDRMSUPPORT/OK.VSM/releases/download/cloud-images";

    private static readonly IReadOnlyList<CloudImageCatalogItem> Items = new[]
    {
        new CloudImageCatalogItem
        {
            Id = Ubuntu2204AzureId,
            DisplayName = "Ubuntu 22.04 LTS cloud image",
            ArchiveFileName = "ubuntu-22.04-server-cloudimg-amd64-azure.vhd.tar.gz",
            ArchiveUri = new Uri($"{GitHubReleasesBaseUrl}/ubuntu-22.04-server-cloudimg-amd64-azure.vhd.tar.gz"),
            Sha256SumsUri = new Uri($"{GitHubReleasesBaseUrl}/SHA256SUMS-22.04"),
            FinalDiskExtension = ".vhdx"
        },
        new CloudImageCatalogItem
        {
            Id = Ubuntu2404AzureId,
            DisplayName = "Ubuntu 24.04 LTS cloud image",
            ArchiveFileName = "ubuntu-24.04-server-cloudimg-amd64-azure.vhd.tar.gz",
            ArchiveUri = new Uri($"{GitHubReleasesBaseUrl}/ubuntu-24.04-server-cloudimg-amd64-azure.vhd.tar.gz"),
            Sha256SumsUri = new Uri($"{GitHubReleasesBaseUrl}/SHA256SUMS-24.04"),
            FinalDiskExtension = ".vhdx"
        }
    };

    public static IReadOnlyList<CloudImageCatalogItem> List() => Items;

    public static CloudImageCatalogItem GetById(string id)
    {
        return Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? Items[0];
    }
}
