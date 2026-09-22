namespace Dse.Scenarios;

/// <summary>
/// What comparing a run's log with a committed one came to. A mismatch reports
/// where, not what the whole difference is: the whole actual log is written
/// beside the golden, and a diff tool is better at the rest.
/// </summary>
/// <param name="Matched">True when the two logs are the same once normalised.</param>
/// <param name="FirstDifferentLine">One-based; zero when they match.</param>
/// <param name="ExpectedLines">Lines in the committed log.</param>
/// <param name="ActualLines">Lines in the run's log.</param>
/// <param name="Report">The human report, ending in a newline. Never empty.</param>
public sealed record LogComparison(
    bool Matched,
    int FirstDifferentLine,
    int ExpectedLines,
    int ActualLines,
    string Report);
