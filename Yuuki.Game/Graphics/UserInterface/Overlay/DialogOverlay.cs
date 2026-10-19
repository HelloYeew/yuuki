// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface.Dialog;

namespace Yuuki.Game.Graphics.UserInterface.Overlay;

/// <summary>
/// Shows one <see cref="PopupDialog"/> at a time over the whole game, dimming everything behind
/// </summary>
public partial class DialogOverlay : FocusedOverlayContainer
{
    private const double fade_duration = 200;

    private Container dialogContainer = null!;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? audioManager { get; set; }

    private IDisposable? duck;

    /// <summary>
    /// Whether it has been shown since it was last put away, so loading (which hides it) stays silent.
    /// </summary>
    private bool shown;

    /// <summary>
    /// The dialog waiting on an answer, or null when none is.
    /// </summary>
    public PopupDialog? CurrentDialog { get; private set; }

    /// <summary>
    /// Escape goes to the dialog instead, which takes its cancel answer.
    /// </summary>
    public override bool CloseOnEscape => false;

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        YuukiColorPalette palette = colorProvider.IsNotNull()
            ? colorProvider!.GetPalette(YuukiScreenPart.MainMenu)
            : DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.MainMenu);

        RelativeSizeAxes = Axes.Both;

        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = palette.Backdrop,
                Alpha = 0.75f
            },
            dialogContainer = new Container
            {
                RelativeSizeAxes = Axes.Both
            }
        };
    }

    /// <summary>
    /// Shows <paramref name="dialog"/>, putting away any dialog already up.
    /// </summary>
    public void Push(PopupDialog dialog)
    {
        if (CurrentDialog != null)
            dismiss(CurrentDialog, hide: false);

        CurrentDialog = dialog;
        dialog.Dismissed = () => dismiss(dialog, hide: true);
        dialogContainer.Add(dialog);
        Show();
    }

    private void dismiss(PopupDialog dialog, bool hide)
    {
        if (CurrentDialog != dialog)
            return;

        CurrentDialog = null;

        dialog.Dismissed = null;
        dialog.FadeOut(fade_duration, Easing.OutQuint);
        Scheduler.AddDelayed(() => dialogContainer.Remove(dialog), fade_duration);

        if (hide)
            Hide();
    }

    // A click on the dim around the dialog is the cancel answer, as Escape is. A dialog with no cancel
    // answer stays up until it's answered.
    public override bool OnClick(MouseButtonEvent e)
    {
        if (State != Visibility.Visible || e.Button != MouseButton.Left || CurrentDialog == null || CurrentDialog.Contains(e.ScreenSpaceMousePosition))
            return base.OnClick(e);

        CurrentDialog.PerformCancelAction();
        return true;
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (State == Visibility.Visible && CurrentDialog?.HandleKey(e) == true)
            return true;

        return base.OnKeyDown(e);
    }

    protected override void PopIn()
    {
        shown = true;
        uiSounds?.Play(UISound.OverlayOpen);

        duck?.Dispose();
        duck = audioManager?.Duck(new DuckParameters
        {
            DuckDuration = 100,
            RestoreDuration = 100
        });

        ClearTransforms();
        this.FadeIn(fade_duration, Easing.OutQuint);
    }

    protected override void PopOut()
    {
        if (shown)
            uiSounds?.Play(UISound.OverlayClose);

        shown = false;
        duck?.Dispose();
        duck = null;
        ClearTransforms();
        this.FadeOut(fade_duration, Easing.OutQuint);
    }
}
