namespace Dse.Core.Catalogue;

/// <summary>What a parameter holds, and therefore what JSON it accepts.</summary>
public enum ParameterKind
{
    Double,
    Int,
    Bool,
    String,
    /// <summary>An array of strings — a material's state names.</summary>
    StringList,
    Enum,
    /// <summary>A nested object with its own parameters — a rating, an instrument spec.</summary>
    Group,
    /// <summary>An array of such objects — recipe lines.</summary>
    GroupList,
    /// <summary>The id of another component that supplies a capability.</summary>
    Reference,
    /// <summary>The name of a material.</summary>
    Material,
    /// <summary>The name of a state of a sibling material parameter.</summary>
    MaterialState,
    /// <summary>One <c>{ "type": … }</c> object from a slot — a hold condition.</summary>
    Object,
    /// <summary>An array of them — a transform chain.</summary>
    ObjectList,
}
