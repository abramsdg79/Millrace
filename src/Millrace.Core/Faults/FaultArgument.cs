namespace Millrace.Core.Faults;

/// <summary>One named numeric argument to a fault.</summary>
public readonly record struct FaultArgument(string Name, double Value);
