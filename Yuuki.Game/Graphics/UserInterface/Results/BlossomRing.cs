// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// The results screen's accuracy, as a blossom ring: a ring filling in the rank's color, a thin ring of the rank bands in
/// the heroes' colors lighting up with it, and a sakura branch growing round the outside as it fills, with a cluster of
/// buds near each rank that open as the fill passes. The big bud at the top, where the ring starts and ends, opens on an
/// All Perfect. The lit band ring never sits still: specks of light drift round it and a comet of light runs round it
/// (<see cref="Shimmer"/>). The dark disc behind it all is alive too, like a soap bubble (<see cref="Backdrop"/>): it
/// wobbles, sends ripples of the fill's color out, and its film swirls with the heroes' colors, bright at its rim.
/// </summary>
/// <remarks>
/// <para>The rank bands are on the true scale: the fill is the accuracy (95% is 95% of the way round).</para>
/// <para>It's <see cref="RING_SIZE"/> units square, drawn round its middle. The screen drives it frame by frame
/// (<see cref="Fill"/>, <see cref="FillColor"/>, <see cref="Pulse"/>, <see cref="Charge"/>, <see cref="Glow"/>); the
/// flowers open by themselves as the fill passes their rank (<see cref="ThresholdCrossed"/>), and on cue
/// (<see cref="OpenAllPerfect"/>, <see cref="WiltRank"/>, <see cref="BloomWave"/>).</para>
/// </remarks>
public partial class BlossomRing : Container
{
    public const float RING_SIZE = 640;

    /// <summary>
    /// The main ring: its middle's radius, and its width.
    /// </summary>
    public const float FILL_RADIUS = 198;

    public const float FILL_WIDTH = 30;

    /// <summary>
    /// The thin ring of rank bands outside it.
    /// </summary>
    public const float BAND_RADIUS = 226;

    public const float BAND_WIDTH = 8;

    /// <summary>
    /// The bubble behind it all. The flowers reach out to about 300, so its edge sits clear of them, with dark between;
    /// it's a little bigger than the ring's box, and its glow and ripples go further (<see cref="BubbleDisc.REACH"/>).
    /// </summary>
    public const float BACKDROP_RADIUS = 340;

    /// <summary>
    /// The gap between two rank bands, along the ring.
    /// </summary>
    private const float band_gap = 4;

    /// <summary>
    /// How much wider the rings get on a <see cref="Pulse"/> of 1.
    /// </summary>
    private const float fill_pulse_width = 8;

    private const float band_pulse_width = 3;

    /// <summary>
    /// How near the fill has to come to a rank, as a fraction of the ring, before its buds grow out, and over how much
    /// more they're grown.
    /// </summary>
    private const float grow_ahead = 0.06f;

    private const float grow_span = 0.03f;

    /// <summary>
    /// How many specks of light drift round the lit band ring, and one in how many is a sparkle rather than a soft dot.
    /// </summary>
    private const int mote_count = 44;

    private const int sparkle_every = 4;

    /// <summary>
    /// How long the comet takes to run round the lit band ring, in ms, and how many dots its tail has.
    /// </summary>
    private const double comet_period = 4200;

    private const int comet_dots = 12;

    private static readonly Color backdrop_color = ColorExtensions.FromHex("#151D30");
    private static readonly Color track_color = ColorExtensions.FromHex("#3B324A");
    private static readonly Color grey = ColorExtensions.FromHex("#9A96A6");

    /// <summary>
    /// The ranks with a band, worst to best, and where each band starts: D at the top, then each rank's threshold.
    /// </summary>
    private static readonly ScoreRank[] band_ranks = { ScoreRank.D, ScoreRank.C, ScoreRank.B, ScoreRank.A, ScoreRank.S };

    [Resolved(canBeNull: true)]
    private ITextureManager? textureManager { get; set; }

    private readonly Sprite glow;
    private readonly BubbleDisc backdrop;
    private readonly RingArc fill;
    private readonly RingArc[] bandFills = new RingArc[band_ranks.Length];
    private readonly SakuraBranch branch;
    private readonly Container stems;
    private readonly Container flowers;
    private readonly Circle head;
    private readonly Circle headHalo;
    private readonly ParticleField motes;
    private readonly ParticleField sparkles;
    private readonly Mote[] moteStates = new Mote[mote_count];

