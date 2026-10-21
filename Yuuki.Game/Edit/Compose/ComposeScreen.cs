// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Edit.Components;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Edit.Compose;

/// <summary>
/// The Compose tab
/// </summary>
public partial class ComposeScreen : Container
{
    private const float left_width = 210;
    private const float right_width = 230;
    private const float margin = 12;

    [Resolved]
    private EditorSession session { get; set; } = null!;

    private readonly Dictionary<ComposeTool, EditorToolButton> toolButtons = new Dictionary<ComposeTool, EditorToolButton>();
    private readonly Dictionary<HitSampleAddition, EditorToolButton> additionButtons = new Dictionary<HitSampleAddition, EditorToolButton>();
    private readonly Dictionary<SwipeDirection, EditorToolButton> swipeButtons = new Dictionary<SwipeDirection, EditorToolButton>();
    private readonly Dictionary<SkillType, EditorToolButton> skillButtons = new Dictionary<SkillType, EditorToolButton>();

    private TextFlowContainer inspector = null!;
    private string? shownInspector;
    private double nextInspectorUpdate;

    public EditorPlayfield Playfield { get; private set; } = null!;

    /// <summary>
    /// The tool panels down the left.
    /// </summary>
    public EditorPanelColumn ToolColumn { get; private set; } = null!;

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    private EditorPanelColumn left = null!;
    private EditorPanelColumn right = null!;
    private EditorPanel tools = null!;
    private EditorPanel hitsounds = null!;
    private EditorPanel noteSettings = null!;
    private bool noteSettingsOnRight;

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (gameLayout != null)
            BindValueChanged(gameLayout.Current, _ => applyLayout(), true);
    }

    /// <summary>
    /// On a phone the height left between the editor's bars (about 490) can't take all three panels down
    /// the left, so "New notes" moves under Selection on the right and the tool rows tighten.
    /// </summary>
    private void applyLayout()
    {
        bool phone = gameLayout?.IsPhone == true;

        if (phone != noteSettingsOnRight)
        {
            (phone ? left : right).RemovePanel(noteSettings);
            (phone ? right : left).AddPanel(noteSettings);
            noteSettingsOnRight = phone;
        }

        tools.SetCompact(phone);
        hitsounds.SetCompact(phone);
        noteSettings.SetCompact(phone);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.Both;
        Size = Vector2.One;

        Children = new Drawable[]
        {
            // Its right-click menu opens in the editor's context menu container, which covers the whole
            // editor so the menus can spill over the bars.
            Playfield = new EditorPlayfield(),
            // Scrolling when the panels outgrow the tab (a big UI scale); placed so the panels themselves
            // sit at the margin, the columns' own edges reaching past it for the shadows.
            ToolColumn = left = new EditorPanelColumn(left_width)
            {
                Position = new Vector2(margin - EditorPanelColumn.SHADOW_ROOM),
                EdgeMargin = margin - EditorPanelColumn.SHADOW_ROOM
            },
            right = new EditorPanelColumn(right_width)
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(-(margin - EditorPanelColumn.SHADOW_ROOM), margin - EditorPanelColumn.SHADOW_ROOM),
                EdgeMargin = margin - EditorPanelColumn.SHADOW_ROOM
            }
        };

        tools = new EditorPanel("Tools");
        addTool(tools, ComposeTool.Select, IconUsage.NearMe, "Select", "1");
        addTool(tools, ComposeTool.Tap, IconUsage.CropSquare, "Tap", "2");
        addTool(tools, ComposeTool.Hold, IconUsage.Height, "Hold", "3");
        addTool(tools, ComposeTool.Swipe, IconUsage.SwapHoriz, "Swipe", "4");
        addTool(tools, ComposeTool.Pass, IconUsage.RadioButtonUnchecked, "Pass", "5");
        addTool(tools, ComposeTool.Skill, IconUsage.AutoAwesome, "Skill", "6");
        addTool(tools, ComposeTool.Touch, IconUsage.HorizontalRule, "Touch", "7");
        left.AddPanel(tools);

        hitsounds = new EditorPanel("Hitsounds");
        addAddition(hitsounds, HitSampleAddition.Whistle, "Whistle", "W");
        addAddition(hitsounds, HitSampleAddition.Finish, "Finish", "E");
        addAddition(hitsounds, HitSampleAddition.Clap, "Clap", "R");
        left.AddPanel(hitsounds);

        noteSettings = new EditorPanel("New notes");
        addSwipe(noteSettings, SwipeDirection.Any, IconUsage.SwapHoriz, "Swipe either side");
        addSwipe(noteSettings, SwipeDirection.Left, IconUsage.KeyboardDoubleArrowLeft, "Swipe left");
        addSwipe(noteSettings, SwipeDirection.Right, IconUsage.KeyboardDoubleArrowRight, "Swipe right");
        addSkill(noteSettings, SkillType.Heal, IconUsage.Favorite, "Skill: heal");
        addSkill(noteSettings, SkillType.BreakToCritical, IconUsage.AutoAwesome, "Skill: break -> critical");
        left.AddPanel(noteSettings);

        var selection = new EditorPanel("Selection");
        selection.AddRow(inspector = new TextFlowContainer(t =>
        {
            t.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Bold);
            t.Color = Color.White;
        })
        {
            AutoSizeAxes = Axes.Y
        });
        right.AddPanel(selection);

        session.Tool.BindValueChanged(e =>
        {
            foreach (var (tool, button) in toolButtons)
                button.Active = tool == e.NewValue;
        }, true);

        session.NewSwipeDirection.BindValueChanged(e =>
        {
            foreach (var (direction, button) in swipeButtons)
                button.Active = direction == e.NewValue;
        }, true);

        session.NewSkill.BindValueChanged(e =>
        {
            foreach (var (skill, button) in skillButtons)
                button.Active = skill == e.NewValue;
        }, true);
    }

    private void addTool(EditorPanel panel, ComposeTool tool, IconUsage icon, string text, string hotkey)
    {
        var button = new EditorToolButton(icon, text, hotkey, () => session.Tool.Value = tool);
        toolButtons[tool] = button;
        panel.AddRow(button);
    }

    private void addAddition(EditorPanel panel, HitSampleAddition addition, string text, string hotkey)
    {
        var button = new EditorToolButton(IconUsage.MusicNote, text, hotkey, () => ToggleAddition(addition));
        additionButtons[addition] = button;
        panel.AddRow(button);
    }

    private void addSwipe(EditorPanel panel, SwipeDirection direction, IconUsage icon, string text)
    {
        var button = new EditorToolButton(icon, text, null, () => session.NewSwipeDirection.Value = direction);
        swipeButtons[direction] = button;
        panel.AddRow(button);
    }

    private void addSkill(EditorPanel panel, SkillType skill, IconUsage icon, string text)
    {
        var button = new EditorToolButton(icon, text, null, () => session.NewSkill.Value = skill);
        skillButtons[skill] = button;
        panel.AddRow(button);
    }

    /// <summary>
    /// Toggles a hitsound: on the selection when there is one (on for all unless all have it already),
    /// otherwise for the notes placed from now on.
    /// </summary>
    public void ToggleAddition(HitSampleAddition addition)
    {
        var selection = session.Beatmap.SelectedNotes.ToArray();

        if (selection.Length == 0)
        {
            session.NewNoteAdditions.Value ^= addition;
            return;
        }

        bool all = selection.All(n => n.Additions.HasFlag(addition));
        session.Beatmap.Update(selection, n => n.Additions = all ? n.Additions & ~addition : n.Additions | addition);
    }

    public override void Update()
    {
        base.Update();

        var selection = session.Beatmap.SelectedNotes;

        // Lit for the selection's hitsounds when there is one, for new notes' otherwise.
        foreach (var (addition, button) in additionButtons)
            button.Active = selection.Count > 0 ? selection.All(n => n.Additions.HasFlag(addition)) : session.NewNoteAdditions.Value.HasFlag(addition);

        if (Clock.CurrentTime < nextInspectorUpdate)
            return;

        nextInspectorUpdate = Clock.CurrentTime + 250;

        string text = describe(selection);

        if (text != shownInspector)
            inspector.Text = shownInspector = text;
    }

    private static string describe(IReadOnlyList<BeatmapNote> selection)
    {
        if (selection.Count == 0)
            return "Nothing selected";

        if (selection.Count == 1)
        {
            var note = selection[0];
            string text = $"{note.Type} in lane {(int)note.Lane + 1}\nat {formatTime(note.StartTime)}";

            if (note.Duration > 0)
                text += $"\nfor {note.Duration:0} ms";

            return text;
        }

        return $"{selection.Count} notes\n{formatTime(selection.Min(n => n.StartTime))} – {formatTime(selection.Max(n => n.EndTime))}";
    }

    private static string formatTime(double ms) => TimeInfoDisplay.FormatTime(ms);
}
