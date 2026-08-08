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

    // Bugfix 07.08.2026 (Fehlerbericht "kein Signalton, obwohl Gerät nicht stummgeschaltet
    // war"): vorher ohne jede Protokollierung - ein Fehlschlag pro Gerät (siehe
    // PlayOnDeviceAsync catch unten) oder gar keine gefundenen Wiedergabegeräte verschwand
    // komplett spurlos, nicht diagnostizierbar. Besonders in VM-Umgebungen mit
    // durchgereichter/virtueller Audio-Ausgabe (WASAPI-Kompatibilität ist dort bekanntlich
    // eingeschränkter als bei echter Hardware) ist genau das der wahrscheinlichste
    // Fehlerort - jetzt wenigstens sichtbar im Audit-Log statt komplett stumm.
    private readonly Action<string>? _audit;

    public MultiDeviceAlarmPlayer(Action<string>? audit = null)
    {
        _audit = audit;
    }

    public async Task PlayOnAllActiveDevicesAsync(IncomingSoundCatalog.Option option, CancellationToken ct = default)
    {
        var floatBuffer = AlarmToneGenerator.Render(option);
        var format = AlarmToneGenerator.Format;

        // Bugfix 07.08.2026 (Fehlerbericht "kein Signalton, obwohl Gerät nicht
        // stummgeschaltet war" - live nachgestellt und per Diagnose-Tool eingegrenzt): der
        // vorherige Weg über ISampleProvider (float-basiert) ließ NAudio den Aufruf intern
        // in NAudio.Wave.SampleProviders.SampleToWaveProvider verpacken - dessen eigene
        // Float->Byte-Konvertierung wirft auf mindestens einer Testumgebung (ARM64-Windows,
        // vermutlich JIT-/Array-Cast-bedingt) eine ArrayTypeMismatchException MITTEN in
        // NAudios eigenem Wiedergabe-Thread. Schlimmer als der Absturz selbst: WasapiOut.
        // Dispose() hing danach unbegrenzt (der interne Thread kam aus diesem Fehlerzustand
        // nie sauber zurück) - genau das erzeugte den beobachteten "kompletten Stillstand,
        // kein Ton, keine Fehlermeldung" (Task.WhenAll unten wartete endlos auf das
        // hängende Dispose in der using-Anweisung). Direkt als Bytes an ein eigenes
        // IWaveProvider zu geben umgeht NAudios SampleToWaveProvider komplett - live
        // verifiziert: identischer Ton, sauberer Abschluss, kein Hänger mehr.
        var buffer = new byte[floatBuffer.Length * sizeof(float)];
        Buffer.BlockCopy(floatBuffer, 0, buffer, 0, buffer.Length);

        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

        if (devices.Count == 0)
        {
            _audit?.Invoke("MultiDeviceAlarmPlayer: kein aktives Wiedergabegerät gefunden (WASAPI Render/Active) - kein Signalton abgespielt.");
            return;
        }

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

    private async Task PlayOnDeviceAsync(MMDevice device, byte[] buffer, WaveFormat format, CancellationToken ct)
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

            var provider = new InMemoryByteProvider(buffer, format);
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

            // Sicherheitsnetz zusätzlich zum eigentlichen Fix oben: eine erwartete
            // Spieldauer von wenigen Sekunden (siehe AlarmToneGenerator/IncomingSoundCatalog)
            // rechtfertigt kein unbegrenztes Warten - lieber diesem einen Gerät nach 10s
            // "aufgeben" (mit Audit-Eintrag) als den gesamten Task.WhenAll in
            // PlayOnAllActiveDevicesAsync für alle anderen Geräte mitblockieren zu lassen,
            // sollte hier je wieder eine andere, heute nicht bekannte Fehlerursache auftreten.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            await using (timeoutCts.Token.Register(() => playbackFinished.TrySetCanceled(timeoutCts.Token)))
            {
                await playbackFinished.Task;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout (siehe timeoutCts oben), nicht die von außen übergebene ct - eigene
            // Meldung, damit das im Audit-Log nicht wie ein normaler Abbruch aussieht.
            _audit?.Invoke($"MultiDeviceAlarmPlayer: Wiedergabe auf \"{device.FriendlyName}\" nach 10s ohne Abschluss-Ereignis abgebrochen (Sicherheitsnetz - vermutlich hängt das Gerät/der Treiber).");
        }
        catch (Exception ex)
        {
            // Best-effort per device (6.): a device that fails to play must not stop the
            // alarm on every other output, and must still have its mute/volume restored below.
            _audit?.Invoke($"MultiDeviceAlarmPlayer: Wiedergabe auf \"{device.FriendlyName}\" fehlgeschlagen: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            endpointVolume.Mute = originalMute;
            endpointVolume.MasterVolumeLevelScalar = originalVolume;
        }
    }

    // IWaveProvider (byte-basiert) statt ISampleProvider (float-basiert) - siehe Kommentar
    // in PlayOnAllActiveDevicesAsync: umgeht NAudios eigene SampleToWaveProvider-
    // Konvertierung, die auf mindestens einer Testumgebung defekt war.
    private sealed class InMemoryByteProvider(byte[] buffer, WaveFormat format) : IWaveProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } = format;

        public int Read(byte[] destBuffer, int offset, int count)
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
