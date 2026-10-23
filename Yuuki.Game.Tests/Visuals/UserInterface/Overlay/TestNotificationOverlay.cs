// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.UserInterface.Notifications;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Overlay;

public partial class TestNotificationOverlay : YuukiTestScene
{
    [Cached]
    private readonly GameLayout gameLayout = new GameLayout();

    private NotificationOverlay overlay = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("add overlay", () =>
        {
            Clear();
            Add(overlay = new NotificationOverlay());
        });

        AddUntilStep("loaded", () => overlay.IsLoaded);
    }

    [Test]
    public void TestANotificationToastsThenMovesIntoThePanel()
    {
        SimpleNotification notification = null!;

        AddStep("post", () => overlay.Post(notification = new SimpleNotification { Text = "Hello from the corner." }));

        AddUntilStep("shown as a toast", () => notification.IsInToastTray);
        AddUntilStep("counted as unread", () => overlay.UnreadCount.Value == 1);

        AddStep("open the panel", () => overlay.Show());
        AddUntilStep("moved into the panel", () => !notification.IsInToastTray && notification.Alpha > 0.99f);

        AddStep("close the panel", () => overlay.Hide());
        AddUntilStep("read once seen", () => overlay.UnreadCount.Value == 0 && notification.Read);
    }

    /// <summary>
    /// The close button shows on hover, and by touch, with nothing hovered, all the time.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void TestTheCloseButtonByTouch(bool touch)
    {
        SimpleNotification notification = null!;

        AddStep(touch ? "by touch" : "with a mouse", () => gameLayout.Touch.Value = touch);
        AddStep("post", () => overlay.Post(notification = new SimpleNotification { Text = "Hello from the corner." }));
        AddUntilStep("shown", () => notification.IsLoaded && notification.Alpha > 0.99f);
        AddUntilStep(touch ? "its close button is up" : "its close button waits for hover", () =>
        {
            Drawable close = descendants(notification).Single(d => d.GetType().Name == "CloseButton");
            return touch ? close.Alpha > 0.99f : close.Alpha == 0;
        });
        AddStep("back to a mouse", () => gameLayout.Touch.Value = false);
    }

    private static System.Collections.Generic.IEnumerable<Drawable> descendants(Drawable drawable)
    {
        yield return drawable;

        if (drawable is Container container)
        {
            foreach (Drawable child in container.Children)
            {
                foreach (Drawable d in descendants(child))
                    yield return d;
            }
        }
    }

    [Test]
    public void TestAFinishedTaskPostsItsCompletion()
    {
        ProgressNotification progress = null!;

        AddStep("open the panel", () => overlay.Show());
        AddStep("start a task", () => overlay.Post(progress = new ProgressNotification
        {
            Text = "Working...",
            CompletionText = "All done.",
            State = ProgressNotificationState.Active
        }));

        AddUntilStep("listed", () => overlay.AllNotifications.Contains(progress));
        AddStep("half way", () => progress.Progress = 0.5f);
        AddStep("finish", () => progress.State = ProgressNotificationState.Completed);

        AddUntilStep("the task closed", () => progress.WasClosed);
        AddUntilStep("its completion was posted", () => overlay.AllNotifications.OfType<ProgressCompletionNotification>().Any(n => n.Text == "All done."));
    }

    [Test]
    public void TestClosingARunningTaskCancelsIt()
    {
        ProgressNotification progress = null!;

        AddStep("start a task", () => overlay.Post(progress = new ProgressNotification { Text = "Working...", State = ProgressNotificationState.Active }));
        AddUntilStep("listed", () => overlay.AllNotifications.Contains(progress));

        AddStep("close it", () => progress.Close(true));

        AddAssert("cancelled, not dismissed", () => progress.State == ProgressNotificationState.Cancelled && !progress.WasClosed);
        AddAssert("the task hears about it", () => progress.CancellationToken.IsCancellationRequested);

        AddStep("close it again", () => progress.Close(true));
        AddAssert("now it goes", () => progress.WasClosed);
    }

    [Test]
    public void TestABackgroundTaskCanReportFromItsOwnThread()
    {
        ProgressNotification progress = null!;
        Task task = null!;

        AddStep("run a task off the update thread", () =>
        {
            progress = new ProgressNotification { Text = "Converting...", CompletionText = "Converted." };

            task = Task.Run(() =>
            {
                overlay.Post(progress);
                progress.State = ProgressNotificationState.Active;

                for (int i = 1; i <= 10; i++)
                {
                    progress.Text = $"Converting {i}/10";
                    progress.Progress = i / 10f;
                }

                progress.State = ProgressNotificationState.Completed;
            });
        });

        AddUntilStep("the task finished", () => task.IsCompleted);
        AddAssert("without throwing", () => !task.IsFaulted);
        AddUntilStep("its completion was posted", () => overlay.AllNotifications.OfType<ProgressCompletionNotification>().Any(n => n.Text == "Converted."));
    }

    [Test]
    public void TestAnErrorStaysAsAToast()
    {
        SimpleErrorNotification error = null!;

        AddStep("post an error", () => overlay.Post(error = new SimpleErrorNotification { Text = "Something went wrong." }));
        AddUntilStep("shown as a toast", () => error.IsInToastTray);
        AddAssert("important, so it stays a while", () => error.IsImportant);
        AddAssert("overlay still closed", () => overlay.State == Visibility.Hidden);
    }
}
