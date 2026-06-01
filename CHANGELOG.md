# Changelog

## v1.3.2 (2026-06-01)

### New Features & Improvements
- **Default/NAT Switch Support**: Automatically lists and supports built-in and internal NAT/DHCP switches (e.g. "Default Switch") in Hyper-V, ensuring compatibility without requiring an External switch configuration.
- **Zero-Configuration DHCP Networking**: Implemented automatic fallback DHCP Netplan configuration inside the cloud-init template if no static network profile is supplied, ensuring VM guests acquire IPs out-of-the-box.
- **Independent Guest IP Auto-Reporting**: Integrates the guest Hyper-V KVP daemon (`hv-kvp-daemon`) offline setup seamlessly, ensuring IP addresses show up in the UI automatically.

## v1.3.1 (2026-06-01)

### Bug Fixes
- **VM Details Persistence**: Fixed a major bug where VM details (OS disk paths, actual sizes) and MAC addresses were being wiped and replaced with empty/dash `—` values every 5 seconds by the live refresh timer.
- **WMI Performance**: Optimised the merge logic in the status merging loop to ensure that fast queries preserve the fully-loaded detailed properties.

### New Features & Improvements
- **Reset Password Action**: Enabled the "Reset Password" button in the VM Details panel for all cloud-init virtual machines. It mounts the `CIDATA` seed disk and updates credentials seamlessly.
- **Rebuild OS Action**: Enabled the "Rebuild OS" button in the VM Details panel for VMs built with differencing disks. This lets you quickly recreate and reset your virtual machine back to a clean template state.
- **Automatic hv-kvp-daemon**: Added automatic installation of the `hv-kvp-daemon` integration services package during the first boot of cloud-init Ubuntu virtual machines. This ensures the guest automatically reports its IP address back to the Hyper-V host for dynamic display.

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
