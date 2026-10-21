// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
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

public partial class EditorPanel : Container
{
    private const float padding = 10;

    private readonly FlowContainer rows;
    private readonly Box background;
    private readonly string title;

    public EditorPanel(string title)
    {
        this.title = title;

        RelativeSizeAxes = Axes.X;
        Width = 1;
        Masking = true;
        CornerRadius = 12;
        EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Shadow,
            Color = Color.Black.WithAlpha(0.3f),
            Radius = 10
        };

        Children = new Drawable[]
        {
            background = new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One },
            new SpriteText
            {
                Position = new Vector2(padding + 2, padding),
                Text = title.ToUpperInvariant(),
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold),
                Color = Color.White.WithAlpha(0.6f)
            },
            rows = new FlowContainer
            {
                Position = new Vector2(padding, padding + 24),
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 5)
            }
        };
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        background.Color = EditorColors.From(colorProvider).Panel;
    }

    /// <summary>
    /// Tighter rows, for a short screen (a phone's): tool buttons 32 tall rather than 36, 4 apart rather
    /// than 5.
    /// </summary>
    public void SetCompact(bool compact)
    {
        rows.Spacing = new Vector2(0, compact ? 4 : 5);

        foreach (var button in rows.Children.OfType<EditorToolButton>())
            button.Height = compact ? 32 : 36;
    }

    public void AddRow(Drawable row)
    {
        row.Anchor = Anchor.TopLeft;
        row.Origin = Anchor.TopLeft;
        rows.Add(row);
    }

    public override void Update()
    {
        base.Update();

        rows.Width = Math.Max(0, DrawWidth - padding * 2);

        foreach (var row in rows.Children)
        {
            if (row.RelativeSizeAxes == Axes.None)
                row.Width = rows.Width;
        }

        Height = rows.Y + rows.DrawHeight + padding;
    }
}

public partial class EditorToolButton : Container
{
    private readonly Action action;
    private readonly Box background;
    private readonly IconSprite icon;
    private readonly SpriteText label;
    private EditorColors colors;
    private bool active;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    public EditorToolButton(IconUsage iconUsage, string text, string? hotkey, Action action)
    {
        this.action = action;

        Height = 36;
        Masking = true;
        CornerRadius = 8;

        Children = new Drawable[]
        {
            background = new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One },
            icon = new IconSprite
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.Centre,
                X = 18,
                Icon = iconUsage,
                IconSize = 18
            },
            label = new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = 36,
                Text = text,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Bold)
            },
            new SpriteText
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -10,
                Text = hotkey ?? string.Empty,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold),
                Color = Color.White.WithAlpha(0.45f)
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
