// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Edit.Components;

public enum EditorScreenMode
{
    Compose,
    Timing,
    Setup
}

public partial class EditorTopBar : Container
{
    public const float HEIGHT = 44;

    private const float tab_width = 104;
    private const float tab_spacing = 6;

    private readonly IReadOnlyList<MenuItem> menuItems;
    private readonly Reactive<EditorScreenMode> mode;

    [Resolved]
    private EditorSession session { get; set; } = null!;

    private SpriteText title = null!;
    private SpriteText unsaved = null!;
    private YuukiMenu menu = null!;
    private Container tabs = null!;

    private readonly Dictionary<EditorScreenMode, EditorButton> tabButtons = new Dictionary<EditorScreenMode, EditorButton>();

    public YuukiMenu Menu => menu;

    public EditorTopBar(IReadOnlyList<MenuItem> menuItems, Reactive<EditorScreenMode> mode)
    {
        this.menuItems = menuItems;
        this.mode = mode;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Height = HEIGHT;

        AddRange(new Drawable[]
        {
            new EditorBarPanel { RelativeSizeAxes = Axes.Both, Size = Vector2.One },
            title = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Bold),
                Color = Color.White.WithAlpha(0.7f),
                Truncate = true
            },
            unsaved = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.CentreLeft,
                Text = "●",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold),
                Color = EditorBeatDivisor.ColorFor(8),
                Alpha = 0
            },
            tabs = new Container
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -5,
                Size = new Vector2(3 * tab_width + 2 * tab_spacing, HEIGHT - 10)
            },
            menu = new YuukiMenu(FlowDirection.Horizontal, topLevelMenu: true)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = 6,
                Items = menuItems
            }
        });

        addTab(EditorScreenMode.Compose, IconUsage.Edit, "Compose", 0);
        addTab(EditorScreenMode.Timing, IconUsage.Timer, "Timing", 1);
        addTab(EditorScreenMode.Setup, IconUsage.Tune, "Setup", 2);

        mode.BindValueChanged(e =>
        {
            foreach (var (tabMode, button) in tabButtons)
                button.Active = tabMode == e.NewValue;
        }, true);

        session.Beatmap.MetadataChanged += updateTitle;
        session.Changes.HasUnsavedChanges.BindValueChanged(e => unsaved.Alpha = e.NewValue ? 1 : 0, true);
        updateTitle();
    }

    private void addTab(EditorScreenMode tabMode, IconUsage icon, string text, int index)
    {
        var button = new EditorButton(icon, text, 14)
        {
            X = index * (tab_width + tab_spacing),
            Size = new Vector2(tab_width, HEIGHT - 10),
            Action = () => mode.Value = tabMode
        };

        tabButtons[tabMode] = button;
        tabs.Add(button);
    }

    private void updateTitle()
    {
        var set = session.Beatmap.BeatmapSet;
        title.Text = $"{set.Artist} - {set.Title} [{session.Beatmap.Beatmap.DifficultyName}]";
    }

    // Positional input only reaches the children of a container the cursor is over, and the menus the
    // bar drops hang below it. So the bar answers for wherever its menu does (the menu answers for its
    // open sub-menus), or those menus would only take the mouse on the strip that overlaps the bar.
    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) =>
        base.ReceivePositionalInputAt(screenSpacePos) || menu.ReceivePositionalInputAt(screenSpacePos);

    public override void Update()
    {
        base.Update();

        // Centred, so it can only be as wide as twice the room on its tighter side: the menu on the left,
        // the tabs (and the unsaved dot) on the right. A long "Artist - Title [Difficulty]" is cut short
        // rather than running into them, which on a phone's 1280-wide canvas it otherwise would.
        float leftRoom = DrawWidth / 2 - (menu.X + menu.DrawWidth) - 20;
        float rightRoom = DrawWidth / 2 - (tabs.DrawWidth - tabs.X) - 40;
        title.MaxWidth = Math.Max(0, 2 * Math.Min(leftRoom, rightRoom));

        // A dot after the title while there's something unsaved.
        unsaved.X = title.DrawWidth / 2 + 6;
    }

    protected override void Dispose(bool isDisposing)
    {
        session.Beatmap.MetadataChanged -= updateTitle;
        base.Dispose(isDisposing);
    }
}
