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
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The game's menus (menu bars, right-click menus)
/// </summary>
public partial class YuukiMenu : Menu
{
    private readonly YuukiScreenPart part;
    private YuukiColorPalette palette;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    public YuukiMenu(FlowDirection direction, bool topLevelMenu = false, YuukiScreenPart part = YuukiScreenPart.Edit)
        : base(direction, topLevelMenu)
    {
        this.part = part;

        if (direction == FlowDirection.Vertical)
            MinWidth = 180;
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        palette = colorProvider?.GetPalette(part) ?? DefaultYuukiColorProvider.GetDefaultPaletteForPart(part);

        BackgroundColor = TopLevelMenu ? Color.Black.WithAlpha(0) : palette.Text.Darken(0.3f);

        if (!TopLevelMenu)
        {
            BackgroundContainer.Masking = true;
            BackgroundContainer.CornerRadius = 10;
            BackgroundContainer.EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Color = Color.Black.WithAlpha(0.35f),
                Radius = 12
            };
        }
    }

    protected override DrawableMenuItem CreateDrawableMenuItem(MenuItem item) => new YuukiDrawableMenuItem(item, palette, TopLevelMenu);

    protected override Menu CreateSubMenu() => new YuukiMenu(FlowDirection.Vertical, part: part);

    protected override void AnimateOpen()
    {
        uiSounds?.Play(UISound.DropdownOpen);
        this.FadeIn(150, Easing.OutQuint);
        Scale = new Vector2(1, 0.94f);
        this.ScaleTo(Vector2.One, 250, UserInterfaceConstant.SoftBounce);
    }

    protected override void AnimateClose() => this.FadeOut(100, Easing.OutQuint);
}

public partial class YuukiDrawableMenuItem : DrawableMenuItem
{
    private const float item_height = 32;
    private const float horizontal_padding = 12;
    private const float gutter = 24;

    private readonly bool inBar;
    private readonly YuukiColorPalette palette;

    private readonly Container highlight;
    private readonly Box separatorLine;
    private readonly SpriteText? label;
    private readonly SpriteText? shortcut;
    private readonly IconSprite? arrow;
    private readonly IconSprite? check;

    private bool isSeparator => Item is MenuSeparator;

    private bool vertical => MenuDirection == FlowDirection.Vertical;

    public YuukiDrawableMenuItem(MenuItem item, YuukiColorPalette palette, bool inBar)
        : base(item)
    {
        this.palette = palette;
        this.inBar = inBar;

        Add(highlight = new Container
        {
            Masking = true,
            CornerRadius = 7,
            Alpha = 0,
            Child = new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One, Color = palette.Accent }
        });

        Add(separatorLine = new Box
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Height = 1,
            Color = Color.White.WithAlpha(0.15f),
            Alpha = 0
        });

        if (isSeparator)
        {
            separatorLine.Alpha = 1;
            return;
        }

        Add(label = new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = item.Text.Value,
            Font = YuukiFontUsage.GetFontUsage(inBar ? Typeface.Quicksand : Typeface.Nunito, inBar ? 16 : 15, FontWeight.Bold),
            Color = Color.White
        });

        if (!string.IsNullOrEmpty(item.ShortcutText))
        {
            Add(shortcut = new SpriteText
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Text = item.ShortcutText,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold),
                Color = Color.White.WithAlpha(0.45f)
            });
        }

        if (item.Items.Count > 0 && !inBar)
        {
            Add(arrow = new IconSprite
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Icon = IconUsage.ChevronRight,
                IconSize = 16,
                Color = Color.White.WithAlpha(0.6f)
            });
        }

        if (item is ToggleMenuItem)
        {
            Add(check = new IconSprite
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.Centre,
                Icon = IconUsage.Check,
                IconSize = 16,
                Color = palette.AccentLight
            });
        }
    }

    public override Vector2 NaturalSize
    {
        get
        {
            if (isSeparator)
                return new Vector2(0, vertical ? 9 : 0);

            if (!IsLoaded || label.IsNull())
                return new Vector2(0, item_height);

            float width = horizontal_padding * 2 + label.ContentSize.X;

            if (vertical)
                width += gutter;

            if (shortcut.IsNotNull())
                width += 28 + shortcut.ContentSize.X;

            if (arrow.IsNotNull())
                width += 18;

            return new Vector2(width, inBar ? 34 : item_height);
        }
    }

    public override void Update()
    {
        base.Update();

        // The highlight sits inside the item, a pill rather than an edge-to-edge band.
        highlight.Position = new Vector2(4, 2);
        highlight.Size = new Vector2(Math.Max(0, DrawWidth - 8), Math.Max(0, DrawHeight - 4));

        separatorLine.X = horizontal_padding;
        separatorLine.Width = Math.Max(0, DrawWidth - horizontal_padding * 2);

        if (label.IsNull())
            return;

        label.X = horizontal_padding + (vertical ? gutter : 0);

        if (check.IsNotNull())
            check.X = horizontal_padding + gutter / 2 - 2;

        if (arrow.IsNotNull())
            arrow.X = -horizontal_padding + 6;

        if (shortcut.IsNotNull())
            shortcut.X = -horizontal_padding - (arrow.IsNotNull() ? 18 : 0);
    }

    protected override void OnTextChanged(string text)
    {
        if (label.IsNotNull())
            label.Text = text;
    }

    protected override void UpdateState()
    {
        if (isSeparator || label.IsNull())
            return;

        bool lit = IsActionable && (Highlighted || Selected);

        highlight.FadeTo(lit ? inBar ? 0.6f : 1 : 0, 120, Easing.OutQuint);
        label.FadeTo(IsActionable ? 1 : 0.35f, 120, Easing.OutQuint);

        if (check.IsNotNull())
            check.Alpha = Item is ToggleMenuItem { State.Value: true } ? 1 : 0;
    }
}
