// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The game's dropdown based on <see cref="BasicDropdown{T}"/>
/// </summary>
public partial class YuukiDropdown<T> : Container
{
    private const float item_height = 38;
    private const float menu_gap = 6;
    private const int max_visible_items = 6;

    public Reactive<T> Current { get; } = new Reactive<T>(default!);

    /// <summary>
    /// How a choice is written. Defaults to its name, with words split apart ("FullScreen" -> "Full Screen").
    /// </summary>
    public Func<T, string> Formatter { get; set; } = DefaultFormat;

    private IEnumerable<T> items = Enumerable.Empty<T>();

    public IEnumerable<T> Items
    {
        get => items;
        set
        {
            items = value.ToList();
            if (IsLoaded)
                buildMenu();
        }
    }

    public bool IsMenuOpen { get; private set; }

    public Vector2 HeaderShear { get; init; }

    /// <summary>The header's (and each choice's) height. Set before load.</summary>
    public float ItemHeight { get; init; } = item_height;

    /// <summary>
    /// A name shown in a darker tab at the header's left ("Sort", "Group"),
    /// have. Null for none. Set before load.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// The label tab's width.
    /// </summary>
    public float LabelWidth { get; init; } = 80;

    private YuukiColorPalette palette;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    private SpriteText headerText = null!;
    private IconSprite chevron = null!;
    private Box headerBackground = null!;
    private Container menu = null!;
    private FlowContainer menuFlow = null!;
    private readonly List<MenuItem> menuItems = new List<MenuItem>();

    // The choice Up/Down/Enter act on while the menu is open.
    private int highlighted = -1;

