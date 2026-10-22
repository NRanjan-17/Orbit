using Orbit.Core.Media;

namespace Orbit.Tests.Media;

public class AutoOutputTests
{
    private static readonly Guid Max = new("6a1d3b2c-0000-4000-8000-000000000001");
    private static readonly Guid Monitor = new("6a1d3b2c-0000-4000-8000-000000000002");

    private sealed class FakeOutputs : IAudioOutputs
    {
        public List<AudioOutput> Outputs { get; } =
        [
            new("max-headphones", "Headphones (AirPods Max)", AudioOutputKind.Headphones, Max),
            new("max-headset", "Headset (AirPods Max)", AudioOutputKind.Headphones, Max),
            new("laptop", "Speakers (Realtek)", AudioOutputKind.Speakers, AudioOutputs.ThisPcContainer),
            new("usb-speakers", "Speakers (USB)", AudioOutputKind.Speakers, Monitor),
            new("hdmi", "LG Monitor", AudioOutputKind.Other, Monitor),
        ];

        public Dictionary<AudioRole, string> Defaults { get; } = new()
        {
            [AudioRole.Console] = "max-headphones",
            [AudioRole.Multimedia] = "max-headphones",
            [AudioRole.Communications] = "max-headset",
        };

        public int Sets { get; private set; }

        public bool FailSets { get; set; }

        public IReadOnlyList<AudioOutput> GetOutputs() => Outputs;

        public string? GetDefault(AudioRole role) => Defaults.GetValueOrDefault(role);

        public bool SetDefault(string endpointId, AudioRole role)
        {
            if (FailSets)
            {
                return false;
            }

            Sets++;
            Defaults[role] = endpointId;
            return true;
        }
    }

    private static (FakeOutputs, AutoOutputController) Create()
    {
        var outputs = new FakeOutputs();
        return (outputs, new AutoOutputController(outputs) { HeadphonesContainer = Max });
    }

    [Fact]
    public void OffHeadMovesEveryHeadphoneRoleToTheLaptopSpeakers()
    {
        var (o, c) = Create();
        Assert.Equal(AutoOutputAction.SwitchedToSpeakers, c.OnWornChanged(false));
        Assert.All(AudioOutputs.AllRoles, r => Assert.Equal("laptop", o.Defaults[r]));
        Assert.True(c.HoldingSwitch);
    }

    [Fact]
    public void OnHeadRestoresEachRoleToItsOwnHeadphoneEndpoint()
    {
        var (o, c) = Create();
        c.OnWornChanged(false);
        Assert.Equal(AutoOutputAction.SwitchedToHeadphones, c.OnWornChanged(true));
        Assert.Equal("max-headphones", o.Defaults[AudioRole.Multimedia]);
        Assert.Equal("max-headset", o.Defaults[AudioRole.Communications]);
        Assert.False(c.HoldingSwitch);
    }

    [Fact]
    public void OnlyRolesThatWereOnTheHeadphonesAreTouched()
    {
        var (o, c) = Create();
        o.Defaults[AudioRole.Communications] = "usb-speakers";
        c.OnWornChanged(false);
        Assert.Equal("usb-speakers", o.Defaults[AudioRole.Communications]);
        c.OnWornChanged(true);
        Assert.Equal("usb-speakers", o.Defaults[AudioRole.Communications]);
        Assert.Equal("max-headphones", o.Defaults[AudioRole.Console]);
    }

    [Fact]
    public void NothingHappensWhenTheHeadphonesArentThisPcsOutput()
    {
        var (o, c) = Create();
        foreach (var r in AudioOutputs.AllRoles)
        {
            o.Defaults[r] = "hdmi";
        }

        Assert.Equal(AutoOutputAction.None, c.OnWornChanged(false));
        Assert.Equal(0, o.Sets);
        Assert.Equal(AutoOutputAction.None, c.OnWornChanged(true));
    }

    [Fact]
    public void AManualChoiceWhileOffHeadIsRespected()
    {
        var (o, c) = Create();
        c.OnWornChanged(false);
        o.Defaults[AudioRole.Multimedia] = "hdmi"; // user picked the monitor meanwhile
        c.OnWornChanged(true);
        Assert.Equal("hdmi", o.Defaults[AudioRole.Multimedia]);
        Assert.Equal("max-headphones", o.Defaults[AudioRole.Console]);
    }

    [Fact]
    public void HeadphonesThatLeftThisPcAreNotRestored()
    {
        var (o, c) = Create();
        c.OnWornChanged(false);
        o.Outputs.RemoveAll(x => x.ContainerId == Max); // switched to the Mac while off-head
        Assert.Equal(AutoOutputAction.None, c.OnWornChanged(true));
        Assert.Equal("laptop", o.Defaults[AudioRole.Multimedia]);
    }

    [Fact]
    public void PutOnWithoutAnEarlierSwitchDoesNothing()
    {
        var (o, c) = Create();
        Assert.Equal(AutoOutputAction.None, c.OnWornChanged(true));
        Assert.Equal(0, o.Sets);
    }

    [Fact]
    public void DisabledOrUnboundDoesNothing()
    {
        var (o, c) = Create();
        c.Enabled = false;
        Assert.Equal(AutoOutputAction.None, c.OnWornChanged(false));
        c.Enabled = true;
        c.HeadphonesContainer = null;
        Assert.Equal(AutoOutputAction.None, c.OnWornChanged(false));
        Assert.Equal(0, o.Sets);
    }

    [Fact]
    public void DisablingWhileOffHeadLeavesTheSpeakersOn()
    {
        var (o, c) = Create();
        c.OnWornChanged(false);
        c.Enabled = false;
        Assert.Equal(AutoOutputAction.None, c.OnWornChanged(true));
        Assert.Equal("laptop", o.Defaults[AudioRole.Multimedia]);
    }

    [Fact]
    public void ResetForgetsThePendingSwitchBack()
    {
        var (o, c) = Create();
        c.OnWornChanged(false);
        c.Reset();
        Assert.Equal(AutoOutputAction.None, c.OnWornChanged(true));
        Assert.Equal("laptop", o.Defaults[AudioRole.Multimedia]);
    }

    [Fact]
    public void FailedSwitchIsNotRemembered()
    {
        var (o, c) = Create();
        o.FailSets = true;
        Assert.Equal(AutoOutputAction.None, c.OnWornChanged(false));
        Assert.False(c.HoldingSwitch);
    }

    [Fact]
    public void NoSpeakersMeansNoSwitch()
    {
        var (o, c) = Create();
        o.Outputs.RemoveAll(x => x.Kind == AudioOutputKind.Speakers);
        Assert.Equal(AutoOutputAction.None, c.OnWornChanged(false));
        Assert.Equal("max-headphones", o.Defaults[AudioRole.Multimedia]);
    }

    [Fact]
    public void BuiltInSpeakersArePreferredOverOtherSpeakers()
    {
        var speakers = new[]
        {
            new AudioOutput("usb", "Speakers (USB)", AudioOutputKind.Speakers, Monitor),
            new AudioOutput("laptop", "Speakers (Realtek)", AudioOutputKind.Speakers, AudioOutputs.ThisPcContainer),
        };
        Assert.Equal("laptop", AutoOutputController.PickSpeakers(speakers, Max)!.Id);
        Assert.Equal("usb", AutoOutputController.PickSpeakers(speakers.Take(1), Max)!.Id);
        Assert.Null(AutoOutputController.PickSpeakers([new AudioOutput("h", "H", AudioOutputKind.Headphones, Max)], Max));
    }
}
