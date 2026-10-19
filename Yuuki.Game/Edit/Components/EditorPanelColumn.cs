// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Containers;

namespace Yuuki.Game.Edit.Components;

public partial class EditorPanelColumn : YuukiScrollableContainer
{
    public const float SHADOW_ROOM = 12;

    private readonly FlowContainer flow;

    public float EdgeMargin { get; set; }

    /// <param name="panelWidth">How wide the panels are.</param>
    public EditorPanelColumn(float panelWidth)
    {
        Width = panelWidth + SHADOW_ROOM * 2;

        Child = flow = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            Width = 1,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 10),
            Padding = new MarginPadding { Top = SHADOW_ROOM, Bottom = SHADOW_ROOM, Left = SHADOW_ROOM, Right = SHADOW_ROOM }
        };
    }

    public void AddPanel(EditorPanel panel) => flow.Add(panel);

    public override void Update()
    {
        base.Update();

        float available = Math.Max(0, (Parent?.DrawHeight ?? 0) - EdgeMargin * 2);
        float wanted = flow.DrawHeight;

        if (Math.Abs(Height - Math.Min(wanted, available)) > 0.5f)
            Height = Math.Min(wanted, available);
    }
}
