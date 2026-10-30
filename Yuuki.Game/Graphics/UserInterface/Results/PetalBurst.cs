// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// What a burst throws.
/// </summary>
public enum BurstPieceKind
{
    /// <summary>
    /// A sakura petal, tumbling as it drifts down.
    /// </summary>
    Petal,

    /// <summary>
    /// A streak of light, shrinking as it flies.
    /// </summary>
    Spark,

    /// <summary>
    /// A shard of a broken rank, dropping fast.
    /// </summary>
    Shard,

    /// <summary>
    /// A four-point sparkle.
    /// </summary>
    Star
}

/// <summary>
/// One piece of a <see cref="PetalBurst"/>. It's thrown from the burst's middle and slows as air would slow it, then
/// falls; its whole path is worked out from the time since the burst, so a burst is the same however it's stepped.
/// </summary>
/// <param name="Kind">What it is.</param>
/// <param name="Angle">Which way it's thrown, in radians (0 is right, down is positive).</param>
/// <param name="Speed">How fast it's thrown, in units a ms.</param>
/// <param name="Size">How big it is (a petal's length is about three times this).</param>
/// <param name="Life">How long it lasts, in ms: it fades out toward the end.</param>
/// <param name="Phase">Where its turning and tumbling start, in radians.</param>
/// <param name="Spin">How fast it turns, in radians a ms.</param>
/// <param name="Tumble">How fast a petal flips over, in radians a ms.</param>
/// <param name="Color">Its color, or none to take the heroes' in turn from <paramref name="Hue"/>.</param>
/// <param name="Hue">Where on the heroes' colors it starts, in degrees, when it has no color.</param>
/// <param name="Drag">How quickly air slows it, as the ms it takes to lose most of its speed; 0 for its kind's own.</param>
/// <param name="Heavy">Falls fast, as a wilted petal.</param>
public readonly record struct BurstPiece(BurstPieceKind Kind, float Angle, float Speed, float Size, double Life, float Phase = 0, float Spin = 0,
                                         float Tumble = 0, Color? Color = null, float Hue = 0, float Drag = 0, bool Heavy = false)
{
    /// <summary>
    /// How long its kind takes to lose most of its speed, in ms, unless it says otherwise.
    /// </summary>
    public float DragTime => Drag > 0 ? Drag : Kind switch
    {
        BurstPieceKind.Spark => 140,
        BurstPieceKind.Shard => 400,
        _ => 300
    };
}

/// <summary>
/// Pieces thrown from one point at one moment: a rank landing, a rank crossed, the fill's trail. The ready-made bursts
/// are the prototype's, each from its own seed so a reveal looks the same every time.
/// </summary>
public class PetalBurst
{
    /// <summary>
    /// The longest any piece lasts, in ms: after this a burst is over.
    /// </summary>
    public const double MAX_LIFE = 3600;

    private static readonly Color white = Color.White;
    private static readonly Color pale_pink = ColorExtensions.FromHex("#F6C3CB");

    /// <summary>
    /// The petals' own pinks: Yuuna's, white, her light swatch and a pale pink.
    /// </summary>
    public static readonly Color[] PINKS = { HeroColors.YUUNA.Main, white, HeroColors.YUUNA.Light, pale_pink };

    /// <summary>
    /// When it bursts, on the clock it's shown on.
    /// </summary>
    public double StartTime { get; }

    /// <summary>
    /// Where it bursts from, in the units it's shown in.
    /// </summary>
    public Vector2 Origin { get; }

    public IReadOnlyList<BurstPiece> Pieces { get; }

    public PetalBurst(double startTime, Vector2 origin, IReadOnlyList<BurstPiece> pieces)
    {
        StartTime = startTime;
        Origin = origin;
        Pieces = pieces;
    }

