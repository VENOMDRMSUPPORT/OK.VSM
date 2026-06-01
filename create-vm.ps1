$ErrorActionPreference = "Stop"

$vmName = "VenomGPT"
$vhdxPath = "G:\VENOM-VM\HyperVMManager\vhdx\VenomGPT\VenomGPT.vhdx"
$switchName = "VENOM-External"
$memory = 4GB

Write-Host "Creating VM: $vmName"
Write-Host "VHD: $vhdxPath"
Write-Host "Switch: $switchName"
Write-Host "Memory: $memory"
Write-Host ""

# Create VM
$vm = New-VM -Name $vmName `
              -MemoryStartupBytes $memory `
              -Generation 2 `
              -VHDPath $vhdxPath `
              -SwitchName $switchName `
              -ErrorAction Stop

Write-Host "VM created successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "VM Details:"
Write-Host "  Name: $($vm.Name)"
Write-Host "  State: $($vm.State)"
Write-Host "  Generation: $($vm.Generation)"
Write-Host ""

# Check if we need to attach the seed disk (cloud-init)
$seedPath = "G:\VENOM-VM\HyperVMManager\vhdx\VenomGPT\VenomGPT-cidata-seed.vhdx"
if (Test-Path $seedPath) {
    Write-Host "Attaching cloud-init seed disk..."
    Add-VMHardDiskDrive -VMName $vmName -Path $seedPath -ErrorAction SilentlyContinue
}

# Configure VM
Set-VM -Name $vmName -AutomaticCheckpointsEnabled $false -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "VM is ready! You can now start it from Hyper-V Manager or this app."
