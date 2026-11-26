using ParallelSpeedup.Bench;

if (args.Contains("--help"))
{
    Console.WriteLine("--algo mc|sieve --variant sequential|shared|thread-local|classic|segmented --p 8 --n 10000000 --runs 10 --seed 42 --segment-size 262144 --out results/results.csv --experiment scaling|segment-size");
    return 0;
}
try { return BenchmarkRunner.Execute(BenchmarkOptions.Parse(args)); }
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