    /// <summary>
    /// A rank landing: petals by how good it is (none below B), with sparks in white and the rank's color. An All Perfect
    /// throws the most, in every hero's color.
    /// </summary>
    /// <param name="rank">The rank landed.</param>
    /// <param name="fullCombo">A full combo: Karin's light blue joins the petals.</param>
    /// <param name="allPerfect">An All Perfect.</param>
    /// <param name="rankColor">The rank's color, for half the sparks.</param>
    /// <param name="seed">Where its randomness starts: the same seed throws the same burst.</param>
    public static List<BurstPiece> Landing(ScoreRank rank, bool fullCombo, bool allPerfect, Color rankColor, int seed = 31)
    {
        var random = new Random(seed);
        var pieces = new List<BurstPiece>();

        int petals = allPerfect ? 96 : rank switch
        {
            ScoreRank.SS or ScoreRank.S => 66,
            ScoreRank.A => 28,
            ScoreRank.B => 10,
            _ => 0
        };
        int sparks = allPerfect || rank >= ScoreRank.S ? 20 : rank == ScoreRank.A ? 22 : 16;

        HeroSwatch hero = HeroColors.ForRank(rank);
        var colors = new List<Color>(PINKS) { hero.Main, hero.Light };
        if (fullCombo)
            colors.Add(HeroColors.KARIN.Light);

        for (int i = 0; i < petals; i++)
        {
            pieces.Add(new BurstPiece(BurstPieceKind.Petal, angle(random), between(random, 0.45f, 1.4f), between(random, 7, 17), between(random, 1500, 2700),
                Phase: between(random, 0, 6.3f), Spin: between(random, -0.006f, 0.006f), Tumble: between(random, 0.004f, 0.014f),
                Color: allPerfect ? null : pick(random, colors), Hue: between(random, 0, 360)));
        }

        for (int i = 0; i < sparks; i++)
        {
            pieces.Add(new BurstPiece(BurstPieceKind.Spark, angle(random), between(random, 1, 2.2f), between(random, 2, 4.4f), between(random, 380, 700),
                Color: random.NextDouble() < 0.5 ? white : rankColor));
        }

        return pieces;
    }

    /// <summary>
    /// A few small pink petals shed from the fill's leading edge, thrown out from the ring; more while it's quick.
    /// </summary>
    /// <param name="random">Where its randomness comes from: the reveal's own, so the trail is the same every time.</param>
    /// <param name="outward">Which way is out from the ring there, in radians.</param>
    /// <param name="count">How many.</param>
    public static List<BurstPiece> Trail(Random random, float outward, int count)
    {
        var pieces = new List<BurstPiece>();

        for (int i = 0; i < count; i++)
        {
            pieces.Add(new BurstPiece(BurstPieceKind.Petal, outward + between(random, -0.75f, 0.75f), between(random, 0.16f, 0.48f), between(random, 4, 8.5f),
                between(random, 800, 1500), Phase: between(random, 0, 6.3f), Spin: between(random, -0.005f, 0.005f), Tumble: between(random, 0.006f, 0.016f),
                Color: pick(random, PINKS)));
        }

        return pieces;
    }

    /// <summary>
    /// A rank crossed: a puff of petals in its hero's colors, out from the ring.
    /// </summary>
    public static List<BurstPiece> Crossing(Random random, ScoreRank rank, float outward)
    {
        HeroSwatch hero = HeroColors.ForRank(rank);
        Color[] colors = { hero.Main, white, hero.Light };
        var pieces = new List<BurstPiece>();

        for (int i = 0; i < 16; i++)
        {
            pieces.Add(new BurstPiece(BurstPieceKind.Petal, outward + between(random, -1.3f, 1.3f), between(random, 0.32f, 0.92f), between(random, 5, 10),
                between(random, 900, 1500), Phase: between(random, 0, 6.3f), Spin: between(random, -0.006f, 0.006f), Tumble: between(random, 0.005f, 0.015f),
                Color: colors[i % colors.Length]));
        }

        return pieces;
    }

