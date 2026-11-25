namespace ParallelSpeedup.Core;

public static class MonteCarloPi
{
    private static void Validate(long n, int p = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(p);
    }

    public static long Sequential(long n, int seed)
    {
        Validate(n);
        return Count(n, new Random(seed));
    }

    private static long Count(long n, Random rng)
    {
        long hits = 0;
        for (long i = 0; i < n; i++)
        {
            double x = rng.NextDouble(), y = rng.NextDouble();
            if (x * x + y * y <= 1.0) hits++;
        }
        return hits;
    }

    private static long Chunk(long n, int p, int i) => n / p + (i < n % p ? 1 : 0);

    public static long ThreadLocalRng(long n, int p, int seed)
    {
        Validate(n, p);
        long total = 0;
        // Each logical chunk owns its RNG and counter, independent of scheduling.
        Parallel.For(0, p, new ParallelOptions { MaxDegreeOfParallelism = p }, i =>
        {
            long hits = Count(Chunk(n, p, i), new Random(unchecked(seed + i)));
            Interlocked.Add(ref total, hits);
        });
        return total;
    }

    public static long SharedRng(long n, int p, int seed)
    {
        Validate(n, p);
        // One shared instance per invocation; reset the seed for every repeat.
        var rng = new Random(seed);
        var gate = new object();
        long total = 0;
        Parallel.For(0, p, new ParallelOptions { MaxDegreeOfParallelism = p }, i =>
        {
            long hits = 0, count = Chunk(n, p, i);
            for (long j = 0; j < count; j++)
            {
                double x, y;
                lock (gate)
                {
                    x = rng.NextDouble();
                    y = rng.NextDouble();
                }
                if (x * x + y * y <= 1.0) hits++;
            }
            Interlocked.Add(ref total, hits);
        });
        return total;
    }

    public static double Estimate(long hits, long n) => 4.0 * hits / n;
    public static double StandardError(long n) => Math.Sqrt(Math.PI * (4 - Math.PI) / n);
}
