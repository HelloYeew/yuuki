// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A line of short facts, each after its icon rather than between dots: the level range, length and
/// BPM, with the icons the info panel's rows use for them.
/// </summary>
public partial class IconStats : FlowContainer
{
    private readonly float textSize;
    private readonly List<Stat> stats = new List<Stat>();

    private Color color = Color.Black;

    public IconStats(float textSize = 15f)
    {
        this.textSize = textSize;

        AutoSizeAxes = Axes.Both;
        Direction = FlowDirection.Horizontal;
        Spacing = new Vector2(textSize * 0.9f, 0);
    }

    public new Color Color
    {
        get => color;
        set
        {
            color = value;

            foreach (var stat in stats)
                stat.SetColor(value);
        }
    }

    /// <summary>
    /// Shows <paramref name="items"/> in order, leaving out any without text. The pieces are kept and
    /// reused, as the windows showing this change song with every step of a scroll.
    /// </summary>
    public void SetItems(params (IconUsage Icon, string Text)[] items)
    {
        int shown = 0;

        foreach (var (icon, text) in items)
        {
            if (string.IsNullOrEmpty(text))
                continue;

            if (shown == stats.Count)
            {
                var added = new Stat(textSize);
                added.SetColor(color);
                stats.Add(added);
                Add(added);
            }

            stats[shown++].Set(icon, text);
        }

        // Alpha 0 also takes a piece out of the flow.
        for (int i = 0; i < stats.Count; i++)
            stats[i].Alpha = i < shown ? 1 : 0;
    }

    private partial class Stat : FlowContainer
    {
        private readonly IconSprite icon;
        private readonly SpriteText text;

        public Stat(float textSize)
        {
            Anchor = Anchor.CentreLeft;
            Origin = Anchor.CentreLeft;
            AutoSizeAxes = Axes.Both;
            Direction = FlowDirection.Horizontal;
            Spacing = new Vector2(textSize * 0.3f, 0);

            Children = new Drawable[]
            {
                icon = new IconSprite
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    IconSize = textSize * 1.1f
                },
                text = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, (int)textSize, FontWeight.Bold)
                }
            };
        }

        public void Set(IconUsage iconUsage, string value)
        {
            icon.Icon = iconUsage;
            text.Text = value;
        }

        public void SetColor(Color value)
        {
            // The icon a step back from the figure it labels.
            icon.Color = value.WithAlpha(0.75f);
            text.Color = value;
        }
    }
}
