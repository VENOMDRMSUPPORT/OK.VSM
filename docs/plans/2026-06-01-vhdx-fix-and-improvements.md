# VENOM VM-WARE — VHDX Fix & Improvements Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Fix existing VMs not showing in app, convert Fixed VHDX to Dynamic, and add source code improvements for better disk management.

**Architecture:** Phase 1 fixes the immediate problem (Hyper-V state). Phase 2 improves the source code. Phase 3 builds and deploys.

**Tech Stack:** C# / .NET 8 / WPF / Hyper-V PowerShell / Inno Setup

---

## Phase 1: Fix Current Problem (Admin Required) ⚠️

### Task 1.1: Convert "Main Server" VHDX from Fixed → Dynamic

**Objective:** Reclaim ~110 GB of disk space by converting the Fixed-size VHDX to Dynamic.

**Prerequisites:** Run terminal as Administrator

**Step 1: Verify current VHDX type**

```powershell
Get-VHD -Path "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\Main Server\Main Server.vhdx"
```

Expected: `VhdType: Fixed` and `FileSize ≈ 113 GB`

**Step 2: Convert to Dynamic**

```powershell
# This creates a NEW dynamic VHDX and copies data from the fixed one
Convert-VHD -Path "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\Main Server\Main Server.vhdx" `
             -DestinationPath "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\Main Server\Main Server-dynamic.vhdx" `
             -VHDType Dynamic
```

**Step 3: Replace old file with new**

```powershell
# Backup old file
Rename-Item "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\Main Server\Main Server.vhdx" "Main Server-fixed-backup.vhdx"

# Rename dynamic to original name
Rename-Item "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\Main Server\Main Server-dynamic.vhdx" "Main Server.vhdx"
```

**Step 4: Verify conversion**

```powershell
Get-VHD -Path "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\Main Server\Main Server.vhdx"
```

Expected: `VhdType: Dynamic` and `FileSize ≈ 1-2 GB` (actual used space)

**Step 5: Delete backup after confirmation**

```powershell
Remove-Item "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\Main Server\Main Server-fixed-backup.vhdx"
```

**Verification:** F: drive should show ~110 GB more free space.

---

### Task 1.2: Re-register VMs in Hyper-V

**Objective:** Create Hyper-V VMs pointing to existing VHDX files so they appear in the app.

**Step 1: Create "Main Server" VM (import existing config)**

```powershell
# The Main Server folder has VM config files (.vmcx)
# Try import first
$vmConfigPath = "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\Main Server\Main Server\Virtual Machines"
Import-VM -Path "$vmConfigPath\E3C8573C-AAE6-47A5-9CF6-BD03D2C81BD8.vmcx" -Copy -GenerateNewId
```

If import fails, create new VM:

```powershell
# Create new VM with existing VHDX
New-VM -Name "Main Server" `
       -MemoryStartupBytes 4GB `
       -Generation 2 `
       -VHDPath "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\Main Server\Main Server.vhdx" `
       -SwitchName "VENOM-External"

# Configure dynamic memory
Set-VMMemory -VMName "Main Server" -DynamicMemoryEnabled $true `
             -MinimumBytes 512MB -StartupBytes 4GB -MaximumBytes 8GB

# Set processor count
Set-VMProcessor -VMName "Main Server" -Count 2

# Disable auto checkpoints
Set-VM -VMName "Main Server" -AutomaticCheckpointsEnabled $false
```

**Step 2: Create "ubuntu24-new" VM**

```powershell
New-VM -Name "ubuntu24-new" `
       -MemoryStartupBytes 2GB `
       -Generation 2 `
       -VHDPath "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\ubuntu24-new\ubuntu24-new.vhdx" `
       -SwitchName "VENOM-External"

Set-VMMemory -VMName "ubuntu24-new" -DynamicMemoryEnabled $true `
             -MinimumBytes 512MB -StartupBytes 2GB -MaximumBytes 4GB

Set-VMProcessor -VMName "ubuntu24-new" -Count 2
Set-VM -VMName "ubuntu24-new" -AutomaticCheckpointsEnabled $false
```

