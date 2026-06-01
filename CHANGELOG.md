# Changelog

## v1.3.0 (2026-06-01)

### New Features
- **Orphaned VHDX Detection**: Scans all drives for VHDX files not linked to any Hyper-V VM
  - Shows orphaned files with folder name, size, and path
  - Delete button for each orphaned VHDX to free disk space
  - Warning section appears automatically when orphans are found
- **VHD Type Display**: Shows Dynamic/Fixed VHDX type in the VM details drawer
  - Green "VHD Type: Dynamic/Fixed" indicator under OS disk info
  - PowerShell `Get-VHD` integration for accurate type detection

### Improvements
- Verified all VHDX creation uses Dynamic (thin provisioned) by default
- Phase 1 script ensures existing VMs have Dynamic Memory enabled
- Better disk info panel with 6 rows for complete disk details

### Technical
- Added `OrphanedVhdxInfo` model class
- Added `ScanOrphanedVhdx()` and `DeleteOrphanedVhdx()` to HyperVService
- Added `OsVhdType` property to VirtualMachine and VmDiskInfo models
- PowerShell script now queries `Get-VHD.VhdType` for each VM

## v1.2.0 (Previous Release)

- Initial Hyper-V VM management features
- Ubuntu cloud VM creation with cloud-init
- VM start/stop/restart/delete controls
- Resource monitoring (CPU, Memory, Disk)
- Network pool management
- VM details drawer with disk info
