// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Gameplay.HUD;

/// <summary>
/// The banner between the last note and the results
/// </summary>
public partial class ClearBanner : Container
{
    /// <summary>
    /// How long the banner is on screen, start to finish.
    /// </summary>
    public const double DURATION = 1800;

    private readonly Container strip;
    private readonly Box background;
    private readonly RainbowFill rainbow;
    private readonly SpriteText text;

    public ClearBanner()
    {
        RelativeSizeAxes = Axes.Both;
        Alpha = 0;

        Children = new Drawable[]
        {
            strip = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.X,
                Height = 150,
                Masking = true,
                Shear = UserInterfaceConstant.Shear,
                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both
                    },
                    // A Full Bloom's. Unseen until then and only ever shown by its alpha: kept present so it's drawn
                    // properly once it is.
                    rainbow = new RainbowFill
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true
                    }
                }
            },
            text = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 96, FontWeight.Bold),
                Color = Color.White
            }
        };
    }

    public void Play(string label, Color color) => play(label, color, false);

    /// <summary>
    /// A Full Bloom's: the heroes' rainbow moving along the strip, dark letters on it.
    /// </summary>
    public void PlayFullBloom() => play("Full Bloom", RainbowColors.INK_ON_PASTEL, true);

    private void play(string label, Color color, bool rainbowStrip)
    {
        text.Text = label.ToUpperInvariant();
        background.Color = color;
        background.Alpha = rainbowStrip ? 0 : 1;
        rainbow.Alpha = rainbowStrip ? 1 : 0;
        text.Color = rainbowStrip ? RainbowColors.INK_ON_PASTEL : Color.White;

        this.FadeIn(100);

        strip.ClearTransforms();
        strip.Scale = new Vector2(0, 1);
        strip.ScaleTo(Vector2.One, 400, Easing.OutQuint);
        strip.Alpha = 0.9f;

        text.ClearTransforms();
        text.Alpha = 0;
        text.Scale = new Vector2(1.4f);
        Scheduler.AddDelayed(() =>
        {
            text.FadeIn(200, Easing.OutQuint);
            text.ScaleTo(1f, 600, UserInterfaceConstant.SoftBounce);
        }, 150);

        Scheduler.AddDelayed(() => this.FadeOut(300, Easing.OutQuint), DURATION - 300);
    }
}
