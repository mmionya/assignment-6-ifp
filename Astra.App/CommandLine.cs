namespace Astra.App;

public static class CommandLine
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is ["agency", .. var agencyArgs])
            return AgencyCommand.Run(agencyArgs, output, error);

        if (args is [] or ["--help"] or ["-h"] or ["help"])
            return AgencyCommand.Run(["help"], output, error);

        error.WriteLine("Usage: dotnet run --project Astra.App -- agency <command>");
        error.WriteLine(AgencyCommand.Usage);
        return 2;
    }
}