    public YuukiDropdown()
    {
        AutoSizeAxes = Axes.Y;
        Width = 360;
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] ScreenPalette? screenPalette)
    {
        palette = ScreenPalette.Resolve(screenPalette, colorProvider);

        Vector2 shear = HeaderShear;
        float textX = Label != null ? LabelWidth + 14 : 16;

        // The backgrounds lean (when sheared); the text on them stays upright.
        Drawable[] header =
        {
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 8,
                Shear = shear,
                Child = headerBackground = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = palette.Control
                }
            },
            new Container
            {
                RelativeSizeAxes = Axes.Y,
                Width = LabelWidth,
                Masking = true,
                CornerRadius = 8,
                Shear = shear,
                Alpha = Label != null ? 1 : 0,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = palette.Backdrop
                }
            },
            new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.Centre,
                X = LabelWidth / 2,
                Text = Label ?? "",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 17, FontWeight.Bold),
                Color = Color.White
            }
        };

        Children = new Drawable[]
        {
            new ClickableContainer
            {
                RelativeSizeAxes = Axes.X,
                Height = ItemHeight,
                Action = toggleMenu,
                Children = new Drawable[]
                {
                    header[0],
                    header[1],
                    header[2],
                    headerText = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = textX,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Bold),
                        Color = Color.White
                    },
                    new HoverClickSounds(null),
                    chevron = new IconSprite
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.Centre,
                        X = -22,
                        Icon = IconUsage.ExpandMore,
                        IconSize = 24,
                        Color = Color.White
                    }
                }
            },
            menu = new Container
            {
                RelativeSizeAxes = Axes.X,
                Y = ItemHeight + menu_gap,
                Height = 0,
                Masking = true,
                CornerRadius = 8,
                Alpha = 0,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = palette.Surface.Lighten(0.3f)
                    },
                    new YuukiScrollableContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Child = menuFlow = new FlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FlowDirection.Vertical
                        }
                    }
                }
            }
        };

        OwnReactive(Current);
        Current.BindValueChanged(e =>
        {
            headerText.Text = Formatter(e.NewValue);

            foreach (MenuItem item in menuItems)
                item.Selected = EqualityComparer<T>.Default.Equals(item.Value, e.NewValue);
        }, true);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        buildMenu();
    }

    private void buildMenu()
    {
        menuFlow.Clear();
        menuItems.Clear();

        foreach (T item in items)
        {
            var menuItem = new MenuItem(item, Formatter(item), palette)
            {
                Selected = EqualityComparer<T>.Default.Equals(item, Current.Value),
                Action = () =>
                {
                    uiSounds?.Play(UISound.Select);
                    Current.Value = item;
                    CloseMenu();
                }
            };

            menuItems.Add(menuItem);
            menuFlow.Add(menuItem);
        }

        headerText.Text = Formatter(Current.Value);
    }

    private float openHeight => Math.Min(menuItems.Count, max_visible_items) * item_height;

    public void OpenMenu()
    {
        if (IsMenuOpen || menuItems.Count == 0)
            return;

        uiSounds?.Play(UISound.DropdownOpen);

        IsMenuOpen = true;
        highlighted = Math.Max(0, menuItems.FindIndex(i => i.Selected));
        updateHighlight();

        menu.ClearTransforms();
        menu.FadeIn(150, Easing.OutQuint);
        menu.ResizeTo(new Vector2(1, openHeight), 250, UserInterfaceConstant.SoftBounce);
        chevron.RotateTo(180, 250, Easing.OutQuint);
        headerBackground.FadeToColor(palette.Control.Lighten(0.2f), 150, Easing.OutQuint);
    }

    public void CloseMenu()
    {
        if (!IsMenuOpen)
            return;

        uiSounds?.Play(UISound.DropdownClose);

        IsMenuOpen = false;
        highlighted = -1;
        updateHighlight();

        menu.ClearTransforms();
        menu.ResizeTo(new Vector2(1, 0), 200, Easing.OutQuint);
        menu.FadeOut(200, Easing.OutQuint);
        chevron.RotateTo(0, 250, Easing.OutQuint);
        headerBackground.FadeToColor(palette.Control, 150, Easing.OutQuint);
    }

    private void toggleMenu()
    {
        if (IsMenuOpen)
            CloseMenu();
        else
            OpenMenu();
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (!IsMenuOpen)
            return false;

        switch (e.Key)
        {
            case Key.Escape:
                CloseMenu();
                return true;

            case Key.Up:
                highlighted = (highlighted - 1 + menuItems.Count) % menuItems.Count;
                updateHighlight();
                uiSounds?.Play(UISound.Hover, volume: 0.7);
                return true;

            case Key.Down:
                highlighted = (highlighted + 1) % menuItems.Count;
                updateHighlight();
                uiSounds?.Play(UISound.Hover, volume: 0.7);
                return true;

            case Key.Enter:
                if (highlighted >= 0)
                    menuItems[highlighted].Action?.Invoke();
                return true;
        }

        return false;
    }

    private void updateHighlight()
    {
        for (int i = 0; i < menuItems.Count; i++)
            menuItems[i].Highlighted = i == highlighted;
    }

    /// <summary>
    /// Splits a name into words: "FullScreen" -> "Full Screen", "Limit2x" -> "Limit 2x".
    /// </summary>
    public static string DefaultFormat(T value)
    {
        // TODO: This do global but it also change our "OpenGL" to "Open GL"???????
        string text = value?.ToString() ?? "";
        return Regex.Replace(text, "(?<=[a-z])(?=[A-Z0-9])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
    }

    private partial class MenuItem : ClickableContainer
    {
        public readonly T Value;

        private readonly Box hover;
        private readonly IconSprite check;
        private bool selected;
        private bool highlighted;
        private bool hovered;

        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                check.Alpha = value ? 1 : 0;
            }
        }

        public bool Highlighted
        {
            set
            {
                highlighted = value;
                updateHover();
            }
        }

        public MenuItem(T value, string text, YuukiColorPalette palette)
        {
            Value = value;

            RelativeSizeAxes = Axes.X;
            Height = item_height;

            Children = new Drawable[]
            {
                hover = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = palette.Control,
                    Alpha = 0
                },
                new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 16,
                    Text = text,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Regular),
                    Color = palette.Text
                },
                check = new IconSprite
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.Centre,
                    X = -22,
                    Icon = IconUsage.Check,
                    IconSize = 20,
                    Color = palette.Control,
                    Alpha = 0
                }
            };

            Add(new HoverClickSounds(null) { HoverVolume = 0.7 });
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

        private void updateHover() => hover.FadeTo(hovered || highlighted ? 0.15f : 0, 120, Easing.OutQuint);
    }
}
