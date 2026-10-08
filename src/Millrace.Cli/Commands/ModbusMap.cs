using System.Globalization;
using System.Text;
using System.Text.Json;
using Millrace.Configuration;
using Millrace.Core;
using Millrace.Core.Catalogue;
using Millrace.Modbus;

namespace Millrace.Cli.Commands;

/// <summary>
/// <c>millrace modbus-map</c> (plan 8): the register map <c>millrace serve</c> serves,
/// as a table, as CSV, or as the <c>tags</c> object of a FUXA Modbus device.
/// The table and the CSV list the entries by area, then address; the FUXA
/// object lists them in directory order, as the map assigns them.
/// </summary>
internal static class ModbusMap
{
    private static readonly ModbusArea[] Areas =
        [ModbusArea.Coils, ModbusArea.DiscreteInputs, ModbusArea.InputRegisters, ModbusArea.HoldingRegisters];

    public static int Run(CliContext context)
    {
        int exit = PlantFile.TryBuild(context, out LoadResult? _, out Simulation? simulation);
        if (exit != ExitCodes.Ok)
        {
            return exit;
        }

        RegisterMap map = RegisterMap.Build(simulation!.IO.Directory);
        string payload = context.CommandLine.Single(CommandTable.MapFormat) switch
        {
            "csv" => Csv(map),
            "fuxa" => Fuxa(map),
            _ => Text(map),
        };
        return context.Emit(payload);
    }

    /// <summary>The entries an area holds, by address.</summary>
    private static IEnumerable<RegisterEntry> ByAddress(RegisterMap map) =>
        Areas.SelectMany(area => map.Entries.Where(e => e.Area == area));

    /// <summary>The FUXA <c>memaddress</c> of an area: the leading digit of its six-digit references.</summary>
    internal static string MemoryAddress(ModbusArea area) => area switch
    {
        ModbusArea.Coils => "000000",
        ModbusArea.DiscreteInputs => "100000",
        ModbusArea.InputRegisters => "300000",
        _ => "400000",
    };

    /// <summary>A FUXA tag id: <c>t_</c> and the Millrace tag name, so it is stable for a given plant and readable in FUXA's editor (R203).</summary>
    internal static string FuxaId(RegisterEntry entry) => "t_" + entry.Tag.Name;

    private static string Access(RegisterEntry entry) => entry.IsWritable ? "ReadWrite" : "ReadOnly";

    private static string Text(RegisterMap map)
    {
        string[] header = ["area", "address", "offset", "type", "tag", "access", "unit", "description"];
        List<string[]> rows = [header];
        rows.AddRange(ByAddress(map).Select(e => new[]
        {
            RegisterMap.Name(e.Area),
            e.Number.ToString(CultureInfo.InvariantCulture),
            e.Offset.ToString(CultureInfo.InvariantCulture),
            e.DataType,
            e.Tag.Name,
            Access(e),
            e.Tag.Unit,
            e.Tag.ClaimedBy.Length > 0 ? $"{e.Tag.Description} (claimed by {e.Tag.ClaimedBy})" : e.Tag.Description,
        }));

        int[] widths = Enumerable.Range(0, header.Length).Select(c => rows.Max(r => r[c].Length)).ToArray();
        var builder = new StringBuilder();
        foreach (string[] row in rows)
        {
            for (int c = 0; c < row.Length; c++)
            {
                bool numeric = c is 1 or 2;
                string cell = numeric ? row[c].PadLeft(widths[c]) : row[c].PadRight(widths[c]);
                builder.Append(c == 0 ? cell : "  " + cell);
            }

            builder.Append('\n');
        }

        return string.Join('\n', builder.ToString().Split('\n').Select(line => line.TrimEnd()));
    }

    private static string Csv(RegisterMap map)
    {
        var builder = new StringBuilder("area,address,offset,type,tag,access,unit,description,claimedBy\n");
        foreach (RegisterEntry e in ByAddress(map))
        {
            string[] cells =
            [
                RegisterMap.Name(e.Area),
                e.Number.ToString(CultureInfo.InvariantCulture),
                e.Offset.ToString(CultureInfo.InvariantCulture),
                e.DataType,
                e.Tag.Name,
                Access(e),
                e.Tag.Unit,
                e.Tag.Description,
                e.Tag.ClaimedBy,
            ];
            builder.Append(string.Join(',', cells.Select(Quote))).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>RFC 4180: a cell with a comma, a quote or a line break is quoted, its quotes doubled.</summary>
    private static string Quote(string cell) =>
        cell.AsSpan().IndexOfAny(",\"\n\r") < 0 ? cell : $"\"{cell.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Fuxa(RegisterMap map)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CatalogueJson.WriterOptions))
        {
            writer.WriteStartObject();
            foreach (RegisterEntry e in map.Entries)
            {
                writer.WriteStartObject(FuxaId(e));
                writer.WriteString("id", FuxaId(e));
                writer.WriteString("name", e.Tag.Name);
                writer.WriteString("type", e.DataType);
                writer.WriteString("memaddress", MemoryAddress(e.Area));
                writer.WriteString("address", e.Number.ToString(CultureInfo.InvariantCulture));
                writer.WriteString("description", e.Tag.Unit.Length > 0 ? $"{e.Tag.Description} ({e.Tag.Unit})" : e.Tag.Description);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return CatalogueJson.Finish(stream);
    }
}
