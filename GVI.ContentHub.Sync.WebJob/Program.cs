using GVI.ContentHub.Sync.WebJob.Core;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVI.ContentHub.Sync.WebJob
{
	// To learn more about Microsoft Azure WebJobs SDK, please see https://go.microsoft.com/fwlink/?LinkID=320976
	internal class Program
	{
		static void Main()
		{
			try
			{
				var config = new JobHostConfiguration();

				if (config.IsDevelopment)
				{
					config.UseDevelopmentSettings();
				}

				#region Read configs
				IConfiguration configuration = new ConfigurationBuilder()
						.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
						.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
						//.AddCommandLine(args)
						.Build();
				var entityConfigurations = new List<EntityConfiguration>();
				configuration.GetSection("Entities").Bind(entityConfigurations);

				var endpointUri = configuration.GetSection("ContentHubClient")["EndpontUri"];
				var clientId = configuration.GetSection("ContentHubClient")["ClientId"];
				var clientSecret = configuration.GetSection("ContentHubClient")["ClientSecret"];
				var userName = configuration.GetSection("ContentHubClient")["UserName"];
				var password = configuration.GetSection("ContentHubClient")["Password"];
				var webRootPath = Environment.GetEnvironmentVariable("WEBROOT_PATH") ?? configuration.GetValue<string>("WebRootPath");
				var logsPath = configuration.GetValue<string>("LogsPath");
				var incomingFolderRelativePath = configuration.GetValue<string>("IncomingFolderRelativePath");
				var processedFolderRelativePath = configuration.GetValue<string>("ProcessedFolderRelativePath");
				var maxEntityCountInFile = configuration.GetValue<int>("MaxEntityCountInFile");
				var deltaOverlapSeconds = configuration.GetValue<int>("DeltaOverlapSeconds");
				var deliveryHostUrl = configuration.GetValue<string>("DeliveryHostUrl");
				var baseUrl = configuration.GetValue<string>("BaseUrl");
				var namespaceGuid = configuration.GetValue<Guid>("NamespaceGuid");
				var incomingFolderPath = Path.Combine(webRootPath, incomingFolderRelativePath);
				var processedFolderPath = Path.Combine(webRootPath, processedFolderRelativePath);
				var logFilePath = Path.Combine(webRootPath, logsPath, $"CH.Downloader.log.{DateTime.Now.ToString("ddMMyyyy_HHmmss")}.txt"); 
				#endregion

				Log.Logger = new LoggerConfiguration()
				  .MinimumLevel.Debug()
				  .WriteTo.File(logFilePath)
				  .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
				  .CreateLogger();

				if (!Directory.Exists(incomingFolderPath))
				{
					Log.Warning($"Creating incoming folder {incomingFolderPath}");
					Directory.CreateDirectory(incomingFolderPath);
				}

				if (!Directory.Exists(processedFolderPath))
				{
					Log.Warning($"Creating processed folder {processedFolderPath}");
					Directory.CreateDirectory(processedFolderPath);
				}

				//Download configured entities to file (since lasrt update)
				if (entityConfigurations != null)
				{
					var runner = new BatchSyncRunner(endpointUri, clientId, clientSecret, userName, password, baseUrl, deliveryHostUrl, namespaceGuid);
					var batchStartTime = DateTime.Now;
					Log.Information($"Starting download batch. Start time: " + batchStartTime);

					foreach (var entityConfiguration in entityConfigurations)
					{
						if (!string.IsNullOrEmpty(entityConfiguration?.EntityDefinition))
						{
							var startTime = DateTime.Now;
							var lastDownloadTime = runner.GetLastProcessedFileTimestamp(entityConfiguration.EntityDefinition, incomingFolderPath, processedFolderPath, deltaOverlapSeconds);
							Log.Information($"Downloading {entityConfiguration?.EntityDefinition}. Start time: {startTime}. Last dowload time: {lastDownloadTime}");

							var entitiesData = runner.GetEntitiesContent(entityConfiguration, lastDownloadTime);

							var count = entitiesData.Count;
							var endTime = DateTime.Now;
							var duration = endTime - startTime;
							var durationInSeconds = duration.TotalSeconds;
							Log.Information($"Downloaded {count} entities of type {entityConfiguration?.EntityDefinition}. Started at: {startTime}, Completed at: {startTime}, total runtime in seconds: {durationInSeconds}");

							if (count > 0)
							{
								int entityCount = entitiesData.Count;
								int fileCount = (int)Math.Ceiling((double)entityCount / maxEntityCountInFile);

								for (int i = 0; i < fileCount; i++)
								{
									int entitiesToSave = (i != fileCount - 1) ? maxEntityCountInFile : entityCount % maxEntityCountInFile;
									var entitiesBatch = entitiesData.Skip(i * maxEntityCountInFile).Take(entitiesToSave);

									string strNum = i.ToString("D3");
									var fileName = $"{entityConfiguration?.EntityDefinition}_{startTime.ToString("yyyyMMddTHHmmss")}_{strNum}.json";

									var filePath = Path.Combine(incomingFolderPath, fileName);
									var json = JsonConvert.SerializeObject(entitiesBatch);

									File.WriteAllText(filePath, json);
									Log.Information($"Saved {entitiesToSave} entities of type {entityConfiguration.EntityDefinition} entities to {filePath}");
								}
							}
							else
							{
								Log.Information($"No entities found - nothing to save. Skipping save for {entityConfiguration.EntityDefinition}.");
							}
						}
					}
					var endtime = DateTime.Now;
					var batchRuntime = endtime - batchStartTime;
					var batchRuntimeInSeconds = batchRuntime.TotalSeconds;
					Log.Information($"Completed download batch at {endtime}. total runtime in seconds: {batchRuntimeInSeconds}");
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex, $"Error running {nameof(WebJob)}");
				//throw;
			}
			finally
			{
				Log.CloseAndFlush();
			}
		}
	}


}
