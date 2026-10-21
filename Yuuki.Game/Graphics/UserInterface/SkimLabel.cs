// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The group the song list is passing through, shown large over the background while it skims: "A",
/// "Lv 12", "170 BPM". A soft shadow keeps it readable over any background.
/// </summary>
public partial class SkimLabel : Container
{
    private const float font_size = 88;

    // How far the shadow's blur spreads past the text, kept clear inside its buffer so it isn't cut off.
    private const float shadow_spread = 28;

    private static readonly Color text_color = ColorExtensions.FromHex("#F3E7D8");

    private readonly SpriteText text;
    private readonly SpriteText shadowText;

    public SkimLabel()
    {
        AutoSizeAxes = Axes.Both;
        Alpha = 0;

        var font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, (int)font_size, FontWeight.Bold);

        Children = new Drawable[]
        {
            // Sakura has no text shadow, so: the same text in black, blurred, a little below.
            new BufferedContainer
            {
                AutoSizeAxes = Axes.Both,
                BypassAutoSizeAxes = Axes.Both,
                Position = new Vector2(-shadow_spread, -shadow_spread + 6),
                Padding = new MarginPadding(shadow_spread),
                BlurSigma = new Vector2(10),
                Alpha = 0.45f,
                Child = shadowText = new SpriteText
                {
                    Font = font,
                    Color = Color.Black
                }
            },
            text = new SpriteText
            {
                Font = font,
                Color = text_color
            }
        };
    }

    /// <summary>
    /// A little smaller on a phone, where the room beside the list is narrower.
    /// </summary>
    public void SetPhone(bool phone)
    {
        var font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, phone ? 80 : (int)font_size, FontWeight.Bold);
        text.Font = font;
        shadowText.Font = font;
    }

    public string Text
    {
        get => text.Text;
        set
        {
            text.Text = value;
            shadowText.Text = value;
        }
    }
}
