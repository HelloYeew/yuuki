// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Tests.Beatmaps;

[TestFixture]
public class BeatmapFilterTest
{
    private BeatmapSet setA = null!;
    private BeatmapSet setB = null!;
    private BeatmapSet setC = null!;
    private Dictionary<BeatmapSet, List<Beatmap>> difficulties = null!;

    [SetUp]
    public void SetUp()
    {
        setA = new BeatmapSet { ID = 1, Title = "Bon Appetit S", TitleUnicode = "ぼなぺてぃーと♡S", Artist = "Blend A", ArtistUnicode = "ブレンド・A", Source = "Blend S", BPM = 152, TotalLength = 89_000, Creator = "Muziyami" };
        setB = new BeatmapSet { ID = 2, Title = "Tao Hua Xiao", Artist = "Wang Rui", BPM = 160, TotalLength = 188_000, Creator = "Muziyami" };
        setC = new BeatmapSet { ID = 3, Title = "Aozora", Artist = "Ziyoou-vachi", BPM = 128, TotalLength = 120_000, Creator = "Someone" };

        difficulties = new Dictionary<BeatmapSet, List<Beatmap>>
        {
            [setA] = new List<Beatmap> { beatmap(setA, "Easy", 2), beatmap(setA, "Hard", 7) },
            [setB] = new List<Beatmap> { beatmap(setB, "Hyper", 11), beatmap(setB, "Insane", 13.4) },
            [setC] = new List<Beatmap> { beatmap(setC, "Normal", 4) }
        };
    }

    private static Beatmap beatmap(BeatmapSet set, string name, double rating) => new Beatmap { BeatmapSet = set, DifficultyName = name, DifficultyRating = rating, Creator = set.Creator };

    private List<BeatmapSet> apply(BeatmapFilter filter) => filter.Apply(new[] { setA, setB, setC }, s => difficulties[s]);

    [Test]
    public void NoFilter_KeepsEverythingInOrder()
    {
        var filter = new BeatmapFilter();

        Assert.That(filter.IsFiltering, Is.False);
        Assert.That(apply(filter), Is.EqualTo(new[] { setA, setB, setC }));
    }

    [Test]
    public void Search_EveryWordMustAppear_AnyCase()
    {
        Assert.That(apply(new BeatmapFilter { Search = "wang" }), Is.EqualTo(new[] { setB }));
        Assert.That(apply(new BeatmapFilter { Search = "muziyami appetit" }), Is.EqualTo(new[] { setA }));
        Assert.That(apply(new BeatmapFilter { Search = "muziyami nothing" }), Is.Empty);
    }

    [Test]
    public void Search_MatchesOriginalScriptAndSource()
    {
        Assert.That(apply(new BeatmapFilter { Search = "ぼなぺ" }), Is.EqualTo(new[] { setA }));
        Assert.That(apply(new BeatmapFilter { Search = "ブレンド" }), Is.EqualTo(new[] { setA }));
        Assert.That(apply(new BeatmapFilter { Search = "blend s" }), Is.EqualTo(new[] { setA }));
    }

    [Test]
    public void Search_DifficultyNamesCount()
    {
        Assert.That(apply(new BeatmapFilter { Search = "insane" }), Is.EqualTo(new[] { setB }));

        // A difficulty passes only when it's the one carrying the word.
        var filter = new BeatmapFilter { Search = "hard" };
        Assert.That(difficulties[setA].Where(filter.Matches).Select(b => b.DifficultyName), Is.EqualTo(new[] { "Hard" }));
    }

    [Test]
    public void LevelRange_NeedsADifficultyInside()
    {
        Assert.That(apply(new BeatmapFilter { MinLevel = 5, MaxLevel = 10 }), Is.EqualTo(new[] { setA }));
        Assert.That(apply(new BeatmapFilter { MinLevel = 12 }), Is.EqualTo(new[] { setB }), "the top of the range is no upper limit");
        Assert.That(apply(new BeatmapFilter { MaxLevel = 3 }), Is.EqualTo(new[] { setA }));
    }

    [Test]
    public void LevelAndSearch_MustHoldForTheSameDifficulty()
    {
        // "Easy" is level 2, so a 5+ range with "easy" finds nothing even though the song has level 7.
        Assert.That(apply(new BeatmapFilter { Search = "easy", MinLevel = 5 }), Is.Empty);
    }

    [Test]
    public void Sorts()
    {
        Assert.That(apply(new BeatmapFilter { Sort = BeatmapSort.Title }), Is.EqualTo(new[] { setC, setA, setB }));
        Assert.That(apply(new BeatmapFilter { Sort = BeatmapSort.Artist }), Is.EqualTo(new[] { setA, setB, setC }));
        Assert.That(apply(new BeatmapFilter { Sort = BeatmapSort.Bpm }), Is.EqualTo(new[] { setC, setA, setB }));
        Assert.That(apply(new BeatmapFilter { Sort = BeatmapSort.Length }), Is.EqualTo(new[] { setA, setC, setB }));
        Assert.That(apply(new BeatmapFilter { Sort = BeatmapSort.Level }), Is.EqualTo(new[] { setC, setA, setB }), "by each song's highest level");
    }

    [Test]
    public void SongWithoutDifficulties_PassesOnItsOwnDetailsUnlessARangeIsSet()
    {
        var empty = new BeatmapSet { ID = 9, Title = "Theme" };
        difficulties[empty] = new List<Beatmap>();

        Assert.That(new BeatmapFilter { Search = "theme" }.Apply(new[] { empty }, s => difficulties[s]), Is.EqualTo(new[] { empty }));
        Assert.That(new BeatmapFilter { MinLevel = 1 }.Apply(new[] { empty }, s => difficulties[s]), Is.Empty);
    }
}
