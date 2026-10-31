// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

public partial class PageTabs : FlowContainer
{
    private readonly Tab[] tabs;
    private int current;

    /// <summary>
    /// Called with the page picked, as it's picked.
    /// </summary>
    public Action<int>? PagePicked { get; set; }

    /// <summary>
    /// The page shown.
    /// </summary>
    public int Current
    {
        get => current;
        set
        {
            current = Math.Clamp(value, 0, tabs.Length - 1);

            for (int i = 0; i < tabs.Length; i++)
                tabs[i].Selected = i == current;
        }
    }

    public PageTabs(YuukiColorPalette palette, params string[] pages)
    {
        AutoSizeAxes = Axes.Both;
        Direction = FlowDirection.Horizontal;
        Spacing = new Vector2(6, 0);

        tabs = pages.Select((page, i) => new Tab(page, palette)
        {
            Action = () =>
            {
                Current = i;
                PagePicked?.Invoke(i);
            }
        }).ToArray();

        Children = tabs;
        Current = 0;
    }

    private partial class Tab : ClickableContainer
    {
        private readonly SpriteText label;
        private readonly Container underline;
        private bool selected;
        private bool hovered;

        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                updateLook();
            }
        }

        public Tab(string text, YuukiColorPalette palette)
        {
            AutoSizeAxes = Axes.Both;

            Children = new Drawable[]
            {
                label = new SpriteText
                {
                    Margin = new MarginPadding { Left = 16, Right = 16, Top = 8, Bottom = 14 },
                    Text = text.ToLowerInvariant(),
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 28, FontWeight.Bold),
                    Color = Color.White
                },
                // Slanted about its middle, as the blades are.
                underline = new Container
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    Width = 0.8f,
                    Height = 6,
                    Y = -5,
                    Masking = true,
                    CornerRadius = 3,
                    Shear = UserInterfaceConstant.Shear,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = palette.Accent
                    }
                }
            };

            updateLook();
        }

        public override bool OnHover(MouseEvent e)
        {
            hovered = true;
            updateLook();
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            hovered = false;
            updateLook();
            return base.OnHoverLost(e);
        }

        private void updateLook()
        {
            label.Alpha = selected ? 1 : hovered ? 0.85f : 0.62f;
            underline.Alpha = selected ? 1 : 0;
        }
    }
}
