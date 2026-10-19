// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Edit.Components;

public partial class BeatDivisorControl : EditorBarPanel
{
    public const float WIDTH = 230;

    private const float padding = 8;
    private const float row_height = 30;

    [Resolved]
    private EditorSession session { get; set; } = null!;

    private EditorButton previous = null!;
    private EditorButton next = null!;
    private EditorButton listToggle = null!;
    private SpriteText value = null!;
    private Container pills = null!;

    private readonly List<(int Divisor, EditorButton Button)> pillButtons = new List<(int, EditorButton)>();
    private int[]? shownPresets;

    [BackgroundDependencyLoader]
    private void load()
    {
        Size = new Vector2(WIDTH, padding * 2 + row_height * 2 + 4);

        AddRange(new Drawable[]
        {
            previous = new EditorButton(IconUsage.ChevronLeft)
            {
                Position = new Vector2(padding, padding),
                Size = new Vector2(30, row_height),
                Action = () => session.Divisor.Previous()
            },
            value = new SpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Position = new Vector2(padding + 30 + 36, padding + row_height / 2),
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 22, FontWeight.Bold)
            },
            next = new EditorButton(IconUsage.ChevronRight)
            {
                Position = new Vector2(padding + 30 + 72, padding),
                Size = new Vector2(30, row_height),
                Action = () => session.Divisor.Next()
            },
            listToggle = new EditorButton(text: "Common", fontSize: 13)
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(-padding, padding),
                Size = new Vector2(84, row_height),
                Action = () => session.Divisor.ToggleTriplets()
            },
            pills = new Container
            {
                Position = new Vector2(padding, padding + row_height + 4),
                Size = new Vector2(WIDTH - padding * 2, row_height)
            }
        });

        session.Divisor.Value.BindValueChanged(_ => updateDisplay(), true);
    }

    private void updateDisplay()
    {
        int divisor = session.Divisor.Value.Value;
        int[] presets = session.Divisor.Presets;

        value.Text = $"1/{divisor}";
        value.Color = EditorBeatDivisor.ColorFor(divisor);
        listToggle.Text = session.Divisor.IsTriplets ? "Triplets" : "Common";
        previous.Enabled = presets[0] != divisor;
        next.Enabled = presets[^1] != divisor;

        if (shownPresets != presets)
            rebuildPills(presets);

        foreach (var (pillDivisor, button) in pillButtons)
            button.Active = pillDivisor == divisor;
    }

    private void rebuildPills(int[] presets)
    {
        shownPresets = presets;
        pills.Clear();
        pillButtons.Clear();

        const float spacing = 5;
        float width = (pills.Width - spacing * (presets.Length - 1)) / presets.Length;

        for (int i = 0; i < presets.Length; i++)
        {
            int divisor = presets[i];

            var button = new EditorButton(text: $"1/{divisor}", fontSize: 13)
            {
                X = i * (width + spacing),
                Size = new Vector2(width, row_height),
                ActiveColor = EditorBeatDivisor.ColorFor(divisor),
                ContentColor = EditorBeatDivisor.ColorFor(divisor),
                Action = () => session.Divisor.Value.Value = divisor
            };

            pillButtons.Add((divisor, button));
            pills.Add(button);
        }
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        base.OnMouseDown(e);
        return true;
    }

    public override bool OnScroll(ScrollEvent e)
    {
        if (e.ScrollDelta.Y > 0)
            session.Divisor.Next();
        else if (e.ScrollDelta.Y < 0)
            session.Divisor.Previous();

        return true;
    }
}
