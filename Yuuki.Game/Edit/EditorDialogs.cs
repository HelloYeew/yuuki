// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Yuuki.Game.Graphics.UserInterface.Dialog;

namespace Yuuki.Game.Edit;

/// <summary>
/// question when adding a difficulty: start from scratch (keeping the timing), copy this one, or
/// carry on.
/// </summary>
public partial class CreateNewDifficultyDialog : PopupDialog
{
    public CreateNewDifficultyDialog(Action<bool> create)
        : base(IconUsage.ContentCopy, "Would you like to create a blank difficulty?", "The timing comes along either way.",
            new PopupDialogButton(IconUsage.Add, "Let's start from scratch!", () => create(false)),
            new PopupDialogButton(IconUsage.ContentCopy, "No, create an exact copy of this difficulty", () => create(true)),
            new PopupDialogButton(IconUsage.ArrowBack, "I changed my mind, I want to keep editing this difficulty", null, IsCancel: true))
    {
    }
}

/// <summary>
/// deletion question, for the difficulty being edited: held to confirm, since a deleted
/// difficulty is gone from disk.
/// </summary>
public partial class DeleteDifficultyDialog : PopupDialog
{
    public DeleteDifficultyDialog(string difficultyName, int noteCount, Action delete)
        : base(IconUsage.Delete, "Are you sure you want to delete the following:", $"Difficulty \"{difficultyName}\" with {noteCount} notes",
            new PopupDialogButton(IconUsage.Delete, "Yes. Go for it.", delete, IsDangerous: true),
            new PopupDialogButton(IconUsage.ArrowBack, "No! Let me go back", null, IsCancel: true))
    {
    }
}

/// <summary>
/// notice before something that needs the beatmap on disk (another difficulty of a set that has
/// never been saved): it's saved first.
/// </summary>
public partial class SaveRequiredDialog : PopupDialog
{
    public SaveRequiredDialog(Action saveAndContinue)
        : base(IconUsage.Save, "The beatmap will be saved to continue with this operation.", "A new beatmap has to be saved before it can have more difficulties.",
            new PopupDialogButton(IconUsage.Save, "Sounds good, let's go!", saveAndContinue),
            new PopupDialogButton(IconUsage.ArrowBack, "Oops, continue editing", null, IsCancel: true))
    {
    }
}
