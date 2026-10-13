namespace Orbit.Core.Audio;

/// <summary>
/// Synthesises Orbit's own short chime as a 16-bit mono PCM WAV (not Apple's sound, which is played by the
/// headphones' firmware and can only be triggered over Apple's accessory protocol).
/// A deep, warm two-note chime over a short sub-bass thump, with soft attack and exponential decay.
/// </summary>
public static class ChimeGenerator
{
    public const int SampleRate = 44100;

    /// <param name="volume">0..1 peak amplitude.</param>
    public static byte[] CreateWav(double volume = 0.25)
    {
        // Bass chime (owner's request): a soft sub "thump" (55 Hz) under a rising low fifth, G2 -> D3.
        // Overtones (2nd-4th harmonic) make the low notes clearly audible on headphones without losing the bass.
        // (frequency Hz, start s, length s, decay rate, harmonic mix)
        (double Freq, double Start, double Length, double Decay, double Harmonics)[] notes =
        [
            (55.00, 0.00, 0.30, 9.0, 0.0),   // thump
            (98.00, 0.00, 0.55, 5.0, 0.6),   // G2
            (146.83, 0.14, 0.70, 4.5, 0.6),  // D3
        ];
        var total = (int)(SampleRate * notes.Max(n => n.Start + n.Length));
        var samples = new double[total];

        foreach (var (freq, start, length, decayRate, harmonics) in notes)
        {
            var offset = (int)(SampleRate * start);
            var count = (int)(SampleRate * length);
            for (var i = 0; i < count && offset + i < total; i++)
            {
                var t = (double)i / SampleRate;
                var attack = Math.Min(1.0, t / 0.012);
                var decay = Math.Exp(-t * decayRate);
                var phase = 2 * Math.PI * freq * t;
                var tone = Math.Sin(phase)
                    + harmonics * (0.5 * Math.Sin(2 * phase) + 0.25 * Math.Sin(3 * phase) + 0.12 * Math.Sin(4 * phase));
                samples[offset + i] += tone * attack * decay;
            }
        }

        // Fade the last 30 ms so the tail never clicks.
        var fade = (int)(SampleRate * 0.03);
        for (var i = 0; i < fade; i++)
        {
            samples[total - 1 - i] *= (double)i / fade;
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
