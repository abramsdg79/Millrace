namespace Dse.Io;

/// <summary>The three value kinds a tag can carry. Ordered so <c>default</c> is Bool.</summary>
public enum TagKind
{
    /// <summary>A discrete signal.</summary>
    Bool,

    /// <summary>An analog value in engineering units.</summary>
    Double,

    /// <summary>A counter, an enumeration or any integer.</summary>
    Int64,
}
