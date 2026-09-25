namespace Dse.Samples.Tests;

/// <summary>A scenario's causal chain, in order, and what must not happen.</summary>
public sealed record Story(IReadOnlyList<EventPattern> Chain, IReadOnlyList<Absence> Absences);

/// <summary>
/// The eight stories of the design (spec section 5), as the measured event logs
/// tell them. Each chain is an ordered subsequence of the scenario's log; each
/// absence is bounded by a start point that must itself occur. Feed-starve's
/// fall in transport order is state, not events: see
/// <c>MineConveyorTests.AStarvedFeedEmptiesTheBeltsInTransportOrder</c>.
/// </summary>
public static class Stories
{
    private static readonly EventPattern StartComplete = E("SEQ_START", "SEQUENCE_COMPLETE");

    public static IReadOnlyDictionary<string, Story> All { get; } = new Dictionary<string, Story>(StringComparer.Ordinal)
    {
        ["normal-start-stop"] = new(
            [
                E("SEQ_START", "STEP_ENTERED", "2: Start CV003."),
                E("CV003.Starter", "CONTACTOR_CLOSED"),
                E("SEQ_START", "STEP_ENTERED", "3: Start CV002."),
                E("CV002.Starter", "CONTACTOR_CLOSED"),
                E("SEQ_START", "STEP_ENTERED", "4: Start CV001."),
                E("CV001.Starter", "CONTACTOR_CLOSED"),
                E("SEQ_START", "STEP_ENTERED", "5: Start the feed."),
                E("Feed.Enabled", "WRITE", "Set to true by SEQ_START."),
                StartComplete,
                E("SEQ_STOP", "STEP_ENTERED", "1: Stop the feed."),
                E("Feed.Enabled", "WRITE", "Set to false by SEQ_STOP."),
                E("CV001.Starter", "CONTACTOR_OPENED"),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Stopped abnormal."),
                E("CV002.Starter", "CONTACTOR_OPENED"),
                E("CV002.ZeroSpeed", "ZERO_SPEED"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV002.Stopped abnormal."),
                E("CV003.Starter", "CONTACTOR_OPENED"),
                E("CV003.ZeroSpeed", "ZERO_SPEED"),
                E("INT_CV002", "INTERLOCK_TRIP", "CV003.Stopped abnormal."),
                E("SEQ_STOP", "SEQUENCE_COMPLETE"),
            ],
            [
                new(StartComplete, E(null, "INTERLOCK_TRIP"), E("SEQ_STOP.Start", "WRITE", "Set to true.")),
                new(null, E(null, "ALARM_RAISED")),
                new(null, E(null, "SEQUENCE_FAULTED")),
            ]),

        ["pull-key"] = new(
            [
                StartComplete,
                E("CV002.PullKey1", "PULLKEY_PULLED"),
                E("CV002.Safety", "SAFETY_TRIP", "Channel1 open"),
                E("CV002.Starter", "CONTACTOR_OPENED"),
                E("PERM_CV002", "PERMISSIVE_LOST", "CV002.SafetyOk dropped."),
                E("INT_CV002", "INTERLOCK_TRIP", "PERM_CV002.Ok abnormal."),
                E("CV002.ZeroSpeed", "ZERO_SPEED"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV002.Stopped abnormal."),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Stopped abnormal."),
                E("Feed.Enabled", "WRITE", "Set to false by INT_FEED."),
            ],
            [
                new(E("CV002.PullKey1", "PULLKEY_PULLED"), E("INT_CV003", "INTERLOCK_TRIP")),
                new(E("CV002.PullKey1", "PULLKEY_PULLED"), E("CV003.Starter", "CONTACTOR_OPENED")),
                new(E("CV002.PullKey1", "PULLKEY_PULLED"), E("CV003.ZeroSpeed", "ZERO_SPEED")),
            ]),

        ["e-stop"] = new(
            [
                StartComplete,
                E("CV001.EStop", "ESTOP_PRESSED"),
                E("CV001.Safety", "SAFETY_TRIP", "Channel3 open"),
                E("CV001.Starter", "CONTACTOR_OPENED"),
                E("PERM_CV001", "PERMISSIVE_LOST", "CV001.SafetyOk dropped."),
                E("INT_CV001", "INTERLOCK_TRIP", "PERM_CV001.Ok abnormal."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Stopped abnormal."),
                E("Feed.Enabled", "WRITE", "Set to false by INT_FEED."),
            ],
            [
                new(E("CV001.EStop", "ESTOP_PRESSED"), E("INT_CV002", "INTERLOCK_TRIP")),
                new(E("CV001.EStop", "ESTOP_PRESSED"), E("INT_CV003", "INTERLOCK_TRIP")),
                new(E("CV001.EStop", "ESTOP_PRESSED"), E("CV002.Starter", "CONTACTOR_OPENED")),
                new(E("CV001.EStop", "ESTOP_PRESSED"), E("CV003.Starter", "CONTACTOR_OPENED")),
            ]),

        ["overload"] = new(
            [
                StartComplete,
                E("CV003.Motor", "FAULT", "thermal-bias injected"),
                E("CV003.Starter", "OVERLOAD_TRIP"),
                E("CV003.Starter", "CONTACTOR_OPENED"),
                E("INT_CV003", "INTERLOCK_TRIP", "CV003.Tripped abnormal."),
                E("CV003.ZeroSpeed", "ZERO_SPEED"),
                E("INT_CV002", "INTERLOCK_TRIP", "CV003.Stopped abnormal."),
                E("CV002.Start", "WRITE", "Set to false by INT_CV002."),
                E("CV002.ZeroSpeed", "ZERO_SPEED"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV002.Stopped abnormal."),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Stopped abnormal."),
            ],
            [
                new(E("CV003.Motor", "FAULT"), E("ALM_CV003", "ALARM_RAISED")),
            ]),

        ["chute-blockage"] = new(
            [
                StartComplete,
                E("CH1", "FAULT", "blockage injected."),
                E("CH1", "FULL"),
                E("ALM_CV001", "ALARM_RAISED", "above 7.5."),
                E("ALM_CV001", "ALARM_RAISED", "above 8.6."),
                E("CV001.Starter", "OVERLOAD_TRIP"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV001.Tripped abnormal."),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Stopped abnormal."),
            ],
            [
                new(E("CH1", "FAULT"), E(null, "INTERLOCK_TRIP"), E("CV001.Starter", "OVERLOAD_TRIP")),
                new(E("CH1", "FAULT"), E("INT_CV002", "INTERLOCK_TRIP")),
                new(E("CH1", "FAULT"), E("INT_CV003", "INTERLOCK_TRIP")),
            ]),

        ["failed-zero-speed"] = new(
            [
                StartComplete,
                E("CV002.ZeroSpeed", "FAULT", "fail-low injected."),
                E("CV002.ZeroSpeed", "ZERO_SPEED"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV002.Stopped abnormal."),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Stopped abnormal."),
            ],
            [
                new(E("CV002.ZeroSpeed", "FAULT"), E("CV002.Starter", "CONTACTOR_OPENED")),
                new(E("CV002.ZeroSpeed", "FAULT"), E("CV003.Starter", "CONTACTOR_OPENED")),
                new(E("CV002.ZeroSpeed", "FAULT"), E("INT_CV002", "INTERLOCK_TRIP")),
                new(E("CV002.ZeroSpeed", "FAULT"), E("INT_CV003", "INTERLOCK_TRIP")),
            ]),

        ["welded-contactor"] = new(
            [
                StartComplete,
                E("CV003.Starter", "FAULT", "contactor-welded injected."),
                E("SEQ_STOP", "STEP_ENTERED", "6: Stop CV003."),
                E("CV003.Start", "WRITE", "Set to false by SEQ_STOP."),
                E("SEQ_STOP", "SEQUENCE_FAULTED", "Step 6 timed out after 20 s."),
                E("CV003.EStop", "ESTOP_PRESSED"),
                E("CV003.Safety", "SAFETY_TRIP", "Channel3 open"),
                E("INT_CV003", "INTERLOCK_TRIP", "PERM_CV003.Ok abnormal."),
                E("CV003.Start", "WRITE", "Set to false by INT_CV003."),
            ],
            [
                new(E("CV003.Starter", "FAULT"), E("CV003.Starter", "CONTACTOR_OPENED")),
                new(E("CV003.Starter", "FAULT"), E("CV003.Motor", "DE_ENERGISED")),
                new(E("CV003.Starter", "FAULT"), E("CV003.ZeroSpeed", "ZERO_SPEED")),
                new(E("CV003.Starter", "FAULT"), E("CV003.Motor", "STOPPED")),
            ]),

        ["feed-starve"] = new(
            [
                StartComplete,
                E("Feed", "FAULT", "starve injected."),
            ],
            [
                new(StartComplete, E(null, "INTERLOCK_TRIP")),
                new(StartComplete, E(null, "ALARM_RAISED")),
                new(StartComplete, E(null, "CONTACTOR_OPENED")),
            ]),
    };

    private static EventPattern E(string? source, string code, string fragment = "") => new(source, code, fragment);
}
