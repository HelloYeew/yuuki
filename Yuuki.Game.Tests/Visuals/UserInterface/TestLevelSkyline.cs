// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

/// <summary>
/// A song's level bars under a filter: every difficulty stays, and the ones it leaves out dim.
/// </summary>
public partial class TestLevelSkyline : YuukiTestScene
{
    [Cached]
    private readonly SongListFilter listFilter = new SongListFilter();

    private readonly BeatmapFilter filter = new BeatmapFilter();

    private LevelSkyline skyline = null!;

    [Test]
    public void TestFilteredOutDifficultiesDim()
    {
        AddStep("show a 4K and a 7K difficulty", () =>
        {
            filter.Lanes.Clear();
            listFilter.Set(filter);

            Child = skyline = new LevelSkyline();
            skyline.SetDifficulties(new[]
            {
                new Beatmap { DifficultyRating = 3, LaneCount = 4 },
                new Beatmap { DifficultyRating = 9, LaneCount = 7 }
            });
        });
        AddUntilStep("both lit", () => skyline.IsLoaded && !skyline.IsDimmed(0) && !skyline.IsDimmed(1));

        AddStep("filter to 7K", () =>
        {
            filter.Lanes.Add(7);
            listFilter.Changed();
        });
        AddUntilStep("the 4K one dims rather than going", () => skyline.IsDimmed(0) && !skyline.IsDimmed(1));

        AddStep("clear the filter", () =>
        {
            filter.Lanes.Clear();
            listFilter.Changed();
        });
        AddUntilStep("both lit again", () => !skyline.IsDimmed(0) && !skyline.IsDimmed(1));
    }
}
