namespace Dse.Samples.Tests;

/// <summary>
/// The six wheel-line stories of the design (6b.2 spec criterion 5, as the
/// plan's rulings amend it), as the measured event logs tell them. Each chain
/// is an ordered subsequence of the scenario's log; each absence is bounded by
/// a start point that must itself occur. What the log cannot show — a held
/// billet's temperature, the belt's count, which billet became which wheel —
/// is asserted on a live run in <c>WheelLineTests</c>.
/// </summary>
public static class WheelLineStories
{
    private static readonly EventPattern ZoneStart = E("FCE.ZONE_SP", "WRITE", "Set to 1250.");

    private static readonly EventPattern BeltStart = E("CV.SPEED_SP", "WRITE", "Set to 0.5.");

    private static readonly EventPattern CoilFirstScan = E("GATE.Reject", "WRITE", "Set to false by COIL_REJECT.");

    private static readonly EventPattern RejectOn = E("GATE.Reject", "WRITE", "Set to true by COIL_REJECT.");

    private static readonly EventPattern Starve = E("Billets", "FAULT", "starve injected.");

    private static readonly EventPattern QueueHigh = E("ALM_QUEUE", "ALARM_RAISED", "Hi: ");

    private static readonly EventPattern QueueClear = E("ALM_QUEUE", "ALARM_CLEARED", "Hi: ");

    /// <summary>A press cycle run wholly under slow-cycle 0.9: 40 s of hold in 400 s.</summary>
    private static readonly EventPattern SlowedHold = E("PRESS", "DISCHARGING", "Hold satisfied after 40.00 s of hold (400.00 s elapsed); discharging 1 items.");

    /// <summary>The press cycle the clear at 1140 s falls in: 40.08 s of hold in 65.1 s.</summary>
    private static readonly EventPattern StraddlingHold = E("PRESS", "DISCHARGING", "Hold satisfied after 40.08 s of hold (65.10 s elapsed); discharging 1 items.");

