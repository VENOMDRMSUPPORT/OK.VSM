# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog and this project follows Semantic Versioning.

## [1.1.5] - 2026-05-08
### Fixed
- Use DHCP networking automatically on Hyper-V Default Switch / WSL switches so Ubuntu keeps internet access.
- Apply the requested admin password through both user creation and `chpasswd`.
- Avoid writing hosts-file entries when the guest uses DHCP and no static IP is known yet.

## [1.1.4] - 2026-05-08
### Fixed
- Convert extracted sparse Ubuntu VHD files to dynamic VHDX in-process instead of using Hyper-V `Convert-VHD`.
- Catch Create VM exceptions so conversion failures show an error dialog instead of closing the application.

## [1.1.3] - 2026-05-08
### Fixed
- Avoid expanding the temporary extracted Ubuntu VHD to its full fixed size before VHDX conversion.
- Fail loudly if temporary extraction cleanup cannot complete instead of leaving hidden `.extract-*` folders silently.

## [1.1.2] - 2026-05-08
### Fixed
- Create Ubuntu cloud VM OS disks as VHDX so Hyper-V Generation 2 can attach them.
- Convert the downloaded Ubuntu Azure VHD image to VHDX before VM creation.

## [1.1.1] - 2026-05-08
### Fixed
- Normalize extracted Ubuntu VHD files so Hyper-V can mount them after archive extraction.
- Clear compression, encryption, and sparse attributes from VM disk folders and cloud-init seed disks before mounting.

## [1.1.0] - 2026-05-07
### Added
- Inno Setup installer script for packaging desktop releases.
- Online update MVP with remote manifest check and checksum validation.
- Manual Check for updates action in the main window title bar.

### Changed
- Centralized application version metadata in project build properties.
- Aligned application manifest version with release version 1.1.0.
- Repository hygiene with ignore rules for generated build artifacts.

### Fixed
- Cleaned stale build outputs and temporary artifacts from the project tree.
