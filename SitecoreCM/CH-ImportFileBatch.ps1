# /sitecore/system/Modules/PowerShell/Script Library/CMP/Web API/CmpRun

Import-Function -Name CH-ImportFileRun
$startTimestamp = Get-Date
Write-Output "Started run at", $startTimestamp.ToString()
$configItems = New-Object 'System.Collections.Generic.Dictionary[[string],[string]]'

$mappingConfigRoot = Get-Item -Path "master:/sitecore/system/Modules/CMP/Config"
Get-ChildItem -Path "master:/sitecore/system/Modules/CMP/Config" -Recurse | Where-Object { $_.TemplateName -eq "Entity Mapping" } | ForEach-Object {
    $entityDefinition = $_["EntityTypeSchema"]
    $configItems.Add( $_.Name , $entityDefinition)
}

$incomingFolderPath = [Sitecore.MainUtil]::MapPath("/App_Data/ContentHubData/Incoming") 
$processedFolderPath = [Sitecore.MainUtil]::MapPath("/App_Data/ContentHubData/Processed") 

$totalFileCounter = 0
$batchStart = Get-Date
$message = "CMP: Starting batch. Started at: {0}" -f $batchStart
Write-Output $message
Write-Log $message -Log Info

if ($utcTime.Hour -ge 1 -and $utcTime.Hour -lt 4) {
    $message = "CMP: making exception to CMP processing run at {0} UTC" -f $utcTime
    Write-Output $message
    Write-Log $message -Log Info
} else {
    try {
        foreach ($key in $configItems.Keys) {
            $runStart = Get-Date
            $message = "CMP: Started processing run: {0}. Started at {1}" -f $key, $runStart
            Write-Output message
            Write-Log $message -Log Info
            
            $configItemsPath = "master:/sitecore/system/Modules/CMP/Config/{0}" -f $key
            $filePattern = "{0}_*" -f $configItems[$key]
        
            $files = Get-ChildItem -Path $incomingFolderPath -Filter $filePattern | Sort-Object -Property Name
            foreach ($file in $files) {
                $fileRunStart = Get-Date
                $incomingFilePath = "{0}\{1}" -f $incomingFolderPath, $file
                $message = "CMP: Started processing of incoming file. Mapping config item: {0}, File Path: {1}. Started at {2}" -f $configItemsPath, $incomingFilePath, $fileRunStart
                Write-Output $message
                Write-Log $message -Log Info
                
                CH-ImportFileRun $configItemsPath $incomingFilePath $false
                
                $totalFileCounter++
                $processedFilePath = "{0}\{1}" -f $processedFolderPath, $file
                Write-Host "file paths: ", $incomingFilePath, $processedFilePath
                Move-Item -Path $incomingFilePath -Destination $processedFilePath -Force
                
                # Delete all previously processed files of this kind
                $excludeFromDeleteFilter = $file.ToString()
                Write-Host "excludeFromDeleteFilter: ", $excludeFromDeleteFilter
                Get-ChildItem -Path $processedFolderPath -Filter $filePattern | Where-Object { $_.Name -notlike $excludeFromDeleteFilter } | Remove-Item
                
                $fileRunEnd = Get-Date
                $fileRunRuntime = $fileRunEnd - $fileRunStart
                $message = "CMP: Finished processing of incoming file. Mapping config item: {0}, File Path: {1}. Started at; {2}. Finished at: {3}. Total seconds: {4}" -f $configItemsPath, $incomingFilePath, $fileRunStart, $fileRunEnd, $fileRunRuntime.TotalSeconds
                Write-Output $message
                Write-Log $message -Log Info
            }
            
            $runEnd = Get-Date
            $runRuntime = $runEnd - $runStart
            $message = "CMP: Finished processing run: {0}. Started at: {1}. Finished At: {2}. Total seconds: {3}" -f $key, $runStart, $runEnd, $runRuntime.TotalSeconds        
            Write-Output $message
            Write-Log $message -Log Info
            
        }
        $batchEnd = Get-Date
        $batchRuntime = $batchEnd - $batchStart
        $message = "CMP: Finished batch. Started at: {0}. Finished at: {1} Total seconds: {2}. Files processed: {3}." -f $batchStart, $batchEnd, $batchRuntime.TotalSeconds, $totalFileCounter
        Write-Output $message
        Write-Log $message -Log Info
    }
    catch {
        $message = "CMP: Error running the batch. {0}" -f $_
        Write-Log $message -Log Error
    }
}

