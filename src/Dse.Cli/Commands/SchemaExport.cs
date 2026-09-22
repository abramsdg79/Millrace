using Dse.Configuration;

namespace Dse.Cli.Commands;

internal static class SchemaExport
{
    public static int Run(CliContext context) => context.Emit(PlantSchema.Generate(context.Catalogue));
}
