namespace Millrace.Core.Catalogue;

/// <summary>The object slots Core knows about. A slot is only a name; a module may introduce more.</summary>
public static class ObjectSlots
{
    /// <summary>An <c>IMaterialTransform</c>.</summary>
    public const string Transform = "transform";

    /// <summary>A hold condition of a process unit.</summary>
    public const string Hold = "hold";
}
