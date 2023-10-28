using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Stylelabs.M.Base.Querying;
using Stylelabs.M.Base.Querying.Filters;
using Stylelabs.M.Framework.Essentials.LoadConfigurations;
using Stylelabs.M.Framework.Essentials.LoadOptions;
using Stylelabs.M.Sdk.Contracts.Base;
using Stylelabs.M.Sdk.WebClient;
using Stylelabs.M.Sdk.WebClient.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Stylelabs.M.Sdk.Constants;

namespace GVI.ContentHub.Sync.WebJob.Core
{
	/// <summary>
	/// CH API downloader, based on CH Web.SDK client
	/// </summary>
	public class ContentHubClient
	{
		IWebMClient _client;
		private string _baseUrl;
		private string _deliveryHostUrl;
		private ILogger logger = Log.ForContext<ContentHubClient>();

		/// <summary>
		/// Create OAuth client. See https://doc.sitecore.com/ch/en/users/content-hub/manage--create-an-oauth-client.html for details on how to create OAuth client in CH
		/// </summary>
		/// <param name="endpointUri">CH host URL</param>
		/// <param name="clientId">OAuth client OD</param>
		/// <param name="clientSecret">OAuth client secret</param>
		/// <param name="userName">username of CH user to authenticate with</param>
		/// <param name="password">password of CH user to authenticate with</param>
		/// <param name="baseUrl">same as CH host URL. Used for image thumbnail urls</param>
		/// <param name="deliveryHostUrl">CH delivery endpoint url to be used for asset public links</param>
		/// <exception cref="ArgumentNullException"></exception>
		public ContentHubClient(string endpointUri, string clientId, string clientSecret, string userName, string password, string baseUrl, string deliveryHostUrl)
		{
			//check parameters for null and empty values
			if (string.IsNullOrEmpty(endpointUri))
				throw new ArgumentNullException(nameof(endpointUri));
			if (string.IsNullOrEmpty(clientId))
				throw new ArgumentNullException(nameof(clientId));
			if (string.IsNullOrEmpty(clientSecret))
				throw new ArgumentNullException(nameof(clientSecret));
			if (string.IsNullOrEmpty(userName))
				throw new ArgumentNullException(nameof(userName));
			if (string.IsNullOrEmpty(password))
				throw new ArgumentNullException(nameof(password));
			if (string.IsNullOrEmpty(baseUrl))
				throw new ArgumentNullException(nameof(baseUrl));
			if (string.IsNullOrEmpty(deliveryHostUrl))
				throw new ArgumentNullException(nameof(deliveryHostUrl));

			_client = CreateClient(endpointUri, clientId, clientSecret, userName, password);
			_baseUrl = baseUrl;
			_deliveryHostUrl = deliveryHostUrl;
		}

		public IWebMClient CreateClient(string endpointUri, string clientId, string clientSecret, string userName, string password)
		{
			Uri endpoint = new Uri(endpointUri);

			// Enter your credentials here
			OAuthPasswordGrant oauth = new OAuthPasswordGrant
			{
				ClientId = clientId,
				ClientSecret = clientSecret,
				UserName = userName,
				Password = password
			};

			// Create the Web SDK client
			IWebMClient client = MClientFactory.CreateMClient(endpoint, oauth);
			return client;
		}

		/// <summary>
		/// Get public link for a given renditoin name
		/// </summary>
		/// <param name="assetId">CH ID of the asset</param>
		/// <param name="renditionName">name of the renditoin</param>
		/// <returns>rendition public link URL</returns>
		public async Task<string> GetRenditionPublicLink(long assetId, string renditionName)
		{
			var renditionAsset = await _client.Entities.GetAsync(assetId).ConfigureAwait(false);
			if (renditionAsset != null && renditionAsset.DefinitionName.Equals("M.Asset", StringComparison.OrdinalIgnoreCase))
			{
				var assetRenditionName = renditionAsset.Renditions.FirstOrDefault(r => r?.Name != null && r.Name.StartsWith(renditionName.ToLower(), StringComparison.OrdinalIgnoreCase))?.Name;

			}

			return string.Empty;
		}

		/// <summary>
		/// Get CH Entity by ID
		/// </summary>
		/// <param name="id"></param>
		/// <returns></returns>
		public async Task<IEntity> GetEntity(long id)
		{
			var entity = await _client.Entities.GetAsync(id).ConfigureAwait(false);
			return entity;
		}

		/// <summary>
		/// Get CH entity, representing renditoin public link
		/// </summary>
		/// <param name="assetId">ID of the asset</param>
		/// <param name="renditionName">name of the rendition</param>
		/// <returns>CH entity, representing renditoin public link</returns>
		public async Task<IEntity> GetAssetPublicLinkEntityAsync(long assetId, string renditionName = null)
		{
			var renditionFallback = string.IsNullOrEmpty(renditionName) ? "downloadoriginal" : renditionName.ToLower();
			var query = new Query()
			{
				Filter = new CompositeQueryFilter()
				{
					Children = new QueryFilter[] {
						new DefinitionQueryFilter() {
							Name = "M.PublicLink"
						},
						new RelationQueryFilter() {
							ParentId = assetId,
							Relation = "AssetToPublicLink"
						},
						new PropertyQueryFilter() {
							Property = "Resource",
							Value = renditionFallback
						}
					}
				}
			};

			var publicLinkEntity = await QuerySingleEntityAsync(query).ConfigureAwait(false);
			if (publicLinkEntity == null)
			{
				publicLinkEntity = await CreatePublicLinkAsync(assetId, renditionName ?? "downloadOriginal");
			}
			return publicLinkEntity;
		}

