namespace Orbit.Core.Audio;

/// <summary>
/// Synthesises Orbit's own short chime as a 16-bit mono PCM WAV (not Apple's sound, which is played by the
/// headphones' firmware and can only be triggered over Apple's accessory protocol).
/// Two soft rising sine notes with fast attack and exponential decay.
/// </summary>
public static class ChimeGenerator
{
    public const int SampleRate = 44100;

    /// <param name="volume">0..1 peak amplitude.</param>
    public static byte[] CreateWav(double volume = 0.25)
    {
        // (frequency Hz, start s, length s): a rising perfect fifth, E5 -> B5.
        (double Freq, double Start, double Length)[] notes = [(659.25, 0.00, 0.35), (987.77, 0.12, 0.45)];
        var total = (int)(SampleRate * notes.Max(n => n.Start + n.Length));
        var samples = new double[total];

        foreach (var (freq, start, length) in notes)
        {
            var offset = (int)(SampleRate * start);
            var count = (int)(SampleRate * length);
            for (var i = 0; i < count && offset + i < total; i++)
            {
                var t = (double)i / SampleRate;
                var attack = Math.Min(1.0, t / 0.008);
                var decay = Math.Exp(-t * 7.0);
                var tone = Math.Sin(2 * Math.PI * freq * t) + 0.2 * Math.Sin(2 * Math.PI * freq * 2 * t);
                samples[offset + i] += tone * attack * decay;
            }
        }

        var peak = samples.Max(Math.Abs);
        var scale = peak > 0 ? Math.Clamp(volume, 0, 1) / peak : 0;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var dataBytes = total * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);                // PCM chunk size
        w.Write((short)1);          // PCM
        w.Write((short)1);          // mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2);    // byte rate
        w.Write((short)2);          // block align
        w.Write((short)16);         // bits per sample
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in samples)
        {
            w.Write((short)Math.Round(s * scale * short.MaxValue));
        }

        w.Flush();
        return ms.ToArray();
    }
}
