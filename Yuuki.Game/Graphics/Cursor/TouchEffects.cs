// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.Cursor;

/// <summary>
/// What a finger leaves on the screen outside a song: a burst of stars and a petal where it taps, and a trail of
/// stars where it drags. The stars take the screen's colors.
/// </summary>
/// <remarks>
/// It only watches the fingers, which the game passes on as they come (see <see cref="YuukiGame"/>), so it never
/// takes a tap from what's under it. Sizes and distances are in screen units wherever it's placed (dp on a phone,
/// so it's the same size on every one), turned into its own as each piece is made: a test scene shows it as the
/// game does. The pieces are drawn by <c>tools/make_touch_effects.py</c>.
/// </remarks>
public partial class TouchEffects : Container
{
    private const int max_particles = 128;

    private const int burst_stars = 8;
    private const int burst_outlines = 2;

    // How hard a burst's stars are thrown, how fast that push dies away (per second), and how they fall after.
    private const float burst_speed_min = 220;
    private const float burst_speed_max = 420;
    private const float burst_damping = 9;
    private const float burst_gravity = 200;

    private const float star_size_min = 6;
    private const float star_size_max = 10;

    private const float petal_size = 30;
    private const double petal_life = 0.6;
    private const float petal_alpha = 0.8f;

    /// <summary>
    /// How far apart a drag drops its stars, and how many drops one move can make up, so a quick swipe still
    /// leaves an even trail.
    /// </summary>
    private const float trail_spacing = 14;

    private const int trail_drops_max = 4;

    // How long a star takes to pop up to its size, and where in its life it starts to fade.
    private const double appear_time = 0.07;
    private const double fade_from = 0.55;

    /// <summary>
    /// The screen's colors, which the stars take.
    /// </summary>
    public readonly Reactive<YuukiColorPalette> Palette = new Reactive<YuukiColorPalette>(DefaultYuukiColorProvider.GetDefaultPalette());

    /// <summary>
    /// How big it all is, as a multiple: at 1 a star is 6–10 dp across and the petal 30 dp long, as far apart and
    /// as far flung as fits them.
    /// </summary>
    public float Size { get; set; } = 1;

    /// <summary>
    /// Whether the player wants them: the Touch effects setting.
    /// </summary>
    public readonly ReactiveBool Enabled = new ReactiveBool(true);

    /// <summary>
    /// Off while a song is played. The game binds it to the cursor's hiding, which a screen asks for in play.
    /// </summary>
    public readonly ReactiveBool Suppressed = new ReactiveBool();

    /// <summary>
    /// How many pieces are on screen (tests).
    /// </summary>
    public int ActiveParticles
    {
        get
        {
            int count = 0;

            foreach (Particle particle in particles)
            {
                if (particle.Alive)
                    count++;
            }

            return count;
        }
    }

