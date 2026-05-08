namespace HyperVMManager.Models;

public static class UbuntuInstallProfileExtensions
{
	public static string DisplayName(this UbuntuInstallProfile p)
	{
		return p switch
		{
			UbuntuInstallProfile.Ubuntu2204Lts => "Ubuntu 22.04 LTS",
			UbuntuInstallProfile.Ubuntu2404Lts => "Ubuntu 24.04 LTS",
			_ => "Linux",
		};
	}

	public static string NotesSuffix(this UbuntuInstallProfile p)
	{
		return p switch
		{
			UbuntuInstallProfile.Ubuntu2204Lts => "Install profile: Ubuntu 22.04 LTS cloud image.",
			UbuntuInstallProfile.Ubuntu2404Lts => "Install profile: Ubuntu 24.04 LTS cloud image.",
			_ => "",
		};
	}

	public static string NotesSuffixCloud(this UbuntuInstallProfile p)
	{
		return p switch
		{
			UbuntuInstallProfile.Ubuntu2204Lts => "Unattended: Ubuntu 22.04 cloud image + cloud-init (NoCloud).",
			UbuntuInstallProfile.Ubuntu2404Lts => "Unattended: Ubuntu 24.04 cloud image + cloud-init (NoCloud).",
			_ => "Unattended cloud-init.",
		};
	}
}
