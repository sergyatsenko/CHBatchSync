using System;

namespace GVI.ContentHub.Sync.WebJob.Core
{
    /// <summary>
    /// Helper class for generating Sitecore GUIDs
    /// </summary>
	public static class HashUtil
    {
        public static Guid GetSitecoreGuid(Guid namespaceGuid, long input)
        {
            return GuidUtils.Create(namespaceGuid, $"{input}{input}{input}");
        }
    }
}
