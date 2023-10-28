function CH-ImportFileRun ([string]$mappingConfigItemPath, [string]$jsonFilePath, [bool]$skipExisting) {
     
     try {

        $startTimestamp = Get-Date
        $message = "CMP: Started importing file content from {0}. Started at: {1}. Config item: {2}. Skip existing items: {3}" -f $jsonFilePath, $startTimestamp, $mappingConfigItemPath, $skipExisting 
        Write-Output $message
        Write-Log $message -Log Info
        
        #########################################################################################################################
        class MappedField {
            [string] $SourceFieldName
            [string[]] $DestinationFieldNames
            [string] $Type
            [bool] $isRelation
        
            MappedField([string] $sourceFieldName, [string[]] $destinationFieldNames, [string] $type, [bool] $isRelation) {
                $this.SourceFieldName = $sourceFieldName
                $this.DestinationFieldNames = $destinationFieldNames
                $this.Type = $type
                $this.isRelation = $isRelation
            }
        }
        
        class EntityMappingConfig {
            [string] $entityTypeSchema
            [string] $bucket
            [string] $template
            # [System.Collections.Generic.List[MappedField]] $mappedFields
        
            EntityMappingConfig([string] $entityTypeSchema, [string] $bucket, [string] $template) {
                #, [System.Collections.Generic.List[MappedField]] $mappedFields) {
                $this.entityTypeSchema = $entityTypeSchema
                $this.Template = $template
                $this.Bucket = $bucket
                # $this.MappedFields = $mappedFields
            }
        }
        
        
        ####################################################################################################################################
        $mappingConfigItem = Get-Item -Path $mappingConfigItemPath
        
        # Read the fields from the item
        $entityTypeSchema = $mappingConfigItem["EntityTypeSchema"]
        $bucket = $mappingConfigItem["Bucket"]
        $template = $mappingConfigItem["Template"]
        $newItemFieldName = $mappingConfigItem["ItemNameProperty"]
        
        
        # get the template item, specified in the Template field and read its fields
        $templateItem = Get-Item -Path "master:" -ID $template
        $templateItemCasted = [Sitecore.Data.Items.TemplateItem]$templateItem
        $targetTemplateFields = New-Object 'System.Collections.Generic.Dictionary[[string],[string]]'
        foreach ($templateField in $templateItemCasted.OwnFields) {
            $targetTemplateFields.Add($templateField.Name, $templateField.Type.ToLower())
        }
        
        
        $mappedFieldsList = New-Object "System.Collections.Generic.Dictionary[string,MappedField]"
        # Read child items and their fields
        $fieldMappingItems = $mappingConfigItem.Children 
        foreach ($fieldMappingItem in $fieldMappingItems) {
            $fieldMappingItemName = $fieldMappingItem.TemplateName
            if ($fieldMappingItemName -ne "Field Mapping" -and $fieldMappingItemName -ne "Related Entity Mapping") {
                Continue
            }
            $sitecoreFieldName = $fieldMappingItem["Sitecore Field Name"]
            $type = ""
            
            # Lookup target field type
            if ($targetTemplateFields.ContainsKey($sitecoreFieldName)) {
                $type = $targetTemplateFields.Item($sitecoreFieldName)
            }
            elseif ( $sitecoreFieldName -eq "{B5E02AD9-D56F-4C41-A065-A133DB87BDEB}") {
                $type = "single-line text"
            }
            else {
                $message = "CMP: Field named {0} not found in target template {1}." -f $fieldMappingItem.Name, $fieldMappingItem.TemplateName
                Write-Output $message
                Write-Log $message -Log Error
            }
            
            if ($fieldMappingItemName -eq "Field Mapping") {
                $cmpFieldName = $fieldMappingItem["CMP Field Name"].ToLower()
               
                if ($sitecoreFieldName.ToLower() -eq "name") {
                    $newItemFieldName = $cmpFieldName
                }
                if ($mappedFieldsList.ContainsKey($cmpFieldName)) {
                    $existingMapping = [MappedField]$mappedFieldsList[$cmpFieldName] 
                    $existingMapping.DestinationFieldNames += $sitecoreFieldName
                    $mappedFieldsList[$cmpFieldName] = $existingMapping
                }
                else {
                    $mappedFieldsList.Add($cmpFieldName, [MappedField]::new($cmpFieldName, @($sitecoreFieldName), $type, $false))
                }
            }
            elseif ($fieldMappingItemName -eq "Related Entity Mapping") {
                $cmpRelation = $fieldMappingItem["CMP Relation"].ToLower()
                if ($mappedFieldsList.ContainsKey($cmpRelation)) {
                    $existingMapping = [MappedField]$mappedFieldsList[$cmpRelation] 
                    $existingMapping.DestinationFieldNames += $sitecoreFieldName
                    $mappedFieldsList[$cmpFieldName] = $existingMapping
                }
                else {
                    $mappedFieldsList.Add($cmpRelation, [MappedField]::new($cmpRelation, @($sitecoreFieldName), $type, $true))
                }
            }
        }
        
        ############################################################################################################################
        
        $entityMappingConfig = [EntityMappingConfig]::new($entityTypeSchema, $template, $bucket)
        # ConvertTo-Json $mappedFieldsList
        
        ############################################################################################################################
        $jsonContent = Get-Content -Path $jsonFilePath -Raw -encoding UTF8
        
        $jsonData = ConvertFrom-Json -InputObject $jsonContent   
        $bucketFolderItem = Get-Item -Path "master:" -ID $bucket
        
        New-UsingBlock (New-Object Sitecore.Data.BulkUpdateContext) {
            foreach ($object in $jsonData) {
                try {
                    $id = $object.id
                    $sitecoreId = $object.sitecoreid
                    $identifier = $object.identifier
                    $sitecoreIdFormatted = "{" + $sitecoreId.ToUpper() + "}"
                    $lastModified = $object.lastmodified
                    $fields = $object.fields;
                    $lastModifiedChanged = $false
                    $relationsChanged = $false
                    
                    $sitecoreContentItem = [Sitecore.Data.Database]::GetDatabase("master").GetItem($sitecoreIdFormatted)
                    if ($sitecoreContentItem -ne $null -and $skipExisting -eq $true) {
                        continue
                    }
                    if ($sitecoreContentItem -ne $null) {
                        if ($lastModified -eq $sitecoreContentItem["ContentHubLastModified"]) {
                            $relationChanged = $false
                            $relations = $object.relations;
                            foreach ($property in $relations.PSObject.Properties) {
                                $relationName = $property.Name.ToLower()
                        
                                if ($mappedFieldsList.ContainsKey($relationName)) {
                                    $mapping = $mappedFieldsList[$relationName]
                                    $relationSitecoreRawValue = ""
                                    if (![string]::IsNullOrEmpty($property.Value)) {
                                        $relationSitecoreRawValue = "{" + ($property.Value.Replace("|", "}|{")) + "}"
                                        foreach($destinationFieldName in $mapping.DestinationFieldNames) {
                                            if ($sitecoreContentItem.Fields[$destinationFieldName] -ne $null) {
                                                if ($sitecoreContentItem[$destinationFieldName] -ne $relationSitecoreRawValue) {
                                                    $relationChanged = $true
                                                }
                                                # $sitecoreContentItem[$mapping.DestinationFieldName] = $relationSitecoreRawValue
                                            }
                                        }
                                        
                                    }
                                }
                                else {
                                    # Write-Output "Field '$fieldName' not found in mappings."
                                }
                            }
                            
                            if ($relationChanged -eq $false) {
                                # No changes detected in item ID - skip the update
                                continue
                            }
                            else {
                               # Changes detected in item ID: ", $sitecoreId
                            }
                            
                        }
                        else {
                            # Timestamp change detected in item ID: ", $sitecoreId
                        }
                    }
                    
                    if ($sitecoreContentItem -eq $null) {
                        # Do not create already deleted items
                        $isDeleted = $object.fields.isdeleted
                        if($isDeleted -eq $true) {
                           continue
                        }
                        
                        $itemName = $id.ToString()
                        if ($newItemFieldName -ne "id") {
                            foreach ($property in $fields.PSObject.Properties) {
                                $fieldName = $property.Name.ToLower()
                                if ($fieldName -eq $newItemFieldName) {
                                    $itemName = $property.Value
                                }
                            }
                        }
                        $safeItemName = ""
                        if(![string]::IsNullOrEmpty($itemName)) {
                            $safeItemName = [Sitecore.Web.WebUtil]::SafeEncode($itemName).Replace("/", "")
                        } else {
                            $safeItemName = $id.ToString()
                        }
                        $sitecoreContentItem = New-Item -Path $bucketFolderItem.Paths.Path -Name $safeItemName -ItemType $templateItem.ID -ForceId $sitecoreId
                    }
                    
                    $sitecoreContentItem.Editing.BeginEdit()
                    $sitecoreContentItem["EntityIdentifier"] = $identifier
                    $sitecoreContentItem["ContentHubLastModified"] = $lastModified
                    $sitecoreContentItem["ContentHubId"] = $id
                    foreach ($property in $fields.PSObject.Properties) {
                        $fieldName = $property.Name.ToLower()
                
                        if ($mappedFieldsList.ContainsKey($fieldName)) {
                            $mapping = $mappedFieldsList[$fieldName]
                            #$destinationFieldNames = $mapping.DestinationFieldName.Split("|")
                
                            foreach ($destinationFieldName in $mapping.DestinationFieldNames) {
                                $existingFieldItem = $sitecoreContentItem.Fields[$destinationFieldName.Trim()]
                                if ($existingFieldItem -ne $null -and $property.Value -ne $null) {
                                    
                                    $stringValue = $property.Value.ToString()
                                    if ($property.Value -ne $null -and $property.Value.GetType() -eq [System.Management.Automation.PSCustomObject]) {
                                        $jsonString = ConvertTo-Json -depth 5 $property.Value
                                        $sitecoreContentItem[$destinationFieldName] = $jsonString.ToString()
                                    }
                                    elseif ($mapping.Type -eq "checkbox") {
                                        if ($property.Value -eq $true) {
                                            $sitecoreContentItem[$destinationFieldName] = "1"
                                        }
                                        else {
                                            $sitecoreContentItem[$destinationFieldName] = "0"
                                        }
                                    }
                                    elseif ($mapping.Type -eq "image" -or $mapping.Type -eq "file") {
                                        if(![string]::IsNullOrEmpty($property.Value)) {
                                            Write-Output "Encoded image", $property.Value
                                            $chImageRefValue = [System.Net.WebUtility]::HtmlDecode($property.Value)
                                            Write-Output "Decoded image", $chImageRefValue
                                            $sitecoreContentItem.Fields[$destinationFieldName].Value = $chImageRefValue
                                        }
                                    }
                                    elseif ($mapping.Type -eq "datetime" -and $property.Value -ne $null -and $property.Value.Length -gt 5) {
                                        #$reformattedDatetime = $property.Value.Replace("-", "").Replace(":", "")
                                        #$reformattedDatetime = $reformattedDatetime.Substring(0, $reformattedDatetime.Length - 5) + "Z"
                                        #$dateTimeString = "2021-04-19T19:00:00-06:00"
                                        $dateTimeOffset = [DateTimeOffset]::Parse($property.Value)
                                        $utcDateTimeOffset = $dateTimeOffset.ToUniversalTime()
                                        $utcDateString = $utcDateTimeOffset.ToString("yyyyMMddTHHmmssZ")
                                        $sitecoreContentItem.Fields[$destinationFieldName].Value = $utcDateString
                                    }
                                    else {
                                        $sitecoreContentItem.Fields[$destinationFieldName].Value = $property.Value
                                    }
                                        
                                }
                                else {
                                    $sitecoreContentItem[$destinationFieldName] = $null
                                }
                                   
                            }
        
                        }
                           
                    }
        
                    $relations = $object.relations;
                    foreach ($property in $relations.PSObject.Properties) {
                        $relationName = $property.Name.ToLower()
                
                        if ($mappedFieldsList.ContainsKey($relationName)) {
                            $mapping = $mappedFieldsList[$relationName]
                            $relationSitecoreRawValue = ""
                            if (![string]::IsNullOrEmpty($property.Value)) {
                                $relationSitecoreRawValue = "{" + ($property.Value.Replace("|", "}|{")) + "}"
                                #$relationSitecoreRawValue = $property.Value 
                               
                                foreach($destinationFieldName in $mapping.DestinationFieldNames) {
                                    if ($sitecoreContentItem.Fields[$destinationFieldName] -ne $null) {
                                        $sitecoreContentItem[$destinationFieldName] = $relationSitecoreRawValue
                                    }
                                    else {
                                        $message = "CMP: Mapped relation not found {0}" -f $destinationFieldName
                                        Write-Output $message
                                        Write-Log $message -Log Error
                                    }
                                }
                            }
                        }
                        else {
                            # Field '$fieldName' not found in mappings."
                        }
                    }
                    
                    $sitecoreContentItem.Editing.EndEdit()
                }
                catch {
                    $e = $_.Exception
                    $line = -1
                    if ($_.InvocationInfo -ne $null) {
						$line = $_.InvocationInfo.ScriptLineNumber
					}
                    $msg = $e.Message 
                    $message = "CMP: Error importing content item content from {0}. Id: {1}, sitecoreId: {2}. Line: {3}, Message: {4} Exception: {5}" -f $jsonFilePath, $id, $sitecoreId, $line, $msg, $_
                    Write-Log $message -Log Error
                }
            }
        
        }
        
        $endTimestamp = Get-Date
        $totalRuntime = $endTimestamp - $startTimestamp
        $message = "CMP: Finished importing file content {0}. Started at: {1}. Finished at: {2} Total seconds: {3}" -f $jsonFilePath, $startTimestamp, $endTimestamp, $totalRuntime.TotalSeconds
        Write-Output $message
        Write-Log $message -Log Info
        
     } catch {
        $e = $_.Exception
        $line = -1
        if ($_.InvocationInfo -ne $null) {
			$line = $_.InvocationInfo.ScriptLineNumber
		}
        $msg = $e.Message 
        $message = "CMP: Error importing file content from {0}. Line: {1}. Message: {2}. Exception: {4}" -f $jsonFilePath, $line, $msg, $_
        Write-Log $message -Log Error
    }
}