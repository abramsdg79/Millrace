namespace Dse.Core.Testing;

/// <summary>
/// What conformance found. <paramref name="Mismatches"/> is empty when every
/// descriptor matches what it builds; <paramref name="BuiltTypes"/> is every CLR
/// type a factory produced, for a "does everything have a descriptor" sweep.
/// </summary>
public sealed record ConformanceReport(IReadOnlyList<string> Mismatches, IReadOnlyList<Type> BuiltTypes);
