// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// Plays the gameplay hitsounds
/// </summary>
public partial class HitSoundPlayer : Component
{
    private const string sample_directory = "Gameplay/";

    /// <summary>
    /// File suffixes preloaded for every bank.
    /// </summary>
    private static readonly string[] bank_suffixes =
    {
        "hitnormal", "hitwhistle", "hitfinish", "hitclap", "sliderslide", "slidertick"
    };

    [Resolved(canBeNull: true)]
    private IAudioStore<ISample>? sampleStore { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    // The hitsound volume setting; full volume without a config (tests).
    private Reactive<double>? volume;

    private double currentVolume => volume?.Value ?? 1;

    // Preloaded samples keyed by bare filename (e.g. "normal-hitnormal.wav"). Null value = missing.
    private readonly Dictionary<string, ISample?> samples = new Dictionary<string, ISample?>();

    // Looping slide channels for holds currently being held, keyed by lane so each lane's hold can
    // start and stop its own loop independently.
    private readonly Dictionary<int, IAudioChannel> slideChannels = new Dictionary<int, IAudioChannel>();

    [BackgroundDependencyLoader]
    private void load()
    {
        if (config != null)
        {
            volume = config.Get<double>(YuukiSetting.HitsoundVolume);
            // Tracked: the setting outlives gameplay. Running hold loops follow it live.
            BindValueChanged(volume, e =>
            {
                foreach (IAudioChannel channel in slideChannels.Values)
                    channel.Volume.Value = e.NewValue;
            });
        }

        if (sampleStore.IsNull())
            return;

        foreach (SampleBank bank in new[] { SampleBank.Normal, SampleBank.Soft, SampleBank.Drum })
        {
            foreach (string suffix in bank_suffixes)
                preload($"{bank.Prefix()}-{suffix}.wav");
        }

        preload("combobreak.wav");
    }

    private void preload(string file)
    {
        if (samples.ContainsKey(file))
            return;

        // AudioStore.Get returns null when the file is missing; cache the miss too so we don't retry.
        samples[file] = sampleStore!.Get(sample_directory + file);
    }

    private ISample? get(string file)
    {
        if (samples.TryGetValue(file, out ISample? sample))
            return sample;

        // Not preloaded (e.g. a bank/suffix combination we didn't expect) — resolve lazily.
        if (sampleStore.IsNull())
            return samples[file] = null;

        return samples[file] = sampleStore!.Get(sample_directory + file);
    }

    /// <summary>
    /// Plays a note's hitsounds: the bank's <c>hitnormal</c> plus every enabled addition.
    /// </summary>
    public void PlayNote(BeatmapNote note)
    {
        string prefix = note.Bank.Prefix();

        play(get($"{prefix}-hitnormal.wav"));

        if (note.Additions.HasFlag(HitSampleAddition.Whistle))
            play(get($"{prefix}-hitwhistle.wav"));
        if (note.Additions.HasFlag(HitSampleAddition.Finish))
            play(get($"{prefix}-hitfinish.wav"));
        if (note.Additions.HasFlag(HitSampleAddition.Clap))
            play(get($"{prefix}-hitclap.wav"));
    }

    /// <summary>
    /// The sting played when a combo is broken.
    /// </summary>
    public void PlayComboBreak() => play(get("combobreak.wav"));

    /// <summary>
    /// Plays a sample once at the hitsound volume; muted, it isn't played at all.
    /// </summary>
    private void play(ISample? sample)
    {
        if (sample == null || currentVolume <= 0)
            return;

        // Backends hand back null when they're out of voices; a skipped hitsound is fine.
        IAudioChannel channel = sample.GetChannel();
        if (channel.IsNull())
            return;

        channel.AutoDispose = true;
        channel.Volume.Value = currentVolume;
        channel.Play();
    }

    /// <summary>
    /// Starts (or restarts) the looping slide sound for a held note in <paramref name="lane"/>.
    /// </summary>
    public void StartSlide(int lane, SampleBank bank)
    {
        StopSlide(lane);

        ISample? slide = get($"{bank.Prefix()}-sliderslide.wav");
        if (slide == null)
            return;

        IAudioChannel channel = slide.GetChannel();
        if (channel.IsNull())
            return;

        channel.Looping = true;
        channel.Volume.Value = currentVolume;
        channel.Play();
        slideChannels[lane] = channel;
    }

    /// <summary>
    /// A discrete tick while a hold is sustained.
    /// </summary>
    public void PlaySlideTick(SampleBank bank) => play(get($"{bank.Prefix()}-slidertick.wav"));

    /// <summary>
    /// Stops and releases the slide loop for <paramref name="lane"/>, if one is running.
    /// </summary>
    public void StopSlide(int lane)
    {
        if (!slideChannels.TryGetValue(lane, out IAudioChannel? channel))
            return;

        channel.Stop();
        channel.Dispose();
        slideChannels.Remove(lane);
    }

    /// <summary>
    /// Stops every running slide loop (e.g. on restart or when leaving gameplay).
    /// </summary>
    public void StopAllSlides()
    {
        foreach (IAudioChannel channel in slideChannels.Values)
        {
            channel.Stop();
            channel.Dispose();
        }

        slideChannels.Clear();
    }
}
