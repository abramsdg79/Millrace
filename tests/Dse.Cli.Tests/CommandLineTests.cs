namespace Dse.Cli.Tests;

public class CommandLineTests
{
    [Fact]
    public void NoArgumentsPrintsGeneralHelp()
    {
        CliRun run = Cli.Run();

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Contains("Commands:", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void HelpPrintsGeneralHelpAndSucceeds(string word)
    {
        CliRun run = Cli.Run(word);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Contains("catalog export", run.Out, StringComparison.Ordinal);
        Assert.Contains("schema export", run.Out, StringComparison.Ordinal);
        Assert.Contains("validate <plant.json>", run.Out, StringComparison.Ordinal);
        Assert.Contains("tags <plant.json>", run.Out, StringComparison.Ordinal);
        Assert.Contains("Exit codes", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ACommandsHelpListsItsOptions()
    {
        CliRun run = Cli.Run("validate", "--help");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Contains("--format", run.Out, StringComparison.Ordinal);
        Assert.Contains("--time-step", run.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("--out", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("catalog")]
    [InlineData("catalog import")]
    public void AnUnknownCommandIsAUsageError(string line)
    {
        CliRun run = Cli.Run(line.Split(' '));

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("dse help", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownOptionNamesTheOnesTheCommandHas()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("minimal.json"), "--fromat", "json");

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Contains("--fromat", run.Err, StringComparison.Ordinal);
        Assert.Contains("--format", run.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("validate")]
    [InlineData("validate a.json b.json")]
    [InlineData("catalog export extra")]
    [InlineData("validate a.json --format")]
    [InlineData("validate a.json --format xml")]
    [InlineData("validate a.json --format json --format text")]
    [InlineData("validate a.json --time-step fast")]
    [InlineData("validate a.json --time-step 0")]
    public void MalformedInvocationsAreUsageErrors(string line)
    {
        CliRun run = Cli.Run(line.Split(' '));

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.NotEmpty(run.Err);
    }

    [Fact]
    public void AnOptionMayUseAnEqualsSign()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("minimal.json"), "--format=json");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.StartsWith("{", run.Out, StringComparison.Ordinal);
    }
}
