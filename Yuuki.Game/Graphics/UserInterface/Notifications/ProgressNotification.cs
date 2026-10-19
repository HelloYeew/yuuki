// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Notifications;

public enum ProgressNotificationState
{
    Queued,
    Active,
    Completed,
    Cancelled
}

public partial class ProgressNotification : Notification
{
    private const float spinner_size = 22;

    /// <summary>
    /// Whether the task is still going.
    /// </summary>
    public bool Ongoing => State is ProgressNotificationState.Queued or ProgressNotificationState.Active;

    /// <summary>
    /// Asked before cancelling on close. Return false to refuse.
    /// </summary>
    public Func<bool>? CancelRequested { get; set; }

    /// <summary>
    /// Where the completion notification goes. Set by the overlay when this is posted.
    /// </summary>
    public Action<Notification>? CompletionTarget { get; set; }

    /// <summary>
    /// Run when the completion notification is clicked.
    /// </summary>
    public Func<bool>? CompletionClickAction { get; set; }

    public string CompletionText { get; set; } = "Task has completed!";

    public override bool DisplayOnTop => false;

    public override UISound? PopInSound => null;

    protected override IconUsage CloseButtonIcon => IconUsage.Close;

    private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();

    public CancellationToken CancellationToken => cancellationSource.Token;

    // Set from whichever thread the task runs on; the display catches up in Update.
    private volatile string text = string.Empty;
    private volatile bool textChanged;

    public override string Text
    {
        get => text;
        set
        {
            text = value;
            textChanged = true;
        }
    }

    private float progress;
    private volatile bool progressChanged;

    /// <summary>
    /// How far along, 0 to 1.
    /// </summary>
    public float Progress
    {
        get => Volatile.Read(ref progress);
        set
        {
            Volatile.Write(ref progress, Math.Clamp(value, 0, 1));
            progressChanged = true;
        }
    }

    private volatile ProgressNotificationState state = ProgressNotificationState.Queued;
    private volatile bool stateChanged = true;

    public ProgressNotificationState State
    {
        get => state;
        set
        {
            if (state == value)
                return;

            state = value;

            if (value == ProgressNotificationState.Cancelled)
                cancellationSource.Cancel();

            stateChanged = true;
        }
    }

    public override void Update()
    {
        base.Update();

        if (textChanged)
        {
            textChanged = false;
            textFlow.Text = text;
        }

        if (progressChanged)
        {
            progressChanged = false;
            progressBar.ResizeTo(new Vector2(Progress, 1), 100, Easing.OutQuad);
        }

        if (stateChanged)
        {
            stateChanged = false;
            updateState();
        }
    }

    private readonly TextFlowContainer textFlow;
    private readonly Container progressTrack;
    private readonly Box progressBar;
    private readonly LoadingSpinner spinner;
    private readonly IconSprite cancelledIcon;

    private int completionSent;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    public ProgressNotification()
    {
        IsImportant = false;

        TextContent.Add(textFlow = new TextFlowContainer(t =>
        {
            t.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Bold);
            t.Color = Color.White;
        })
        {
            RelativeSizeAxes = Axes.X,
            Width = 1,
            AutoSizeAxes = Axes.Y
        });

        // A pill under the message, the width of the text column.
        Footer.Height = 6;
        Footer.Add(progressTrack = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Masking = true,
            CornerRadius = 3,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = Vector2.One,
                    Color = Color.White.WithAlpha(0.15f)
                },
                progressBar = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0, 1)
                }
            }
        });

        IconContent.AddRange(new Drawable[]
        {
            spinner = new LoadingSpinner(Color.White, spinner_size)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            },
            cancelledIcon = new IconSprite
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = IconUsage.Block,
                IconSize = 20,
                Color = Color.White,
                Alpha = 0
            }
        });

        // A running task isn't dismissed by clicking it.
        Activated = () => false;
    }

    private void updateState()
    {
        switch (state)
        {
            case ProgressNotificationState.Queued:
                AccentColor = NotificationColors.YELLOW;
                progressBar.FadeToColor(NotificationColors.YELLOW, 150);
                spinner.Show();
                break;

            case ProgressNotificationState.Active:
                AccentColor = NotificationColors.BLUE;
                progressBar.FadeToColor(NotificationColors.BLUE, 150);
                spinner.Show();
                break;

            case ProgressNotificationState.Cancelled:
                AccentColor = Color.Gray;
                progressBar.FadeToColor(Color.Gray, 150);
                spinner.Hide();
                cancelledIcon.FadeIn(200, Easing.OutQuint);
                uiSounds?.Play(UISound.NotificationCancel);
                break;

            case ProgressNotificationState.Completed:
                AccentColor = NotificationColors.GREEN;
                spinner.Hide();
                postCompletion();
                break;
        }
    }

    private void postCompletion()
    {
        if (CompletionTarget == null || Interlocked.Exchange(ref completionSent, 1) == 1)
            return;

        CompletionTarget(CreateCompletionNotification());
        Close(false);
    }

    protected virtual Notification CreateCompletionNotification() => new ProgressCompletionNotification
    {
        Text = CompletionText,
        Activated = CompletionClickAction
    };

    /// <summary>
    /// Finishes without posting anything in its place.
    /// </summary>
    public void CompleteSilently()
    {
        CompletionTarget = _ => { };
        State = ProgressNotificationState.Completed;
        Close(false);
    }

    public override void Close(bool byUser)
    {
        // Closing a running task cancels it, if it agrees; only a finished one actually goes away.
        if (Ongoing)
        {
            if (CancelRequested?.Invoke() != false)
                State = ProgressNotificationState.Cancelled;

            return;
        }

        base.Close(byUser);
    }
}
