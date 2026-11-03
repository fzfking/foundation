using Foundation.Audio;
using NUnit.Framework;
using System;

namespace Foundation.Tests.Audio
{
    public sealed class AudioServiceTests
    {
        [Test]
        public void MissingIdentifier_FailsClearly()
        {
            Assert.Throws<ArgumentException>(() => CreateService(new NullAudioPlaybackBackend()).PlayAsync("", "ui"));
        }

        [Test]
        public void AudioSettings_UpdatesOnlyAfterARealChange()
        {
            var definitions = new IAudioChannelDefinition[]
            {
                new AudioChannelDefinition("ui"),
            };
            var settings = new AudioSettingsState(definitions);
            var changes = 0;
            settings.Changed += () => changes++;

            settings.SetMasterVolume(1f);
            settings.SetChannelVolume("ui", 0.4f);
            settings.SetChannelVolume("ui", 0.4f);
            settings.SetOutputGroupVolume("SFX", 0.25f);

            Assert.That(changes, Is.EqualTo(2));
            Assert.That(settings.GetChannelVolume("ui"), Is.EqualTo(0.4f));
            Assert.That(settings.GetOutputGroupVolume("SFX"), Is.EqualTo(0.25f));
        }

        [Test]
        public void MuteChange_RaisesChangedOnlyWhenTheStateReallyChanges()
        {
            var settings = new AudioSettingsState(new IAudioChannelDefinition[] { new AudioChannelDefinition("ui") });
            var changes = 0;
            settings.Changed += () => changes++;

            settings.SetChannelMuted("ui", true);
            settings.SetChannelMuted("ui", true);
            settings.SetChannelMuted("ui", false);

            Assert.That(changes, Is.EqualTo(2));
            Assert.That(settings.IsChannelMuted("ui"), Is.False);
        }

        private static AudioService CreateService(IAudioPlaybackBackend backend)
            => new AudioService(backend, new AudioEngine(new NullAudioBackend(), new IAudioChannelDefinition[0], new AudioSettingsState(null), new AudioEventRegistry()));
    }
}
