namespace Millrace.Realtime;

/// <summary>What a subscription does when its consumer falls behind (spec 10.5).</summary>
public enum BackpressurePolicy
{
    /// <summary>Keep only the latest value per tag; never fault. Right for analog values feeding an HMI.</summary>
    Conflate,

    /// <summary>Keep every delta up to capacity, then fault the subscription. Mandatory for events and alarms.</summary>
    Lossless,
}
