// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ObjectExtensions;

namespace Yuuki.Game.Audio;

/// <summary>
/// The tick for stepping through a list (song selects carousels, by wheel, keys, or the chevrons),
/// played so that a fast scroll doesn't turn into a machine gun
/// <list type="bullet">
/// <item>ticks closer together than <see cref="MIN_INTERVAL"/> are dropped,</item>
/// <item>a quick run gets quieter the longer it goes, and is back to full volume after a pause,</item>
/// <item>every tick's pitch varies slightly, so repeats don't sound identical,</item>
/// <item>a step that can't move (at either end) gives a lower, softer tick instead.</item>
/// </list>
/// </summary>
public class NavigationSoundPlayer
{
    /// <summary>
    /// The shortest gap between two ticks, in ms; anything sooner is skipped.
    /// </summary>
    public const double MIN_INTERVAL = 45;

    /// <summary>
    /// How quickly a run of ticks is forgotten, as a time constant in ms.
    /// </summary>
    private const double run_memory = 180;

    /// <summary>
    /// The quietest a long, fast run gets, relative to a lone tick.
    /// </summary>
    private const double min_run_volume = 0.45;

    /// <summary>
    /// Total width of the random pitch spread (±1.5%).
    /// </summary>
    private const double pitch_spread = 0.03;

    private const double edge_frequency = 0.8;
    private const double edge_volume = 0.6;

    /// <summary>
    /// What to play for one step: which way, how loud, and at what pitch.
    /// </summary>
    public readonly record struct Tick(int Direction, double Volume, double Frequency);

    private readonly ISample? previousSample;
    private readonly ISample? nextSample;
    private readonly Random random;

    private double lastTickTime = double.NegativeInfinity;

    /// <summary>
    /// How much of a run is still "heard": +1 per tick, fading with run_memory.
    /// </summary>
    private double run;

    public NavigationSoundPlayer(ISample? previousSample, ISample? nextSample, Random? random = null)
    {
        this.previousSample = previousSample;
        this.nextSample = nextSample;
        this.random = random ?? Random.Shared;
    }

    /// <summary>
    /// Works out the tick for a step at <paramref name="time"/> (ms), or null when it comes too soon
    /// after the last one. <paramref name="direction"/> is +1 for next, -1 for previous;
    /// <paramref name="moved"/> is false when the list was already at that end.
    /// </summary>
    public Tick? GetTick(int direction, bool moved, double time)
    {
        double sinceLast = time - lastTickTime;

        if (sinceLast < MIN_INTERVAL)
            return null;

        run = run * Math.Exp(-sinceLast / run_memory) + 1;
        lastTickTime = time;

        double volume = Math.Max(min_run_volume, 1 / Math.Sqrt(run));
        double frequency = 1 - pitch_spread / 2 + random.NextDouble() * pitch_spread;

        if (!moved)
        {
            volume *= edge_volume;
            frequency *= edge_frequency;
        }

        return new Tick(Math.Sign(direction), volume, frequency);
    }

    /// <summary>
    /// Plays the tick for a step, if one is due (see <see cref="GetTick"/>).
    /// </summary>
    public void Play(int direction, bool moved, double time)
    {
        if (GetTick(direction, moved, time) is not Tick tick)
            return;

        ISample? sample = tick.Direction > 0 ? nextSample : previousSample;
        if (sample.IsNull())
            return;

        // fire and forget: the channel frees itself when done (backends return null when out of voices).
        IAudioChannel channel = sample.GetChannel();
        if (channel.IsNull())
            return;

        channel.AutoDispose = true;
        channel.Volume.Value = tick.Volume;
        channel.Frequency.Value = tick.Frequency;
        channel.Play();
    }
}
