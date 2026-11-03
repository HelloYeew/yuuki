// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Graphics.Transforms;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// The rank in the middle of the results ring
/// </summary>
public partial class RankLetter : Container
{
    public const float LETTER_SIZE = 200;

    /// <summary>
    /// How long it takes to drop into place, in ms: it hits at the time it's told to land.
    /// </summary>
    public const double FALL = 140;

    /// <summary>
    /// What a run that failed shows in place of a rank, set smaller than a two-letter rank to fit the ring.
    /// </summary>
    public const string FAILED_TEXT = "FAILED";

    /// <summary>
    /// FAILED's size against a one-letter rank's: the prototype's 62 px against its 150 px two-letter rank, as two letters
    /// are set here.
    /// </summary>
    public const float FAILED_SIZE = 0.64f * 62 / 150;

    private static readonly Color shadow_color = ColorExtensions.FromHex("#1B1622");

    private readonly Container body;
    private readonly SpriteText shadow;
    private readonly SpriteText letter;
    private readonly Box slash;

    private ScoreRank rank = ScoreRank.D;
    private bool failed;
    private string shownText = string.Empty;
    private Color color = Color.White;
    private double landsAt = double.PositiveInfinity;
    private double cracksAt = double.PositiveInfinity;

    public RankLetter()
    {
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        Size = new Vector2(LETTER_SIZE * 1.6f, LETTER_SIZE * 1.15f);

        Child = body = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            // Unseen at first and only ever shown by its alpha: kept present so it's drawn properly once it is.
            Alpha = 0,
            AlwaysPresent = true,
            Children = new Drawable[]
            {
                // A hard shadow straight down, as the flowers' (and the prototype's text shadow).
                shadow = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Y = 5,
                    Color = shadow_color,
                    Alpha = 0.28f
                },
                letter = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre
                },
                slash = new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(LETTER_SIZE, 14),
                    Rotation = -32,
                    Color = JudgementColors.MISS,
                    Scale = new Vector2(0, 1)
                }
            }
        };

        setText();
    }

    /// <summary>
    /// The rank showing.
    /// </summary>
    public ScoreRank Rank => rank;

    /// <summary>
    /// Whether it shows FAILED (<see cref="LandFailed"/>) in place of the rank.
    /// </summary>
    public bool Failed => failed;

    /// <summary>
    /// The text showing.
    /// </summary>
    public string Text => shownText;

    /// <summary>
    /// Whether it takes the heroes' colors in turn, a Full Bloom's, rather than its own: lightened, so they read on the
    /// dark ring.
    /// </summary>
    public bool Rainbow { get; set; }

    /// <summary>
    /// Lands <paramref name="landing"/> in <paramref name="landingColor"/>, hitting at <paramref name="time"/> (now if not
    /// given): it starts dropping <see cref="FALL"/> before. Anything it was doing is forgotten.
    /// </summary>
    public void Land(ScoreRank landing, Color landingColor, double? time = null)
    {
        rank = landing;
        failed = false;
        setText();

        color = landingColor;
        landsAt = time ?? Clock.CurrentTime;
        cracksAt = double.PositiveInfinity;
    }

    /// <summary>
    /// Shows FAILED from <paramref name="time"/> (now if not given): it doesn't drop and hit as a rank does, but eases in
    /// from a little larger, quietly. Anything it was doing is forgotten.
    /// </summary>
    public void LandFailed(double? time = null)
    {
        failed = true;
        setText();

        color = JudgementColors.FAILED;
        landsAt = time ?? Clock.CurrentTime;
        cracksAt = double.PositiveInfinity;
    }

    /// <summary>
    /// Breaks the rank showing, from <paramref name="time"/> (now if not given).
    /// </summary>
    public void Crack(double? time = null) => cracksAt = time ?? Clock.CurrentTime;

    /// <summary>
    /// Takes it away, to land again.
    /// </summary>
    public void Clear() => landsAt = cracksAt = double.PositiveInfinity;

    public override void Update()
    {
        base.Update();

        double now = Clock.CurrentTime;
        double sinceLanding = now - landsAt;

        if (sinceLanding < (failed ? 0 : -FALL) || double.IsNaN(sinceLanding))
        {
            body.Alpha = 0;
            return;
        }

        float scale, alpha;

        if (failed)
        {
            // Easing in from a little larger, with nothing to hit.
            scale = 1.3f - 0.3f * (float)EasingFunctions.Apply(Easing.OutQuint, clamp01(sinceLanding / 400));
            alpha = clamp01(sinceLanding / 200);
        }
        else if (sinceLanding < 0)
        {
            // Dropping from above its size, faster and faster, coming into view as it falls.
            float fall = (float)((sinceLanding + FALL) / FALL);
            scale = 1.7f - 0.7f * (float)EasingFunctions.Apply(Easing.InQuad, fall);
            alpha = Math.Clamp(fall * 2.5f, 0, 1);
        }
        else
        {
            // The hit: it gives a little, and back.
            scale = 1 - 0.07f * MathF.Sin(clamp01(sinceLanding / 170) * MathF.PI);
            alpha = 1;
        }

        Color shown = Rainbow ? HeroColors.Cycle(now) : color;
        Vector2 offset = Vector2.Zero;
        float rotation = 0, slashed = 0;
        double sinceCrack = now - cracksAt;

        if (sinceCrack >= 0)
        {
            float d = (float)sinceCrack;

            shown = color.Blend(JudgementColors.MISS, clamp01(d / 120));
            offset.X = 16 * MathF.Sin(d / 22) * MathF.Exp(-d / 160);
            slashed = outCubic(clamp01((d - 60) / 110));

            // Then it lets go: dropping faster and faster, turning, fading.
            if (d > 300)
            {
                float falling = d - 300;
                offset.Y = 0.0016f * falling * falling;
                rotation = 0.05f * falling;
                alpha *= 1 - clamp01(falling / 360);
            }
        }

        body.Alpha = alpha;
        body.Scale = new Vector2(scale);
        body.Position = offset;
        body.Rotation = rotation;
        letter.Color = shown;
        slash.Scale = new Vector2(slashed, 1);
    }

    private void setText()
    {
        string text = failed ? FAILED_TEXT : rank.ToString();

        if (text == shownText)
            return;

        shownText = text;

        // A two-letter rank is set smaller, to fit the ring as one does, and FAILED smaller again.
        int size = (int)(failed ? LETTER_SIZE * FAILED_SIZE : text.Length > 1 ? LETTER_SIZE * 0.64f : LETTER_SIZE);

        letter.Text = shadow.Text = text;
        letter.Font = shadow.Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, size, FontWeight.Bold);
    }

    private static float clamp01(double value) => (float)Math.Clamp(value, 0, 1);

    private static float outCubic(float t) => (float)EasingFunctions.Apply(Easing.OutCubic, t);
}
