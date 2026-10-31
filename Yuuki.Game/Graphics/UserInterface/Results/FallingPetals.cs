// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// Sakura petals falling across the screen, as real ones fall: each sways from side to side, slowing almost to a stop
/// at the end of a swing and dropping through the middle of it, tipping the way it swings and flipping over now and
/// then, while a gentle wind that varies across the screen carries it along. Nearer petals are bigger, quicker and
/// more solid than far ones, and the far ones are drawn first.
/// </summary>
/// <remarks>
/// It's the motion only, stepped by whatever shows it (<see cref="PetalEffects"/>), in that one's units and ms. Speeds
/// are units a ms, sized for a 1920 × 1080 screen.
/// </remarks>
public class FallingPetals
{
    /// <summary>
    /// One petal, as it is now.
    /// </summary>
    public struct Petal
    {
        /// <summary>
        /// Where it is, before its sway.
        /// </summary>
        public Vector2 Position;

        /// <summary>
        /// How it's moving apart from falling and swaying: the wind it has caught, and any push.
        /// </summary>
        public Vector2 Drift;

        /// <summary>
        /// A push it's been given (the pointer, a gather, a blast), dying away.
        /// </summary>
        public Vector2 Push;

        /// <summary>
        /// How near it is, 0 (far) to 1 (near).
        /// </summary>
        public float Depth;

        public float Size;

        /// <summary>
        /// How fast it falls, on average, in units a ms.
        /// </summary>
        public float FallSpeed;

        // The sway: where in it, how fast it goes (radians a ms) and how wide.
        public float SwayPhase;
        public float SwayRate;
        public float SwayWidth;

        // Flipping over: where in it and how fast.
        public float FlipPhase;
        public float FlipRate;

        /// <summary>
        /// How far it's turned, apart from tipping with its sway, in radians.
        /// </summary>
        public float Rotation;

        public float Spin;

        /// <summary>
        /// Its color, or none for the heroes' in turn from <see cref="Hue"/>.
        /// </summary>
        public Color? Color;

        public float Hue;

        /// <summary>
        /// How long it's been falling, in ms.
        /// </summary>
        public double Age;

        /// <summary>
        /// Where it's drawn: its position, swayed.
        /// </summary>
        public Vector2 DrawPosition => Position + new Vector2(SwayWidth * MathF.Sin(SwayPhase), 0);

        /// <summary>
        /// How far it's turned as drawn: tipped into its swing.
        /// </summary>
        public float DrawRotation => Rotation + max_tip * MathF.Cos(SwayPhase);

        /// <summary>
        /// How wide it looks as it flips, 0–1 (edge on to face on), and whether its back is toward us.
        /// </summary>
        public float Width => Math.Max(0.15f, MathF.Abs(MathF.Cos(FlipPhase)));

        public bool ShowsBack => MathF.Cos(FlipPhase) < 0;
    }

    /// <summary>
    /// How far a petal tips into its swing, in radians.
    /// </summary>
    private const float max_tip = 0.55f;

    /// <summary>
    /// How slowly a petal comes round to the wind, as the ms it takes to take on most of it.
    /// </summary>
    private const float wind_follow_time = 700;

    /// <summary>
    /// How quickly a push dies away, as the ms it takes to lose most of it.
    /// </summary>
    private const float push_decay_time = 420;

    /// <summary>
    /// How near the pointer pushes, and how hard.
    /// </summary>
    private const float pointer_reach = 160;

    private const float pointer_strength = 0.012f;

    /// <summary>
    /// How far past the edges petals start and are let go.
    /// </summary>
    private const float margin = 100;

    private readonly List<Petal> petals = new List<Petal>();
    private readonly Random random;

    private double spawnDue;
    private double time;

    /// <summary>
    /// The area they fall across, in the units they're drawn in.
    /// </summary>
    public Vector2 Area { get; set; } = new Vector2(1920, 1080);

    /// <summary>
    /// How many start falling a second.
    /// </summary>
    public double Rate { get; set; }

    /// <summary>
    /// The most falling at once.
    /// </summary>
    public int Limit { get; set; } = 180;

    /// <summary>
    /// The colors new petals take, or none for the heroes' in turn.
    /// </summary>
    public IReadOnlyList<Color>? Colors { get; set; }

    /// <summary>
    /// The steady wind, in units a ms (right is positive): the wind varies round it across the screen.
    /// </summary>
    public float Wind { get; set; }

    /// <summary>
    /// How much the wind varies, in units a ms.
    /// </summary>
    public float Gustiness { get; set; } = 0.03f;

    /// <summary>
    /// Where the pointer is, if it's over them: petals near it are pushed away.
    /// </summary>
    public Vector2? Pointer { get; set; }

    public IReadOnlyList<Petal> Petals => petals;

    public FallingPetals(int seed = 99)
    {
        random = new Random(seed);
    }

