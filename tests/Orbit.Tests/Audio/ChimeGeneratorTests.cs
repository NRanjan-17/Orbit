using System.Text;
using Orbit.Core.Audio;

namespace Orbit.Tests.Audio;

public class ChimeGeneratorTests
{
    [Fact]
    public void ProducesValidPcmWav()
    {
        var wav = ChimeGenerator.CreateWav(0.25);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));                       // PCM
        Assert.Equal(ChimeGenerator.SampleRate, BitConverter.ToInt32(wav, 24));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));          // RIFF size
        Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));        // data size

        var duration = (wav.Length - 44) / 2.0 / ChimeGenerator.SampleRate;
        Assert.InRange(duration, 0.4, 1.0);

        var peak = Enumerable.Range(0, (wav.Length - 44) / 2).Max(i => Math.Abs((int)BitConverter.ToInt16(wav, 44 + i * 2)));
        Assert.InRange(peak, (int)(short.MaxValue * 0.24), (int)(short.MaxValue * 0.26)); // volume honoured, no clipping
    }
}
