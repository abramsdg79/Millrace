namespace Dse.Samples.Tests;

/// <summary>A scenario's causal chain, in order, and what must not happen.</summary>
public sealed record Story(IReadOnlyList<EventPattern> Chain, IReadOnlyList<Absence> Absences);

/// <summary>
/// The eight stories of the design (spec section 5), as the measured event logs
/// tell them. Each chain is an ordered subsequence of the scenario's log; each
/// absence is bounded by a start point that must itself occur.
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
    };

    private static EventPattern E(string? source, string code, string fragment = "") => new(source, code, fragment);
}
