function CH-DeleteCMPImportFiles () {
    $incomingFolderPath = [Sitecore.MainUtil]::MapPath("/App_Data/ContentHubData/Incoming") 
    $processedFolderPath = [Sitecore.MainUtil]::MapPath("/App_Data/ContentHubData/Processed") 
    
    Get-ChildItem -Path $incomingFolderPath | Remove-Item
    Get-ChildItem -Path $processedFolderPath | Remove-Item
}

CH-DeleteCMPImportFiles