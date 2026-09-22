using System.Globalization;
using Dse.Configuration;
using Dse.Core;

namespace Dse.Cli.Commands;

internal static class Validate
{
    public static int Run(CliContext context)
    {
        int exit = PlantFile.TryBuild(context, out LoadResult? result, out Simulation? simulation);
        if (exit != ExitCodes.Ok)
        {
            return exit;
        }

        PlantSummary summary = result!.Summary!;
        int leaves = simulation!.Components.Count;
        int tags = simulation.IO.Directory.Count;
        double stepMs = result.Options!.TimeStep.TotalMilliseconds;
        string path = context.CommandLine.Argument!;

        if (context.Json)
        {
            context.Out.Write(PlantFile.ValidationJson(path, result, w =>
            {
                w.WriteNumber("components", summary.Components);
                w.WriteNumber("leaves", leaves);
                w.WriteNumber("signalLinks", summary.SignalLinks);
                w.WriteNumber("flowLinks", summary.FlowLinks);
                w.WriteNumber("tags", tags);
                w.WriteNumber("explicitTags", summary.ExplicitTags);
                w.WriteNumber("timeStepMs", stepMs);
            }));
            return ExitCodes.Ok;
        }

        context.Out.Write(string.Create(CultureInfo.InvariantCulture, $"""
            OK  {path}
              components    {summary.Components}
              leaves        {leaves}
              signal links  {summary.SignalLinks}
              flow links    {summary.FlowLinks}
              tags          {tags} ({summary.ExplicitTags} explicit)
              time step     {stepMs} ms

            """).ReplaceLineEndings("\n"));
        return ExitCodes.Ok;
    }
}
