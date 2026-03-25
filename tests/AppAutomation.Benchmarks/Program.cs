using BenchmarkDotNet.Running;

namespace AppAutomation.Benchmarks;

/// <summary>
/// Entry point for BenchmarkDotNet performance benchmarks.
/// Run with: dotnet run -c Release
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        // Run all benchmarks in this assembly
        // Use BenchmarkSwitcher to allow selecting specific benchmarks at runtime
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
