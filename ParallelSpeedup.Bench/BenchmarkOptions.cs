using System.Globalization;
using ParallelSpeedup.Core;

namespace ParallelSpeedup.Bench;

internal sealed record BenchmarkOptions(string Algo, string Variant, int P, long N, int Runs,
    int Seed, int SegmentSize, string Out, string Experiment)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] allowed = ["--algo", "--variant", "--p", "--n", "--runs", "--seed", "--segment-size", "--out", "--experiment"];
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !allowed.Contains(args[i]) || !values.TryAdd(args[i], args[i+1]))
                throw new ArgumentException($"Invalid or duplicate option: {args[i]}");
        }
        string Get(string key, string fallback) => values.GetValueOrDefault(key, fallback);
        int Int(string key, int fallback) => int.Parse(Get(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        var o = new BenchmarkOptions(Get("--algo", "mc"), Get("--variant", "thread-local"), Int("--p", 1),
            long.Parse(Get("--n", "10000000"), CultureInfo.InvariantCulture), Int("--runs", 10), Int("--seed", 42),
            Int("--segment-size", SieveOfEratosthenes.DefaultSegmentSize), Get("--out", "results/results.csv"), Get("--experiment", "scaling"));
        if (o.P < 1 || o.N < 1 || o.Runs < 1 || o.SegmentSize < 1) throw new ArgumentException("p, n, runs and segment-size must be positive.");
        if (!((o.Algo == "mc" && o.Variant is "sequential" or "shared" or "thread-local") ||
              (o.Algo == "sieve" && o.Variant is "classic" or "segmented"))) throw new ArgumentException("Unknown algorithm/variant pair.");
        if ((o.Variant is "sequential" or "classic") && o.P != 1) throw new ArgumentException("Sequential variants require p=1.");
        if (o.Algo == "sieve" && o.N > int.MaxValue - 1L) throw new ArgumentException("Sieve N is too large.");
        if (o.Experiment is not ("scaling" or "segment-size")) throw new ArgumentException("Unknown experiment.");
        if (o.Experiment == "segment-size" && (o.Algo != "sieve" || o.Variant != "segmented")) throw new ArgumentException("Segment experiment requires segmented sieve.");
        return o;
    }
}
