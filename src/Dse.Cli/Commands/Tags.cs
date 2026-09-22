using System.Text.Json;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Catalogue;
using Dse.Io;

namespace Dse.Cli.Commands;

internal static class Tags
{
    public static int Run(CliContext context)
    {
        int exit = PlantFile.TryBuild(context, out LoadResult? _, out Simulation? simulation);
        if (exit != ExitCodes.Ok)
        {
            return exit;
        }

        if (!context.Json)
        {
            context.Out.Write(simulation!.IO.Directory.ToText().ReplaceLineEndings("\n"));
            return ExitCodes.Ok;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CatalogueJson.WriterOptions))
        {
            writer.WriteStartArray();
            foreach (TagDescriptor tag in simulation!.IO.Directory.Tags)
            {
                writer.WriteStartObject();
                writer.WriteString("name", tag.Name);
                writer.WriteString("kind", CatalogueJson.Camel(tag.Kind.ToString()));
                writer.WriteString("access", CatalogueJson.Camel(tag.Access.ToString()));
                writer.WriteString("unit", tag.Unit);
                if (tag.HasRange)
                {
                    writer.WriteNumber("rangeLow", tag.RangeLow);
                    writer.WriteNumber("rangeHigh", tag.RangeHigh);
                }

                writer.WriteString("description", tag.Description);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        context.Out.Write(CatalogueJson.Finish(stream));
        return ExitCodes.Ok;
    }
}
