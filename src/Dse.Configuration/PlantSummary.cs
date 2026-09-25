namespace Dse.Configuration;

/// <summary>What a valid plant file declared. Counts are of entries in the file, not of flattened leaves.</summary>
public sealed record PlantSummary(int Components, int SignalLinks, int FlowLinks, int ExplicitTags, int Controllers = 0);
