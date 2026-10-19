// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Yuuki.Game.Graphics.UserInterface.Dialog;

namespace Yuuki.Game.Edit;

/// <summary>
/// question on leaving the editor with unsaved changes: save and leave, leave without saving (held
/// to confirm, since the changes can't be got back), or stay.
/// </summary>
public partial class PromptForSaveDialog : PopupDialog
{
    public PromptForSaveDialog(Action exit, Action saveAndExit, Action? cancel = null)
        : base(IconUsage.Save, "Did you want to save your changes?", "They'll be gone if you leave without saving.",
            new PopupDialogButton(IconUsage.Save, "Save it!", saveAndExit),
            new PopupDialogButton(IconUsage.Delete, "Forget all changes", exit, IsDangerous: true),
            new PopupDialogButton(IconUsage.ArrowBack, "Oops, continue editing", cancel, IsCancel: true))
    {
    }
}
