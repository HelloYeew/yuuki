// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class RightBottomShearButton : ShearButton
{
    public IconUsage Icon { get; set; } = IconUsage.ArrowRightAlt;

    public string Text { get; set; } = "Next";

    // As wide as the short labels make it, and wider for a longer one, with the room they have either side.
    private const float min_width = 240;
    private const float label_room = 48;

    private FlowContainer label = null!;

    public static readonly Vector2 DEFAULT_POSITION = new Vector2(32, -16);

    public RightBottomShearButton()
    {
        // The way forward keeps the beat whether or not it's hovered, on every screen it's on.
        FlashToBeatOnlyWhenHovered = false;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Size = new Vector2(240, 66);
        Children = new Drawable[]
        {
            new Container()
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                Shear = UserInterfaceConstant.Shear,
                CornerRadius = 10,
                // Its slanted end goes past the screen's edge, under a notch or camera cutout too.
                Bleed = Edges.Right,
                Child = BackgroundBox = new Box()
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Color = BackgroundColor
                }
            },
            label = new FlowContainer()
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(10, 0),
                Children = new Drawable[]
                {
                    new SpriteText()
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = Text.ToUpper(),
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 36, FontWeight.Bold),
                        Color = BackgroundColor.Darken(0.5f)
                    },
                    new IconSprite()
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Icon = Icon,
                        IconSize = 40,
                        Color = BackgroundColor.Darken(0.3f)
                    }
                }
            }
        };
    }

    public override void Update()
    {
        // Before the children update, so it's the right width the frame the label's measured.
        Width = Math.Max(min_width, label.DrawWidth + 2 * label_room);
        base.Update();
    }

    public override bool OnHover(MouseEvent e)
    {
        BackgroundBox.FadeToColor(BackgroundColor.Lighten(0.5f), 200, Easing.OutQuint);
        this.MoveToX(DEFAULT_POSITION.X - 10, 200, Easing.OutQuint);
        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        BackgroundBox.FadeToColor(BackgroundColor, 200, Easing.OutQuint);
        this.MoveToX(DEFAULT_POSITION.X, 200, Easing.OutQuint);
        return base.OnHoverLost(e);
    }

    public override bool OnClick(MouseButtonEvent e)
    {
        Flash(Color.White, 300, Easing.OutQuint);
        return base.OnClick(e);
    }
}
