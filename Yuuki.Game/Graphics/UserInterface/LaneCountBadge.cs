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
/// A carousel card's lane count, "4K" (or a song's "4-7K"), in the dark pill the difficulty card's star
/// rating wears, with the info panel's lanes icon. Hidden while it has nothing to say.
/// </summary>
public partial class LaneCountBadge : Container
{
    private readonly SpriteText text;

    /// <param name="textSize">The text's size, which the icon, spacing and rounding follow.</param>
    /// <param name="verticalPadding">Space above and below the text; a quarter of the size by default. A
    /// slim row (the song card's, beside the difficulty dots) wants less.</param>
    public LaneCountBadge(float textSize = 13, float? verticalPadding = null)
    {
        float vertical = verticalPadding ?? textSize * 0.25f;

        AutoSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = textSize * 0.55f;
        Alpha = 0;

        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = Color.Black.WithAlpha(0.55f)
            },
            new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(textSize * 0.3f, 0),
                Padding = new MarginPadding
                {
                    Left = textSize * 0.6f,
                    Right = textSize * 0.7f,
                    Top = vertical,
                    Bottom = vertical
                },
                Children = new Drawable[]
                {
                    new IconSprite
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Icon = IconUsage.ViewColumn,
                        IconSize = textSize,
                        Color = Color.White
                    },
                    text = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, (int)textSize, FontWeight.Bold),
                        Color = Color.White
                    }
                }
            }
        };
    }

    public string Text
    {
        get => text.Text;
        set
        {
            text.Text = value;
            Alpha = string.IsNullOrEmpty(value) ? 0 : 1;
        }
    }
}
