using System.Collections.Generic;

/// <summary>
/// Entity download configuration as it's defined in the appsettings.json file
/// </summary>
public class EntityConfiguration
{
	public string EntityDefinition { get; set; }
	public List<string> IncludedFields { get; set; }
	public Dictionary<string, string> RenditionRelations { get; set; }
	public List<string> Renditions { get; set; }
}
