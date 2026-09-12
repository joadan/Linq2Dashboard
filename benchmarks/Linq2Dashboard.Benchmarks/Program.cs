using BenchmarkDotNet.Running;
using Linq2Dashboard.Benchmarks;

// dotnet run -c Release -- --memory            memory per facet kind and wall times
// dotnet run -c Release -- --filter *          every BenchmarkDotNet scenario
// dotnet run -c Release -- --job short --filter *Calculate*   a quicker pass
if (args.Contains("--memory", StringComparer.OrdinalIgnoreCase))
{
    MemoryReport.Run();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
