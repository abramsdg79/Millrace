namespace Dse.Components.Instruments;

/// <summary>
/// What a transmitter reports about its own signal. Maps onto the I/O
/// layer's quality code. This library emits Good and Bad; Uncertain is
/// reserved for plausibility checks that arrive with the I/O layer.
/// </summary>
public enum InstrumentHealth
{
    Good,
    Uncertain,
    Bad,
}
