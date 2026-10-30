// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Platform;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Resources;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// Yuuki's voice once the reveal is over
/// </summary>
/// <remarks>
/// <para>The lines are 春日部つむぎ's voice, made with VOICEVOX (tools/make_results_voice.py), and her terms ask for
/// <see cref="CREDIT"/> to be shown where players see it: the settings show it, in About and by the voice's toggle.</para>
/// <para>The screen shows <see cref="Subtitle"/> while she speaks, and lowers its music under her
/// (<see cref="IsSpeaking"/>).</para>
/// </remarks>
public partial class ResultsVoice : Component
{
    /// <summary>
    /// The credit 春日部つむぎ's terms ask for, wherever her voice is heard.
    /// </summary>
    public const string CREDIT = "VOICEVOX:春日部つむぎ";

    /// <summary>
    /// Her official site, for the credit to link to.
    /// </summary>
    public const string CREDIT_URL = "https://tsumugi-official.studio.site/top";

    private const string sample_directory = "Results/Voice/";

    /// <summary>
    /// The pause between one line and the next, in ms.
    /// </summary>
    private const double gap = 140;

    /// <summary>
    /// How long the last line's subtitle stays after she's done, in ms.
    /// </summary>
    private const double subtitle_linger = 1200;

    /// <summary>
    /// How long a line is taken to last when its sample can't say (headless), in ms.
    /// </summary>
    private const double unknown_length = 1500;

    /// <summary>
    /// How close to the next rank, in accuracy, counts as just missing it.
    /// </summary>
    public const double CLOSE_TO_NEXT_RANK = 0.005;

    /// <summary>
    /// How likely a reaction for the moment is, when one fits, over the usual one.
    /// </summary>
    private const double moment_chance = 2 / 3.0;

    /// <summary>
    /// The take each line last said, this session, so the next reveal picks another.
    /// </summary>
    private static readonly Dictionary<string, int> last_takes = new Dictionary<string, int>();

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<VoiceTake>>> lines = new Lazy<IReadOnlyDictionary<string, IReadOnlyList<VoiceTake>>>(load);

    /// <summary>
    /// Every line, and its takes.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<VoiceTake>> Lines => lines.Value;

    [Resolved(canBeNull: true)]
    private IAudioStore<ISample>? sampleStore { get; set; }

    private readonly Random random;
    private readonly Queue<VoiceTake> queue = new Queue<VoiceTake>();
    private IAudioChannel? channel;
    private double lineEnds = double.NaN;
    private double subtitleEnds = double.NaN;

    /// <summary>
    /// The words being said now (or just said), as their English subtitle; null when she's quiet.
    /// </summary>
    public string? Subtitle { get; private set; }

    /// <summary>
    /// Whether she's in the middle of what she was given to say.
    /// </summary>
    public bool IsSpeaking { get; private set; }

    /// <param name="random">Where the takes and reactions are picked from; seeded, they're the same every time.</param>
    public ResultsVoice(Random? random = null)
    {
        this.random = random ?? new Random();
    }

    /// <summary>
    /// Says <paramref name="say"/> (line names, as <see cref="LinesFor"/> gives), one after another, from now. Anything
    /// still being said stops.
    /// </summary>
    public void Say(IEnumerable<string> say)
    {
        Stop();

        foreach (string line in say)
        {
            if (Lines.TryGetValue(line, out IReadOnlyList<VoiceTake>? takes) && takes.Count > 0)
                queue.Enqueue(takes[PickTake(line, takes.Count, random)]);
        }

        if (queue.Count > 0)
        {
            IsSpeaking = true;
            next();
        }
    }

    /// <summary>
    /// Stops speaking, and clears the subtitle.
    /// </summary>
    public void Stop()
    {
        queue.Clear();
        channel?.Stop();
        channel = null;
        lineEnds = subtitleEnds = double.NaN;
        IsSpeaking = false;
        Subtitle = null;
    }

    public override void Update()
    {
        base.Update();

        double now = Clock.CurrentTime;

        if (!double.IsNaN(lineEnds) && now >= lineEnds)
        {
            if (queue.Count > 0)
            {
                if (now >= lineEnds + gap)
                    next();
            }
            else
            {
                lineEnds = double.NaN;
                channel = null;
                IsSpeaking = false;
                subtitleEnds = now + subtitle_linger;
            }
        }

        if (!double.IsNaN(subtitleEnds) && now >= subtitleEnds)
        {
            subtitleEnds = double.NaN;
            Subtitle = null;
        }
    }

    private void next()
    {
        VoiceTake take = queue.Dequeue();
        ISample? sample = sampleStore.IsNull() ? null : sampleStore!.Get(sample_directory + take.File);

        channel = sample?.GetChannel();

        if (channel.IsNotNull())
        {
            channel!.AutoDispose = true;
            channel.Play();
        }

        Subtitle = take.English;
        lineEnds = Clock.CurrentTime + (sample is { Length: > 0 } ? sample.Length : unknown_length);
    }

    protected override void Dispose(bool isDisposing)
    {
        channel?.Stop();
        base.Dispose(isDisposing);
    }

