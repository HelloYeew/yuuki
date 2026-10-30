// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Graphics.Transforms;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// The tag under the results ring (FULL COMBO, ALL PERFECT).
/// </summary>
/// <remarks>
/// It stamps when told to (<see cref="Stamp"/>), on its own clock, so a time already past shows it as it would be by now.
/// It's <see cref="SIZE"/>, round its middle.
/// </remarks>
public partial class ResultsBadge : Container
{
    public static readonly Vector2 SIZE = new Vector2(300, 58);

    /// <summary>
    /// How long it takes to come down, in ms, and how long after it starts the glint sweeps.
    /// </summary>
    private const double stamp_down = 150;

    private const double glint_after = 120;

    /// <summary>
    /// How often an All Perfect's badge catches the light again, in ms.
    /// </summary>
    private const double shine_every = 1600;

    private readonly Container face;
    private readonly Box fill;
    private readonly RainbowFill rainbow;
    private readonly SpriteText text;
    private readonly ShineSweep glint;
    private readonly ShineSweep shine;

    private double stampsAt = double.PositiveInfinity;
    private double fadesAt = double.PositiveInfinity;
    private float drop = 0.5f;

    public ResultsBadge()
    {
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        Size = SIZE;

        Child = face = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            // Unseen at first and only ever shown by its alpha: kept present so it's drawn properly once it is.
            Alpha = 0,
            AlwaysPresent = true,
            Children = new Drawable[]
            {
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 8,
                    Shear = UserInterfaceConstant.Shear,
                    Children = new Drawable[]
                    {
                        fill = new Box { RelativeSizeAxes = Axes.Both },
                        // A Full Bloom's, over the fill. Unseen until then and only ever shown by its alpha: kept present
                        // so it's drawn properly once it is.
                        rainbow = new RainbowFill { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                        shine = new ShineSweep { BandWidth = 120, Brightness = 0.35f, Every = shine_every },
                        glint = new ShineSweep()
                    }
                },
                text = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 27, FontWeight.Bold)
                }
            }
        };

        Look(JudgementColors.FULL_COMBO, "FULL COMBO");
    }

    /// <summary>
    /// What it says, and its color: the fill is a light tint of it, the text a dark one.
    /// </summary>
    public void Look(Color color, string label)
    {
        fill.Color = SetlistColors.TierFill(color);
        text.Color = SetlistColors.TierInk(color);
        text.Text = label;
        showRainbow(false);
    }

    /// <summary>
    /// What it says, on the heroes' rainbow moving along it, the text dark on it: a Full Bloom's.
    /// </summary>
    public void LookRainbow(string label)
    {
        text.Color = RainbowColors.INK_ON_PASTEL;
        text.Text = label;
        showRainbow(true);
    }

    private void showRainbow(bool show) => rainbow.Alpha = show ? 1 : 0;

    /// <summary>
    /// Whether it keeps catching the light after its stamp, as an All Perfect's does.
    /// </summary>
    public bool Shining { get; set; }

    /// <summary>
    /// Stamps it on at <paramref name="time"/> (now if not given), down from <paramref name="from"/> times its size.
    /// </summary>
    public void Stamp(double? time = null, float from = 1.5f)
    {
        stampsAt = time ?? Clock.CurrentTime;
        fadesAt = double.PositiveInfinity;
        drop = from - 1;
        glint.Sweep(stampsAt + glint_after);
    }

    /// <summary>
    /// Fades it out quickly from <paramref name="time"/> (now if not given), until it's stamped again.
    /// </summary>
    public void FadeOutAt(double? time = null) => fadesAt = time ?? Clock.CurrentTime;

    /// <summary>
    /// Takes it away, to stamp again.
    /// </summary>
    public void Clear()
    {
        stampsAt = fadesAt = double.PositiveInfinity;
        glint.Stop();
        shine.Stop();
    }

    public override void Update()
    {
        base.Update();

        double since = Clock.CurrentTime - stampsAt;

        if (!(since >= 0))
        {
            face.Alpha = 0;
            return;
        }

        float scale = since < stamp_down
            ? 1 + drop - drop * (float)EasingFunctions.Apply(Easing.InQuad, since / stamp_down)
            : 1 - 0.05f * MathF.Sin(Math.Clamp((float)((since - stamp_down) / 160), 0, 1) * MathF.PI);

        face.Alpha = Math.Min(1, (float)(since / 60)) * (1 - Math.Clamp((float)((Clock.CurrentTime - fadesAt) / 120), 0, 1));
        face.Scale = new Vector2(scale);

        // An All Perfect's shine starts once the stamp's glint has passed.
        if (Shining)
            shine.Sweep(stampsAt + glint_after + ShineSweep.DURATION);
        else
            shine.Stop();
    }

}
