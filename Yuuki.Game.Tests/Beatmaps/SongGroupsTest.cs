// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Tests.Beatmaps;

[TestFixture]
public class SongGroupsTest
{
    private static readonly BeatmapSet song = new BeatmapSet { Title = "aozora", Artist = "Ziyoou-vachi", BPM = 174.5f, TotalLength = 95_000 };

    [Test]
    public void EachSortGroupsByItsOwnKey()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SongGroups.LabelOf(song, BeatmapSort.Title, 7), Is.EqualTo("A"), "the first letter, upper case");
            Assert.That(SongGroups.LabelOf(song, BeatmapSort.Artist, 7), Is.EqualTo("Z"));
            Assert.That(SongGroups.LabelOf(song, BeatmapSort.Level, 7), Is.EqualTo("Lv 7"));
            Assert.That(SongGroups.LabelOf(song, BeatmapSort.Bpm, 7), Is.EqualTo("170 BPM"), "in bands of ten");
            Assert.That(SongGroups.LabelOf(song, BeatmapSort.Length, 7), Is.EqualTo("1:30+"), "in bands of half a minute");
        }
    }

    [Test]
    public void AddedOrderHasNoGroups()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SongGroups.LabelOf(song, BeatmapSort.Added, 7), Is.Null);
            Assert.That(SongGroups.TickOf(song, BeatmapSort.Added, 7), Is.Null);
        }
    }

    [Test]
    public void TicksAreShorterAndCoarser()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SongGroups.TickOf(song, BeatmapSort.Level, 7), Is.EqualTo("7"));
            Assert.That(SongGroups.TickOf(song, BeatmapSort.Bpm, 7), Is.EqualTo("160"), "in bands of twenty");
            Assert.That(SongGroups.TickOf(song, BeatmapSort.Length, 7), Is.EqualTo("1'"), "in whole minutes");
            Assert.That(SongGroups.TickOf(song, BeatmapSort.Title, 7), Is.EqualTo("A"));
        }
    }

    [Test]
    public void ATitleNotStartingWithALetterIsHash()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SongGroups.LabelOf(new BeatmapSet { Title = "+ERABY+E CONNEC+10N" }, BeatmapSort.Title, 0), Is.EqualTo("#"));
            Assert.That(SongGroups.LabelOf(new BeatmapSet { Title = "" }, BeatmapSort.Title, 0), Is.EqualTo("#"));
        }
    }
}