    /// <summary>
    /// What Yuuki says after a reveal, in order: the rank's call, the badge's (full combo or all perfect), a new best, and
    /// her reaction. A reaction for the moment, when one fits, takes the usual one's place two times in three. A lost S
    /// is only that, and how she feels about it; a failed run, only her reaction.
    /// </summary>
    public static IReadOnlyList<string> LinesFor(VoiceContext run, Random random)
    {
        if (run.Failed)
            return new[] { "react-fail" };

        if (run.RankLost)
            return new[] { "lost", "react-lost" };

        var say = new List<string> { $"rank-{run.Rank.ToString().ToLowerInvariant()}" };

        if (run.AllPerfect)
            say.Add("ap");
        else if (run.FullCombo)
            say.Add("fc");

        if (run.NewBest)
            say.Add("best");

        // All perfect is a moment of its own.
        if (run.AllPerfect)
        {
            say.Add("react-ap");
            return say;
        }

        string[] moments = MomentsFor(run).ToArray();

        say.Add(moments.Length > 0 && random.NextDouble() < moment_chance
            ? moments[random.Next(moments.Length)]
            : run.Rank switch
            {
                ScoreRank.S => "react-s",
                ScoreRank.A => "react-a",
                ScoreRank.B => "react-b",
                _ => "react-low"
            });

        return say;
    }

    /// <summary>
    /// The reactions for the moment that fit a run (not a failed one, a lost S or an all perfect, which have their own).
    /// </summary>
    public static IEnumerable<string> MomentsFor(VoiceContext run)
    {
        ScoreRank? next = ScoreRankExtensions.THRESHOLD_RANKS.Cast<ScoreRank?>().FirstOrDefault(r => r > run.Rank);

        if (next is ScoreRank above && run.Accuracy >= above.MinimumAccuracy() - CLOSE_TO_NEXT_RANK)
            yield return "react-close";

        if (run.FirstPlay && run.Rank >= ScoreRank.A)
            yield return "react-first";

        if (run.Attempt >= 3)
            yield return "react-retry";

        if (run.Hour < 5)
            yield return "react-late";
        else if (run.Hour < 9)
            yield return "react-morning";
    }

    /// <summary>
    /// Which of a line's <paramref name="count"/> takes to say: any but the one it said last.
    /// </summary>
    public static int PickTake(string line, int count, Random random)
    {
        int take = random.Next(count);

        lock (last_takes)
        {
            if (count > 1 && last_takes.TryGetValue(line, out int last) && take == last)
                take = (take + 1 + random.Next(count - 1)) % count;

            last_takes[line] = take;
        }

        return take;
    }

    /// <summary>
    /// The lines, from lines.json beside their takes (tools/make_results_voice.py writes it).
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<VoiceTake>> load()
    {
        var storage = new EmbeddedResourceStorage(YuukiResourceAssembly.ResourceAssembly, "Yuuki.Game.Resources.Samples.Results.Voice");

        using Stream stream = storage.GetStream("lines.json") ?? throw new FileNotFoundException("Yuuki's lines (lines.json) aren't in the game's resources.");
        using JsonDocument document = JsonDocument.Parse(stream);

        return document.RootElement.GetProperty("lines").EnumerateObject().ToDictionary(
            line => line.Name,
            line => (IReadOnlyList<VoiceTake>)line.Value.EnumerateArray()
                                                  .Select(take => new VoiceTake(take.GetProperty("file").GetString()!, take.GetProperty("text").GetString()!,
                                                      take.GetProperty("english").GetString()!))
                                                  .ToArray());
    }
}

/// <summary>
/// One take of a line: its file, its words, and their English subtitle.
/// </summary>
public readonly record struct VoiceTake(string File, string Text, string English);

/// <summary>
/// What Yuuki knows of a run when she speaks about it.
/// </summary>
/// <param name="Rank">The rank it earned.</param>
/// <param name="Accuracy">Its accuracy, 0–1.</param>
/// <param name="FullCombo">No miss, no hold broken.</param>
/// <param name="AllPerfect">Every note Critical.</param>
/// <param name="RankLost">A miss kept it from the S its accuracy reached.</param>
/// <param name="Failed">Its health ran out.</param>
/// <param name="NewBest">It beat the best on its difficulty.</param>
/// <param name="FirstPlay">It's the first play of its difficulty.</param>
/// <param name="Attempt">Which try in a row on it this is (1 for the first).</param>
/// <param name="Hour">The local hour it ended in, 0–23.</param>
public readonly record struct VoiceContext(ScoreRank Rank, double Accuracy, bool FullCombo = false, bool AllPerfect = false, bool RankLost = false, bool Failed = false,
                                           bool NewBest = false, bool FirstPlay = false, int Attempt = 1, int Hour = 12)
{
    public static VoiceContext For(GameplayResult result, bool newBest, bool firstPlay, int attempt, DateTime ended) => new VoiceContext(result.Rank, result.Accuracy,
        result.IsFullCombo, result.IsAllPerfect, result.RankLostToMiss, result.Failed, newBest, firstPlay, attempt, ended.Hour);
}
