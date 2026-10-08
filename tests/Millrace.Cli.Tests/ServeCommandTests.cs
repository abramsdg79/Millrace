using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Millrace.Cli.Tests;

/// <summary>
/// <c>millrace serve</c>'s command line, loading and stopping. A cancelled token
/// stops the run before its first tick, so these tests never wait on the
/// wall clock; the mine-conveyor run over a real connection is
/// <c>Millrace.Samples.Tests.ServeTests</c>.
/// </summary>
public partial class ServeCommandTests
{
    [GeneratedRegex(@"^Listening on 127\.0\.0\.1:(\d+) \(Modbus TCP, any unit id\) at 1x real time\. Press Ctrl\+C to stop\.$", RegexOptions.Multiline)]
    private static partial Regex ListeningLine();

    private static CliRun Stopped(params string[] args)
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        return Cli.Run(stop.Token, args);
    }

    [Theory]
    [InlineData("serve a.json --port x")]
    [InlineData("serve a.json --port 65536")]
    [InlineData("serve a.json --port -1")]
    [InlineData("serve a.json --port 50.5")]
    [InlineData("serve a.json --speed 0")]
    [InlineData("serve a.json --speed 0.0009")]
    [InlineData("serve a.json --bind not-an-address")]
    [InlineData("serve a.json --speed -2")]
    [InlineData("serve a.json --speed fast")]
    [InlineData("serve a.json --speed NaN")]
    [InlineData("serve a.json --speed Infinity")]
    [InlineData("serve a.json --format json")]
    [InlineData("serve")]
    public void MalformedServeInvocationsAreUsageErrors(string line)
    {
        CliRun run = Cli.Run(line.Split(' '));

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("millrace serve --help", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void ABadBindValueIsAUsageErrorThatNamesIt()
    {
        CliRun run = Cli.Run("serve", "a.json", "--bind", "not-an-address");

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Contains("'--bind not-an-address' is not an IP address", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AServedPlantSaysWhatItServesAndWhereAndStopsWhenCancelled()
    {
        string plant = Cli.Plant("claimed-permit.json");

        CliRun run = Stopped("serve", plant, "--port", "0");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        string[] lines = run.Out.Split('\n');
        Assert.Equal($"Serving {plant}: 13 tags as 2 coils, 5 discrete inputs, 10 input registers and 2 holding registers.", lines[0]);
        Match listening = ListeningLine().Match(lines[1]);
        Assert.True(listening.Success, lines[1]);
        Assert.NotEqual("0", listening.Groups[1].Value);
        Assert.Equal("Stopped at 2026-01-01 06:00:00.000 after 0 ticks.", lines[2]);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public void AScenarioForThePlantIsBoundBeforeServing()
    {
        CliRun run = Stopped("serve", Cli.Plant("minimal.json"), "--scenario", Cli.Scenario("minimal.json"), "--port", "0", "--speed", "4");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Contains(") at 4x real time.", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void AScenarioForAnotherPlantIsAUsageError()
    {
        CliRun run = Stopped("serve", Cli.Plant("claimed-permit.json"), "--scenario", Cli.Scenario("minimal.json"), "--port", "0");

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains($"runs the plant '{Cli.Plant("minimal.json")}', not '{Cli.Plant("claimed-permit.json")}'", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidPlantBehavesAsValidateDoes()
    {
        CliRun run = Stopped("serve", Cli.Plant("broken.json"), "--port", "0");

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("MR102", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AScenarioThatDoesNotBindBehavesAsRunDoes()
    {
        CliRun run = Stopped("serve", Cli.Plant("broken.json"), "--scenario", Cli.Scenario("broken-plant.json"), "--port", "0");

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("MR102", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingScenarioFileIsUnreadable()
    {
        CliRun run = Stopped("serve", Cli.Plant("minimal.json"), "--scenario", Cli.Scenario("no-such.json"), "--port", "0");

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Contains("Cannot read", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void APortInUseExits3AndSaysWhichPort()
    {
        using var holder = new TcpListener(IPAddress.Loopback, 0);
        holder.Start();
        int port = ((IPEndPoint)holder.LocalEndpoint).Port;

        CliRun run = Stopped("serve", Cli.Plant("minimal.json"), "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.StartsWith($"Cannot listen on port {port}: ", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpListsServeAndModbusMapWithTheirOptions()
    {
        CliRun help = Cli.Run("help");
        CliRun serve = Cli.Run("serve", "--help");
        CliRun map = Cli.Run("modbus-map", "--help");

        Assert.Contains("  serve <plant.json>  ", help.Out, StringComparison.Ordinal);
        Assert.Contains("  modbus-map <plant.json>  ", help.Out, StringComparison.Ordinal);
        Assert.Contains("the port could not be opened", help.Out, StringComparison.Ordinal);
        Assert.All(["--scenario <scenario.json>", "--port <n>", "--bind <address>", "--speed <x>", "--assembly <path>"], o => Assert.Contains(o, serve.Out, StringComparison.Ordinal));
        Assert.All(["--format <text|csv|fuxa>", "--out <file>", "--assembly <path>"], o => Assert.Contains(o, map.Out, StringComparison.Ordinal));
    }
}