    /// <summary>
    /// Moves every petal on by <paramref name="elapsed"/> ms, lets go of those gone off the screen, and starts new ones
    /// as <see cref="Rate"/> says.
    /// </summary>
    public void Step(double elapsed)
    {
        if (elapsed <= 0)
            return;

        time += elapsed;
        float dt = (float)elapsed;

        spawnDue += Rate * elapsed / 1000;

        while (spawnDue >= 1)
        {
            spawnDue--;

            if (petals.Count < Limit)
                add(spawn());
        }

        float follow = 1 - MathF.Exp(-dt / wind_follow_time);
        float pushKeep = MathF.Exp(-dt / push_decay_time);

        for (int i = petals.Count - 1; i >= 0; i--)
        {
            Petal petal = petals[i];
            petal.Age += elapsed;

            // Big petals are heavier: they come round to the wind more slowly.
            Vector2 wind = new Vector2(windAt(petal.Position), 0);
            petal.Drift += (wind - petal.Drift) * follow * (1.15f - 0.3f * petal.Depth);

            if (Pointer is Vector2 pointer)
            {
                Vector2 away = petal.DrawPosition - pointer;
                float distance = away.Length();

                if (distance < pointer_reach)
                {
                    petal.Push += away / (distance + 1) * ((1 - distance / pointer_reach) * pointer_strength * dt);
                    petal.Spin += (1 - distance / pointer_reach) * 0.00004f * dt;
                }
            }

            petal.Push *= pushKeep;

            // A falling leaf drops fastest through the middle of its swing and almost hangs at either end of it.
            float cos = MathF.Cos(petal.SwayPhase);
            float fall = petal.FallSpeed * (0.45f + 0.9f * cos * cos) / 0.9f;

            petal.Position += (petal.Drift + petal.Push + new Vector2(0, fall)) * dt;
            petal.SwayPhase += petal.SwayRate * dt;
            petal.FlipPhase += petal.FlipRate * dt;
            petal.Rotation += petal.Spin * dt;
            petal.Spin *= MathF.Exp(-dt / 1500);

            if (petal.Position.Y > Area.Y + margin / 2 || petal.Position.X < -margin * 2 || petal.Position.X > Area.X + margin * 2)
                petals.RemoveAt(i);
            else
                petals[i] = petal;
        }
    }

    /// <summary>
    /// Draws every petal toward <paramref name="point"/> this step, by <paramref name="strength"/> units a ms per ms.
    /// </summary>
    public void Attract(Vector2 point, float strength, double elapsed)
    {
        for (int i = 0; i < petals.Count; i++)
        {
            Petal petal = petals[i];
            Vector2 toward = point - petal.Position;
            petal.Push += toward / (toward.Length() + 1) * (strength * (float)elapsed);
            petals[i] = petal;
        }
    }

    /// <summary>
    /// Throws every petal away from <paramref name="point"/> at <paramref name="speed"/> units a ms.
    /// </summary>
    public void Blast(Vector2 point, float speed)
    {
        for (int i = 0; i < petals.Count; i++)
        {
            Petal petal = petals[i];
            Vector2 away = petal.Position - point;
            petal.Push = away / (away.Length() + 1) * speed;
            petal.Spin += (float)(random.NextDouble() - 0.5) * 0.01f;
            petals[i] = petal;
        }
    }

    public void Clear()
    {
        petals.Clear();
        spawnDue = 0;
    }

    /// <summary>
    /// The wind at <paramref name="position"/> now: the steady wind, and gusts that roll across the screen and drift down
    /// it, so neighbours move together and far-apart petals don't.
    /// </summary>
    private float windAt(Vector2 position)
    {
        float t = (float)time;
        return Wind + Gustiness * (0.65f * MathF.Sin(0.0021f * position.X - 0.0007f * t) * MathF.Cos(0.0017f * position.Y + 0.0004f * t)
                                   + 0.35f * MathF.Sin(0.0009f * position.Y - 0.0011f * t + 1.3f));
    }

    private Petal spawn()
    {
        float depth = (float)random.NextDouble();

        return new Petal
        {
            Position = new Vector2(between(-margin, Area.X + margin), between(-90, -30)),
            Depth = depth,
            Size = (5 + 9 * depth) * between(0.85f, 1.15f),
            FallSpeed = (0.045f + 0.06f * depth) * between(0.85f, 1.15f),
            SwayPhase = between(0, MathF.Tau),
            SwayRate = MathF.Tau / between(1800, 3200),
            SwayWidth = between(18, 42) * (0.6f + 0.4f * depth),
            FlipPhase = between(0, MathF.Tau),
            FlipRate = between(0.0008f, 0.0032f) * (random.NextDouble() < 0.5 ? -1 : 1),
            Rotation = between(0, MathF.Tau),
            Spin = between(-0.0006f, 0.0006f),
            Color = Colors is { Count: > 0 } colors ? colors[random.Next(colors.Count)] : null,
            Hue = between(0, 360)
        };
    }

    // Kept far to near, so the near ones are drawn over the far.
    private void add(Petal petal)
    {
        int index = petals.Count;
        while (index > 0 && petals[index - 1].Depth > petal.Depth)
            index--;

        petals.Insert(index, petal);
    }

    private float between(float min, float max) => min + (float)random.NextDouble() * (max - min);
}
