namespace Millrace.Core.Catalogue;

/// <summary>A telemetry channel a type registers, named relative to the component.</summary>
public sealed record TelemetryKey(string Name, string Unit = "");
