namespace Millrace.Core.Flow;

/// <summary>
/// The simulation's item id counter. Ids are issued in creation order, so a
/// replay mints the same ids for the same items.
/// </summary>
public sealed class ItemIdSequence
{
    private long _next = 1;

    /// <summary>How many ids have been issued so far.</summary>
    public long Issued => _next - 1;

    public long Next() => _next++;
}
