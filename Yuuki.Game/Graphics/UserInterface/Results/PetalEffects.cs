// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// The results screen's petals: <see cref="PetalBurst"/>s thrown from a point (a rank landing, a rank crossed, the
/// fill's trail) and the petals falling across the screen afterwards (<see cref="Fall"/>), which the pointer pushes
/// aside. Each kind of piece is drawn in one go (<see cref="ParticleField"/>).
/// </summary>
/// <remarks>
/// It fills its parent; burst origins and the pointer are in its units. It never takes a click from what's under it.
/// </remarks>
public partial class PetalEffects : Container
{
    /// <summary>
    /// A petal texture's length for a piece of size 1: the texture's 44 units hold a petal 15.5 long.
    /// </summary>
    private const float petal_scale = 160 / 3.6f / 15.5f;

    // The shard's and the sparkle's textures hold their shape a little inside their edges.
    private const float shard_scale = 64 / 28f;
    private const float star_scale = 64 / 30f;

    /// <summary>
    /// The longest step it moves the falling petals by: a frame much longer than this (the window hidden a while) is
    /// taken as this, rather than flinging everything.
    /// </summary>
    private const double max_step = 50;

    [Resolved(canBeNull: true)]
    private ITextureManager? textureManager { get; set; }

    private readonly ParticleField fallField;
    private readonly ParticleField petalField;
    private readonly ParticleField shardField;
    private readonly ParticleField starField;
    private readonly ParticleField sparkField;

    private readonly List<PetalBurst> bursts = new List<PetalBurst>();

    private double lastTime = double.NaN;

    /// <summary>
    /// The petals falling across the screen: set its <see cref="FallingPetals.Rate"/> to start them.
    /// </summary>
    public FallingPetals Fall { get; } = new FallingPetals();

    /// <summary>
    /// Whether the pointer pushes the falling petals aside.
    /// </summary>
    public bool PushedByPointer { get; set; } = true;

    /// <summary>
    /// Bursts not yet over.
    /// </summary>
    public int ActiveBursts => bursts.Count;

    /// <summary>
    /// Pieces drawn last frame, falling petals included.
    /// </summary>
    public int ActivePieces => fallField.Count + petalField.Count + shardField.Count + starField.Count + sparkField.Count;

    public PetalEffects()
    {
        RelativeSizeAxes = Axes.Both;

        Children = new Drawable[]
        {
            fallField = new ParticleField(),
            petalField = new ParticleField(),
            shardField = new ParticleField(),
            starField = new ParticleField(),
            sparkField = new ParticleField()
        };
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Texture? petal = textureManager?.Get("Results/petal.png");
        fallField.Texture = petal;
        petalField.Texture = petal;
        shardField.Texture = textureManager?.Get("Results/shard.png");
        starField.Texture = textureManager?.Get("Results/star.png");
        sparkField.Texture = textureManager?.Get("Results/spark.png");
    }

    /// <summary>
    /// Throws <paramref name="pieces"/> from <paramref name="origin"/> (in its units) at <paramref name="time"/> on its
    /// clock, now if not given. A time already past shows the burst as far on as it would be.
    /// </summary>
    public PetalBurst Burst(Vector2 origin, IReadOnlyList<BurstPiece> pieces, double? time = null)
    {
        var burst = new PetalBurst(time ?? Clock.CurrentTime, origin, pieces);
        bursts.Add(burst);
        return burst;
    }

    /// <summary>
    /// Ends every burst, and lets go of every falling petal.
    /// </summary>
    public void ClearAll()
    {
        bursts.Clear();
        Fall.Clear();
    }

    // Only ever watched: the pointer pushing petals aside never takes it from anything.
    public override bool OnMouseMove(MouseEvent e)
    {
        Fall.Pointer = PushedByPointer ? ToLocalSpace(e.ScreenSpaceMousePosition) : null;
        return false;
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        Fall.Pointer = null;
        return false;
    }

    public override void Update()
    {
        base.Update();

        double now = Clock.CurrentTime;
        double elapsed = double.IsNaN(lastTime) ? 0 : Math.Clamp(now - lastTime, 0, max_step);
        lastTime = now;

        Fall.Area = DrawSize;
        Fall.Step(elapsed);

        fallField.Clear();
        petalField.Clear();
        shardField.Clear();
        starField.Clear();
        sparkField.Clear();

        drawFall();

        bursts.RemoveAll(b => now - b.StartTime > PetalBurst.MAX_LIFE);

        foreach (PetalBurst burst in bursts)
            drawBurst(burst, now - burst.StartTime);
    }

    private void drawFall()
    {
        foreach (FallingPetals.Petal petal in Fall.Petals)
        {
            float fadeIn = (float)Math.Min(1, petal.Age / 300);
            Color color = petal.Color ?? HeroColors.Cycle(petal.Age, petal.Hue);

            // Its back is paler, catching less light; far petals are fainter.
            if (petal.ShowsBack)
                color = color.Blend(Color.White, 0.3f);

            float length = petal.Size * petal_scale;
            fallField.Add(new Particle(petal.DrawPosition, new Vector2(length * petal.Width, length), petal.DrawRotation, color,
                fadeIn * (0.55f + 0.4f * petal.Depth)));
        }
    }

    private void drawBurst(PetalBurst burst, double elapsed)
    {
        if (elapsed < 0)
            return;

        float dt = (float)elapsed;

        foreach (BurstPiece piece in burst.Pieces)
        {
            if (elapsed > piece.Life)
                continue;

            float life = (float)(elapsed / piece.Life);
            float tau = piece.DragTime;

            // Thrown, then slowed by the air.
            float travelled = piece.Speed * tau * (1 - MathF.Exp(-dt / tau));
            var direction = new Vector2(MathF.Cos(piece.Angle), MathF.Sin(piece.Angle));
            Vector2 position = burst.Origin + direction * travelled;
            float alpha = 1 - life * life;
            Color color = piece.Color ?? HeroColors.Cycle(elapsed, piece.Hue);

            switch (piece.Kind)
            {
                case BurstPieceKind.Spark:
                {
                    // A streak behind it, shrinking: round ends past its length either side.
                    float length = piece.Size * 9 * (1 - life);
                    sparkField.Add(new Particle(position - direction * (length / 2), new Vector2(length + piece.Size, piece.Size), piece.Angle, color, alpha));
                    break;
                }

                case BurstPieceKind.Shard:
                    position.Y += 0.0012f * dt * dt;
                    shardField.Add(new Particle(position, new Vector2(piece.Size * shard_scale), piece.Phase + piece.Spin * dt, color, alpha));
                    break;

                case BurstPieceKind.Star:
                    position.Y += 0.00006f * dt * dt;
                    starField.Add(new Particle(position, new Vector2(piece.Size * star_scale), piece.Phase + piece.Spin * dt, color, alpha));
                    break;

                default:
                {
                    position.Y += (piece.Heavy ? 0.00028f : 0.00006f) * dt * dt;
                    position.X += MathF.Sin(dt / 260 + piece.Phase) * 18 * life;

                    float flip = MathF.Cos(piece.Tumble * dt + piece.Phase);
                    float length = piece.Size * petal_scale;

                    petalField.Add(new Particle(position, new Vector2(length * Math.Max(0.14f, MathF.Abs(flip)), length), piece.Phase + piece.Spin * dt,
                        flip < 0 ? color.Blend(Color.White, 0.3f) : color, alpha));
                    break;
                }
            }
        }
    }
}
