using Millrace.Io;

namespace Millrace.Io.Abstractions.Tests;

public class DirtyMaskTests
{
    [Fact]
    public void EmptyHasNoBits()
    {
        DirtyMask mask = DirtyMask.Empty(130);

        Assert.Equal(130, mask.Length);
        Assert.Equal(0, mask.Count);
        Assert.False(mask[0]);
        Assert.False(mask[129]);
        Assert.Empty(mask.Indices());
    }

    [Fact]
    public void AllSetsEveryBitAndNoMore()
    {
        DirtyMask mask = DirtyMask.All(70);

        Assert.Equal(70, mask.Count);
        Assert.True(mask[0]);
        Assert.True(mask[63]);
        Assert.True(mask[64]);
        Assert.True(mask[69]);
        Assert.Equal(Enumerable.Range(0, 70), mask.Indices());
    }

    [Fact]
    public void FromBitsExposesTheGivenWords()
    {
        ulong[] words = [1UL << 3 | 1UL << 63, 1UL];
        DirtyMask mask = DirtyMask.FromBits(words, length: 65, count: 3);

        Assert.True(mask[3]);
        Assert.True(mask[63]);
        Assert.True(mask[64]);
        Assert.False(mask[4]);
        Assert.Equal(new[] { 3, 63, 64 }, mask.Indices());
        Assert.Equal(3, mask.Count);
    }

    [Fact]
    public void IndexOutOfRangeThrows()
    {
        DirtyMask mask = DirtyMask.Empty(10);

        Assert.Throws<ArgumentOutOfRangeException>(() => mask[10]);
        Assert.Throws<ArgumentOutOfRangeException>(() => mask[-1]);
    }

    [Fact]
    public void ZeroLengthIsValid()
    {
        DirtyMask mask = DirtyMask.All(0);

        Assert.Equal(0, mask.Length);
        Assert.Equal(0, mask.Count);
        Assert.Empty(mask.Indices());
    }
}
