namespace AppAutomation.Tooling;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintHelp();
                return 0;
            }

            return args[0].ToLowerInvariant() switch
            {
                "doctor" => DoctorCommand.Run(args[1..]),
                _ => Fail($"Unknown command '{args[0]}'.")
            };
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return Fail(ex.ToString());
        }
    }

    private static bool IsHelp(string argument)
    {
        return string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, "help", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("AppAutomation tooling");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  appautomation doctor [--repo-root <path>] [--strict]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  appautomation doctor");
        Console.WriteLine("  appautomation doctor --repo-root C:\\Projects\\MyApp --strict");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
