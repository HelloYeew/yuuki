// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Utilities;

public class BeatmapTestUtility
{
    private readonly Random random = new Random();

    public List<BeatmapSet> BeatmapSets { get; }
    public List<Beatmap> Beatmaps { get; }

    /// <summary>
    /// The hidden theme beatmap set played on the menu. Not shown in song selection.
    /// </summary>
    public BeatmapSet ThemeSet { get; } = new BeatmapSet
    {
        ID = BeatmapsCollection.THEME_BEATMAP_SET_ID,
        Title = "2:23 AM",
        TitleUnicode = "2:23 AM",
        Artist = "Sharou",
        ArtistUnicode = "しゃろう",
        Source = "OpenTracks",
        SourceUnicode = "OpenTracks",
        TotalLength = 192000,
        PreviewTime = 21000,
        BPM = 180,
        Creator = "Yuuki",
        Hide = true,
        UseLocalSource = true,
        CoverPath = "Beatmaps/Album/theme.jpg",
        TrackPath = "theme/theme.mp3",
        BackgroundPath = "Beatmaps/Background/theme.jpeg"
    };

    private static readonly DifficultyLevel[] all_difficulty_levels = Enum.GetValues<DifficultyLevel>();

    private static readonly string[] creator_names = { "Yuuki" };

    public BeatmapTestUtility()
    {
        BeatmapSets = getLocalBeatmapSets();
        Beatmaps = generateBeatmaps();
    }

    private List<BeatmapSet> getLocalBeatmapSets()
    {
        return new List<BeatmapSet>
        {
            new BeatmapSet
            {
                ID = 1,
                Title = "Innocence (TV Size)",
                TitleUnicode = "Innocence (TV Size)",
                Artist = "Eir Aoi",
                ArtistUnicode = "藍井エイル",
                Source = "Sword Art Online II",
                SourceUnicode = "ソードアート・オンライン II",
                TotalLength = 89000,
                PreviewTime = 54300,
                BPM = 183,
                Creator = getRandomCreator(),
                UseLocalSource = true,
                CoverPath = "Beatmaps/Album/innocence-tv-size.jpg",
                TrackPath = "beatmaps/innocence-tv-size.mp3",
                BackgroundPath = "Beatmaps/Background/innocence-tv-size.jpg"
            },
            new BeatmapSet
            {
                ID = 2,
                Title = "Courage",
                TitleUnicode = "courage",
                Artist = "Haruka Tomatsu",
                ArtistUnicode = "戸松遥",
                Source = "Sword Art Online II",
                SourceUnicode = "ソードアート・オンライン II",
                TotalLength = 254000,
                PreviewTime = 49700,
                BPM = 98,
                Creator = getRandomCreator(),
                UseLocalSource = true,
                CoverPath = "Beatmaps/Album/courage.jpg",
                TrackPath = "beatmaps/courage.mp3",
                BackgroundPath = "Beatmaps/Background/courage.jpg"
            },
            new BeatmapSet
            {
                ID = 3,
                Title = "Passionate Starmine",
                TitleUnicode = "熱色スターマイン",
                Artist = "Roselia",
                ArtistUnicode = "Roselia",
                Source = "Passionate Starmine - EP",
                SourceUnicode = "熱色スターマイン - EP",
                TotalLength = 267000,
                PreviewTime = 242000,
                BPM = 187,
                Creator = getRandomCreator(),
                UseLocalSource = true,
                CoverPath = "Beatmaps/Album/passionate-starmine.jpg",
                TrackPath = "beatmaps/passionate-starmine.mp3",
                BackgroundPath = "Beatmaps/Background/passionate-starmine.jpg"
            }
        };
    }

    private List<Beatmap> generateBeatmaps()
    {
        int incrementId = 0;
        var beatmaps = new List<Beatmap>();

        foreach (BeatmapSet beatmapSet in BeatmapSets)
        {
            foreach (DifficultyLevel difficultyLevel in all_difficulty_levels)
            {
                double difficultyRating = random.NextDouble() * 18;

                beatmaps.Add(new Beatmap
                {
                    ID = incrementId++,
                    BeatmapSet = beatmapSet,
                    Creator = getRandomCreator(),
                    DifficultyName = $"{BeatmapUtility.CalculateDifficultyLevel(difficultyRating)} {difficultyLevel}",
                    DifficultyRating = difficultyRating,
                    BackgroundPath = beatmapSet.BackgroundPath,
                });
            }
        }

        return beatmaps;
    }

    private string getRandomCreator() => creator_names[random.Next(creator_names.Length)];

    public static List<BeatmapSet> CreateDummyBeatmapSets(int count) => Enumerable.Range(1, count).Select(MakeBeatmapSet).ToList();

    public static BeatmapSet MakeBeatmapSet(int id) => new BeatmapSet
    {
        ID = id,
        Title = $"Test Song {id}",
        Artist = $"Artist {id}",
        Source = "Test Source",
        BPM = 120 + id,
        TotalLength = 180000,
        Creator = "Tester"
    };

    public static List<Beatmap> CreateDummyBeatmaps(BeatmapSet beatmapSet, int count) => Enumerable.Range(1, count).Select(i => MakeBeatmap(beatmapSet, i)).ToList();

    public static Beatmap MakeBeatmap(BeatmapSet beatmapSet, int id) => new Beatmap
    {
        ID = id,
        BeatmapSet = beatmapSet,
        Creator = "Tester",
        DifficultyName = $"Difficulty {id}",
        DifficultyRating = 5 + id,
        BackgroundPath = beatmapSet.BackgroundPath
    };
}
