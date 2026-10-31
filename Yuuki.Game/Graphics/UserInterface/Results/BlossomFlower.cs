// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// A blossom on the sakura branch: a bud (the yuuki logo's petal) that opens into a five-petal flower, its one petal
/// fanning out into five. It can stay a bud that only swells, wilt and fall (a lost rank), and take on every hero's color
/// in turn (a Full Bloom).
/// </summary>
/// <remarks>
/// Everything it does is worked out each frame from when it was told to (<see cref="Open"/>, <see cref="Wilt"/>,
/// <see cref="BloomWave"/>), on its own clock, so telling it a time already past shows it as it would be by now: a
/// skipped reveal lands every flower where it should be. Sizes are in the parent's units, at <see cref="FlowerSize"/> 1
/// as the prototype's.
/// </remarks>
public partial class BlossomFlower : Container
{
    /// <summary>
    /// The petal textures' size in the petals' own units (tools/make_results_textures.py: 160 px at 3.6 px a unit).
    /// </summary>
    private const float texture_units = 160 / 3.6f;

    /// <summary>
    /// How far below the texture's middle a petal's base is, in its units.
    /// </summary>
    private const float petal_base = 16;

    private static readonly Color shadow_color = ColorExtensions.FromHex("#1B1622");
    private static readonly Color grey = ColorExtensions.FromHex("#9A96A6");
    private static readonly Color wilted = ColorExtensions.FromHex("#8E8A99");
    private static readonly Vector2 shadow_offset = new Vector2(1.2f, 2.6f);
    private const float shadow_alpha = 0.38f;

    // How long each part of opening takes, in ms.
    private const double pop_duration = 240;
    private const double settle_duration = 320;
    private const double bud_fade = 220;
    private const double fan_duration = 460;

    // How a Full Bloom's wave runs the colors: a little faster than the cycle, as everything else then.
    private const double wave_color_speed = 1.4;

    [Resolved(canBeNull: true)]
    private ITextureManager? textureManager { get; set; }

    private readonly StemLayer stem;
    private readonly Sprite glow;
    private readonly Container budGroup;
    private readonly Container flowerGroup;
    private readonly Sprite[] petals = new Sprite[5];
    private readonly Sprite[] petalShadows = new Sprite[5];
    private readonly Sprite bud;
    private readonly Sprite budShadow;
    private readonly Circle core;

    private double openAt = double.PositiveInfinity;
    private double swellAt = double.PositiveInfinity;
    private double wiltAt = double.PositiveInfinity;
    private double waveAt = double.PositiveInfinity;
    private float waveHue;

    /// <summary>
    /// Its own color, the hero's.
    /// </summary>
    public Color FlowerColor { get; set; } = HeroColors.YUUNA.Main;

    /// <summary>
    /// How big it is, as a multiple of the prototype's.
    /// </summary>
    public float FlowerSize { get; init; } = 1;

    /// <summary>
    /// Which way the bud points, in degrees (0 is up): out from the ring, or tilted as the logo is.
    /// </summary>
    public float BudDirection { get; init; }

    /// <summary>
    /// How far the open flower is turned, in degrees, so the flowers in a cluster don't all face the same way.
    /// </summary>
    public float FlowerTwist { get; init; }

    /// <summary>
    /// A bud that stays closed when its rank is reached (it only swells), and opens only in a Full Bloom.
    /// </summary>
    public bool StaysClosed { get; init; }

    /// <summary>
    /// Where its stem joins the branch, from the flower; none when it sits on the branch itself. <see cref="Stem"/> draws
    /// it.
    /// </summary>
    public Vector2? StemFrom { get; init; }

    /// <summary>
    /// Its stem (<see cref="StemFrom"/>), apart from it so that it can go under every flower: a stem drawn with its own
    /// flower would cross any flower added before it. Add it in a parent just like the flower's (the same, or one the same
    /// size in the same place), under all the flowers there. It goes wherever the flower goes, and grows out with it.
    /// </summary>
    public Drawable Stem => stem;

    /// <summary>
    /// How far it has grown out of the branch, 0–1: it grows as the fill comes near.
    /// </summary>
    public float Grow { get; set; } = 1;

    /// <summary>
    /// Greyed out, as on a failed run.
    /// </summary>
    public bool Greyed { get; set; }

    /// <summary>
    /// How far it nods in the breeze, in degrees (clockwise): the bud or flower leans on its stem.
    /// </summary>
    public float Nod { get; set; }

    /// <summary>
    /// Whether it has been told to open (or is closed for good).
    /// </summary>
    public bool IsOpen => !double.IsPositiveInfinity(openAt);

