using Millrace.Configuration;

namespace Millrace.Cli.Commands;

internal static class SchemaExport
{
    public static int Run(CliContext context) => context.Emit(PlantSchema.Generate(context.Catalogue));
}
