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
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class RevertToDefaultButton : ClickableContainer
{
    /// <summary>
    /// The dot's diameter
    /// </summary>
    public const float SIZE = 20;

    /// <summary>
    /// The gap between the dot and what it sits beside (see <see cref="LeftOf"/>).
    /// </summary>
    public const float SPACING = 12;

    private const double fade_duration = 200;

    private readonly Func<bool> isDefault;

    private Container body = null!;
    private Circle circle = null!;
    private Container iconSpin = null!;
    private IconSprite icon = null!;

    private Color circleColor;
    private Color iconColor;

    private bool? shown;
    private bool hovered;

    /// <summary>
    /// A sibling to sit just left of, following it as it moves or resizes (a value readout whose text
    /// changes). Both should share a right anchor, with this button's origin on its right edge.
    /// </summary>
    public Drawable? LeftOf { get; set; }

    /// <param name="isDefault">Whether the setting is at its default now.</param>
    /// <param name="revert">Puts the setting back to its default.</param>
    public RevertToDefaultButton(Func<bool> isDefault, Action revert)
    {
        this.isDefault = isDefault;

        Size = new Vector2(SIZE);
        Alpha = 0;
        Action = revert;
    }

    /// <summary>
    /// A button for a setting held in a <see cref="Reactive{T}"/>, which knows its own default.
    /// </summary>
    public static RevertToDefaultButton For<T>(Reactive<T> setting) =>
        new RevertToDefaultButton(() => setting.IsDefault, () => setting.Value = setting.Default);

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] ScreenPalette? screenPalette)
    {
        YuukiColorPalette palette = ScreenPalette.Resolve(screenPalette, colorProvider);

        circleColor = palette.Control;
        iconColor = palette.AccentLight;

        Children = new Drawable[]
        {
            // Alpha is this button's, the hover swell the body's, the pop-in spin the icon wrapper's and
            // the hover turn the icon's, so no two animations ever share a property.
            body = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    circle = new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        Color = circleColor
                    },
                    iconSpin = new Container
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        Child = icon = new IconSprite
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Icon = IconUsage.Undo,
                            IconSize = 14,
                            IconWeight = FontWeights.Bold,
                            Color = iconColor
                        }
                    }
                }
            },
            new HoverClickSounds { Enabled = () => Enabled.Value }
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        updateState(animate: false);
    }

    public override void Update()
    {
        base.Update();

        if (LeftOf != null)
            X = LeftOf.X - LeftOf.DrawWidth - SPACING;

        updateState(animate: true);
    }

    private void updateState(bool animate)
    {
        bool show = !isDefault();

        if (show == shown)
            return;

        shown = show;
        Enabled.Value = show;
        updateHover();

        ClearTransforms();

        if (!animate)
        {
            Alpha = show ? 1 : 0;
            return;
        }

        if (show)
        {
            this.FadeIn(fade_duration, Easing.OutQuint);

            iconSpin.ClearTransforms();
            iconSpin.Rotation = 150;
            iconSpin.Scale = new Vector2(0.7f);
            iconSpin.RotateTo(0, fade_duration * 2, Easing.OutQuint);
            iconSpin.ScaleTo(1f, fade_duration * 2, Easing.OutQuint);
        }
        else
            this.FadeOut(fade_duration, Easing.OutQuint);
    }

    public override bool OnHover(MouseEvent e)
    {
        hovered = true;
        updateHover();
        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        hovered = false;
        updateHover();
        return base.OnHoverLost(e);
    }

    private void updateHover()
    {
        if (body == null)
            return;

        bool lit = hovered && Enabled.Value;
        double duration = lit ? 300 : 100;

        icon.ClearTransforms();
        icon.RotateTo(lit ? -40 : 0, lit ? 500 : 100, Easing.OutQuint);
        icon.FadeToColor(lit ? iconColor.Lighten(0.25f) : iconColor, duration, Easing.OutQuint);

        circle.ClearTransforms();
        circle.FadeToColor(lit ? circleColor.Lighten(0.25f) : circleColor, duration, Easing.OutQuint);

        body.ClearTransforms();
        body.ScaleTo(lit ? 1.2f : 1f, duration, Easing.OutQuint);
    }
}
