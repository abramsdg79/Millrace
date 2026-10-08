namespace Millrace.Core.Catalogue;

/// <summary>A signal port as the catalogue describes it. Build with <see cref="PortSpec"/>.</summary>
public sealed record PortDescriptor(
    string Name,
    PortDirection Direction,
    string ValueType,
    string Unit,
    string Description,
    bool Required,
    PortRepeat? Repeat);
