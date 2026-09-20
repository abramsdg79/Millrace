using Dse.Core.Flow;

namespace Dse.Core.Catalogue;

/// <summary>A material inlet or outlet as the catalogue describes it.</summary>
public sealed record FlowPortDescriptor(
    string Name,
    PortDirection Direction,
    PayloadKind Payload,
    string Description,
    PortRepeat? Repeat);