    /// <summary>
    /// A rank broken: red shards thrown up, falling fast.
    /// </summary>
    public static List<BurstPiece> Shards(int seed = 5)
    {
        var random = new Random(seed);
        Color[] colors = { JudgementColors.MISS, ColorExtensions.FromHex("#E58A86") };
        var pieces = new List<BurstPiece>();

        for (int i = 0; i < 12; i++)
        {
            pieces.Add(new BurstPiece(BurstPieceKind.Shard, -MathF.PI * between(random, 0.15f, 0.85f), between(random, 0.25f, 0.65f), between(random, 8, 18),
                between(random, 700, 1000), Phase: between(random, 0, 6.3f), Spin: between(random, -0.01f, 0.01f), Color: pick(random, colors)));
        }

        return pieces;
    }

    /// <summary>
    /// A lost rank's own petals wilting: grey, heavy, falling.
    /// </summary>
    public static List<BurstPiece> Wilted(int seed = 77)
    {
        var random = new Random(seed);
        Color[] colors = { ColorExtensions.FromHex("#9A9AA6"), ColorExtensions.FromHex("#C2B9BE"), ColorExtensions.FromHex("#7E7A88") };
        var pieces = new List<BurstPiece>();

        for (int i = 0; i < 14; i++)
        {
            pieces.Add(new BurstPiece(BurstPieceKind.Petal, -MathF.PI / 2 + between(random, -1, 1), between(random, 0.12f, 0.32f), between(random, 7, 13),
                between(random, 1300, 1800), Phase: between(random, 0, 6.3f), Spin: between(random, -0.004f, 0.004f), Tumble: between(random, 0.003f, 0.008f),
                Color: colors[i % colors.Length], Heavy: true));
        }

        return pieces;
    }

    /// <summary>
    /// An All Perfect's sparkles, white and gold.
    /// </summary>
    public static List<BurstPiece> Sparkles(int seed = 9)
    {
        var random = new Random(seed);
        Color gold = ColorExtensions.FromHex("#FFE08A");
        var pieces = new List<BurstPiece>();

        for (int i = 0; i < 22; i++)
        {
            pieces.Add(new BurstPiece(BurstPieceKind.Star, angle(random), between(random, 0.45f, 1.05f), between(random, 7, 15), between(random, 900, 1600),
                Phase: between(random, 0, 6.3f), Spin: between(random, -0.005f, 0.005f), Color: random.NextDouble() < 0.5 ? white : gold));
        }

        return pieces;
    }

    /// <summary>
    /// A Full Bloom's storm: petals in every hero's color, thrown hard enough to reach every corner, with sparks.
    /// </summary>
    public static List<BurstPiece> Storm(int seed = 41)
    {
        var random = new Random(seed);
        var pieces = new List<BurstPiece>();

        for (int i = 0; i < 240; i++)
        {
            pieces.Add(new BurstPiece(BurstPieceKind.Petal, angle(random), between(random, 0.35f, 2.45f), between(random, 7, 18), between(random, 2000, 3400),
                Phase: between(random, 0, 6.3f), Spin: between(random, -0.007f, 0.007f), Tumble: between(random, 0.004f, 0.014f), Hue: between(random, 0, 360),
                Drag: 520));
        }

        for (int i = 0; i < 36; i++)
        {
            pieces.Add(new BurstPiece(BurstPieceKind.Spark, angle(random), between(random, 1.6f, 3.2f), between(random, 2.5f, 5.5f), between(random, 420, 780),
                Color: i % 2 == 1 ? white : HeroColors.CYCLE[i % HeroColors.CYCLE.Length]));
        }

        return pieces;
    }

    private static float angle(Random random) => (float)(random.NextDouble() * MathF.Tau);

    private static float between(Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    private static Color pick(Random random, IReadOnlyList<Color> colors) => colors[random.Next(colors.Count)];
}