    private readonly List<Cluster> clusters = new List<Cluster>();
    private readonly Cluster allPerfect;

    private float fillAmount;
    private float pulse;
    private float beat;
    private bool greyed;

    /// <summary>
    /// The colors the rank bands light up in, worst first: the heroes' by default. A screen can set them each frame
    /// (an All Perfect's pink pulse, a Full Bloom's moving colors).
    /// </summary>
    public readonly Color[] BandColors = band_ranks.Select(r => HeroColors.ForRank(r).Main).ToArray();

    /// <summary>
    /// Fired as the fill passes each rank's threshold, in order; that rank's buds open on their own.
    /// </summary>
    public event Action<ScoreRank>? ThresholdCrossed;

    public BlossomRing()
    {
        Size = new Vector2(RING_SIZE);

        // The glow lies under everything: a soft ring of light on the fill.
        glow = new Sprite
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(RING_SIZE),
            // The fill's color until it's given one.
            Color = JudgementColors.For(ScoreRank.D),
            Alpha = 0
        };

        backdrop = new BubbleDisc
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(2 * BACKDROP_RADIUS),
            DiscColor = backdrop_color,
            DiscAlpha = 0.5f
        };

        var children = new List<Drawable>
        {
            glow,
            backdrop,
            ring(FILL_RADIUS, FILL_WIDTH, track_color, 0.8f)
        };

        for (int i = 0; i < band_ranks.Length; i++)
        {
            (float start, float end) = bandSpan(i);
            RingArc track = ring(BAND_RADIUS, BAND_WIDTH, HeroColors.ForRank(band_ranks[i]).Main, 0.3f);
            track.Start = start;
            track.Progress = end;
            children.Add(track);
        }

        children.Add(fill = ring(FILL_RADIUS, FILL_WIDTH, JudgementColors.For(ScoreRank.D), 1));
        fill.Progress = 0;

        for (int i = 0; i < band_ranks.Length; i++)
        {
            children.Add(bandFills[i] = ring(BAND_RADIUS, BAND_WIDTH, BandColors[i], 1));
            bandFills[i].Start = bandSpan(i).Start;
            bandFills[i].Progress = 0;
        }

        // Light drifting round the band ring once it's lit, so it never sits still.
        children.Add(motes = new ParticleField { Blending = BlendingMode.Additive });
        children.Add(sparkles = new ParticleField { Blending = BlendingMode.Additive });

        // A tick across the band ring at each rank: the ring stays a clean chart, the flowers grow off it.
        foreach (ScoreRank rank in ScoreRankExtensions.THRESHOLD_RANKS)
        {
            float angle = MathF.Tau * (float)rank.MinimumAccuracy() - MathF.PI / 2;

            children.Add(new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * BAND_RADIUS,
                Size = new Vector2(16, 2),
                Rotation = angle * 180 / MathF.PI,
                Color = Color.White,
                Alpha = 0.55f
            });
        }

        children.Add(branch = new SakuraBranch());
        // The ring's size, not none: a child is drawn through its parent's size, and nothing shows in a parent of none.
        // Every stem under every flower, so none crosses a flower beside its own.
        children.Add(stems = new Container { RelativeSizeAxes = Axes.Both });
        children.Add(flowers = new Container { RelativeSizeAxes = Axes.Both });

        // The fill's leading edge: a bright head with a halo.
        children.Add(headHalo = new Circle
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(38),
            Color = Color.White,
            Alpha = 0
        });
        children.Add(head = new Circle
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(18),
            Color = Color.White,
            Alpha = 0
        });

        // The bubble's near side over it all, so the ring and flowers sit inside the bubble: its glints cross them.
        children.Add(backdrop.Front);

        Children = children;

        // A cluster near each rank: a main flower, a smaller lighter one and a bud that stays closed; S's is fuller.
        // The twists (and the motes) are only for variety, from a fixed seed so the ring is the same every time.
        var random = new Random(23);

        for (int i = 0; i < mote_count; i++)
        {
            moteStates[i] = new Mote(
                Turn: (float)random.NextDouble(),
                Speed: 0.006f + 0.016f * (float)random.NextDouble(),
                Offset: -7 + 14 * (float)random.NextDouble(),
                Size: 6 + 10 * (float)random.NextDouble(),
                Phase: (float)(random.NextDouble() * MathF.Tau),
                TwinkleRate: 1.5f + 3 * (float)random.NextDouble(),
                IsSparkle: i % sparkle_every == 0);
        }

        foreach (ScoreRank rank in ScoreRankExtensions.THRESHOLD_RANKS)
        {
            float at = (float)rank.MinimumAccuracy();
            Color hero = HeroColors.ForRank(rank).Main;
            bool big = rank == ScoreRank.S;
            var cluster = new Cluster(rank, at);

            cluster.Add(this, new FlowerSpot(big ? 0.95f : 0.85f, hero, Along: -0.035f, Out: -4, Twist: (float)random.NextDouble() * 40 - 20));
            cluster.Add(this, new FlowerSpot(big ? 0.7f : 0.6f, hero.Blend(Color.White, 0.25f), Along: 0.065f + (float)random.NextDouble() * 0.02f, Out: 13,
                Twist: (float)random.NextDouble() * 60, Delay: 90));
            cluster.Add(this, new FlowerSpot(0.45f, hero, Along: -0.095f, Out: 11, Delay: 150, Tilt: -30, StaysClosed: true));

            if (big)
                cluster.Add(this, new FlowerSpot(0.55f, hero.Blend(Color.White, 0.35f), Along: 0.12f, Out: -11, Twist: 20, Delay: 210));

            clusters.Add(cluster);
        }

        // Grown as far as the fill (none yet) has brought them, so they don't show before it comes near.
        updateGrowth();

        // Where the branch starts and the ring closes: the logo's bud, tilted as the logo is, with two small ones.
        Color yuuna = HeroColors.YUUNA.Main;
        allPerfect = new Cluster(null, 0);
        allPerfect.Add(this, new FlowerSpot(1.3f, yuuna, Out: -2, Logo: true));
        allPerfect.Add(this, new FlowerSpot(0.58f, yuuna.Blend(Color.White, 0.3f), Along: 0.05f, Out: 9, Twist: 30, Delay: 120));
        allPerfect.Add(this, new FlowerSpot(0.5f, yuuna, Along: -0.055f, Out: 7, Twist: -10, Delay: 200));
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        glow.Texture = textureManager?.Get("Results/ring-glow.png");
        motes.Texture = textureManager?.Get("Results/mote.png");
        sparkles.Texture = textureManager?.Get("Results/star.png");
    }

    /// <summary>
    /// How far round the ring is filled, 0–1: the accuracy shown so far.
    /// </summary>
    public float Fill
    {
        get => fillAmount;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (value == fillAmount)
                return;

            fillAmount = value;
            onFillChanged();
        }
    }

    /// <summary>
    /// The main ring's color: the rank reached so far.
    /// </summary>
    public Color FillColor
    {
        get => fill.Color;
        set
        {
            fill.Color = value;
            glow.Color = value;
            backdrop.LightColor = value.Blend(Color.White, 0.35f);
        }
    }

    /// <summary>
    /// How much a rank just reached swells the rings, 0–1.
    /// </summary>
    public float Pulse
    {
        get => pulse;
        set
        {
            pulse = value;

            setWidth(fill, FILL_RADIUS, FILL_WIDTH + fill_pulse_width * value);

            foreach (RingArc band in bandFills)
                setWidth(band, BAND_RADIUS, BAND_WIDTH + band_pulse_width * value);
        }
    }

    /// <summary>
    /// How charged the ring is before its rank lands, 0–1: the head's halo grows and brightens.
    /// </summary>
    public float Charge { get; set; }

    /// <summary>
    /// How bright the ring's glow is, 0–1.
    /// </summary>
    public float Glow
    {
        get => glow.Alpha;
        set => glow.Alpha = value;
    }

    /// <summary>
    /// How visible the fill's head is, 0–1: it shows while the fill moves.
    /// </summary>
    public float HeadAlpha { get; set; } = 1;

    /// <summary>
    /// The music's beat, 0–1 (1 on the beat), which the flowers pulse to and the backdrop breathes to.
    /// </summary>
    public float Beat
    {
        get => beat;
        set
        {
            beat = value;
            backdrop.Beat = value;
        }
    }

    /// <summary>
    /// How strongly the lit band ring shimmers, 0–1: specks of light drifting round it and twinkling, and a comet of
    /// light running round it every few seconds.
    /// </summary>
    public float Shimmer { get; set; } = 1;

    /// <summary>
    /// Greys the flowers, bands and the backdrop's film, as on a failed run.
    /// </summary>
    public bool Greyed
    {
        get => greyed;
        set
        {
            greyed = value;
            backdrop.Greyed = value;
        }
    }

    /// <summary>
    /// The dark disc behind the rings, alive like a bubble. It wobs by itself every few seconds; the screen can make it
    /// wob on cue instead (<see cref="BubbleDisc.Wob"/>: a rank landing, the beat), or calm it.
    /// </summary>
    public BubbleDisc Backdrop => backdrop;

    /// <summary>
    /// The sakura branch round the ring, in a breeze that carries the flowers with it. Gusts cross it every few
    /// seconds by themselves; the screen can send one on cue (<see cref="SakuraBranch.Gust"/>) or calm it.
    /// </summary>
    public SakuraBranch Branch => branch;

    /// <summary>
    /// Opens the top bud and its two small ones, at <paramref name="time"/> (now if not given): an All Perfect.
    /// </summary>
    public void OpenAllPerfect(double? time = null) => allPerfect.Open(time ?? Clock.CurrentTime);

    /// <summary>
    /// A rank the fill reached but the run didn't earn: its flowers wilt and fall from <paramref name="time"/>.
    /// </summary>
    public void WiltRank(ScoreRank rank, double? time = null)
    {
        foreach (Cluster cluster in clusters.Where(c => c.Rank == rank))
            cluster.Wilt(time ?? Clock.CurrentTime);
    }

    /// <summary>
    /// A Full Bloom: from <paramref name="time"/> a wave of blooming runs down the branch from the top, opening even the
    /// buds that stay closed, every flower taking on the heroes' colors in turn.
    /// </summary>
    public void BloomWave(double? time = null)
    {
        double at = time ?? Clock.CurrentTime;

        allPerfect.BloomWave(at, 0);

        foreach (Cluster cluster in clusters)
            cluster.BloomWave(at + (1 - cluster.At) * 1100, cluster.At * 360);
    }

    /// <summary>
    /// Every flower back to a bud, the fill emptied.
    /// </summary>
    public void Reset()
    {
        foreach (Cluster cluster in clusters)
            cluster.Close();

        allPerfect.Close();
        Fill = 0;
    }

    /// <summary>
    /// The point on the fill's leading edge, from the ring's middle, in its units.
    /// </summary>
    public Vector2 HeadPosition => pointAt(fillAmount, FILL_RADIUS);

    public override void Update()
    {
        base.Update();

        float angle = MathF.Tau * fillAmount - MathF.PI / 2;
        head.Position = headHalo.Position = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * FILL_RADIUS;
        head.Alpha = fillAmount > 0 ? HeadAlpha : 0;
        headHalo.Alpha = fillAmount > 0 ? HeadAlpha * (0.25f + 0.35f * Charge) : 0;
        headHalo.Size = new Vector2(2 * (19 + 10 * Charge));

        for (int i = 0; i < bandFills.Length; i++)
            bandFills[i].Color = bandColor(i);

        foreach (Cluster cluster in clusters)
            cluster.Update(Greyed, Beat, branch);

        allPerfect.Update(Greyed, Beat, branch);

        updateShimmer();
    }

    /// <summary>
    /// The motes drift and twinkle, and the comet runs round, wherever the band ring is lit: never past the fill's head.
    /// </summary>
    private void updateShimmer()
    {
        motes.Clear();
        sparkles.Clear();

        if (Shimmer <= 0 || fillAmount <= 0)
            return;

        float seconds = (float)(Clock.CurrentTime / 1000);
        var middle = new Vector2(RING_SIZE / 2);

        foreach (Mote mote in moteStates)
        {
            float turn = (mote.Turn + mote.Speed * seconds) % 1;
            float lit = litAt(turn);
            if (lit <= 0)
                continue;

            float twinkle = 0.5f + 0.5f * MathF.Sin(mote.Phase + mote.TwinkleRate * seconds);
            float radius = BAND_RADIUS + mote.Offset + 2 * MathF.Sin(1.3f * seconds + mote.Phase);
            Vector2 position = middle + pointAt(turn, radius);
            Color color = bandColorAt(turn).Blend(Color.White, 0.5f);
            float alpha = Shimmer * lit * (0.2f + 0.8f * twinkle * twinkle);

            if (mote.IsSparkle)
                sparkles.Add(new Particle(position, new Vector2(mote.Size * (0.6f + 0.6f * twinkle)), mote.Phase + 0.4f * seconds, color, alpha));
            else
                motes.Add(new Particle(position, new Vector2(mote.Size * (0.7f + 0.4f * twinkle)), 0, color, alpha * 0.8f));
        }

        // The comet runs from the top round to a little past the head, where it fades out, then starts again.
        float run = (float)(Clock.CurrentTime % comet_period / comet_period) * (fillAmount + 0.08f);

        for (int i = 0; i < comet_dots; i++)
        {
            float turn = run - i * 0.004f;
            if (turn < 0)
                break;

            float lit = litAt(turn);
            if (lit <= 0)
                continue;

            float tail = 1 - (float)i / comet_dots;
            Color color = bandColorAt(turn).Blend(Color.White, 0.6f);
            motes.Add(new Particle(middle + pointAt(turn, BAND_RADIUS), new Vector2(6 + 12 * tail), 0, color, Shimmer * lit * 0.7f * tail * tail));
        }
    }

    // How lit the band ring is at a turn: fully behind the fill's head, fading out just short of it.
    private float litAt(float turn) => Math.Clamp((fillAmount - turn) / 0.012f, 0, 1);

    private Color bandColorAt(float turn)
    {
        int band = 0;
        while (band + 1 < band_ranks.Length && turn >= band_ranks[band + 1].MinimumAccuracy())
            band++;

        return bandColor(band);
    }

    private Color bandColor(int band) => Greyed ? BandColors[band].Blend(grey, 0.62f) : BandColors[band];

    private void onFillChanged()
    {
        fill.Progress = fillAmount;
        branch.Progress = fillAmount;

        for (int i = 0; i < bandFills.Length; i++)
        {
            (float start, float end) = bandSpan(i);
            bandFills[i].Progress = Math.Clamp(fillAmount, start, end);
        }

        updateGrowth();

        foreach (Cluster cluster in clusters)
        {
            bool reached = fillAmount >= cluster.At;

            if (reached && !cluster.Reached)
            {
                cluster.Reached = true;
                cluster.Open(Clock.CurrentTime);
                ThresholdCrossed?.Invoke(cluster.Rank!.Value);
            }
            // Scrubbed back (a test): closes again, unless it's wilting, which the fill pulls back below on purpose.
            else if (!reached && cluster.Reached && !cluster.Wilting)
            {
                cluster.Reached = false;
                cluster.Close();
            }
        }
    }

    /// <summary>
    /// How far each rank's buds have grown out of the branch: not at all until the fill comes near their rank.
    /// </summary>
    private void updateGrowth()
    {
        foreach (Cluster cluster in clusters)
            cluster.Grow = outCubic(Math.Clamp((fillAmount - (cluster.At - grow_ahead)) / grow_span, 0, 1));
    }

    /// <summary>
    /// Where band <paramref name="index"/> starts and ends, as fractions of the ring, a gap left either side.
    /// </summary>
    private static (float Start, float End) bandSpan(int index)
    {
        float gap = band_gap / (MathF.Tau * BAND_RADIUS) / 2;
        float start = index == 0 ? 0 : (float)band_ranks[index].MinimumAccuracy();
        float end = index == band_ranks.Length - 1 ? 1 : (float)band_ranks[index + 1].MinimumAccuracy();
        return (start + gap, end - gap);
    }

    private static RingArc ring(float radius, float width, Color color, float alpha)
    {
        var arc = new RingArc
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            SmoothEdges = true,
            Color = color,
            Alpha = alpha
        };

        setWidth(arc, radius, width);
        return arc;
    }

    private static void setWidth(RingArc arc, float radius, float width)
    {
        float outer = radius + width / 2;
        arc.Size = new Vector2(2 * outer);
        arc.InnerRadius = (radius - width / 2) / outer;
    }

    private static Vector2 pointAt(float turn, float radius)
    {
        float angle = MathF.Tau * turn - MathF.PI / 2;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private static float outCubic(float t) => (float)EasingFunctions.Apply(Easing.OutCubic, t);

    /// <summary>
    /// A speck of light on the band ring: where it starts and how fast it drifts (turns a second), how far in or out of
    /// the band it sits, how big it is, and how it twinkles.
    /// </summary>
    private readonly record struct Mote(float Turn, float Speed, float Offset, float Size, float Phase, float TwinkleRate, bool IsSparkle);

    /// <summary>
    /// Where a flower goes in its cluster, and how it looks.
    /// </summary>
    /// <param name="Size">As a multiple of the prototype's.</param>
    /// <param name="Color">Its hero's color.</param>
    /// <param name="Along">How far along the branch from the cluster's rank, in radians.</param>
    /// <param name="Out">How far out from the branch, in units (in is negative); past 3 it's on a stem.</param>
    /// <param name="Twist">How far the open flower is turned, in degrees.</param>
    /// <param name="Delay">How long after the cluster's rank it opens, in ms.</param>
    /// <param name="Tilt">How far its bud leans from pointing straight out, in degrees.</param>
    /// <param name="StaysClosed">Only swells when its rank is reached.</param>
    /// <param name="Logo">Points as the logo's petal does, whichever way out is.</param>
    private readonly record struct FlowerSpot(float Size, Color Color, float Along = 0, float Out = 0, float Twist = 0, double Delay = 0, float Tilt = 0,
                                              bool StaysClosed = false, bool Logo = false);

    /// <summary>
    /// The flowers near one rank (or at the top, for an All Perfect), opening together, each a little after the last.
    /// </summary>
    private class Cluster
    {
        public readonly ScoreRank? Rank;

        /// <summary>
        /// Where on the ring it is, 0–1.
        /// </summary>
        public readonly float At;

        public bool Reached;
        public bool Wilting;
        public float Grow = 1;

        private readonly List<(BlossomFlower Flower, double Delay, float Turn, float Out)> flowers = new List<(BlossomFlower, double, float, float)>();

        public Cluster(ScoreRank? rank, float at)
        {
            Rank = rank;
            At = at;
        }

        public void Add(BlossomRing ring, FlowerSpot spot)
        {
            float turn = At + spot.Along / MathF.Tau;
            float angle = MathF.Tau * turn - MathF.PI / 2;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            float branchRadius = SakuraBranch.RadiusAt(turn);
            Vector2 position = direction * (branchRadius + spot.Out);

            var flower = new BlossomFlower
            {
                Position = position,
                FlowerSize = spot.Size,
                FlowerColor = spot.Color,
                FlowerTwist = spot.Twist,
                BudDirection = spot.Logo ? -28 : angle * 180 / MathF.PI + 90 + spot.Tilt,
                StaysClosed = spot.StaysClosed,
                StemFrom = Math.Abs(spot.Out) > 3 ? direction * branchRadius - position : null
            };

            ring.stems.Add(flower.Stem);
            ring.flowers.Add(flower);
            flowers.Add((flower, spot.Delay, turn, spot.Out));
        }

        public void Open(double time)
        {
            foreach ((BlossomFlower flower, double delay, _, _) in flowers)
                flower.Open(time + delay);
        }

        public void Wilt(double time)
        {
            Wilting = true;

            foreach ((BlossomFlower flower, _, _, _) in flowers)
                flower.Wilt(time);
        }

        public void BloomWave(double time, float hue)
        {
            foreach ((BlossomFlower flower, double delay, _, _) in flowers)
                flower.BloomWave(time + delay * 0.4, hue + (float)delay);
        }

        public void Close()
        {
            Wilting = false;

            foreach ((BlossomFlower flower, _, _, _) in flowers)
                flower.Close();
        }

        public void Update(bool greyed, float beat, SakuraBranch branch)
        {
            foreach ((BlossomFlower flower, _, float turn, float @out) in flowers)
            {
                flower.Grow = Rank == null ? 1 : Grow;
                flower.Greyed = greyed;
                flower.Beat = beat;

                // Riding the branch as the breeze moves it, nodding with it; a stem moves with its flower.
                float angle = MathF.Tau * turn - MathF.PI / 2;
                flower.Position = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (SakuraBranch.RadiusAt(turn) + branch.SwayAt(turn) + @out);
                flower.Nod = branch.NodAt(turn);
            }
        }
    }
}
