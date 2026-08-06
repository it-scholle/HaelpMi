using HaelpMi.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HaelpMi.Core.Audio;

/// <summary>
/// Plays the alarm tone on every active audio output device simultaneously (FR-11),
/// temporarily un-muting and raising the volume of each device that needs it and
/// always restoring its original mute/volume state afterwards - even if playback on
/// that device fails, so one bad output device can't leave speakers muted forever.
/// </summary>
public sealed class MultiDeviceAlarmPlayer
{
    private const float MinimumAudibleVolume = 0.8f;

    public async Task PlayOnAllActiveDevicesAsync(IncomingSoundCatalog.Option option, CancellationToken ct = default)
    {
        var buffer = AlarmToneGenerator.Render(option);
        var format = AlarmToneGenerator.Format;

        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

        try
        {
            await Task.WhenAll(devices.Select(device => PlayOnDeviceAsync(device, buffer, format, ct)));
        }
        finally
        {
            foreach (var device in devices)
            {
                device.Dispose();
            }
        }
    }

    private static async Task PlayOnDeviceAsync(MMDevice device, float[] buffer, WaveFormat format, CancellationToken ct)
    {
        var endpointVolume = device.AudioEndpointVolume;
        var originalMute = endpointVolume.Mute;
        var originalVolume = endpointVolume.MasterVolumeLevelScalar;

        try
        {
            endpointVolume.Mute = false;
            if (originalVolume < MinimumAudibleVolume)
            {
                endpointVolume.MasterVolumeLevelScalar = MinimumAudibleVolume;
            }

            var provider = new InMemorySampleProvider(buffer, format);
            using var output = new WasapiOut(device, AudioClientShareMode.Shared, false, 100);

            var playbackFinished = new TaskCompletionSource();
            output.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is not null)
                {
                    playbackFinished.TrySetException(args.Exception);
                }
                else
                {
                    playbackFinished.TrySetResult();
                }
            };

            output.Init(provider);
            output.Play();

            await using (ct.Register(() => playbackFinished.TrySetCanceled(ct)))
            {
                await playbackFinished.Task;
            }
        }
        catch (Exception)
        {
            // Best-effort per device (6.): a device that fails to play must not stop the
            // alarm on every other output, and must still have its mute/volume restored below.
        }
        finally
        {
            endpointVolume.Mute = originalMute;
            endpointVolume.MasterVolumeLevelScalar = originalVolume;
        }
    }

    private sealed class InMemorySampleProvider(float[] buffer, WaveFormat format) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } = format;

        public int Read(float[] destBuffer, int offset, int count)
        {
            var remaining = buffer.Length - _position;
            var toCopy = Math.Min(remaining, count);
            if (toCopy <= 0)
            {
                return 0;
            }

            Array.Copy(buffer, _position, destBuffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }
    }
}
