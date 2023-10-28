using System;
using System.Collections.Generic;

namespace GVI.ContentHub.Sync.WebJob.Core
{
    /// <summary>
    /// Entity data to be saved in the resulting JSON file.
    /// </summary>
    public class EntityContent
    {
        public EntityContent(long id, string identifier, DateTime? timestamp, Guid namespaceGuid)
        {
            this.id = id;
            sitecoreid = HashUtil.GetSitecoreGuid(namespaceGuid, id);
            this.identifier = identifier;
            lastmodified = timestamp.HasValue ? timestamp.Value.ToString("o") : DateTime.MinValue.ToString("o");
        }
        public long id { get; set; }
        public string identifier { get; set; }
        public Guid sitecoreid { get; set; }
        public string lastmodified { get; set; }

        public Dictionary<string, object> fields { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, string> relations { get; set; } = new Dictionary<string, string>();
    }
}