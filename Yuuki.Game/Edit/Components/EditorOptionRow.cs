// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
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
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Edit.Components;

/// <summary>
/// A row of choices side by side, one lit, under the tool they're for: a swipe's direction under Swipe,
/// say. Shorter than a tool button, so it reads as the tool's own setting.
/// </summary>
public partial class EditorOptionRow : Container
{
    public const float HEIGHT = 30;
    public const float COMPACT_HEIGHT = 28;

    private const float spacing = 4;

    private readonly List<EditorOption> options = new List<EditorOption>();

    public EditorOptionRow()
    {
        Height = HEIGHT;
    }

    /// <summary>
    /// Adds a choice at the end of the row; <paramref name="action"/> picks it.
    /// </summary>
    public EditorOption AddOption(IconUsage icon, string text, Action action)
    {
        var option = new EditorOption(icon, text, action) { RelativeSizeAxes = Axes.Y, Height = 1 };
        options.Add(option);
        Add(option);
        return option;
    }

    public override void Update()
    {
        base.Update();

        // The row's width shared out evenly.
        float width = (DrawWidth - spacing * (options.Count - 1)) / Math.Max(1, options.Count);

        for (int i = 0; i < options.Count; i++)
        {
            options[i].X = i * (width + spacing);
            options[i].Width = Math.Max(0, width);
        }
    }
}

/// <summary>
/// One choice in an <see cref="EditorOptionRow"/>: an icon and a short label, lit while it's the one
/// picked.
/// </summary>
public partial class EditorOption : Container
{
    private readonly Action action;
    private readonly Box background;
    private readonly IconSprite icon;
    private readonly SpriteText label;
    private EditorColors colors;
    private bool active;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    public EditorOption(IconUsage iconUsage, string text, Action action)
    {
        this.action = action;

        Masking = true;
        CornerRadius = 7;

        Children = new Drawable[]
        {
            background = new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One },
            new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(4, 0),
                Children = new Drawable[]
                {
                    icon = new IconSprite
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Icon = iconUsage,
                        IconSize = 15
                    },
                    label = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = text,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold)
                    }
                }
            }
        };
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        colors = EditorColors.From(colorProvider);
        updateState(false);
    }

    public bool Active
    {
        get => active;
        set
        {
            if (active == value)
                return;

            active = value;

            if (IsLoaded)
                updateState(IsHovered);
        }
    }

    private void updateState(bool hovered)
    {
        background.FadeToColor(active ? colors.AccentStrong : hovered ? colors.ControlHover : colors.Control, 150, Easing.OutQuint);
        icon.FadeToColor(active ? Color.White : colors.Accent, 150, Easing.OutQuint);
        label.FadeToColor(active ? Color.White : Color.White.WithAlpha(0.85f), 150, Easing.OutQuint);
    }

    public override bool OnHover(MouseEvent e)
    {
        updateState(true);
        uiSounds?.Play(UISound.Hover);
        return true;
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        updateState(false);
        return true;
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        base.OnMouseDown(e);
        return true;
    }

    public override bool OnClick(MouseButtonEvent e)
    {
        if (e.Button != MouseButton.Left)
            return false;

        uiSounds?.Play(UISound.Select);
        action();
        return true;
    }
}
