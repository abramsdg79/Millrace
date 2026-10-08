namespace Millrace.Control;

/// <summary>
/// One Bool tag a permissive or an interlock watches, with the value that means
/// "normal". A conveyor's <c>SafetyOk</c> is normal true; its <c>Tripped</c> is
/// normal false.
/// </summary>
/// <param name="Tag">The full name of a Bool tag.</param>
/// <param name="Normal">The value that means the condition is satisfied.</param>
public sealed record Condition(string Tag, bool Normal);
