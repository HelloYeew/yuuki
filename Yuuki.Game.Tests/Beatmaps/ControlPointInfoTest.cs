// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.ControlPoints;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Beatmaps;

[TestFixture]
public class ControlPointInfoTest
{
    /// <summary>
    /// 120 BPM from 100 ms, then 4/3 the tempo (160 BPM, 3/4) from 2100 ms.
    /// </summary>
    private static ControlPointInfo twoSections()
    {
        var info = new ControlPointInfo();
        info.Add(new TimingPoint(2100, 375, TimeSignature.SimpleTriple));
        info.Add(new TimingPoint(100, 500));
        return info;
    }

    [Test]
    public void TimingPointAtFindsTheOneInForce()
    {
        var info = twoSections();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.TimingPointAt(-500).Time, Is.EqualTo(100), "Before the first point its grid runs backwards.");
            Assert.That(info.TimingPointAt(100).Time, Is.EqualTo(100));
            Assert.That(info.TimingPointAt(2099).Time, Is.EqualTo(100));
            Assert.That(info.TimingPointAt(2100).Time, Is.EqualTo(2100));
            Assert.That(info.TimingPointAt(99999).Time, Is.EqualTo(2100));
            Assert.That(info.TimingPointAfter(100)!.Time, Is.EqualTo(2100));
            Assert.That(info.TimingPointAfter(2100), Is.Null);
        }
    }

    [Test]
    public void NoTimingAnswersWithTheDefault()
    {
        var info = new ControlPointInfo();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.HasTiming, Is.False);
            Assert.That(info.TimingPointAt(1234), Is.SameAs(ControlPointInfo.DEFAULT));
        }
    }

    [Test]
    public void AddKeepsTimeOrderAndReplacesAPointAtTheSameTime()
    {
        var info = twoSections();
        info.Add(new TimingPoint(1000, 400));
        info.Add(new TimingPoint(2100, 300));

        Assert.That(info.TimingPoints, Has.Count.EqualTo(3));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.TimingPoints[0].Time, Is.EqualTo(100));
            Assert.That(info.TimingPoints[1].Time, Is.EqualTo(1000));
            Assert.That(info.TimingPoints[2].BeatLength, Is.EqualTo(300));
        }
    }

    [Test]
    public void BeatsAreCountedFromTheirSection()
    {
        var info = twoSections();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.BeatIndexAt(100), Is.EqualTo(0));
            Assert.That(info.BeatIndexAt(599), Is.EqualTo(0));
            Assert.That(info.BeatIndexAt(600), Is.EqualTo(1));
            Assert.That(info.BeatIndexAt(-401), Is.EqualTo(-2), "The count-in runs on the first point's grid.");
            Assert.That(info.BeatIndexAt(2100), Is.EqualTo(0), "A new timing point starts a new bar.");
            Assert.That(info.BeatStartAt(2500), Is.EqualTo(2475));
            Assert.That(ControlPointInfo.IsDownbeat(info.TimingPointAt(2100), 3), Is.True, "3/4: every third beat starts a bar.");
            Assert.That(ControlPointInfo.IsDownbeat(info.TimingPointAt(100), -4), Is.True);
            Assert.That(ControlPointInfo.IsDownbeat(info.TimingPointAt(100), -3), Is.False);
        }
    }

    [TestCase(220, 4, 225)]
    [TestCase(330, 3, 266.66666666666669)]
    [TestCase(1000, 1, 1100)]
    [TestCase(2000, 1, 2100, Description = "Nearer the next timing point than the grid: snaps to the point.")]
    [TestCase(2300, 2, 2287.5)]
    public void SnapsToTheNearestDivisorOrTimingPoint(double time, int divisor, double expected)
    {
        Assert.That(twoSections().GetClosestSnappedTime(time, divisor), Is.EqualTo(expected).Within(1e-6));
    }

    [TestCase(600, 1)]
    [TestCase(350, 2)]
    [TestCase(266.67, 3)]
    [TestCase(225, 4)]
    [TestCase(162.5, 8)]
    public void FindsTheSmallestDivisorATimeIsOn(double time, int expected)
    {
        Assert.That(twoSections().GetClosestBeatDivisor(time), Is.EqualTo(expected));
    }

    [Test]
    public void TheDisplayBpmIsTheTempoThatLastsLongest()
    {
        var info = new ControlPointInfo();
        info.Add(new TimingPoint(5000, 60000 / 200.0));
        info.Add(new TimingPoint(1000, 60000 / 150.0));
        info.Add(new TimingPoint(8000, 60000 / 150.0));

        // 150 BPM: 0 -> 5000 (counted from 0) + 8000 -> 20000 = 17 s; 200 BPM: 3 s.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.GetMostCommonBpm(20000), Is.EqualTo(150).Within(1e-9));
            Assert.That(info.BpmMinimum, Is.EqualTo(150).Within(1e-9));
            Assert.That(info.BpmMaximum, Is.EqualTo(200).Within(1e-9));
        }
    }

    [Test]
    public void LegacyChartsKeepABeatOnTheFirstNote()
    {
        var info = ControlPointInfo.FromLegacyBpm(120, 1750);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.TimingPoints, Has.Count.EqualTo(1));
            Assert.That(info.TimingPoints[0].Time, Is.EqualTo(250));
            Assert.That(info.BeatStartAt(1750), Is.EqualTo(1750));
            Assert.That(ControlPointInfo.FromLegacyBpm(0, 1750).HasTiming, Is.False);
        }
    }

    #region Storage

    private static Beatmap timedBeatmap() => new Beatmap
    {
        ID = 7,
        BeatmapSet = new BeatmapSet { ID = 3, Title = "t", Artist = "a", BPM = 180 },
        DifficultyName = "Hard",
        ControlPoints = twoSections(),
        Notes = new[]
        {
            new BeatmapNote { Lane = NoteLane.Lane1, StartTime = 1000, EndTime = 1000 }
        }
    };

    private static MemoryStream toStream(object value) => new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(value));

    [Test]
    public void TimingSurvivesARoundTrip()
    {
        var read = BeatmapUtility.Deserialize(toStream(timedBeatmap()));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Version, Is.EqualTo(Beatmap.CURRENT_VERSION));
            Assert.That(read.ControlPoints.TimingPoints, Is.EqualTo(twoSections().TimingPoints));
            Assert.That(read.ControlPoints.TimingPoints[1].TimeSignature, Is.EqualTo(TimeSignature.SimpleTriple));
        }
    }

    [Test]
    public void TheLibraryScanReadsTimingButNotNotes()
    {
        var read = BeatmapUtility.DeserializeMetadata(toStream(timedBeatmap()));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Notes, Is.Empty);
            Assert.That(read.ControlPoints.TimingPoints, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void AVersionOneFileReadsWithNoTimingAndPlaysOnTheSetBpm()
    {
        const string json = """
                            {
                              "Version": 1,
                              "ID": 1,
                              "BeatmapSet": { "Version": 1, "ID": 1, "Title": "t", "Artist": "a", "BPM": 120 },
                              "DifficultyName": "Old",
                              "Notes": [ { "Lane": 0, "Type": 0, "StartTime": 1750, "EndTime": 1750 } ]
                            }
                            """;

        var read = BeatmapUtility.Deserialize(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        var chart = PlayableBeatmap.FromBeatmap(read);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Version, Is.EqualTo(1));
            Assert.That(read.ControlPoints.HasTiming, Is.False);
            Assert.That(chart.ControlPoints.TimingPoints.Single().Time, Is.EqualTo(250));
            Assert.That(chart.Bpm, Is.EqualTo(120).Within(1e-9));
        }
    }

    [Test]
    public void AChartUsesItsOwnTimingOverTheSetBpm()
    {
        var chart = PlayableBeatmap.FromBeatmap(timedBeatmap());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(chart.ControlPoints.TimingPoints, Is.EqualTo(twoSections().TimingPoints));
            Assert.That(chart.Bpm, Is.EqualTo(120).Within(1e-9), "Only 120 BPM is in force up to the last note.");
        }
    }

    #endregion
}
