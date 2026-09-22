using Dse.Core.Catalogue;

namespace Dse.Cli.Commands;

internal static class CatalogExport
{
    public static int Run(CliContext context) => context.Emit(CatalogueJson.Export(context.Catalogue));
}
