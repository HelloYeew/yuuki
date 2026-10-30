// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Drawables;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// The score screen's reveal sounds (see tools/make_results_sounds.py)
/// </summary>
public partial class ResultsSoundPlayer : Component
{
    private const string sample_directory = "Results/";

    private const string tick = "tick.wav";
    private const string band = "band.wav";
    private const string charge = "charge.wav";
    private const string impact_s = "impact-s.wav";
    private const string impact_a = "impact-a.wav";
    private const string impact_b = "impact-b.wav";
    private const string impact_low = "impact-low.wav";
    private const string lost = "lost.wav";
    private const string miss = "miss.wav";
    private const string full_combo = "fc.wav";
    private const string all_perfect = "ap.wav";
    private const string best = "best.wav";
    private const string petals = "petals.wav";
    private const string bloom_gather = "fb-charge.wav";
    private const string full_bloom = "fullbloom.wav";

    /// <summary>
    /// How long the charge rises before it falls into the hit, in ms: the hit lands this long after it starts.
    /// </summary>
    public const double CHARGE_TO_HIT = 560;

    /// <summary>
    /// The fill's ticks climb these steps (semitones up), C E G A and on up two octaves, over the first three quarters of
    /// the fill.
    /// </summary>
    private static readonly int[] tick_steps = { 0, 4, 7, 9, 12, 16, 19, 21, 24 };

    /// <summary>
    /// Each rank crossed chimes higher: C, E, G, then the octave.
    /// </summary>
    private static readonly int[] crossing_steps = { 0, 4, 7, 12 };

    [Resolved(canBeNull: true)]
    private IAudioStore<ISample>? sampleStore { get; set; }

    private readonly Dictionary<string, ISample?> samples = new Dictionary<string, ISample?>();

    [BackgroundDependencyLoader]
    private void load()
    {
        if (sampleStore.IsNull())
            return;

        // Preloaded so the first tick of the fill doesn't hitch on a disk read.
        foreach (string file in new[] { tick, band, charge, impact_s, impact_a, impact_b, impact_low, lost, miss, full_combo, all_perfect, best, petals, bloom_gather, full_bloom })
            samples[file] = sampleStore!.Get(sample_directory + file);
    }

    /// <summary>
    /// A fill tick, <paramref name="progress"/> (0–1) of the way through the fill: it climbs as the ring fills.
    /// </summary>
    public void PlayTick(float progress) => play(tick, semitones(tick_steps[Math.Min(tick_steps.Length - 1, (int)(Math.Clamp(progress / 0.75f, 0, 1) * tick_steps.Length))]), 0.75);

    /// <summary>
    /// The fill crossed a rank's threshold, the <paramref name="index"/>th it crossed (0 for C).
    /// </summary>
    public void PlayCrossing(int index) => play(band, semitones(crossing_steps[Math.Clamp(index, 0, crossing_steps.Length - 1)]));

    /// <summary>
    /// The rise before the rank lands; the hit should follow <see cref="CHARGE_TO_HIT"/> later.
    /// </summary>
    public void PlayCharge() => play(charge);

    /// <summary>
    /// The misses' count landing, as the fill settles.
    /// </summary>
    public void PlayMiss() => play(miss);

    /// <summary>
    /// The rank letter landing: the better the rank, the bigger the hit.
    /// </summary>
    public void PlayLanding(ScoreRank rank) => play(rank switch
    {
        ScoreRank.SS or ScoreRank.S => impact_s,
        ScoreRank.A => impact_a,
        ScoreRank.B => impact_b,
        _ => impact_low
    });

    /// <summary>
    /// An S the fill reached breaks (the run had a miss).
    /// </summary>
    public void PlayRankLost() => play(lost);

    /// <summary>
    /// The full combo's (or all perfect's) badge stamping.
    /// </summary>
    public void PlayBadge(bool allPerfect) => play(allPerfect ? all_perfect : full_combo);

    public void PlayNewBest() => play(best);

    /// <summary>
    /// The swirl of petals after a good rank lands.
    /// </summary>
    public void PlayPetals() => play(petals, volume: 0.9);

    /// <summary>
    /// A Full Bloom's gather: rising for <see cref="BLOOM_GATHER_TO_BLOOM"/> before the bloom, which should land then.
    /// </summary>
    public void PlayBloomGather() => play(bloom_gather);

    /// <summary>
    /// How long the gather rises before the bloom lands, in ms.
    /// </summary>
    public const double BLOOM_GATHER_TO_BLOOM = 640;

    /// <summary>
    /// A Full Bloom: the biggest hit of the set.
    /// </summary>
    public void PlayFullBloom() => play(full_bloom);

    /// <summary>
    /// The Full Bloom badge stamping, after the cut-in: the crossing chime an octave up.
    /// </summary>
    public void PlayBloomStamp() => play(band, semitones(12), 0.7);

    private static double semitones(int steps) => Math.Pow(2, steps / 12.0);

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
