namespace Millrace.Control;

/// <summary>The three IEC 61131-3 timers.</summary>
public enum TimerMode
{
    /// <summary>TON. <c>Q</c> goes high once the input has held high for the preset.</summary>
    OnDelay,

    /// <summary>TOF. <c>Q</c> stays high for the preset after the input falls.</summary>
    OffDelay,

    /// <summary>TP. A preset-long pulse on a rising edge, not retriggerable while it runs.</summary>
    Pulse,
}
