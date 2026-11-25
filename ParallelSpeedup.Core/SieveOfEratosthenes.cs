namespace ParallelSpeedup.Core;

public static class SieveOfEratosthenes
{
    public const int DefaultSegmentSize = 256 * 1024;

    private static bool[] ClassicFlags(int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(n);
        var composite = new bool[checked(n + 1)];
        for (int pr = 2; (long)pr * pr <= n; pr++)
        {
            if (composite[pr]) continue;
            for (long j = (long)pr * pr; j <= n; j += pr)
                composite[j] = true;
        }
        return composite;
    }

    public static long ClassicSequential(int n)
    {
        var composite = ClassicFlags(n);
        long count = 0;
        for (int i = 2; i < composite.Length; i++)
            if (!composite[i]) count++;
        return count;
    }

    private static int[] BasePrimes(long n)
    {
        int root = (int)Math.Sqrt(n);
        while ((long)(root + 1) * (root + 1) <= n) root++;
        while ((long)root * root > n) root--;
        var composite = ClassicFlags(root);
        var primes = new List<int>();
        for (int i = 2; i <= root; i++)
            if (!composite[i]) primes.Add(i);
        return primes.ToArray();
    }

    private static long CountSegment(long lo, long hi, int[] primes, bool[] buffer)
    {
        int length = checked((int)(hi - lo));
        Array.Clear(buffer, 0, length);
        foreach (int pr in primes)
        {
            long square = (long)pr * pr;
            if (square >= hi) break;
            long start = Math.Max(square, (lo + pr - 1) / pr * pr);
            for (long j = start; j < hi; j += pr)
                buffer[j - lo] = true;
        }
        long count = 0;
        for (int k = 0; k < length; k++)
            if (!buffer[k] && lo + k >= 2) count++;
        return count;
    }

    public static long Segmented(long n, int p, int segmentSize = DefaultSegmentSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(n);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(p);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segmentSize);
        if (n > int.MaxValue - 1L) throw new ArgumentOutOfRangeException(nameof(n), "Supported range is 0..2147483646.");
        if (n < 2) return 0;
        int[] primes = BasePrimes(n);
        long end = n + 1; // half-open segments, inclusive upper bound N
        long segments = (end + segmentSize - 1) / segmentSize;
        long total = 0;
        Parallel.For<(bool[] Buffer, long Count)>(0L, segments,
            new ParallelOptions { MaxDegreeOfParallelism = p },
            () => (new bool[segmentSize], 0L),
            (i, _, local) =>
            {
                long lo = i * segmentSize;
                long hi = Math.Min(lo + segmentSize, end);
                local.Count += CountSegment(lo, hi, primes, local.Buffer);
                return local;
            },
            local => Interlocked.Add(ref total, local.Count));
        return total;
    }

    // Same implementation and overhead for the p=1 baseline.
    public static long SegmentedSequential(long n, int segmentSize = DefaultSegmentSize)
        => Segmented(n, 1, segmentSize);
}
