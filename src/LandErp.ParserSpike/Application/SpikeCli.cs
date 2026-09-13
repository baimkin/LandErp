namespace LandErp.ParserSpike.Application;

/// <summary>Offline entry point for the contract-only Gate.</summary>
public static class SpikeCli
{
    /// <summary>Shows help or rejects unsupported input without echoing untrusted arguments.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Length == 1 && args[0] == "--help")
        {
            output.WriteLine("LandErp.ParserSpike — offline contracts (Gate 01)");
            output.WriteLine("Usage: LandErp.ParserSpike --help");
            return 0;
        }

        error.WriteLine("CLI_UNKNOWN_COMMAND: use --help.");
        return 2;
    }
}
