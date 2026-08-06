using HaelpMi.Core.Models;
using NAudio.Wave;

namespace HaelpMi.Core.Audio;

/// <summary>
/// Synthesizes the bundled alarm tones (FR-12) as simple sine-wave beep patterns
/// on the fly, rather than shipping binary .wav assets. Rendered once per playback
/// into an in-memory float buffer (a few hundred KB at most - these are short beeps).
/// </summary>
public static class AlarmToneGenerator
{
    private const int SampleRate = 44100;
    private const double Amplitude = 0.6;

    public static WaveFormat Format { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);

    public static float[] Render(IncomingSoundCatalog.Option option)
    {
        var beepSamples = MillisecondsToSamples(option.BeepDurationMs);
        var gapSamples = MillisecondsToSamples(option.GapMs);
        var fadeSamples = Math.Min(beepSamples / 4, MillisecondsToSamples(5));

        var samplesPerRepeat = option.BeepFrequenciesHz.Length * (beepSamples + gapSamples);
        var buffer = new float[samplesPerRepeat * option.RepeatCount];

        var index = 0;
        for (var repeat = 0; repeat < option.RepeatCount; repeat++)
        {
            foreach (var frequencyHz in option.BeepFrequenciesHz)
            {
                for (var s = 0; s < beepSamples; s++)
                {
                    var envelope = Envelope(s, beepSamples, fadeSamples);
                    var t = (double)s / SampleRate;
                    buffer[index++] = (float)(Amplitude * envelope * Math.Sin(2 * Math.PI * frequencyHz * t));
                }

                for (var s = 0; s < gapSamples; s++)
                {
                    buffer[index++] = 0f;
                }
            }
        }

        return buffer;
    }

    private static double Envelope(int sampleIndex, int totalSamples, int fadeSamples)
    {
        if (fadeSamples <= 0)
        {
            return 1.0;
        }

        if (sampleIndex < fadeSamples)
        {
            return (double)sampleIndex / fadeSamples;
        }

        if (sampleIndex > totalSamples - fadeSamples)
        {
            return (double)(totalSamples - sampleIndex) / fadeSamples;
        }

        return 1.0;
    }

    private static int MillisecondsToSamples(int milliseconds) => milliseconds * SampleRate / 1000;
}
