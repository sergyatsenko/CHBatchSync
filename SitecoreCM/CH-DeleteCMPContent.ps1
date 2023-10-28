function CH-DeleteCMPContent () {
    $item = Get-Item -Path "master:/sitecore/content/CMP" 
    $children = $item.Axes.GetDescendants() | Where-Object { $_.TemplateName -ne "Folder" } 
    foreach ($child in $children) { 
        Remove-Item -Path $child.Paths.Path -Recurse -Force
    }
}

CH-DeleteCMPContent 