using ParallelSpeedup.Core;
using Xunit;

namespace ParallelSpeedup.Tests;

public class MonteCarloPiTests
{
    [Fact]
    public void SequentialIsRepeatable() => Assert.Equal(MonteCarloPi.Sequential(10003, 42), MonteCarloPi.Sequential(10003, 42));

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(8)]
    public void SharedGeneratorPreservesPointPairs(int p)
        => Assert.Equal(MonteCarloPi.Sequential(10003, 42), MonteCarloPi.SharedRng(10003, p, 42));

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(8)]
    public void ThreadLocalUsesEverySample(int p)
    {
        const int n = 10003;
        long expected = 0;
        for (int i = 0; i < p; i++) expected += MonteCarloPi.Sequential(n / p + (i < n % p ? 1 : 0), 42 + i);
        Assert.Equal(expected, MonteCarloPi.ThreadLocalRng(n, p, 42));
        Assert.Equal(expected, MonteCarloPi.ThreadLocalRng(n, p, 42));
    }

    [Fact]
    public void HandlesMoreWorkersThanSamples()
    {
        Assert.InRange(MonteCarloPi.ThreadLocalRng(3, 8, 42), 0L, 3L);
        Assert.Equal(MonteCarloPi.Sequential(3, 42), MonteCarloPi.SharedRng(3, 8, 42));
    }

    [Theory]
    [InlineData("sequential")] [InlineData("thread-local")] [InlineData("shared")]
    [Trait("Category", "Full")]
    public void AccuracyAtTenMillion(string variant)
    {
        const long n = 10_000_000;
        long hits = variant switch { "sequential" => MonteCarloPi.Sequential(n, 42), "shared" => MonteCarloPi.SharedRng(n, 8, 42), _ => MonteCarloPi.ThreadLocalRng(n, 8, 42) };
        Assert.InRange(Math.Abs(MonteCarloPi.Estimate(hits, n) - Math.PI), 0, 5 * MonteCarloPi.StandardError(n));
    }

    [Fact]
    public void RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MonteCarloPi.Sequential(0, 42));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonteCarloPi.ThreadLocalRng(10, 0, 42));
    }
}
