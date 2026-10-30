// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.UserInterface.Results;
using Yuuki.Game.Resources;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// Yuuki's lines on the results screen: which a run gets, that every take is there, and that one reveal never says the
/// same word twice, whichever takes it picks.
/// </summary>
[TestFixture]
public class ResultsVoiceTest
{
    /// <summary>
    /// Words written in hiragana that would sound repeated (the rest of a line's words are its katakana and kanji).
    /// </summary>
    private static readonly string[] watched_words =
    {
        "やった", "すごい", "すっごい", "すごすぎ", "きれい", "ぜんぶ", "いっしょ", "ほんと", "うそ", "おしい", "くやし", "だいじょうぶ", "がんば",
        "えらい", "たのし", "しあわせ", "いける", "いこ", "きた", "かっこいい", "ばっちり", "おつかれ", "おめでとう", "すてき"
    };

    [Test]
    public void TestEveryTakeIsThere()
    {
        string[] files = YuukiResourceAssembly.ResourceAssembly.GetManifestResourceNames();

        var missing = ResultsVoice.Lines.SelectMany(line => line.Value).Where(take => !files.Contains($"Yuuki.Game.Resources.Samples.Results.Voice.{take.File}"));

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void TestEveryLineSaidHasTakes()
    {
        var said = everyRun().SelectMany(run => everyWay(run)).SelectMany(lines => lines).Distinct();

        Assert.That(said.Where(line => !ResultsVoice.Lines.TryGetValue(line, out var takes) || takes.Count == 0), Is.Empty);
    }

    [Test]
    public void TestAnSFullComboWithANewBest()
    {
        var run = new VoiceContext(ScoreRank.S, 0.98, FullCombo: true, NewBest: true);

        Assert.That(ResultsVoice.LinesFor(run, new Random(1)), Is.EqualTo(new[] { "rank-s", "fc", "best", "react-s" }));
    }

    [Test]
    public void TestAnAllPerfectIsItsOwnMoment()
    {
        // Late at night and a third try, but an all perfect says so whatever else fits.
        var run = new VoiceContext(ScoreRank.S, 1, FullCombo: true, AllPerfect: true, Attempt: 3, Hour: 2);

        foreach (IReadOnlyList<string> lines in everyWay(run))
            Assert.That(lines, Is.EqualTo(new[] { "rank-s", "ap", "react-ap" }));
    }

    [Test]
    public void TestAFullBloom()
    {
        var run = new VoiceContext(ScoreRank.SS, 1, true, true, NewBest: true, FullBloom: true);

        Assert.That(ResultsVoice.LinesFor(run, new Random(1)), Is.EqualTo(new[] { "rank-ss", "fullbloom", "best", "react-fullbloom" }));
    }

    [Test]
    public void TestALostSIsOnlyThat()
    {
        var run = new VoiceContext(ScoreRank.A, 0.97, RankLost: true, NewBest: true);

        Assert.That(ResultsVoice.LinesFor(run, new Random(1)), Is.EqualTo(new[] { "lost", "react-lost" }));
    }

    [TestCase(0.947, true)]
    [TestCase(0.944, false)]
    public void TestJustMissingTheNextRank(double accuracy, bool close)
    {
        var run = new VoiceContext(ScoreRank.A, accuracy);

        Assert.That(ResultsVoice.MomentsFor(run).Contains("react-close"), Is.EqualTo(close));
    }

    [TestCase(1, false)]
    [TestCase(4, false)]
    [TestCase(5, true)]
    [TestCase(8, true)]
    [TestCase(9, false)]
    public void TestTheMorning(int hour, bool morning)
    {
        var run = new VoiceContext(ScoreRank.B, 0.85, Hour: hour);

        Assert.That(ResultsVoice.MomentsFor(run).Contains("react-morning"), Is.EqualTo(morning));
    }

    /// <summary>
    /// A moment that fits takes the usual reaction's place about two times in three.
    /// </summary>
    [Test]
    public void TestAMomentIsSaidMostTimes()
    {
        var run = new VoiceContext(ScoreRank.B, 0.85, Hour: 2);
        var random = new Random(7);

        int late = Enumerable.Range(0, 3000).Count(_ => ResultsVoice.LinesFor(run, random).Last() == "react-late");

        Assert.That(late / 3000.0, Is.EqualTo(2 / 3.0).Within(0.03));
    }

    [Test]
    public void TestATakeIsNeverSaidTwiceInARow()
    {
        var random = new Random(3);
        int last = ResultsVoice.PickTake("test-line", 5, random);

        for (int i = 0; i < 500; i++)
        {
            int take = ResultsVoice.PickTake("test-line", 5, random);
            Assert.That(take, Is.Not.EqualTo(last));
            last = take;
        }
    }

    /// <summary>
    /// Every way every kind of run can go, and every take of every line it says: no word comes up twice.
    /// </summary>
    [Test]
    public void TestNoRevealRepeatsAWord()
    {
        var repeats = new List<string>();
        int picks = 0;

        foreach (IReadOnlyList<string> lines in everyRun().SelectMany(everyWay).Distinct(new SequenceComparer()))
        {
            IReadOnlyList<VoiceTake>[] takes = lines.Select(line => ResultsVoice.Lines[line]).ToArray();

            foreach (VoiceTake[] pick in combinations(takes))
            {
                picks++;
                var seen = new Dictionary<string, int>();

                for (int i = 0; i < pick.Length; i++)
                {
                    foreach (string word in words(pick[i].Text))
                    {
                        if (seen.TryGetValue(word, out int said) && said != i)
                            repeats.Add($"{word}: {string.Join(" / ", pick.Select(t => t.Text))}");

                        seen[word] = i;
                    }
                }
            }
        }

        Assert.That(picks, Is.GreaterThan(1000), "picks checked");
        Assert.That(repeats, Is.Empty);
    }

    // A line's words: its runs of katakana and of kanji, and the watched words it has.
    private static IEnumerable<string> words(string text) =>
        Regex.Matches(text, "[ァ-ヴ][ァ-ヴー]+|[一-龯々]+").Select(m => m.Value)
             .Concat(watched_words.Where(text.Contains))
             .Distinct();

    // Every kind of run: each rank, with and without a full combo, an all perfect, a new best, a first play, a third try,
    // at every hour that changes anything, and just short of the next rank or not; a lost S; a failed run.
    private static IEnumerable<VoiceContext> everyRun()
    {
        foreach (ScoreRank rank in new[] { ScoreRank.D, ScoreRank.C, ScoreRank.B, ScoreRank.A, ScoreRank.S })
        {
            foreach (double accuracy in new[] { rank.MinimumAccuracy(), nextThreshold(rank) - 0.001 })
            {
                foreach (bool fullCombo in new[] { false, true })
                {
                    foreach (bool allPerfect in rank == ScoreRank.S && fullCombo ? new[] { false, true } : new[] { false })
                    {
                        foreach (bool newBest in new[] { false, true })
                        {
                            foreach (bool firstPlay in new[] { false, true })
                            {
                                foreach (int attempt in new[] { 1, 3 })
                                {
                                    foreach (int hour in new[] { 2, 7, 15 })
                                        yield return new VoiceContext(rank, accuracy, fullCombo, allPerfect, NewBest: newBest, FirstPlay: firstPlay, Attempt: attempt, Hour: hour);
                                }
                            }
                        }
                    }
                }
            }
        }

        // 100%: an SS, an all perfect, and maybe a Full Bloom.
        foreach (bool fullBloom in new[] { false, true })
        {
            foreach (bool newBest in new[] { false, true })
                yield return new VoiceContext(ScoreRank.SS, 1, true, true, NewBest: newBest, FullBloom: fullBloom);
        }

        yield return new VoiceContext(ScoreRank.A, 0.97, RankLost: true);
        yield return new VoiceContext(ScoreRank.D, 0.4, Failed: true);
    }

    private static double nextThreshold(ScoreRank rank) =>
        ScoreRankExtensions.THRESHOLD_RANKS.Where(r => r > rank).Select(r => r.MinimumAccuracy()).DefaultIfEmpty(1).First();

    // Every set of lines a run can be given: the usual reaction, and each moment that fits.
    private static IEnumerable<IReadOnlyList<string>> everyWay(VoiceContext run)
    {
        // The usual reaction: a random that never picks a moment.
        yield return ResultsVoice.LinesFor(run, new NeverRandom());

        IReadOnlyList<string> usual = ResultsVoice.LinesFor(run, new NeverRandom());

        if (run.Failed || run.RankLost || run.AllPerfect || run.FullBloom)
            yield break;

        foreach (string moment in ResultsVoice.MomentsFor(run))
            yield return usual.Take(usual.Count - 1).Append(moment).ToArray();
    }

    private static IEnumerable<VoiceTake[]> combinations(IReadOnlyList<VoiceTake>[] takes)
    {
        int[] at = new int[takes.Length];

        while (true)
        {
            yield return at.Select((take, line) => takes[line][take]).ToArray();

            int i = takes.Length - 1;

            while (i >= 0 && ++at[i] == takes[i].Count)
                at[i--] = 0;

            if (i < 0)
                yield break;
        }
    }

    /// <summary>
    /// Never rolls under a chance: a moment is never picked.
    /// </summary>
    private class NeverRandom : Random
    {
        public override double NextDouble() => 1;
    }

    private class SequenceComparer : IEqualityComparer<IReadOnlyList<string>>
    {
        public bool Equals(IReadOnlyList<string>? x, IReadOnlyList<string>? y) => x!.SequenceEqual(y!);

        public int GetHashCode(IReadOnlyList<string> obj) => string.Join("|", obj).GetHashCode();
    }
}
