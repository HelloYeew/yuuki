// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

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
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class LeftBottomShearButton : ShearButton
{
    public IconUsage Icon { get; set; } = IconUsage.ArrowLeftAlt;

    public string Text { get; set; } = "Back";

    /// <summary>
    /// Shown but unusable: dimmed, with no hover or click (e.g. Replay on a score saved without one).
    /// Set before load.
    /// </summary>
    public bool Locked { get; init; }

    // The corner back button goes back.
    protected override UISound ClickSound => UISound.Back;

    protected override bool IsLocked => Locked || base.IsLocked;

    public static readonly Vector2 DEFAULT_POSITION = new Vector2(-32, -16);

    [BackgroundDependencyLoader]
    private void load()
    {
        // The back button pulses to the beat continuously, not just on hover.
        FlashToBeatOnlyWhenHovered = false;

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
                CornerRadius = 5,
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
                Spacing = new Vector2(10, 0),
                Children = new Drawable[]
                {
                    new IconSprite()
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Icon = Icon,
                        IconSize = 40,
                        Color = BackgroundColor.Darken(0.3f)
                    },
                    new SpriteText()
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = Text.ToUpper(),
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 36, FontWeight.Bold),
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
        this.MoveToX(DEFAULT_POSITION.X + 10, 200, Easing.OutQuint);
        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        if (Locked)
            return base.OnHoverLost(e);

        BackgroundBox.FadeToColor(BackgroundColor, 200, Easing.OutQuint);
        this.MoveToX(DEFAULT_POSITION.X, 200, Easing.OutQuint);
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
