// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Edit.Components;

public partial class EditorTextBox : YuukiTextBox
{
    private string committed = string.Empty;

    public event Action<string>? Committed;

    public EditorTextBox()
    {
        Size = new Vector2(200, 34);
        OnCommit += commit;
    }

    /// <summary>
    /// Shows <paramref name="text"/> without firing <see cref="Committed"/>, unless it's being typed in.
    /// </summary>
    public void SetText(string text)
    {
        if (HasFocus)
            return;

        committed = text;

        if (Text.Value != text)
            Text.Value = text;
    }

    protected override void OnFocusLost()
    {
        base.OnFocusLost();
        commit(Text.Value);
    }

    private void commit(string text)
    {
        if (text == committed)
            return;

        committed = text;
        Committed?.Invoke(text);
    }
}
