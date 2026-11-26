using System.Diagnostics;
using System.Globalization;
using ParallelSpeedup.Core;

namespace ParallelSpeedup.Bench;

internal static class BenchmarkRunner
{
    private const string Header = "algorithm,variant,p,n,run,time_ms,result,correct,seed,segment_size,experiment,pi_estimate,abs_error,error_over_se,utc";
    private static readonly Dictionary<long, long> KnownPrimes = new()
    {
        [10] = 4, [100] = 25, [1000] = 168, [10000] = 1229, [100000] = 9592,
        [1000000] = 78498, [10000000] = 664579, [100000000] = 5761455, [1000000000] = 50847534
    };

    public static int Execute(BenchmarkOptions o)
    {
        Func<long> work = (o.Algo, o.Variant) switch
        {
            ("mc", "sequential") => () => MonteCarloPi.Sequential(o.N, o.Seed),
            ("mc", "shared") => () => MonteCarloPi.SharedRng(o.N, o.P, o.Seed),
            ("mc", "thread-local") => () => MonteCarloPi.ThreadLocalRng(o.N, o.P, o.Seed),
            ("sieve", "classic") => () => SieveOfEratosthenes.ClassicSequential((int)o.N),
            ("sieve", "segmented") => () => SieveOfEratosthenes.Segmented(o.N, o.P, o.SegmentSize),
            _ => throw new ArgumentException("Invalid variant")
        };
        // Avoid reducing the runtime's default minimum below its existing value.
        ThreadPool.GetMinThreads(out int workerMin, out int ioMin);
        if (!ThreadPool.SetMinThreads(Math.Max(workerMin, o.P), ioMin))
            throw new InvalidOperationException("Could not configure thread pool.");
        string path = Path.GetFullPath(o.Out);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bool exists = File.Exists(path) && new FileInfo(path).Length > 0;
        if (exists && File.ReadLines(path).First() != Header)
            throw new InvalidOperationException("Existing CSV has an incompatible header.");
        using var writer = new StreamWriter(path, append: true) { AutoFlush = true };
        if (!exists) writer.WriteLine(Header);
        Console.WriteLine($"Warm-up: {o.Algo}/{o.Variant}, p={o.P}, N={o.N}, segment={o.SegmentSize}");
        _ = work();
        bool allCorrect = true;
        for (int r = 1; r <= o.Runs; r++)
        {
            var sw = Stopwatch.StartNew();
            long result = work();
            sw.Stop();
            double? estimate = null, error = null, ratio = null;
            string correct;
            if (o.Algo == "mc")
            {
                estimate = MonteCarloPi.Estimate(result, o.N);
                error = Math.Abs(estimate.Value - Math.PI);
                ratio = error.Value / MonteCarloPi.StandardError(o.N);
                correct = ratio <= 5 && result >= 0 && result <= o.N ? "true" : "false";
            }
            else correct = KnownPrimes.TryGetValue(o.N, out long expected) ? (result == expected ? "true" : "false") : "unknown";
            allCorrect &= correct != "false";
            string F(double? value) => value?.ToString("G17", CultureInfo.InvariantCulture) ?? "";
            writer.WriteLine(FormattableString.Invariant($"{o.Algo},{o.Variant},{o.P},{o.N},{r},{sw.Elapsed.TotalMilliseconds:F6},{result},{correct},{o.Seed},{(o.Variant == "segmented" ? o.SegmentSize : 0)},{o.Experiment},{F(estimate)},{F(error)},{F(ratio)},{DateTimeOffset.UtcNow:O}"));
            Console.WriteLine(FormattableString.Invariant($"Run {r}/{o.Runs}: {sw.Elapsed.TotalMilliseconds:F3} ms; result={result}; correct={correct}"));
        }
        return allCorrect ? 0 : 2;
    }
}
