# VENOM VM-WARE Agent Workflow

This repository has three different states that agents must keep distinct:

1. Source tree under `G:\VENOM-VM`
2. Local test build under `G:\VENOM-VM\HyperVMManager\bin\Release\net8.0-windows`
3. Installed app and in-app update channel used by end users

## Before changing code

- First ask yourself which state the user wants to affect:
  - local source/build only
  - local installer only
  - published update for installed users
- If the user did not explicitly ask to publish, default to local source/build work only.
- If the user did not explicitly ask to release now, ask one concise question before any publish step:
  - "Do you want this as a local test build only, or should I publish it as a new app update?"

## After code changes

- Always build locally with:
  - `dotnet build G:\VENOM-VM\HyperVMManager\HyperVMManager.csproj -c Release`
- Do not assume the installed app contains the latest fix just because the repo was edited.
- Tell the user clearly when a fix exists only in the local build and has not been published yet.
- For local verification, run or ask the user to run:
  - `G:\VENOM-VM\HyperVMManager\bin\Release\net8.0-windows\HyperVMManager.exe`

## Publishing rules

- Never publish an update silently.
- Publish only when the user explicitly wants a release/update now.
- Before publishing, confirm whether they want:
  - a single fix released now
  - or multiple fixes grouped into one update
- The official publish flow is:
  1. bump version
  2. update `installer/HyperVMManager.iss`
  3. update `CHANGELOG.md`
  4. build and verify
  5. commit intended source changes
  6. run `G:\VENOM-VM\release\Publish-Update.ps1 -GitHubRelease`

## Update-channel facts

- The app checks GitHub release manifest, not the local source tree.
- In-app update detection depends on:
  - GitHub release assets
  - `release/latest.json`
- Therefore, source edits or local builds alone do not create a visible update for installed users.

## VM storage facts

- Ubuntu images are downloaded on demand from Canonical cloud images, not bundled with the installer.
- The current supported cloud image in this repo is Ubuntu 24.04.
- Shared storage design in this repo means:
  - shared prepared parent template
  - per-VM differencing OS disk
  - per-VM seed disk
- CPU is shared, memory uses dynamic allocation, and disk writes still consume real host bytes in the child disk.
