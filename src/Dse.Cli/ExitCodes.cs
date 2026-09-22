namespace Dse.Cli;

public static class ExitCodes
{
    public const int Ok = 0;

    /// <summary>The plant was read and has errors.</summary>
    public const int PlantInvalid = 1;

    /// <summary>The command line itself is wrong.</summary>
    public const int Usage = 2;

    /// <summary>A file or an assembly could not be read, written or loaded.</summary>
    public const int Unreadable = 3;
}
