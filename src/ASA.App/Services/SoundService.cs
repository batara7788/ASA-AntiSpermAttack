using System.Media;
using Asa.Core;

namespace Asa.App.Services;

public enum SoundEvent
{
    ProtectionStarted,
    ThreatDetected,
    ScanCompleted,
    DatabaseUpdated
}

public sealed class SoundService
{
    private readonly GameModeDetector gameMode;
    private readonly Func<bool> soundsEnabled;
    private readonly Dictionary<SoundEvent, Lazy<byte[]>> sounds = new()
    {
        [SoundEvent.ProtectionStarted] = new Lazy<byte[]>(SoundSynthesizer.CreateStartupFanfare),
        [SoundEvent.ThreatDetected] = new Lazy<byte[]>(SoundSynthesizer.CreateThreatAlarm),
        [SoundEvent.ScanCompleted] = new Lazy<byte[]>(SoundSynthesizer.CreateScanCompleteChime),
        [SoundEvent.DatabaseUpdated] = new Lazy<byte[]>(SoundSynthesizer.CreateDatabaseUpdatedChime)
    };
    private DateTime lastThreatSound = DateTime.MinValue;

    public SoundService(GameModeDetector gameMode, Func<bool> soundsEnabled)
    {
        this.gameMode = gameMode;
        this.soundsEnabled = soundsEnabled;
    }

    public void Play(SoundEvent soundEvent)
    {
        if (!soundsEnabled() || gameMode.IsUserBusy())
        {
            return;
        }

        if (soundEvent == SoundEvent.ThreatDetected)
        {
            if (DateTime.Now - lastThreatSound < TimeSpan.FromSeconds(5))
            {
                return;
            }

            lastThreatSound = DateTime.Now;
        }

        Task.Run(() =>
        {
            try
            {
                using var stream = new MemoryStream(sounds[soundEvent].Value);
                using var player = new SoundPlayer(stream);
                player.PlaySync();
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
            {
            }
        });
    }
}
