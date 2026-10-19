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

public partial class MenuShearButton : ShearButton
{
    public IconUsage Icon { get; set; } = IconUsage.PlayArrow;

    public string Text { get; set; } = "Button";

    public Action OnHoverAction { get; set; } = () => { };
    public Action OnHoverLostAction { get; set; } = () => { };

    /// <summary>
    /// The button's size; the icon and label scale with its height. Set before load.
    /// </summary>
    public Vector2 ButtonSize { get; init; } = new Vector2(395, 150);

    /// <summary>
    /// Shown but unusable: dimmed, with no hover or click (e.g. Replay on a score saved without one).
    /// Set before load.
    /// </summary>
    public bool Locked { get; init; }

    protected override bool IsLocked => Locked || base.IsLocked;

    [BackgroundDependencyLoader]
    private void load()
    {
        Size = ButtonSize;
        float scale = ButtonSize.Y / 150;

        Children = new Drawable[]
        {
            new Container()
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                Shear = new Vector2(-0.67f, 0),
                CornerRadius = 20 * scale,
                Child = BackgroundBox = new Box()
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Color = BackgroundColor
                }
            },
            new FlowContainer()
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(20 * scale, 0),
                Children = new Drawable[]
                {
                    new IconSprite()
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Icon = Icon,
                        IconSize = 60 * scale,
                        Color = BackgroundColor.Darken(0.3f)
                    },
                    new SpriteText()
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = Text.ToUpper(),
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, (int)Math.Round(48 * scale), FontWeight.Bold),
                        Color = BackgroundColor.Darken(0.5f)
                    }
                }
            }
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (Locked)
        {
            FlashToBeat = false;
            BackgroundBox.Alpha = 0.45f;
        }
    }

    public override bool OnHover(MouseEvent e)
    {
        if (Locked)
            return base.OnHover(e);

        BackgroundBox.FadeToColor(BackgroundColor.Lighten(0.5f), 200, Easing.OutQuint);
        OnHoverAction.Invoke();
        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        if (Locked)
            return base.OnHoverLost(e);

        BackgroundBox.FadeToColor(BackgroundColor, 200, Easing.OutQuint);
        OnHoverLostAction.Invoke();
        return base.OnHoverLost(e);
    }

    public override bool OnClick(MouseButtonEvent e)
    {
        if (Locked)
            return true;

        Flash(Color.White, 300, Easing.OutQuint);
        return base.OnClick(e);
    }
}