    public bool IsWilting => !double.IsPositiveInfinity(wiltAt);

    public BlossomFlower()
    {
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        Size = new Vector2(64);
        // A ring's buds start unseen and are only ever shown by their alpha (Grow): kept present so they're drawn
        // properly once they are.
        AlwaysPresent = true;

        stem = new StemLayer(this);

        Children = new Drawable[]
        {
            // A soft light in the flower's own color as it opens or a wave passes over it. Always present: a drawable
            // that starts unseen and is only ever shown by its alpha isn't drawn again properly when it's shown.
            glow = new Sprite
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Blending = BlendingMode.Additive,
                Alpha = 0,
                AlwaysPresent = true
            },
            budGroup = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(texture_units),
                Children = new Drawable[]
                {
                    budShadow = petalSprite(1, shadow_offset),
                    bud = petalSprite(1, Vector2.Zero)
                }
            },
            flowerGroup = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(texture_units),
                Alpha = 0
            }
        };

        // Shadows first, so no petal's shadow falls on another petal.
        for (int i = 0; i < 5; i++)
            flowerGroup.Add(petalShadows[i] = petalSprite(petal_base, shadow_offset));

        for (int i = 0; i < 5; i++)
            flowerGroup.Add(petals[i] = petalSprite(petal_base, Vector2.Zero));

        flowerGroup.Add(core = new Circle
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(8.4f)
        });

        budShadow.Color = shadow_color;
        budShadow.Alpha = shadow_alpha;

        foreach (Sprite petalShadow in petalShadows)
        {
            petalShadow.Color = shadow_color;
            petalShadow.Alpha = shadow_alpha;
        }
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        // A thin twig from the branch out to the flower.
        if (StemFrom is Vector2 from)
        {
            stem.Add(new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.CentreLeft,
                Size = new Vector2(from.Length(), 2),
                Rotation = MathF.Atan2(from.Y, from.X) * 180 / MathF.PI,
                Color = ColorExtensions.FromHex("#6E5262")
            });
        }

        // The soft dot fades out well inside its box, so the light reaches a little past the petals.
        glow.Size = new Vector2(110 * FlowerSize);
        glow.Texture = textureManager?.Get("Results/mote.png");

        Texture? budTexture = textureManager?.Get("Results/bud.png");
        Texture? budShadowTexture = textureManager?.Get("Results/bud-shadow.png");
        Texture? petalTexture = textureManager?.Get("Results/petal.png");
        Texture? petalShadowTexture = textureManager?.Get("Results/petal-shadow.png");

        bud.Texture = budTexture;
        budShadow.Texture = budShadowTexture;

        foreach (Sprite petal in petals)
            petal.Texture = petalTexture;

        foreach (Sprite petalShadow in petalShadows)
            petalShadow.Texture = petalShadowTexture;

        // The bud sits centred on its spot, pointing out; the open petals start from where it points.
        bud.Rotation = budShadow.Rotation = BudDirection;
        bud.Scale = budShadow.Scale = new Vector2(FlowerSize * 0.8f);
    }

    /// <summary>
    /// Opens it at <paramref name="time"/> on its clock (now if not given): a pop past its size, the bud fading as its
    /// petal fans into five. A flower that <see cref="StaysClosed"/> only swells.
    /// </summary>
    public void Open(double? time = null)
    {
        if (StaysClosed)
            swellAt = time ?? Clock.CurrentTime;
        else
            openAt = time ?? Clock.CurrentTime;
    }

    /// <summary>
    /// Takes back a rank: from <paramref name="time"/> it greys, shakes "no", then drops away, turning as it falls.
    /// </summary>
    public void Wilt(double? time = null) => wiltAt = time ?? Clock.CurrentTime;

    /// <summary>
    /// A Full Bloom reaches it at <paramref name="time"/>: it pops (a closed bud opens at last) and takes on every hero's
    /// color in turn, starting <paramref name="hue"/> degrees round, so a wave of color runs along the branch.
    /// </summary>
    public void BloomWave(double? time = null, float hue = 0)
    {
        waveAt = time ?? Clock.CurrentTime;
        waveHue = hue;
    }

    /// <summary>
    /// Back to a closed bud.
    /// </summary>
    public void Close()
    {
        openAt = swellAt = wiltAt = waveAt = double.PositiveInfinity;
    }

    public override void Update()
    {
        base.Update();

        double now = Clock.CurrentTime;

        // A bud that stays closed opens only with a Full Bloom's wave.
        double opened = StaysClosed ? waveAt : openAt;
        bool open = now >= opened;
        double sinceOpen = now - opened;
        double sinceWave = now - waveAt;
        double sinceWilt = now - wiltAt;

        float scale = 1, rotation = 0, alpha = 1, glowAlpha = 0;
        Vector2 offset = Vector2.Zero;
        Color color = Greyed ? FlowerColor.Blend(grey, 0.62f) : FlowerColor;

        if (open)
        {
            // Pops a little past its size, then settles.
            scale = sinceOpen < pop_duration
                ? 1.12f * outCubic((float)(sinceOpen / pop_duration))
                : 1.12f - 0.12f * outCubic(clamp01((sinceOpen - pop_duration) / settle_duration));
            glowAlpha = 0.45f * MathF.Exp((float)(-sinceOpen / 240));
        }

        if (sinceWave >= 0)
        {
            color = HeroColors.Cycle(sinceWave * wave_color_speed, waveHue);

            if (!StaysClosed)
                scale *= 1 + 0.42f * clamp01(sinceWave / 40) * MathF.Exp((float)(-Math.Max(0, sinceWave - 40) / 170));

            glowAlpha = Math.Max(glowAlpha, 0.6f * MathF.Exp((float)(-sinceWave / 260)));
        }

        if (sinceWilt >= 0)
        {
            float wilt = (float)sinceWilt;
            color = FlowerColor.Blend(wilted, clamp01(wilt / 200));
            offset.X = 8 * MathF.Sin(wilt / 22) * MathF.Exp(-wilt / 160);

            // Then it lets go: falling faster and faster, turning, fading.
            if (wilt > 300)
            {
                float falling = wilt - 300;
                offset.Y = 0.0015f * falling * falling;
                rotation += 0.09f * falling;
                alpha = 1 - clamp01(falling / 650);
            }
        }

        Alpha = Grow;

        // The bud: fades as the flower opens; a closed one swells when its rank is reached.
        float swell = !open && now >= swellAt ? 1 + 0.3f * MathF.Exp((float)(-(now - swellAt) / 200)) : 1;
        budGroup.Alpha = open ? Math.Max(0, 1 - clamp01(sinceOpen / bud_fade)) : sinceWilt >= 0 ? alpha : 1;
        budGroup.Position = offset;
        budGroup.Rotation = Nod;
        budGroup.Scale = new Vector2(swell * (0.4f + 0.6f * Grow));
        bud.Color = color;

        flowerGroup.Alpha = open ? alpha : 0;
        flowerGroup.Position = offset;
        flowerGroup.Rotation = rotation + FlowerTwist + Nod;
        flowerGroup.Scale = new Vector2(scale * FlowerSize);

        // Its one petal fans out into five.
        float fan = open ? outCubic(clamp01(sinceOpen / fan_duration)) : 0;

        for (int i = 0; i < 5; i++)
        {
            petals[i].Rotation = petalShadows[i].Rotation = BudDirection + i * 72 * fan;
            petals[i].Scale = petalShadows[i].Scale = new Vector2(0.8f + 0.2f * fan);
            petals[i].Color = color;
        }

        core.Color = color.Blend(shadow_color, 0.4f);
        core.Alpha = fan;
        glow.Color = color.Blend(Color.White, 0.5f);
        glow.Alpha = glowAlpha;
    }

    /// <summary>
    /// A petal texture's sprite, turned and scaled about its base, which is <paramref name="baseBelowMiddle"/> below the
    /// texture's middle and goes at <paramref name="at"/> from the group's middle.
    /// </summary>
    private static Sprite petalSprite(float baseBelowMiddle, Vector2 at) => new Sprite
    {
        Anchor = Anchor.Centre,
        CustomOrigin = new Vector2(0.5f, 0.5f + baseBelowMiddle / texture_units),
        Position = at,
        Size = new Vector2(texture_units)
    };

    private static float clamp01(double value) => (float)Math.Clamp(value, 0, 1);

    private static float outCubic(float t) => (float)EasingFunctions.Apply(Easing.OutCubic, t);

    /// <summary>
    /// Where a flower's stem is drawn: over the flower's spot, wherever it is, as grown out as it is. The flower's size,
    /// not none: a child is drawn through its parent's size, and nothing shows in a parent of none.
    /// </summary>
    private partial class StemLayer : Container
    {
        private readonly BlossomFlower flower;

        public StemLayer(BlossomFlower flower)
        {
            this.flower = flower;
            Anchor = flower.Anchor;
            Origin = flower.Origin;
            Size = flower.Size;
            AlwaysPresent = true;
        }

        public override void Update()
        {
            // Before its own update, so it's drawn where the flower is this frame.
            Position = flower.Position;
            Alpha = flower.Alpha;
            base.Update();
        }
    }
}
