namespace Millrace.Control;

/// <summary>
/// One configured limit. The alarm raises when the value crosses
/// <paramref name="Value"/> and stays across for <paramref name="OnDelay"/>,
/// and clears when it recrosses by <paramref name="Deadband"/>.
/// </summary>
/// <param name="Kind">Which limit this is.</param>
/// <param name="Value">The limit, in the tag's engineering unit.</param>
/// <param name="Deadband">How far back inside the limit the value must come before the alarm clears. Zero is allowed.</param>
/// <param name="OnDelay">How long the value must stay across before the alarm raises. Zero is allowed.</param>
public sealed record AlarmLimit(AlarmLimitKind Kind, double Value, double Deadband, TimeSpan OnDelay);