    [Resolved(canBeNull: true)]
    private ITextureManager? textureManager { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    private readonly Particle[] particles = new Particle[max_particles];
    private readonly Sprite[] sprites = new Sprite[max_particles];

    // The next slot to use: round and round, so when they're all taken the oldest goes first.
    private int next;

    private readonly bool[] fingerDown = new bool[InputState.MAX_TOUCHES];

    // Where each finger last dropped its trail, on screen.
    private readonly Vector2[] lastDrop = new Vector2[InputState.MAX_TOUCHES];

    private Color[] starColors = Array.Empty<Color>();
    private Texture? star;
    private Texture? starOutline;
    private Texture? petal;

    private readonly Random random = new Random();

    private double lastTime = double.NaN;

    private bool showing => Enabled.Value && !Suppressed.Value;

    // They never take input: the game shows them for fingers something else may well be using.
    public override bool HandlePositionalInput => false;

    [BackgroundDependencyLoader]
    private void load()
    {
        star = textureManager?.Get("TouchEffects/star.png");
        starOutline = textureManager?.Get("TouchEffects/star-outline.png");
        petal = textureManager?.Get("TouchEffects/petal.png");

        for (int i = 0; i < max_particles; i++)
        {
            Add(sprites[i] = new Sprite
            {
                Origin = Anchor.Centre,
                Alpha = 0
            });
        }

        if (config != null)
            Enabled.BindTo(config.Get<bool>(YuukiSetting.TouchEffects));

        // Two of the screen's mid tones and two of its light ones, so some of every burst shows on any background.
        Palette.BindValueChanged(e => starColors = new[] { e.NewValue.Accent, e.NewValue.TextMuted, e.NewValue.AccentLight, e.NewValue.TextLight }, true);
    }

    /// <summary>
    /// A finger went down at <paramref name="screenPosition"/>: a burst of stars, and a petal.
    /// </summary>
    public void FingerDown(TouchSource source, Vector2 screenPosition)
    {
        int finger = (int)source;

        fingerDown[finger] = true;
        lastDrop[finger] = screenPosition;

        if (!showing)
            return;

        Vector2 position = ToLocalSpace(screenPosition);
        float scale = unitsPerScreenUnit(screenPosition) * Size;

        spawnPetal(position, scale);

        for (int i = 0; i < burst_stars + burst_outlines; i++)
        {
            float angle = (float)(random.NextDouble() * Math.PI * 2);
            float speed = range(burst_speed_min, burst_speed_max);

            spawnStar(position, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed * scale, burst_gravity * scale, outline: i >= burst_stars,
                size: range(star_size_min, star_size_max) * scale, life: range(0.55f, 0.8f));
        }
    }

    /// <summary>
    /// A finger that's down moved to <paramref name="screenPosition"/>: stars drop along the way it went.
    /// </summary>
    public void FingerMove(TouchSource source, Vector2 screenPosition)
    {
        int finger = (int)source;
        if (!fingerDown[finger])
            return;

        Vector2 from = lastDrop[finger];
        float distance = Vector2.Distance(from, screenPosition);
        float spacing = trail_spacing * Size;

        if (distance < spacing)
            return;

        lastDrop[finger] = screenPosition;

        if (!showing)
            return;

        float scale = unitsPerScreenUnit(screenPosition) * Size;
        int drops = Math.Min(trail_drops_max, (int)(distance / spacing));

        for (int i = 1; i <= drops; i++)
        {
            Vector2 at = ToLocalSpace(from + (screenPosition - from) * ((float)i / drops));

            for (int j = 0; j < 2; j++)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2);
                Vector2 jitter = new Vector2(range(-4, 4), range(-4, 4)) * scale;

                spawnStar(at + jitter, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * range(30, 90) * scale, 140 * scale, outline: random.NextDouble() < 0.2,
                    size: range(5, 8) * scale, life: range(0.38f, 0.55f));
            }
        }
    }

    /// <summary>
    /// A finger lifted, or the system took it.
    /// </summary>
    public void FingerUp(TouchSource source) => fingerDown[(int)source] = false;

    public override void Update()
    {
        base.Update();

        // From the clock's time rather than its frame length, which a clock driven from outside (a test's)
        // may not report. Clamped, so coming back to the game after a while doesn't fling what was on screen.
        double now = Clock.CurrentTime;
        float dt = double.IsNaN(lastTime) ? 0 : (float)Math.Clamp((now - lastTime) / 1000, 0, 0.1);
        lastTime = now;

        for (int i = 0; i < max_particles; i++)
        {
            ref Particle particle = ref particles[i];
            if (!particle.Alive)
                continue;

            Sprite sprite = sprites[i];
            particle.Age += dt;

            double t = particle.Age / particle.Life;

            if (t >= 1)
            {
                particle.Alive = false;
                sprite.Alpha = 0;
                continue;
            }

            particle.Burst *= MathF.Exp(-burst_damping * dt);
            particle.Fall += particle.Gravity * dt;
            particle.Position += (particle.Burst + new Vector2(0, particle.Fall)) * dt;
            particle.Rotation += particle.Spin * dt;

            sprite.Position = particle.Position;
            sprite.Rotation = particle.Rotation;

            if (particle.Kind == ParticleKind.Petal)
            {
                // Blooms open a little, then fades as it drifts down.
                float grow = (float)Math.Min(1, particle.Age / 0.25);
                sprite.Scale = new Vector2(0.75f + 0.25f * (1 - MathF.Pow(1 - grow, 5)));
                sprite.Alpha = petal_alpha * (float)(1 - t * t);
            }
            else
            {
                float appear = (float)Math.Min(1, particle.Age / appear_time);
                sprite.Scale = new Vector2(0.3f + 0.7f * (1 - (1 - appear) * (1 - appear)));
                sprite.Alpha = t < fade_from ? 1 : (float)(1 - (t - fade_from) / (1 - fade_from));
            }
        }
    }

    private void spawnStar(Vector2 position, Vector2 burst, float gravity, bool outline, float size, float life)
    {
        int i = take();

        particles[i] = new Particle
        {
            Alive = true,
            Kind = outline ? ParticleKind.Outline : ParticleKind.Star,
            Position = position,
            Burst = burst,
            Gravity = gravity,
            Rotation = range(0, 72),
            Spin = range(90, 240) * (random.NextDouble() < 0.5 ? -1 : 1),
            Life = life
        };

        Sprite sprite = sprites[i];
        sprite.Texture = outline ? starOutline : star;
        // An outline a little bigger than a filled star, being so much lighter.
        sprite.Size = new Vector2(outline ? size * 1.12f : size);
        sprite.Color = outline ? Color.White : starColors[random.Next(starColors.Length)];
        show(i);
    }

    private void spawnPetal(Vector2 position, float scale)
    {
        int i = take();

        particles[i] = new Particle
        {
            Alive = true,
            Kind = ParticleKind.Petal,
            Position = position,
            // It drifts down slowly, turning a little, as a petal falls.
            Fall = 18 * scale,
            Rotation = range(-35, 35),
            Spin = range(-20, 20),
            Life = petal_life
        };

        Sprite sprite = sprites[i];
        sprite.Texture = petal;
        sprite.Size = new Vector2(petal_size * 0.8f, petal_size) * scale;
        sprite.Color = Color.White;
        show(i);
    }

    private int take()
    {
        int i = next;
        next = (next + 1) % max_particles;
        return i;
    }

    // Placed at once, rather than a frame late where the last one using the sprite left it.
    private void show(int i)
    {
        Sprite sprite = sprites[i];
        sprite.Position = particles[i].Position;
        sprite.Rotation = particles[i].Rotation;
        sprite.Scale = new Vector2(particles[i].Kind == ParticleKind.Petal ? 0.75f : 0.3f);
        sprite.Alpha = particles[i].Kind == ParticleKind.Petal ? petal_alpha : 1;
    }

    // How many of its own units one on screen is, where it's measured: 1 filling the window.
    private float unitsPerScreenUnit(Vector2 screenPosition) =>
        Vector2.Distance(ToLocalSpace(screenPosition), ToLocalSpace(screenPosition + new Vector2(1, 0)));

    private float range(float min, float max) => min + (float)random.NextDouble() * (max - min);

    private enum ParticleKind
    {
        Star,
        Outline,
        Petal
    }

    private struct Particle
    {
        public bool Alive;
        public ParticleKind Kind;
        public Vector2 Position;

        // The throw outward, which dies away, and the fall, which builds.
        public Vector2 Burst;
        public float Fall;
        public float Gravity;

        public float Rotation;
        public float Spin;
        public double Age;
        public double Life;
    }
}
