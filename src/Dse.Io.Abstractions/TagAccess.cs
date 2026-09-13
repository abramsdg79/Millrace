namespace Dse.Io;

/// <summary>Whether a tag accepts external writes.</summary>
public enum TagAccess
{
    /// <summary>Published by the plant; writes are rejected.</summary>
    ReadOnly,

    /// <summary>Drives a plant input; the image also reflects the current value.</summary>
    ReadWrite,
}
