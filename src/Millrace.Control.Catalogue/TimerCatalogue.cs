using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue;

/// <summary>The <see cref="Timer"/> in a plant file: <c>{ "type": "timer", … }</c>.</summary>
public static class TimerCatalogue
{
    private static readonly string[] Modes = ["on-delay", "off-delay", "pulse"];

    public static BlockDescriptor Descriptor { get; } = new(
        "timer",
        "An IEC 61131-3 timer over one Bool tag: on-delay (TON), off-delay (TOF) or pulse (TP).",
        (id, p) =>
        [
            ControlCatalogue.Output(id, "Q", TagKind.Bool, string.Empty, "Timer output"),
            ControlCatalogue.Output(id, "ET", TagKind.Double, "s", "Elapsed time, quantised to the scan period"),
        ],
        (id, period, p) => new Timer(id, Mode(p.String("mode")), p.Tag("input"), TimeSpan.FromSeconds(p.Double("presetS")), period))
    {
        Parameters =
        [
            Param.Enum(
                "mode",
                "on-delay raises Q once the input has held true for the preset; off-delay holds Q for the preset after the input falls; pulse gives one preset-long pulse on a rising edge.",
                Modes),
            Param.Tag("input", "The Bool tag timed.", TagKind.Bool),
            ControlCatalogue.Seconds("presetS", "The delay or pulse length. Zero acts immediately."),
        ],
    };

    private static TimerMode Mode(string mode) => mode switch
    {
        "on-delay" => TimerMode.OnDelay,
        "off-delay" => TimerMode.OffDelay,
        _ => TimerMode.Pulse,
    };
}