**Step 3: Create "venomgpt" VM**

```powershell
New-VM -Name "venomgpt" `
       -MemoryStartupBytes 2GB `
       -Generation 2 `
       -VHDPath "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\venomgpt\venomgpt.vhdx" `
       -SwitchName "VENOM-External"

Set-VMMemory -VMName "venomgpt" -DynamicMemoryEnabled $true `
             -MinimumBytes 512MB -StartupBytes 2GB -MaximumBytes 4GB

Set-VMProcessor -VMName "venomgpt" -Count 2
Set-VM -VMName "venomgpt" -AutomaticCheckpointsEnabled $false
```

**Step 4: Verify all VMs are registered**

```powershell
Get-VM | Select-Object Name, State, CPUUsage, MemoryAssigned, MemoryStartup, MemoryMinimum, MemoryMaximum | Format-Table -AutoSize
```

Expected: 3 VMs listed (Main Server, ubuntu24-new, venomgpt), all Off state.

**Step 5: Open VENOM VM-WARE app and verify VMs appear**

Launch: `F:\VENOM VM-WARE\VENOM VM-WARE\HyperVMManager.exe`

Expected: All 3 VMs visible in the list.

---

## Phase 2: Source Code Improvements

### Task 2.1: Ensure Dynamic VHDX Creation

**Objective:** Verify and fix that all VHDX creation paths use Dynamic (not Fixed) allocation.

**Files to check:**
- `G:\VENOM-VM\HyperVMManager\Services\VmControlService.cs` — `CreateUbuntuCloudVm` method
- `G:\VENOM-VM\HyperVMManager\Services\VmControlService.cs` — `CreateUbuntuInstallVm` method

**Current status:**
- `CreateUbuntuCloudVm` uses `New-VHD -Differencing` → inherently Dynamic ✅
- `CreateUbuntuInstallVm` uses `New-VM -NewVHDPath -NewVHDSizeBytes` → defaults to Dynamic ✅

**Action:** Verify no code path creates Fixed VHDX. Add explicit `-Type Dynamic` where possible.

**Step 1: Search for any Fixed VHD creation**

```bash
grep -n "VHDType\|Fixed\|Dynamic" G:/VENOM-VM/HyperVMManager/Services/VmControlService.cs
```

**Step 2: If no explicit type set, add `-Type Dynamic` to New-VHD calls**

In `CreateUbuntuCloudVm`, the differencing disk is already dynamic. But add a comment for clarity:

```csharp
// Differencing disks are inherently dynamic/thin-provisioned
New-VHD -Path {text} -ParentPath {text3} -Differencing | Out-Null
```

**Step 3: Build and verify**

```bash
dotnet build G:\VENOM-VM\HyperVMManager\HyperVMManager.csproj -c Release
```

---

### Task 2.2: Add Orphaned VHDX Detection

**Objective:** Scan the vhdx folder for VHDX files that aren't linked to any Hyper-V VM, and show them in the UI with an option to create VMs from them.

**Files to modify:**
- `G:\VENOM-VM\HyperVMManager\Services\HyperVService.cs` — add orphan detection logic
- `G:\VENOM-VM\HyperVMManager\ViewModels\MainViewModel.cs` — add orphaned VMs to UI
- `G:\VENOM-VM\HyperVMManager\MainWindow.xaml` — add orphaned section in UI

**Step 1: Add `ScanOrphanedVhdx` method to `HyperVService.cs`**

```csharp
/// <summary>
/// Scans the VM storage folder for VHDX files not linked to any Hyper-V VM.
/// Returns list of (folderName, vhdxPath, sizeBytes) for orphaned disks.
/// </summary>
public static List<(string name, string vhdxPath, long sizeBytes)> ScanOrphanedVhdx(
    string vmStorageRoot, 
    IReadOnlyList<string> registeredVmNames)
{
    var orphans = new List<(string name, string path, long size)>();
    if (string.IsNullOrWhiteSpace(vmStorageRoot) || !Directory.Exists(vmStorageRoot))
        return orphans;

    var registeredSet = new HashSet<string>(registeredVmNames, StringComparer.OrdinalIgnoreCase);

    foreach (var dir in Directory.GetDirectories(vmStorageRoot))
    {
        string folderName = Path.GetFileName(dir);
        if (registeredSet.Contains(folderName))
            continue;

        // Look for VHDX files in this folder
        var vhdxFiles = Directory.GetFiles(dir, "*.vhdx", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).Contains("cidata-seed", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (vhdxFiles.Length > 0)
        {
            var fileInfo = new FileInfo(vhdxFiles[0]);
            orphans.Add((folderName, vhdxFiles[0], fileInfo.Length));
        }
    }

    return orphans;
}
```

**Step 2: Add orphaned VMs property to `MainViewModel.cs`**

```csharp
private ObservableCollection<OrphanedVhdxInfo> _orphanedVhdx = new();
public ObservableCollection<OrphanedVhdxInfo> OrphanedVhdx
{
    get => _orphanedVhdx;
    set { _orphanedVhdx = value; OnPropertyChanged(nameof(OrphanedVhdx)); }
}

public bool HasOrphanedVhdx => _orphanedVhdx.Count > 0;
```

**Step 3: Add `OrphanedVhdxInfo` model**

Create: `G:\VENOM-VM\HyperVMManager\Models\OrphanedVhdxInfo.cs`

```csharp
namespace HyperVMManager.Models;

public class OrphanedVhdxInfo
{
    public string FolderName { get; set; } = "";
    public string VhdxPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string SizeDisplay { get; set; } = "";
}
```

**Step 4: Add orphan scan to refresh logic in `MainViewModel.cs`**

In the refresh method, after loading VMs:

```csharp
// Scan for orphaned VHDX files
string vmPath = ResolveVmStoragePath();
var registeredNames = VirtualMachines.Select(vm => vm.Name).ToList();
var orphans = HyperVService.ScanOrphanedVhdx(vmPath, registeredNames);

OrphanedVhdx.Clear();
foreach (var orphan in orphans)
{
    OrphanedVhdx.Add(new OrphanedVhdxInfo
    {
        FolderName = orphan.name,
        VhdxPath = orphan.vhdxPath,
        SizeBytes = orphan.sizeBytes,
        SizeDisplay = FormatSize(orphan.sizeBytes)
    });
}
OnPropertyChanged(nameof(HasOrphanedVhdx));
```

**Step 5: Add orphaned section to `MainWindow.xaml`**

Add a section below the VM list showing orphaned VHDX files with a "Create VM" button for each.

**Step 6: Build and verify**

```bash
dotnet build G:\VENOM-VM\HyperVMManager\HyperVMManager.csproj -c Release
```

---

### Task 2.3: Add VHDX Type Info & Convert Option

**Objective:** Show whether each VHDX is Fixed or Dynamic, and add a "Convert to Dynamic" button.

**Files to modify:**
- `G:\VENOM-VM\HyperVMManager\Models\VirtualMachine.cs` — add VhdType property
- `G:\VENOM-VM\HyperVMManager\Services\HyperVService.cs` — query VHD type
- `G:\VENOM-VM\HyperVMManager\Controls\VmDetailsDrawer.xaml` — show VHD type + convert button

**Step 1: Add VhdType property to `VirtualMachine.cs`**

```csharp
private string _vhdType = "";
public string VhdType
{
    get => _vhdType;
    set { _vhdType = value; OnPropertyChanged(nameof(VhdType)); OnPropertyChanged(nameof(IsFixedVhd)); }
}

public bool IsFixedVhd => _vhdType.Equals("Fixed", StringComparison.OrdinalIgnoreCase);
```

**Step 2: Query VHD type in `HyperVService.cs`**

In `QueryVmDiskInfoViaPowerShell`, add VHD type to the query:

```powershell
$vhd = Get-VHD -Path $os -ErrorAction Stop
# Add: $vhdType = [string]$vhd.VhdType
```

**Step 3: Add "Convert to Dynamic" action**

In `VmControlService.cs`:

```csharp
public static (bool ok, string message) ConvertVhdxToDynamic(string vmName, string vhdxPath)
{
    // 1. Stop VM if running
    // 2. Convert-VHD -Path $vhdxPath -DestinationPath "$vhdxPath.dynamic" -VHDType Dynamic
    // 3. Replace original with dynamic version
    // 4. Start VM
}
```

**Step 4: Add button in `VmDetailsDrawer.xaml`**

Show "⚠️ Fixed VHDX (wasting space)" with a "Convert to Dynamic" button when `IsFixedVhd` is true.

**Step 5: Build and verify**

```bash
dotnet build G:\VENOM-VM\HyperVMManager\HyperVMManager.csproj -c Release
```

---

## Phase 3: Build & Deploy

### Task 3.1: Bump Version

**Objective:** Update version from 1.2.0 to 1.3.0

**Files to modify:**
- `G:\VENOM-VM\HyperVMManager\HyperVMManager.csproj` — Version, AssemblyVersion, FileVersion, InformationalVersion
- `G:\VENOM-VM\installer\HyperVMManager.iss` — AppVersion
- `G:\VENOM-VM\release\latest.json` — version, downloadUrl, sha256

**Step 1: Update csproj**

```xml
<Version>1.3.0</Version>
<AssemblyVersion>1.3.0.0</AssemblyVersion>
<FileVersion>1.3.0.0</FileVersion>
<InformationalVersion>1.3.0</InformationalVersion>
```

**Step 2: Update installer script**

```iss
#define AppVersion "1.3.0"
```

**Step 3: Update CHANGELOG.md**

Add new section at top:

```markdown
## [1.3.0] - 2026-06-01
### Added
- Orphaned VHDX detection: scans VM storage folder for disks not linked to Hyper-V VMs
- VHDX type display (Fixed vs Dynamic) in VM details drawer
- Convert Fixed VHDX to Dynamic button in VM details

### Fixed
- Verified all VHDX creation uses Dynamic allocation (thin provisioning)
```

---

### Task 3.2: Build & Test Locally

**Step 1: Build**

```bash
dotnet build G:\VENOM-VM\HyperVMManager\HyperVMManager.csproj -c Release
```

**Step 2: Run locally**

```bash
G:\VENOM-VM\HyperVMManager\bin\Release\net8.0-windows\HyperVMManager.exe
```

**Step 3: Verify**
- VMs appear in list
- Orphaned VHDX section shows (if any)
- VHDX type displays correctly
- All existing functionality works

---

### Task 3.3: Build Installer & Publish

**Step 1: Build installer**

Compile `G:\VENOM-VM\installer\HyperVMManager.iss` with Inno Setup.

**Step 2: Update release manifest**

Update `G:\VENOM-VM\release\latest.json` with new version, download URL, and SHA256 hash.

**Step 3: Commit and push**

```bash
cd G:\VENOM-VM
git add -A
git commit -m "feat: v1.3.0 - orphaned VHDX detection, dynamic disk conversion, VHDX type display"
git push origin main
```

**Step 4: Publish update**

```bash
G:\VENOM-VM\release\Publish-Update.ps1 -GitHubRelease
```

**Step 5: Update installed version**

Launch the installed app → it should detect the update → install it.

OR manually run the new installer: `installer\HyperVMManager-Setup-1.3.0.exe`

---

## Execution Order Summary

| Step | Task | Admin? | Time |
|------|------|--------|------|
| 1 | Convert Main Server VHDX Fixed → Dynamic | ✅ | ~5 min |
| 2 | Re-register 3 VMs in Hyper-V | ✅ | ~3 min |
| 3 | Verify VMs appear in app | ❌ | ~1 min |
| 4 | Code: Ensure Dynamic VHDX creation | ❌ | ~10 min |
| 5 | Code: Add orphaned VHDX detection | ❌ | ~30 min |
| 6 | Code: Add VHDX type + convert button | ❌ | ~30 min |
| 7 | Build & test locally | ❌ | ~5 min |
| 8 | Bump version + changelog | ❌ | ~5 min |
| 9 | Build installer + publish | ❌ | ~10 min |
| 10 | Update installed version | ❌ | ~2 min |

**Total estimated time: ~1.5 hours**
