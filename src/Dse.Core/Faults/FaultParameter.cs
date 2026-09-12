namespace Dse.Core.Faults;

/// <summary>One parameter of a fault, with the default used when an injection omits it.</summary>
public sealed record FaultParameter(string Name, string Unit, double DefaultValue, string Description);