		public async Task<IEntity> CreatePublicLinkAsync(long assetId, string renditionName)
		{
			// Create new entity
			var publicLink = await _client.EntityFactory.CreateAsync("M.PublicLink");

			// Set the rendition type/resource
			publicLink.SetPropertyValue("resource", renditionName);

			// Link the public link to the asset
			var assetTopublicLinkRelation = publicLink.GetRelation("AssetToPublicLink", RelationRole.Child);
			assetTopublicLinkRelation.SetIds(new long[] { assetId });

			// Save the public link
			long publicLinkId = await _client.Entities.SaveAsync(publicLink);
			publicLink = await _client.Entities.GetAsync(publicLinkId);

			return publicLink;
		}

		/// <summary>
		/// Get the first entity returned by the query or null
		/// </summary>
		/// <param name="query"></param>
		/// <returns></returns>
		public async Task<IEntity> QuerySingleEntityAsync(Query query)
		{
			try
			{
				var relatedEntityItems = (await _client.Querying.QueryAsync(query, EntityLoadConfiguration.Full).ConfigureAwait(false))?
						.Items;

				if (relatedEntityItems != null && relatedEntityItems.Count > 0)
				{
					return relatedEntityItems.FirstOrDefault();
				}
			}
			catch (Exception ex)
			{
				logger.Error(ex, $"Error retrieving entity with query: {query?.ToString()}. Error: {ex.Message}");
				//throw;
			}

			return null;
		}

		/// <summary>
		/// Get formatted field value for CH image value as it is stored in Sitecore CMS
		/// </summary>
		/// <param name="assetEntity"></param>
		/// <param name="publicLinkEntity"></param>
		/// <returns></returns>
		public async Task<string> GetPublicLinkXmlAsync(IEntity assetEntity, IEntity publicLinkEntity)
		{
			var versionHash = await publicLinkEntity.GetPropertyValueAsync<string>("VersionHash").ConfigureAwait(false);
			var resource = await publicLinkEntity.GetPropertyValueAsync<string>("Resource").ConfigureAwait(false);
			var title = await assetEntity.GetPropertyValueAsync<string>("Title").ConfigureAwait(false);
			var relativeUrl = await publicLinkEntity.GetPropertyValueAsync<string>("RelativeUrl").ConfigureAwait(false);
			var fileProperties = await assetEntity.GetPropertyValueAsync<dynamic>("FileProperties").ConfigureAwait(false);
			var renditions = await assetEntity.GetPropertyValueAsync<dynamic>("Renditions").ConfigureAwait(false);
			var fileName = await assetEntity.GetPropertyValueAsync<string>("FileName").ConfigureAwait(false);

			string height = null, width = null;
			if (renditions != null && !string.IsNullOrEmpty(resource))
			{
				var renditionsJson = JObject.Parse(JsonConvert.SerializeObject(renditions));
				width = renditionsJson.SelectToken($"$.{resource}.properties.width")?.ToString();
				height = renditionsJson.SelectToken($"$.{resource}.properties.height")?.ToString();
			}

			if (width == null)
				width = fileProperties?.properties?.width?.Value as string;
			if (height == null)
				height = fileProperties?.properties?.height?.Value as string;

			var altText = string.IsNullOrEmpty(title) ? fileName : title;
			var widthString = string.IsNullOrEmpty(width) ? "" : $"width=\"{width}\"";
			var heightString = string.IsNullOrEmpty(height) ? "" : $"height=\"{height}\"";

			//TODO: do we need  mediaid="""" in imageHtml?
			var imageHtml = $@"<image stylelabs-content-id=""{assetEntity.Id}"" thumbnailsrc=""{_baseUrl}/api/gateway/{assetEntity.Id}/thumbnail"" src=""{_deliveryHostUrl}/api/public/content/{relativeUrl}?v={versionHash}"" stylelabs-content-type=""Image"" alt=""{altText}"" {heightString} {widthString} />";

			return imageHtml;
		}

		/// <summary>
		/// Get all entities with given definition name, created or modified since given date
		/// </summary>
		/// <param name="definitionName"></param>
		/// <param name="modifiedAfter"></param>
		/// <returns></returns>
		public async Task<List<IEntity>> GetEntitiesByDefinition(string definitionName, DateTime modifiedAfter)
		{
			List<IEntity> results = new List<IEntity>();
			List<IEntity> iteratorResults = new List<IEntity>();
			List<long> idResults = new List<long>();
			var query = Query.CreateQuery(entities =>
							from e in entities
							where e.DefinitionName == definitionName && e.ModifiedOn >= modifiedAfter
							select e);

			var scroller = _client.Querying.CreateEntityScroller(query, TimeSpan.FromSeconds(30), EntityLoadConfiguration.Full);

			while (await scroller.MoveNextAsync().ConfigureAwait(false))
			{
				var items = scroller.Current.Items;
				if (items != null && items.Any())
				{
					results.AddRange(items);
				}
			}

			return results;
		}
	}
}