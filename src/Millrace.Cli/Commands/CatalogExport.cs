using Millrace.Core.Catalogue;

namespace Millrace.Cli.Commands;

internal static class CatalogExport
{
    public static int Run(CliContext context) => context.Emit(CatalogueJson.Export(context.Catalogue));
}
