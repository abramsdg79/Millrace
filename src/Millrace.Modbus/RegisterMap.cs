using Millrace.Io;

namespace Millrace.Modbus;

/// <summary>
/// A plant's Modbus register map (plan 8 spec §1, criterion 1). Built from the
/// tag directory, in directory order: a Bool tag is one bit — a coil when it
/// is read-write, a discrete input when it is read-only; a Double or an Int64
/// tag is two registers — holding when read-write, input when read-only.
/// Within each area addresses are assigned from 0 with no gaps. Access is the
/// directory's published access, so a tag a block claims maps read-only. The
/// same directory always gives the same map.
/// </summary>
public sealed class RegisterMap
{
    /// <summary>The 0-based offsets a Modbus request can address in one area: 0 to 65535.</summary>
    public const int AreaCapacity = 65536;

    private readonly RegisterEntry[] _entries;
    private readonly RegisterEntry?[][] _byAddress;
    private readonly Dictionary<string, RegisterEntry> _byName = new(StringComparer.Ordinal);

    private RegisterMap(ITagDirectory directory, RegisterEntry[] entries, RegisterEntry?[][] byAddress)
    {
        Directory = directory;
        _entries = entries;
        _byAddress = byAddress;
        foreach (RegisterEntry entry in entries)
        {
            _byName.Add(entry.Tag.Name, entry);
        }
    }

    /// <summary>The directory the map was built from.</summary>
    public ITagDirectory Directory { get; }

    /// <summary>Every tag's entry, in directory order.</summary>
    public IReadOnlyList<RegisterEntry> Entries => _entries;

    /// <summary>Builds the map. Throws <see cref="ArgumentException"/> when an area would need more than 65 536 addresses.</summary>
    public static RegisterMap Build(ITagDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        int[] next = new int[4];
        var entries = new RegisterEntry[directory.Count];
        for (int i = 0; i < directory.Count; i++)
        {
            TagDescriptor tag = directory[i];
            ModbusArea area = AreaOf(tag);
            entries[i] = new RegisterEntry(area, next[(int)area], tag);
            next[(int)area] += entries[i].Width;
            if (next[(int)area] > AreaCapacity)
            {
                throw new ArgumentException(
                    $"The plant's {Name(area)} need more than {AreaCapacity} addresses; a Modbus area holds no more.",
                    nameof(directory));
            }
        }

        var byAddress = new RegisterEntry?[4][];
        for (int a = 0; a < 4; a++)
        {
            byAddress[a] = new RegisterEntry?[next[a]];
        }

        foreach (RegisterEntry entry in entries)
        {
            for (int w = 0; w < entry.Width; w++)
            {
                byAddress[(int)entry.Area][entry.Offset + w] = entry;
            }
        }

        return new RegisterMap(directory, entries, byAddress);
    }

    /// <summary>The area a tag maps to.</summary>
    public static ModbusArea AreaOf(TagDescriptor tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        bool writable = tag.Access == TagAccess.ReadWrite;
        return tag.Kind == TagKind.Bool
            ? writable ? ModbusArea.Coils : ModbusArea.DiscreteInputs
            : writable ? ModbusArea.HoldingRegisters : ModbusArea.InputRegisters;
    }

    /// <summary>The plural name of an area, as the text map and messages print it: <c>coils</c>, <c>discrete inputs</c>, <c>input registers</c>, <c>holding registers</c>.</summary>
    public static string Name(ModbusArea area) => area switch
    {
        ModbusArea.Coils => "coils",
        ModbusArea.DiscreteInputs => "discrete inputs",
        ModbusArea.InputRegisters => "input registers",
        _ => "holding registers",
    };

    /// <summary>How many bits or registers of <paramref name="area"/> the plant occupies; every offset below it is mapped.</summary>
    public int Size(ModbusArea area) => _byAddress[(int)area].Length;

    /// <summary>The entry occupying a 0-based offset, or null past the end of the area.</summary>
    public RegisterEntry? At(ModbusArea area, int offset)
    {
        RegisterEntry?[] addresses = _byAddress[(int)area];
        return offset >= 0 && offset < addresses.Length ? addresses[offset] : null;
    }

    /// <summary>A tag's entry by name.</summary>
    public bool TryFind(string name, out RegisterEntry entry)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(name, out entry!);
    }

    /// <summary>A tag's entry by name; throws <see cref="KeyNotFoundException"/> if the plant has no such tag.</summary>
    public RegisterEntry Find(string name) =>
        TryFind(name, out RegisterEntry entry) ? entry : throw new KeyNotFoundException($"No tag '{name}' in the register map.");
}
