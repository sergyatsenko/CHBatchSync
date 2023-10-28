using Nito.AsyncEx.Synchronous;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web;

namespace GVI.ContentHub.Sync.WebJob.Core
{
	public class BatchSyncRunner
	{
		static CultureInfo defaultCulture = CultureInfo.GetCultureInfo("en-US");
		ContentHubClient _client;
		Guid _namespaceGuid;

		private ILogger logger = Log.ForContext<BatchSyncRunner>();
		public BatchSyncRunner
			(string endpointUri, string clientId, string clientSecret, string userName, string password, string baseUrl, string deliveryHostUrl, Guid namespaceGuid)
		{
			_client = new ContentHubClient(endpointUri, clientId, clientSecret, userName, password, baseUrl, deliveryHostUrl);
			_namespaceGuid = namespaceGuid;
		}

		public List<EntityContent> GetEntitiesContent(EntityConfiguration entityMapping, DateTime modifiedAfter)
		{
			var startTime = DateTime.Now;
			var renditionMappingNames = entityMapping.RenditionRelations != null
				? entityMapping.RenditionRelations.Keys.Select(x => x.ToLower()).ToList()
				: new List<string>();

			var task = _client.GetEntitiesByDefinition(entityMapping.EntityDefinition, modifiedAfter);
			var result = task.WaitAndUnwrapException();
			var entities = new List<EntityContent>();
			foreach (var entity in result)
			{
				try
				{
					if (entity != null && entity.Id.HasValue && !string.IsNullOrEmpty(entity.Identifier))
					{
						var timestamp = entity.ModifiedOn ?? entity.CreatedOn;
						var entityContent = new EntityContent(entity.Id.Value, entity.Identifier, timestamp, _namespaceGuid);

						foreach (var property in entity.Properties)
						{
							try
							{
								if (property != null
									&& ((entityMapping.IncludedFields == null || entityMapping.IncludedFields.Count == 0)
									|| (entityMapping.IncludedFields != null && entityMapping.IncludedFields.Contains(property.Name))))
								{
									object value = null;
									if (!string.IsNullOrEmpty(property?.DataType?.Name) && property.DataType.Name.Equals("string[]", StringComparison.OrdinalIgnoreCase))
									{
										var optionListvalues = entity.GetPropertyValue<string[]>(property.Name);
										if (optionListvalues != null && optionListvalues.Length > 0)
										{
											value = string.Join("|", optionListvalues);
										}
									}
									else
									{
										value = entity.GetPropertyValue(property.Name);

									}
									if (value != null)
									{
										entityContent.fields.Add(property.Name.ToLower(), value);
									}
								}
							}
							catch (Exception ex) when (ex.Message == "Culture is required for culture sensitive properties.")
							{
								var value = entity.GetPropertyValue(property.Name, defaultCulture);
								if (value != null)
								{
									entityContent.fields.Add(property.Name.ToLower(), value);
								}
							}
						}

						if (entity.DefinitionName.Equals("M.Asset", StringComparison.OrdinalIgnoreCase) && entityMapping.Renditions != null && entityMapping.Renditions.Count > 0)
						{
							foreach (var rendition in entity.Renditions)
							{
								var foundRenditionName = entityMapping.Renditions.FirstOrDefault(x => rendition.Name.StartsWith(x, StringComparison.OrdinalIgnoreCase));
								if (!string.IsNullOrEmpty(foundRenditionName))
								{
									var publicLinkEntity = _client.GetAssetPublicLinkEntityAsync(entity.Id.Value, rendition.Name).WaitAndUnwrapException();
									if (publicLinkEntity != null)
									{
										var linkXml = _client.GetPublicLinkXmlAsync(entity, publicLinkEntity).WaitAndUnwrapException();
										if (!string.IsNullOrEmpty(linkXml))
										{
											var encodedLinkXml = HttpUtility.HtmlEncode(linkXml);
											if (!string.IsNullOrEmpty(encodedLinkXml))
											{
												entityContent.fields[foundRenditionName] = HttpUtility.HtmlEncode(linkXml);
											}
										}
									}
								}
							}
						}

						if (entity.Relations != null)
						{
							foreach (var relation in entity.Relations)
							{
								var relationName = relation.Name?.ToLower();
								if (!string.IsNullOrEmpty(relationName))
								{
									var ids = relation.GetIds();
									if (ids.Count > 0)
									{
										if (!renditionMappingNames.Contains(relationName))
										{
											var relationSitecoreIds = ids.Select(id => HashUtil.GetSitecoreGuid(_namespaceGuid, id));
											entityContent.relations.Add(relationName, string.Join("|", relationSitecoreIds).ToUpper());
										}
										else if (!entity.DefinitionName.Equals("M.Asset", StringComparison.OrdinalIgnoreCase))
										{
											//Assuming this link is meant to become an image field, which only takes one asset value, so just take the first one
											var id = ids[0];
											var assetEntity = _client.GetEntity(id).WaitAndUnwrapException();
											var renditionName = entityMapping.RenditionRelations.FirstOrDefault(x => x.Key.Equals(relationName, StringComparison.OrdinalIgnoreCase)).Value;
											if (!string.IsNullOrEmpty(renditionName))
											{
												if (assetEntity != null && assetEntity.DefinitionName.Equals("M.Asset", StringComparison.OrdinalIgnoreCase))
												{
													var publicLinkEntity = _client.GetAssetPublicLinkEntityAsync(id, renditionName).WaitAndUnwrapException();
													if (publicLinkEntity != null)
													{
														var linkXml = _client.GetPublicLinkXmlAsync(assetEntity, publicLinkEntity).WaitAndUnwrapException();
														if (!string.IsNullOrEmpty(linkXml))
														{
															entityContent.fields.Add(relationName, HttpUtility.HtmlEncode(linkXml));
														}
													}
												}
											}
										}
									}
								}
							}
						}

						//if (entityMapping.RenditionRelations != null && entityMapping.RenditionRelations.Count > 0)
						//{
						//	foreach (var renditionRelation in entityMapping.RenditionRelations.Keys)
						//	{
						//		var relationName = renditionRelation.Name?.ToLower();
						//		if (!string.IsNullOrEmpty(relationName))
						//		{
						//			var ids = renditionRelation.GetIds();
						//			if (ids.Count > 0)
						//			{
						//				var relationSitecoreIds = ids.Select(id => HashUtil.GetSitecoreGuid(id.ToString()));
						//				entityContent.relations.Add(relationName, string.Join("|", relationSitecoreIds).ToUpper());
						//			}
						//		}
						//	}
						//}

						//if (entityMapping.EntityDefinition.Equals("M.Asset", StringComparison.OrdinalIgnoreCase))
						//{ 
						//	entityContent.renditions = new Dictionary<string, string>();
						//	foreach (var rendition in entity.Renditions)
						//	{
						//		var renditionName = rendition.Name?.ToLower();
						//		if (!string.IsNullOrEmpty(renditionName))
						//		{
						//			//var renditionUrl = rendition.;
						//			if (!string.IsNullOrEmpty(renditionUrl))
						//			{
						//				entityContent.renditions.Add(renditionName, renditionUrl);
						//			}
						//		}
						//	}
						//	//entity.Renditions
						//}

						entities.Add(entityContent);
					}

				}
				catch (Exception ex)
				{
					logger.Error($"Error processing entity ID: {entity?.Id}. Error: {ex.Message}");
				}
			}

			return entities;
		}

		public DateTime GetLastProcessedFileTimestamp(string entityName, string incomingFolderPath, string processedFolderPath, int deltaOverlapSeconds)
		{

			string filter = $"{entityName}_*.json";
			string[] incomingFiles = Directory.GetFiles(incomingFolderPath, filter).Select(file => Path.GetFileName(file)).ToArray();
			string[] processedFiles = Directory.GetFiles(processedFolderPath, filter).Select(file => Path.GetFileName(file)).ToArray();
			string[] allFiles = incomingFiles.Concat(processedFiles).ToArray();
			if (allFiles.Length > 0)
			{
				string lastFile = allFiles.OrderBy(file => file).Last();
				string fileName = Path.GetFileNameWithoutExtension(lastFile);
				string[] parts = fileName.Split('_');
				if (parts.Length > 1)
				{
					DateTime lastDate;
					string dateString = parts[1];
					if (DateTime.TryParseExact(dateString, "yyyyMMddTHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out lastDate))
					{
						if (lastDate != DateTime.MinValue)
						{
							return lastDate.AddSeconds(-deltaOverlapSeconds);
						}

						return lastDate;
					}
				}
			}

			return DateTime.MinValue;
		}
	}
}