    public static IReadOnlyDictionary<string, Story> All { get; } = new Dictionary<string, Story>(StringComparer.Ordinal)
    {
        ["normal-run"] = new(
            [
                ZoneStart,
                BeltStart,
                CoilFirstScan,
                E("FCE", "DISCHARGING", "Hold satisfied after 50.20 s; discharging 1 items."),
                E("PRESS", "DISCHARGING", "Hold satisfied after 40.00 s; discharging 1 items."),
                Starve,
                E("PRESS", "IDLE"),
            ],
            [
                new(null, E(null, "ALARM_RAISED")),
                new(null, E("GATE", "REJECTED")),
                new(null, E(null, "INTERLOCK_TRIP")),
                new(CoilFirstScan, E("GATE.Reject", "WRITE")),

                // Nothing slows a hold, so no hold message gives a wall time.
                new(null, E(null, "DISCHARGING", " elapsed)")),
            ]),

        ["slow-press"] = new(
            [
                ZoneStart,
                BeltStart,
                CoilFirstScan,
                E("PRESS", "FAULT", "slow-cycle injected: fraction=0.9."),
                QueueHigh,
                SlowedHold,
                E("ALM_PYRO", "ALARM_RAISED", "Hi: 1250.0 above 1130."),
                E("ALM_PYRO", "ALARM_RAISED", "HiHi: 1250.0 above 1150."),
                RejectOn,
                E("GATE", "REJECTED", "Item 13 rejected."),
                E("ALM_PYRO", "ALARM_CLEARED", "HiHi: "),
                E("GATE.Reject", "WRITE", "Set to false by COIL_REJECT."),
                E("PRESS", "FAULT_CLEARED", "slow-cycle cleared."),
                StraddlingHold,
                QueueClear,
                Starve,
            ],
            [
                new(null, E("ALM_PYRO", "ALARM_RAISED"), E("PRESS", "FAULT", "slow-cycle")),
                new(null, E(null, "DISCHARGING", " elapsed)"), E("PRESS", "FAULT", "slow-cycle")),
                new(StraddlingHold, E(null, "DISCHARGING", " elapsed)")),
                new(E("GATE", "REJECTED"), E("GATE", "REJECTED")),
                new(E("PRESS", "FAULT_CLEARED"), E("ALM_PYRO", "ALARM_RAISED")),
                new(QueueClear, E("ALM_QUEUE", "ALARM_RAISED")),
                new(null, E("Bay", "FULL")),
                new(null, E(null, "INTERLOCK_TRIP")),
            ]),

        ["stuck-kicker"] = new(
            [
                ZoneStart,
                BeltStart,
                CoilFirstScan,
                E("PRESS", "FAULT", "slow-cycle injected: fraction=0.9."),
                E("GATE", "FAULT", "stuck injected."),
                QueueHigh,
                SlowedHold,
                E("ALM_PYRO", "ALARM_RAISED", "HiHi: 1250.0 above 1150."),
                RejectOn,
                E("PRESS", "FAULT_CLEARED", "slow-cycle cleared."),
                StraddlingHold,
                E("ALM_PYRO", "ALARM_CLEARED", "HiHi: "),
                E("GATE.Reject", "WRITE", "Set to false by COIL_REJECT."),
                QueueClear,
                Starve,
            ],
            [
                new(null, E("GATE", "REJECTED")),
                new(QueueClear, E("ALM_QUEUE", "ALARM_RAISED")),
                new(null, E(null, "INTERLOCK_TRIP")),
            ]),

        ["press-jam"] = new(
            [
                ZoneStart,
                BeltStart,
                CoilFirstScan,
                E("PRESS", "FAULT", "discharge-jam injected."),
                QueueHigh,
                E("PRESS", "FAULT_CLEARED", "discharge-jam cleared."),
                E("ALM_PYRO", "ALARM_RAISED", "HiHi: 1250.0 above 1150."),
                RejectOn,
                E("GATE", "REJECTED", "Item 12 rejected."),
                E("GATE.Reject", "WRITE", "Set to false by COIL_REJECT."),
                QueueClear,
                Starve,
            ],
            [
                // While the press is jammed the over-soaking billet is in the furnace,
                // behind a good one waiting on the gate: the pyrometer cannot see it.
                new(E("PRESS", "FAULT", "discharge-jam"), E("ALM_PYRO", "ALARM_RAISED"), E("PRESS", "FAULT_CLEARED")),
                new(E("PRESS", "FAULT", "discharge-jam"), E("GATE", "REJECTED"), E("PRESS", "FAULT_CLEARED")),
                new(E("GATE", "REJECTED"), E("GATE", "REJECTED")),
                new(QueueClear, E("ALM_QUEUE", "ALARM_RAISED")),
                new(null, E("Bay", "FULL")),
                new(null, E(null, "INTERLOCK_TRIP")),
            ]),

        ["zone-low"] = new(
            [
                ZoneStart,
                BeltStart,
                CoilFirstScan,
                E("FCE.ZONE_SP", "WRITE", "Set to 1050."),
                E("ALM_ZONE", "ALARM_RAISED", "Lo: 1050.0 below 1200."),
                E("PRESS", "IDLE"),
            ],
            [
                // The zone alarm's on-delay keeps it quiet at power-up, before the line-start write lands.
                new(null, E("ALM_ZONE", "ALARM_RAISED"), E("FCE.ZONE_SP", "WRITE", "Set to 1050.")),
                new(E("FCE.ZONE_SP", "WRITE", "Set to 1050."), E("FCE", "DISCHARGING")),
                new(E("FCE.ZONE_SP", "WRITE", "Set to 1050."), E("PRESS", "FILLING")),
                new(null, E("GATE", "REJECTED")),
                new(null, E(null, "INTERLOCK_TRIP")),
            ]),

        ["pyro-fail-high"] = new(
            [
                ZoneStart,
                BeltStart,
                CoilFirstScan,
                E("PYRO", "FAULT", "fail-high injected."),
                E("ALM_PYRO", "ALARM_RAISED", "HiHi: 1400.0 above 1150."),
                RejectOn,
                E("GATE", "REJECTED", "Item 5 rejected."),
                E("GATE", "REJECTED", "Item 6 rejected."),
                E("GATE", "REJECTED", "Item 7 rejected."),
                E("GATE", "REJECTED", "Item 8 rejected."),
                E("Bay", "FULL", "Capacity reached; accepting nothing more."),
                E("INT_BAY", "INTERLOCK_TRIP", "Bay.Full abnormal."),
                E("Billets.Enabled", "WRITE", "Set to false by INT_BAY."),
                E("INT_BAY.Reset", "WRITE", "Set to true."),
                E("INT_BAY", "RESET_REFUSED", "Reset refused: Bay.Full is not normal."),
                E("INT_BAY.Reset", "WRITE", "Set to false."),
            ],
            [
                new(E("PYRO", "FAULT"), E("GATE.Reject", "WRITE", "Set to false by COIL_REJECT.")),
                new(E("GATE", "REJECTED"), E("PRESS", "FILLING")),
                new(E("INT_BAY", "INTERLOCK_TRIP"), E("FCE", "FILLING")),

                // The bay cannot be emptied from a scenario, so the operator's reset is refused.
                new(null, E("INT_BAY", "INTERLOCK_RESET")),
            ]),
    };

    private static EventPattern E(string? source, string code, string fragment = "") => new(source, code, fragment);
}
