using ParallelSpeedup.Core;
using Xunit;

namespace ParallelSpeedup.Tests;

public class SieveOfEratosthenesTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(2, 1)] [InlineData(3, 2)]
    [InlineData(49, 15)] [InlineData(100, 25)] [InlineData(1000, 168)] [InlineData(1000000, 78498)]
    public void KnownCounts(int n, int expected)
    {
        Assert.Equal((long)expected, SieveOfEratosthenes.ClassicSequential(n));
        Assert.Equal((long)expected, SieveOfEratosthenes.SegmentedSequential(n, 127));
        Assert.Equal((long)expected, SieveOfEratosthenes.Segmented(n, 8, 127));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(7)] [InlineData(64)] [InlineData(10000)]
    public void HandlesSegmentBoundaries(int size)
        => Assert.Equal(168L, SieveOfEratosthenes.Segmented(1000, 8, size));

    [Theory]
    [InlineData(10000000, 664579)] [InlineData(100000000, 5761455)] [InlineData(1000000000, 50847534)]
    [Trait("Category", "Full")]
    public void LargeCountsAndParallelAgreement(int n, int expected)
    {
        Assert.Equal((long)expected, SieveOfEratosthenes.SegmentedSequential(n));
        Assert.Equal((long)expected, SieveOfEratosthenes.Segmented(n, 8));
        if (n <= 100_000_000) Assert.Equal((long)expected, SieveOfEratosthenes.ClassicSequential(n));
    }

    [Fact]
    public void RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SieveOfEratosthenes.Segmented(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SieveOfEratosthenes.Segmented(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SieveOfEratosthenes.Segmented(10, 1, 0));
    }
}
