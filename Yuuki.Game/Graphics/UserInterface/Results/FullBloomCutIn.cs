// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using FontUsage = Sakura.Framework.Graphics.Text.FontUsage;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// A Full Bloom's cut-in across the screen, as an arcade's: a strip of the heroes' rainbow snaps open, tilted, with a
/// white edge; FULL BLOOM stamps on letter by letter, a glint of light runs along it, and then it all folds away.
/// </summary>
/// <remarks>
/// It plays when told to (<see cref="Play"/>), on its own clock, so a time already past shows it as it would be by now.
/// It fills its parent: put it over the screen. It never takes input.
/// </remarks>
public partial class FullBloomCutIn : Container
{
    /// <summary>
    /// How long it's on screen, start to finish, in ms.
    /// </summary>
    public const double DURATION = 1150;

    private const float strip_height = 180;
    private const float edge = 7;
    private const double letter_stagger = 34;

    private static readonly Color shadow = ColorExtensions.FromHex("#1B1622");
    private static readonly FontUsage letter_font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 132, FontWeight.Bold);

    private readonly Container cut;
    private readonly Container strip;
    private readonly ShineSweep glint;
    private readonly SpriteText[] letters;

    private double startsAt = double.PositiveInfinity;

    public FullBloomCutIn()
    {
        RelativeSizeAxes = Axes.Both;
        // Unseen until it plays, and only ever shown by its alpha: kept present so it's drawn properly once it is.
        Alpha = 0;
        AlwaysPresent = true;

        letters = "FULL BLOOM".Select(c => new SpriteText
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Text = c.ToString(),
            Font = letter_font,
            Color = RainbowColors.INK_ON_PASTEL,
            // Stamping, it grows past its place without moving its neighbours (its place is the spacer's).
            BypassAutoSizeAxes = Axes.Both,
            Alpha = 0,
            AlwaysPresent = true
        }).ToArray();

        Child = cut = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.X,
            // Past the screen's sides, so its tilt never shows an end.
            Width = 1.1f,
            Height = strip_height,
            Rotation = -4,
            Children = new Drawable[]
            {
                strip = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        // A soft shadow under it, and its white edge.
                        new Box
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.X,
                            Height = strip_height,
                            Y = 22,
                            Color = shadow,
                            Alpha = 0.3f
                        },
                        new Box
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.X,
                            Height = strip_height + 2 * edge,
                            Color = Color.White
                        },
                        new RainbowFill { RelativeSizeAxes = Axes.Both },
                        glint = new ShineSweep { BandWidth = 160, Height = 1 }
                    }
                },
                new FlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Horizontal,
                    Spacing = new Vector2(9, 0),
                    Children = letters.Select(letter => new Container
                    {
                        AutoSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            // Its place: the letter's size, unseen.
                            new SpriteText { Text = letter.Text, Font = letter_font, Alpha = 0, AlwaysPresent = true },
                            letter
                        }
                    }).ToArray()
                }
            }
        };
    }

    /// <summary>
    /// Plays it from <paramref name="time"/> (now if not given).
    /// </summary>
    public void Play(double? time = null)
    {
        startsAt = time ?? Clock.CurrentTime;
        glint.Sweep(startsAt + 420);
    }

    /// <summary>
    /// Takes it away.
    /// </summary>
    public void Clear()
    {
        startsAt = double.PositiveInfinity;
        glint.Stop();
    }

    public override bool HandlePositionalInput => false;

    public override void Update()
    {
        base.Update();

        double since = Clock.CurrentTime - startsAt;

        if (!(since >= 0 && since < DURATION))
        {
            Alpha = 0;
            return;
        }

        Alpha = 1;

        // It snaps open, and folds away over its last 200 ms.
        float opening = (float)EasingFunctions.Apply(Easing.OutQuint, Math.Clamp(since / 150, 0, 1));
        float closing = (float)EasingFunctions.Apply(Easing.InQuad, Math.Clamp((since - (DURATION - 200)) / 200, 0, 1));
        strip.Scale = new Vector2(1, opening * (1 - closing));

        // Each letter stamps on a little after the last: down from nearly twice its size, then a little give.
        for (int i = 0; i < letters.Length; i++)
        {
            double d = since - 60 - i * letter_stagger;
            SpriteText letter = letters[i];

            if (d < 0)
            {
                letter.Alpha = 0;
                continue;
            }

            float stamp = d < 110
                ? 1.9f - 0.9f * (float)EasingFunctions.Apply(Easing.OutQuint, d / 110)
                : 1 - 0.06f * MathF.Sin(Math.Clamp((float)((d - 110) / 150), 0, 1) * MathF.PI);

            letter.Alpha = Math.Clamp((float)(d / 50), 0, 1) * (1 - closing);
            letter.Scale = new Vector2(stamp * (1 - 0.3f * closing));
            letter.Y = -40 * closing;
        }
    }
}
