#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"

# Log to file
$logFile = "G:\VENOM-VM\scripts\phase1-output.log"
function Log($msg) {
    Write-Host $msg
    $msg | Out-File -Append -FilePath $logFile -Encoding UTF8
}

"=== Phase 1 Log: $(Get-Date) ===" | Out-File -FilePath $logFile -Encoding UTF8

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " VENOM VM-WARE - Phase 1: Fix VHDX & Register VMs" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$vhdxRoot = "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx"
$switchName = "VENOM-External"

# ============================================
# Step 0: Check/Ensure External switch exists
# ============================================
Write-Host "[0/4] Checking External switch..." -ForegroundColor Yellow
$switch = Get-VMSwitch -Name $switchName -ErrorAction SilentlyContinue
if (-not $switch) {
    Write-Host "  External switch '$switchName' not found. Creating..." -ForegroundColor Yellow
    $adapter = Get-NetAdapter -Physical | Where-Object { $_.Status -eq 'Up' } | Select-Object -First 1
    if (-not $adapter) {
        Write-Host "  ERROR: No active network adapter found!" -ForegroundColor Red
        exit 1
    }
    $switch = New-VMSwitch -Name $switchName -NetAdapterName $adapter.Name -AllowManagementOS $true
    Write-Host "  Created switch '$switchName' using adapter '$($adapter.Name)'" -ForegroundColor Green
} else {
    Write-Host "  Switch '$switchName' exists (Type: $($switch.SwitchType))" -ForegroundColor Green
}
Write-Host ""

# ============================================
# Step 1: Convert Main Server VHDX Fixed -> Dynamic
# ============================================
Write-Host "[1/4] Converting Main Server VHDX (Fixed -> Dynamic)..." -ForegroundColor Yellow
$mainVhdx = "$vhdxRoot\Main Server\Main Server.vhdx"
$mainDynamic = "$vhdxRoot\Main Server\Main Server-dynamic.vhdx"
$mainBackup = "$vhdxRoot\Main Server\Main Server-fixed-backup.vhdx"

