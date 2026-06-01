$ErrorActionPreference = "Stop"
$logFile = "G:\VENOM-VM\scripts\phase1-output2.log"

try {
    "=== Phase 1 Step 2: Check VHDX Types ===" | Out-File -FilePath $logFile -Encoding UTF8
    "" | Out-File -Append -FilePath $logFile

    # Check Main Server VHDX
    $mainVhdx = "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\Main Server\Main Server.vhdx"
    "[1] Main Server VHDX:" | Out-File -Append -FilePath $logFile
    $vhd = Get-VHD -Path $mainVhdx
    "  Type: $($vhd.VhdType)" | Out-File -Append -FilePath $logFile
    "  FileSize: $([math]::Round($vhd.FileSize/1GB,2)) GB" | Out-File -Append -FilePath $logFile
    "  MaxSize: $([math]::Round($vhd.Size/1GB,2)) GB" | Out-File -Append -FilePath $logFile
    
    if ($vhd.VhdType -eq "Fixed") {
        "" | Out-File -Append -FilePath $logFile
        "[2] Converting Main Server VHDX: Fixed -> Dynamic..." | Out-File -Append -FilePath $logFile
        
        $dynamicPath = "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\Main Server\Main Server-dynamic.vhdx"
        $backupPath = "F:\VENOM VM-WARE\VENOM VM-WARE\vhdx\Main Server\Main Server-fixed-backup.vhdx"
        
        # Stop VM if running
        $vm = Get-VM -Name "Main Server" -ErrorAction SilentlyContinue
        if ($vm -and $vm.State -eq "Running") {
            "  Stopping VM..." | Out-File -Append -FilePath $logFile
            Stop-VM -Name "Main Server" -Force
        }
        
        # Convert
        "  Converting (this may take a few minutes)..." | Out-File -Append -FilePath $logFile
        Convert-VHD -Path $mainVhdx -DestinationPath $dynamicPath -VHDType Dynamic
        "  Conversion complete!" | Out-File -Append -FilePath $logFile
        
        # Replace
        Rename-Item $mainVhdx "Main Server-fixed-backup.vhdx"
        Rename-Item $dynamicPath "Main Server.vhdx"
        "  Replaced old file" | Out-File -Append -FilePath $logFile
        
        # Verify
        $newVhd = Get-VHD -Path $mainVhdx
        "  New Type: $($newVhd.VhdType)" | Out-File -Append -FilePath $logFile
        "  New FileSize: $([math]::Round($newVhd.FileSize/1GB,2)) GB" | Out-File -Append -FilePath $logFile
        "  Space saved: $([math]::Round(($vhd.FileSize - $newVhd.FileSize)/1GB,2)) GB" | Out-File -Append -FilePath $logFile
        
        # Delete backup
        Remove-Item $backupPath -Force
        "  Backup deleted" | Out-File -Append -FilePath $logFile
    } else {
        "  Already Dynamic - no conversion needed" | Out-File -Append -FilePath $logFile
    }
    
    "" | Out-File -Append -FilePath $logFile
    "[3] All VMs:" | Out-File -Append -FilePath $logFile
    Get-VM | ForEach-Object {
        $mem = Get-VMMemory -VMName $_.Name
        "  $($_.Name): State=$($_.State) CPUs=$($_.ProcessorCount) MemDynamic=$($mem.DynamicMemoryEnabled) MemStartup=$([math]::Round($mem.Startup/1GB,2))GB MemMin=$([math]::Round($mem.Minimum/1GB,2))GB MemMax=$([math]::Round($mem.Maximum/1GB,2))GB" | Out-File -Append -FilePath $logFile
    }
    
    "" | Out-File -Append -FilePath $logFile
    "[4] Disk space:" | Out-File -Append -FilePath $logFile
    $drive = Get-PSDrive -Name F
    "  F: Used=$([math]::Round($drive.Used/1GB,2))GB Free=$([math]::Round($drive.Free/1GB,2))GB" | Out-File -Append -FilePath $logFile
    
    "" | Out-File -Append -FilePath $logFile
    "DONE" | Out-File -Append -FilePath $logFile
    
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -Append -FilePath $logFile
    "STACK: $($_.ScriptStackTrace)" | Out-File -Append -FilePath $logFile
}
