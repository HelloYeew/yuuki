// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Edit;
using Yuuki.Game.Edit.Timing;

namespace Yuuki.Game.Tests.Edit;

[TestFixture]
public class EditorClipboardTest
{
    private static BeatmapNote note(double time, NoteLane lane, double duration = 0) =>
        new BeatmapNote { StartTime = time, EndTime = time + duration, Lane = lane };

    [Test]
    public void PasteShiftsTheGroupToTheTime()
    {
        var clipboard = new EditorClipboard();
        clipboard.Copy(new[] { note(1500, NoteLane.Lane2, 250), note(1000, NoteLane.Lane1) });

        var pasted = clipboard.Paste(5000, 4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pasted, Has.Length.EqualTo(2));
            Assert.That(pasted[0].StartTime, Is.EqualTo(5000));
            Assert.That(pasted[1].StartTime, Is.EqualTo(5500));
            Assert.That(pasted[1].EndTime, Is.EqualTo(5750));
            Assert.That(pasted[1].Lane, Is.EqualTo(NoteLane.Lane2));
        }
    }

    [Test]
    public void PastedNotesAreFreshCopies()
    {
        var original = note(1000, NoteLane.Lane1);
        var clipboard = new EditorClipboard();
        clipboard.Copy(new[] { original });

        var first = clipboard.Paste(2000, 4);
        var second = clipboard.Paste(3000, 4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(original.StartTime, Is.EqualTo(1000));
            Assert.That(first[0], Is.Not.SameAs(second[0]));
            Assert.That(first[0].StartTime, Is.EqualTo(2000));
        }
    }

    [Test]
    public void LanesTheBeatmapLacksAreLeftOut()
    {
        var clipboard = new EditorClipboard();
        clipboard.Copy(new[] { note(1000, NoteLane.Lane1), note(1000, NoteLane.Lane7) });

        Assert.That(clipboard.Paste(0, 4), Has.Length.EqualTo(1));
    }

    [Test]
    public void NothingCopiedPastesNothing()
    {
        var clipboard = new EditorClipboard();
        clipboard.Copy(System.Array.Empty<BeatmapNote>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clipboard.HasContent, Is.False);
            Assert.That(clipboard.Paste(0, 4), Is.Empty);
        }
    }

    [Test]
    public void TimestampIsOsuManiaFormat()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(EditorClipboard.Timestamp(83456, System.Array.Empty<BeatmapNote>()), Is.EqualTo("01:23:456 - "));
            Assert.That(EditorClipboard.Timestamp(0, new[] { note(83456, NoteLane.Lane3), note(83456, NoteLane.Lane1) }),
                Is.EqualTo("01:23:456 (83456|2,83456|0) - "));
        }
    }

    [Test]
    public void TapTempoNeedsFourTaps()
    {
        var tap = new TapTempo();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tap.Tap(0), Is.Null);
            Assert.That(tap.Tap(500), Is.Null);
            Assert.That(tap.Tap(1000), Is.Null);
            Assert.That(tap.Tap(1500), Is.EqualTo(120).Within(0.001));
        }
    }

    [Test]
    public void TapTempoStartsOverAfterAPause()
    {
        var tap = new TapTempo();

        foreach (double time in new double[] { 0, 500, 1000, 1500 })
            tap.Tap(time);

        Assert.That(tap.Tap(10000), Is.Null);
    }
}
