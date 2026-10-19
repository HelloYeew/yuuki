// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Yuuki.Game.Graphics.UserInterface.Notifications;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Overlay;

public partial class TestNotificationOverlay : YuukiTestScene
{
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