if (Test-Path $mainVhdx) {
    $vhdInfo = Get-VHD -Path $mainVhdx
    Write-Host "  Current type: $($vhdInfo.VhdType)" -ForegroundColor White
    Write-Host "  File size: $([math]::Round($vhdInfo.FileSize/1GB,2)) GB" -ForegroundColor White
    Write-Host "  Max size: $([math]::Round($vhdInfo.Size/1GB,2)) GB" -ForegroundColor White
    
    if ($vhdInfo.VhdType -eq 'Fixed') {
        Write-Host "  Converting to Dynamic..." -ForegroundColor Yellow
        Convert-VHD -Path $mainVhdx -DestinationPath $mainDynamic -VHDType Dynamic
        Write-Host "  Conversion complete!" -ForegroundColor Green
        
        # Replace old with new
        Rename-Item $mainVhdx (Split-Path $mainBackup -Leaf)
        Rename-Item $mainDynamic (Split-Path $mainVhdx -Leaf)
        Write-Host "  Replaced old VHDX with dynamic version" -ForegroundColor Green
        
        # Verify
        $newInfo = Get-VHD -Path $mainVhdx
        Write-Host "  New type: $($newInfo.VhdType)" -ForegroundColor Green
        Write-Host "  New file size: $([math]::Round($newInfo.FileSize/1GB,2)) GB" -ForegroundColor Green
        Write-Host "  Space saved: $([math]::Round(($vhdInfo.FileSize - $newInfo.FileSize)/1GB,2)) GB" -ForegroundColor Green
        
        # Delete backup
        Remove-Item $mainBackup -Force
        Write-Host "  Backup deleted" -ForegroundColor Green
    } else {
        Write-Host "  Already Dynamic - no conversion needed" -ForegroundColor Green
        Remove-Item $mainDynamic -ErrorAction SilentlyContinue
        Remove-Item $mainBackup -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "  Main Server VHDX not found at: $mainVhdx" -ForegroundColor Red
}
Write-Host ""

# ============================================
# Step 2: Register VMs in Hyper-V
# ============================================
Write-Host "[2/4] Registering VMs in Hyper-V..." -ForegroundColor Yellow

$vmConfigs = @(
    @{
        Name = "Main Server"
        VhdxPath = "$vhdxRoot\Main Server\Main Server.vhdx"
        MemoryStartup = 4GB
        MemoryMin = 512MB
        MemoryMax = 8GB
        CPUs = 2
    },
    @{
        Name = "ubuntu24-new"
        VhdxPath = "$vhdxRoot\ubuntu24-new\ubuntu24-new.vhdx"
        MemoryStartup = 2GB
        MemoryMin = 512MB
        MemoryMax = 4GB
        CPUs = 2
    },
    @{
        Name = "venomgpt"
        VhdxPath = "$vhdxRoot\venomgpt\venomgpt.vhdx"
        MemoryStartup = 2GB
        MemoryMin = 512MB
        MemoryMax = 4GB
        CPUs = 2
    }
)

foreach ($vmConfig in $vmConfigs) {
    $existing = Get-VM -Name $vmConfig.Name -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "  '$($vmConfig.Name)' already registered (State: $($existing.State))" -ForegroundColor Green
        continue
    }
    
    if (-not (Test-Path $vmConfig.VhdxPath)) {
        Write-Host "  '$($vmConfig.Name)' VHDX not found: $($vmConfig.VhdxPath)" -ForegroundColor Red
        continue
    }
    
    Write-Host "  Creating '$($vmConfig.Name)'..." -ForegroundColor Yellow
    
    $vm = New-VM -Name $vmConfig.Name `
                 -MemoryStartupBytes $vmConfig.MemoryStartup `
                 -Generation 2 `
                 -VHDPath $vmConfig.VhdxPath `
                 -SwitchName $switchName `
                 -ErrorAction Stop
    
    # Configure dynamic memory
    Set-VMMemory -VMName $vmConfig.Name `
                 -DynamicMemoryEnabled $true `
                 -MinimumBytes $vmConfig.MemoryMin `
                 -StartupBytes $vmConfig.MemoryStartup `
                 -MaximumBytes $vmConfig.MemoryMax
    
    # Set processor count
    Set-VMProcessor -VMName $vmConfig.Name -Count $vmConfig.CPUs
    
    # Disable auto checkpoints
    Set-VM -VMName $vmConfig.Name -AutomaticCheckpointsEnabled $false
    
    # Set firmware
    Set-VMFirmware -VMName $vmConfig.Name -SecureBootTemplate MicrosoftUEFICertificateAuthority
    
    Write-Host "  '$($vmConfig.Name)' created successfully!" -ForegroundColor Green
    Write-Host "    Memory: $($vmConfig.MemoryMin/1GB)GB min / $($vmConfig.MemoryStartup/1GB)GB start / $($vmConfig.MemoryMax/1GB)GB max" -ForegroundColor Gray
    Write-Host "    CPUs: $($vmConfig.CPUs)" -ForegroundColor Gray
    Write-Host "    VHDX: $($vmConfig.VhdxPath)" -ForegroundColor Gray
}
Write-Host ""

# ============================================
# Step 3: Verify all VMs
# ============================================
Write-Host "[3/4] Verifying VMs..." -ForegroundColor Yellow
$vms = Get-VM | Select-Object Name, State, CPUUsage, MemoryAssigned, MemoryStartup, MemoryMinimum, MemoryMaximum, Generation
$vms | Format-Table -AutoSize
Write-Host "  Total VMs registered: $($vms.Count)" -ForegroundColor Green
Write-Host ""

# ============================================
# Step 4: Check disk space
# ============================================
Write-Host "[4/4] Disk space check..." -ForegroundColor Yellow
$drive = Get-PSDrive -Name F
$freeGB = [math]::Round($drive.Free/1GB, 2)
$usedGB = [math]::Round($drive.Used/1GB, 2)
Write-Host "  F: Drive - Used: $usedGB GB, Free: $freeGB GB" -ForegroundColor White
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " DONE! Open VENOM VM-WARE to verify VMs appear." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
