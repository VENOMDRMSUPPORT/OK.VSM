using System;
using System.Collections.Generic;
using System.Linq;

namespace HyperVMManager.Services;

public static class CloudImageCatalog
{
    public const string Ubuntu2204AzureId = "ubuntu-22.04-server-cloudimg-amd64-azure";
    public const string Ubuntu2404AzureId = "ubuntu-24.04-server-cloudimg-amd64-azure";

    private static readonly IReadOnlyList<CloudImageCatalogItem> Items = new[]
    {
        new CloudImageCatalogItem
        {
            Id = Ubuntu2204AzureId,
            DisplayName = "Ubuntu 22.04 LTS cloud image",
            ArchiveFileName = "ubuntu-22.04-server-cloudimg-amd64-azure.vhd.tar.gz",
            ArchiveUri = new Uri("https://cloud-images.ubuntu.com/releases/jammy/release/ubuntu-22.04-server-cloudimg-amd64-azure.vhd.tar.gz"),
            Sha256SumsUri = new Uri("https://cloud-images.ubuntu.com/releases/jammy/release/SHA256SUMS"),
            FinalDiskExtension = ".vhdx"
        },
        new CloudImageCatalogItem
        {
            Id = Ubuntu2404AzureId,
            DisplayName = "Ubuntu 24.04 LTS cloud image",
            ArchiveFileName = "ubuntu-24.04-server-cloudimg-amd64-azure.vhd.tar.gz",
            ArchiveUri = new Uri("https://cloud-images.ubuntu.com/releases/server/24.04/release/ubuntu-24.04-server-cloudimg-amd64-azure.vhd.tar.gz"),
            Sha256SumsUri = new Uri("https://cloud-images.ubuntu.com/releases/server/24.04/release/SHA256SUMS"),
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
