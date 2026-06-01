$ErrorActionPreference = 'Stop'
$logFile = 'G:\VENOM-VM\scripts\phase1-output.log'
try {
    "=== Phase 1 Log: $(Get-Date) ===" | Out-File -FilePath $logFile -Encoding UTF8
    
    $vhdxRoot = 'F:\VENOM VM-WARE\VENOM VM-WARE\vhdx'
    $switchName = 'VENOM-External'
    
    # Check switch
    "[0] Checking switch..." | Out-File -Append -FilePath $logFile
    $switch = Get-VMSwitch -Name $switchName -ErrorAction SilentlyContinue
    if (-not $switch) {
        $adapter = Get-NetAdapter -Physical | Where-Object { $_._.Status -eq 'Up' } | Select-Object -First 1
        $switch = New-VMSwitch -Name $switchName -NetAdapterName $adapter.Name -AllowManagementOS $true
        "  Created switch" | Out-File -Append -FilePath $logFile
    } else {
        "  Switch exists: $($switch.SwitchType)" | Out-File -Append -FilePath $logFile
    }
    
    # List existing VMs
    "[1] Existing VMs:" | Out-File -Append -FilePath $logFile
    Get-VM -ErrorAction SilentlyContinue | ForEach-Object { "  $($_.Name) - $($_.State)" | Out-File -Append -FilePath $logFile }
    
    "DONE" | Out-File -Append -FilePath $logFile
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -Append -FilePath $logFile
}
