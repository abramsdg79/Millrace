using Dse.Core.Catalogue;

namespace Dse.Control.Catalogue;

/// <summary>The two things that end a sequencer step, as objects in the <c>transition</c> slot.</summary>
public static class TransitionCatalogue
{
    private static readonly string[] Operators = ["==", "!=", "<", "<=", ">", ">="];

    /// <summary><c>{ "type": "when", "tag": …, "op": …, "value": … }</c>.</summary>
    public static ObjectDescriptor When { get; } = new(
        ControlCatalogue.TransitionSlot,
        "when",
        "Ends the step when a tag compares with a value as asked.",
        p => StepTransition.When(p.Tag("tag"), Operator(p.String("op")), p.Value("value")))
    {
        Parameters =
        [
            Param.Tag("tag", "The tag compared."),
            Param.Enum("op", "How the tag is compared. A Bool tag allows only == and !=.", Operators),
            Param.Value("value", "What the tag is compared with.", "tag"),
        ],
    };

    /// <summary><c>{ "type": "after", "delayS": … }</c>.</summary>
    public static ObjectDescriptor After { get; } = new(
        ControlCatalogue.TransitionSlot,
        "after",
        "Ends the step when its clock reaches a delay.",
        p => StepTransition.After(TimeSpan.FromSeconds(p.Double("delayS"))))
    {
        Parameters = [ControlCatalogue.Seconds("delayS", "How long the step runs.")],
    };

    private static PredicateOperator Operator(string op) => op switch
    {
        "==" => PredicateOperator.Equal,
        "!=" => PredicateOperator.NotEqual,
        "<" => PredicateOperator.Less,
        "<=" => PredicateOperator.LessOrEqual,
        ">" => PredicateOperator.Greater,
        _ => PredicateOperator.GreaterOrEqual,
    };
}
