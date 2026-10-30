// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Transforms;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// When everything in a results screen's reveal happens, in ms from its start
/// </summary>
/// <remarks>
/// The screen works out everything it shows from the time alone, so skipping (<see cref="SkipFrom"/>) shows the reveal
/// as it would be then.
/// </remarks>
public class RevealTimeline
{
    public const double FILL_START = 620;
    public const double FILL_DURATION = 1900;

    /// <summary>
    /// The charge starts this long before the fill settles, and builds over <see cref="CHARGE_DURATION"/>.
    /// </summary>
    public const double CHARGE_LEAD = 120;

    public const double CHARGE_DURATION = 350;

    /// <summary>
    /// The stillness between the charge and the letter falling, and how long the letter takes to fall.
    /// </summary>
    public const double STILL = 70;

    public const double LETTER_FALL = 140;

    /// <summary>
    /// For a run a miss kept from S: how long after the S lands it breaks, and after that, how long until the rank it
    /// earned lands.
    /// </summary>
    public const double CRACK_AFTER_LANDING = 320;

    public const double LANDING_AFTER_CRACK = 820;

    /// <summary>
    /// How long after the crack the fill starts pulling back to where S begins, and over how long.
    /// </summary>
    public const double PULL_AFTER_CRACK = 200;

    public const double PULL_DURATION = 450;

    public const double BADGE_AFTER_LANDING = 560;
    public const double BEST_AFTER = 540;
    public const double MUSIC_AFTER_BEST = 700;
    public const double VOICE_AFTER_MUSIC = 350;
    public const double END_AFTER_MUSIC = 700;

    /// <summary>
    /// The fill ticks this often, while it's visibly moving (the first three quarters).
    /// </summary>
    public const double TICK_INTERVAL = 45;

    /// <summary>
    /// A skip first goes to this long before the first rank lands, so the charge's end and the hit still play.
    /// </summary>
    public const double SKIP_LEAD = 220;

    /// <summary>
    /// A second skip goes to this long before the end, so the music still comes in.
    /// </summary>
    public const double SKIP_TAIL = 300;

    /// <summary>
    /// Where the fill goes, 0–1: the run's accuracy (the true scale).
    /// </summary>
    public readonly float Target;

    /// <summary>
    /// The ranks the fill crosses, in order, and when.
    /// </summary>
    public readonly IReadOnlyList<(ScoreRank Rank, double Time)> Crossings;

    public double FillEnd => FILL_START + FILL_DURATION;

    public double ChargeStart => FillEnd - CHARGE_LEAD;

    public double ChargeFull => ChargeStart + CHARGE_DURATION;

    /// <summary>
    /// When the first rank lands: the S that breaks, for a run a miss kept from it, or the rank.
    /// </summary>
    public double FirstLanding => ChargeFull + STILL + LETTER_FALL;

    /// <summary>
    /// When the S breaks; NaN for a run that keeps its rank.
    /// </summary>
    public readonly double Crack;

    /// <summary>
    /// When the rank the run earned lands.
    /// </summary>
    public readonly double Landing;

    /// <summary>
    /// When the full combo's (or all perfect's) badge stamps; NaN without one.
    /// </summary>
    public readonly double Badge;

    /// <summary>
    /// When the run is compared with the best (and a new best stamps).
    /// </summary>
    public readonly double Best;

    public double Music => Best + MUSIC_AFTER_BEST;

    public double Voice => Music + VOICE_AFTER_MUSIC;

    /// <summary>
    /// When the reveal is over.
    /// </summary>
    public double End => Music + END_AFTER_MUSIC;

    public bool RankLost => !double.IsNaN(Crack);

    public RevealTimeline(double accuracy, bool rankLost, bool fullCombo)
    {
        Target = (float)Math.Clamp(accuracy, 0, 1);

        Crossings = ScoreRankExtensions.THRESHOLD_RANKS
                                       .Where(rank => Target + 1e-9 >= rank.MinimumAccuracy() && Target > 0)
                                       .Select(rank => (rank, FILL_START + timeToReach((float)rank.MinimumAccuracy() / Target) * FILL_DURATION))
                                       .ToArray();

        if (rankLost)
        {
            Crack = FirstLanding + CRACK_AFTER_LANDING;
            Landing = Crack + LANDING_AFTER_CRACK;
        }
        else
        {
            Crack = double.NaN;
            Landing = FirstLanding;
        }

        Badge = fullCombo ? Landing + BADGE_AFTER_LANDING : double.NaN;
        Best = (fullCombo ? Badge : Landing) + BEST_AFTER;
    }

    public static RevealTimeline For(GameplayResult result) => new RevealTimeline(result.Accuracy, result.RankLostToMiss, result.IsFullCombo);

    /// <summary>
    /// How far through the fill's motion it is at <paramref name="time"/>, 0–1.
    /// </summary>
    public float FillProgress(double time) => (float)Math.Clamp((time - FILL_START) / FILL_DURATION, 0, 1);

    /// <summary>
    /// How far round the ring the fill is at <paramref name="time"/>, 0–1: easing out to the accuracy, and for a lost S,
    /// pulled back after the crack to just under where S begins, so the ring ends in the rank earned.
    /// </summary>
    public float FillAt(double time)
    {
        float fill = Target * ease(FillProgress(time));

        if (RankLost)
        {
            float pull = outCubic((float)Math.Clamp((time - Crack - PULL_AFTER_CRACK) / PULL_DURATION, 0, 1));
            fill += ((float)ScoreRank.S.MinimumAccuracy() - 0.0005f - fill) * pull;
        }

        return fill;
    }

    /// <summary>
    /// The share of the run's numbers (score, combo, accuracy) shown at <paramref name="time"/>, 0–1, as they roll up with
    /// the fill.
    /// </summary>
    public float RollAt(double time) => ease(FillProgress(time));

    /// <summary>
    /// How charged the ring is at <paramref name="time"/>, 0–1: building before the first landing, gone once it lands.
    /// </summary>
    public float ChargeAt(double time) => time >= FirstLanding ? 0 : (float)Math.Clamp((time - ChargeStart) / CHARGE_DURATION, 0, 1);

    /// <summary>
    /// When the fill ticks: every <see cref="TICK_INTERVAL"/> while it's in the first three quarters of its motion.
    /// </summary>
    public IEnumerable<double> Ticks
    {
        get
        {
            for (double time = FILL_START; (time - FILL_START) / FILL_DURATION < 0.75; time += TICK_INTERVAL)
                yield return time;
        }
    }

    /// <summary>
    /// Where a skip at <paramref name="time"/> goes: just before the first rank lands, or once past that, nearly the end.
    /// </summary>
    public double SkipFrom(double time) => time < FirstLanding - SKIP_LEAD ? FirstLanding - SKIP_LEAD : Math.Max(time, End - SKIP_TAIL);

    // The fill's ease: fast at first, settling gently.
    private static float ease(float x) => (float)EasingFunctions.Apply(Easing.OutQuad, x);

    // How far through the fill's motion it reaches a share of its way: the ease above (out quad) solved backwards.
    private static double timeToReach(float share) => 1 - Math.Sqrt(Math.Max(0, 1 - Math.Min(1, share)));

    private static float outCubic(float t) => (float)EasingFunctions.Apply(Easing.OutCubic, t);
}
