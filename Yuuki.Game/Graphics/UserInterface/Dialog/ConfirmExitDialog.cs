// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Drawables;

namespace Yuuki.Game.Graphics.UserInterface.Dialog;

public partial class ConfirmExitDialog : PopupDialog
{
    /// <param name="ongoingOperations">What's still running, by name; empty when nothing is.</param>
    public ConfirmExitDialog(Action onConfirm, Action? onCancel = null, IReadOnlyList<string>? ongoingOperations = null)
        : base(IconUsage.PowerSettingsNew, "Are you sure you want to exit?", bodyFor(ongoingOperations),
            ongoingOperations is { Count: > 0 }
                ? new[]
                {
                    new PopupDialogButton(IconUsage.PowerSettingsNew, "Let me out!", onConfirm, IsDangerous: true),
                    new PopupDialogButton(IconUsage.ArrowBack, "Back", onCancel, IsCancel: true)
                }
                : new[]
                {
                    new PopupDialogButton(IconUsage.PowerSettingsNew, "Let me out!", onConfirm),
                    new PopupDialogButton(IconUsage.ArrowBack, "Just a little more...", onCancel, IsCancel: true)
                })
    {
    }

    private static string bodyFor(IReadOnlyList<string>? ongoing) => ongoing switch
    {
        null or
            { Count: 0 } => "Last chance to turn back.",
            { Count: 1 } => $"\"{ongoing[0]}\" is still running. Leaving stops it.",
            _ => $"{ongoing.Count} tasks are still running ({string.Join(", ", ongoing.Take(2))}...). Leaving stops them."
    };
}
