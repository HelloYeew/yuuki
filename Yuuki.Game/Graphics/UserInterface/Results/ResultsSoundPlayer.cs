// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Drawables;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// The score screen's reveal sounds
/// </summary>
public partial class ResultsSoundPlayer : Component
{
    private const string sample_directory = "Results/";

    private const string tick = "tick.wav";
    private const string threshold = "threshold.wav";
    private const string rank_low = "rank-low.wav";
    private const string rank_mid = "rank-mid.wav";
    private const string rank_high = "rank-high.wav";
    private const string rank_lost = "rank-lost.wav";
    private const string full_combo = "full-combo.wav";
    private const string all_perfect = "all-perfect.wav";

    [Resolved(canBeNull: true)]
    private IAudioStore<ISample>? sampleStore { get; set; }

    private readonly Dictionary<string, ISample?> samples = new Dictionary<string, ISample?>();

    [BackgroundDependencyLoader]
    private void load()
    {
        if (sampleStore.IsNull())
            return;

        // Preloaded so the first tick of the fill doesn't hitch on a disk read.
        foreach (string file in new[] { tick, threshold, rank_low, rank_mid, rank_high, rank_lost, full_combo, all_perfect })
            samples[file] = sampleStore!.Get(sample_directory + file);
    }

    /// <summary>
    /// A fill tick; <paramref name="progress"/> (0–1) raises its pitch as the ring fills.
    /// </summary>
    public void PlayTick(float progress) => play(tick, 1 + progress * 0.6, 0.6);

    /// <summary>
    /// A rank threshold was passed; higher ranks chime higher.
    /// </summary>
    public void PlayThreshold(ScoreRank rank) => play(threshold, 1 + (int)rank * 0.06);

    /// <summary>
    /// The rank letter landing, by how good it is.
    /// </summary>
    public void PlayRankReveal(ScoreRank rank) => play(rank switch
    {
        ScoreRank.S => rank_high,
        >= ScoreRank.B => rank_mid,
        _ => rank_low
    });

    /// <summary>
    /// A rank the fill reached is taken back (S with a miss).
    /// </summary>
    public void PlayRankLost() => play(rank_lost);
    public void PlayFullCombo() => play(full_combo);
    public void PlayAllPerfect() => play(all_perfect);

    private void play(string file, double frequency = 1, double volume = 1)
    {
        if (!samples.TryGetValue(file, out ISample? sample) || sample == null)
            return;

        // Backends hand back null when they're out of voices; a skipped tick is fine.
        IAudioChannel channel = sample.GetChannel();
        if (channel.IsNull())
            return;

        channel.AutoDispose = true;
        channel.Frequency.Value = frequency;
        channel.Volume.Value = volume;
        channel.Play();
    }
}
