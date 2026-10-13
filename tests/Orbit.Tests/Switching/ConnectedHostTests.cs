using Orbit.Core.Parsing;
using Orbit.Core.Switching;

namespace Orbit.Tests.Switching;

/// <summary>
/// Connected-host classification (protocol-findings F11). Address values here are synthetic; the owner's real
/// addresses are never put in the repo.
/// </summary>
public class ConnectedHostTests
{
    private const int ThisPc = 0x123456;

    private static Reading<int> Host(int v) => new(v, Confidence.VerifiedOnDevice, "t");

    [Fact]
    public void MatchingBytesMeanThisPc() =>
        Assert.Equal(ConnectedTo.ThisPc, ConnectedHost.Classify(Host(ThisPc), ThisPc));

    [Fact]
    public void DifferentBytesMeanAnotherDevice() =>
        Assert.Equal(ConnectedTo.OtherDevice, ConnectedHost.Classify(Host(0x654321), ThisPc));

    [Theory]
    [InlineData(0)]   // all-zero: meaning not captured yet
    [InlineData(-1)]  // not decoded
    public void UndecidedValuesAreUnknown(int host) =>
        Assert.Equal(ConnectedTo.Unknown, ConnectedHost.Classify(Host(host), ThisPc));

    [Fact]
    public void NoKeyOrNoAdapterIsUnknown()
    {
        Assert.Equal(ConnectedTo.Unknown, ConnectedHost.Classify(Reading<int>.Unknown(), ThisPc));
        Assert.Equal(ConnectedTo.Unknown, ConnectedHost.Classify(Host(ThisPc), null));
    }
}
