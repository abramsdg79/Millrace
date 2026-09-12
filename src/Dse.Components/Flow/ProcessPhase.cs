namespace Dse.Components.Flow;

/// <summary>Where a batch unit is in its cycle.</summary>
public enum ProcessPhase
{
    /// <summary>Empty and ready; the first deposit starts filling.</summary>
    Idle,
    Filling,
    Processing,
    Discharging,
}
