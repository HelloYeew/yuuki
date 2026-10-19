// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Edit.Components;

public partial class EditorButton : Container
{
    private readonly Box background;
    private readonly IconSprite? icon;
    private readonly SpriteText? label;
    private EditorColors colors;
    private bool colorsReady;
    private bool active;
    private bool enabled = true;
    private Color? activeColor;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    public Action? Action { get; set; }

    public EditorButton(IconUsage? iconUsage = null, string? text = null, int fontSize = 15)
    {
        Masking = true;
        CornerRadius = 8;

        Add(background = new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One });

        if (iconUsage is { } iconValue)
        {
            Add(icon = new IconSprite
            {
                Anchor = text.IsNull() ? Anchor.Centre : Anchor.CentreLeft,
                Origin = Anchor.Centre,
                X = text.IsNull() ? 0 : 16,
                Icon = iconValue,
                IconSize = 18
            });
        }

        if (text.IsNotNull())
        {
            Add(label = new SpriteText
            {
                Anchor = iconUsage.IsNull() ? Anchor.Centre : Anchor.CentreLeft,
                Origin = iconUsage.IsNull() ? Anchor.Centre : Anchor.CentreLeft,
                X = iconUsage.IsNull() ? 0 : 30,
                Text = text,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, fontSize, FontWeight.Bold)
            });
        }
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        colors = EditorColors.From(colorProvider);
        colorsReady = true;
        updateState(false, false);
    }

    public string Text
    {
        set
        {
            if (label.IsNotNull() && label.Text != value)
                label.Text = value;
        }
    }

    public IconUsage Icon
    {
        set
        {
            if (icon.IsNotNull())
                icon.Icon = value;
        }
    }

    public bool Active
    {
        get => active;
        set
        {
            if (active == value)
                return;

            active = value;
            updateState(IsHovered, true);
        }
    }

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value)
                return;

            enabled = value;
            updateState(IsHovered, true);
        }
    }

    public Color? ActiveColor
    {
        set
        {
            activeColor = value;
            updateState(IsHovered, false);
        }
    }

    public Color ContentColor { get; set; } = Color.White;

    private void updateState(bool hovered, bool animate)
    {
        if (!colorsReady)
            return;

        double duration = animate ? 150 : 0;
        var fill = active ? activeColor ?? colors.AccentStrong : hovered && enabled ? colors.ControlHover : colors.Control;
        var content = active ? (activeColor.HasValue ? Color.Black.WithAlpha(0.85f) : Color.White) : ContentColor;

        background.FadeToColor(fill, duration, Easing.OutQuint);
        icon?.FadeToColor(content, duration, Easing.OutQuint);
        label?.FadeToColor(content, duration, Easing.OutQuint);
        this.FadeTo(enabled ? 1 : 0.4f, duration, Easing.OutQuint);
    }

    public override bool OnHover(MouseEvent e)
    {
        updateState(true, true);

        if (enabled)
            uiSounds?.Play(UISound.Hover);

        return true;
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        updateState(false, true);
        return true;
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        base.OnMouseDown(e);
        return true;
    }

    public override bool OnClick(MouseButtonEvent e)
    {
        if (e.Button != MouseButton.Left || !enabled)
            return false;

        uiSounds?.Play(UISound.Select);
        Action?.Invoke();
        return true;
    }
}

public partial class EditorBarPanel : Container
{
    private readonly Box background;

    public EditorBarPanel()
    {
        Masking = true;
        CornerRadius = 12;
        EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Shadow,
            Color = Color.Black.WithAlpha(0.3f),
            Radius = 10
        };

        Add(background = new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One });
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        background.Color = EditorColors.From(colorProvider).Panel;
    }
}
