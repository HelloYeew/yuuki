// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

public partial class TestResultsVoice : YuukiTestScene
{
    private ResultsVoice voice = null!;
    private SpriteText subtitle = null!;
    private SpriteText said = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show the subtitle", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(voice = new ResultsVoice());
            Add(subtitle = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 30, FontWeight.Bold)
            });
            Add(said = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Y = 50,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 18, FontWeight.Regular)
            });
            Add(new SpriteText
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                Y = -20,
                Text = ResultsVoice.CREDIT,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Regular)
            });
        });
    }

    [Test]
    public void TestEveryLine()
    {
        foreach (string line in ResultsVoice.Lines.Keys)
            AddStep($"say {line}", () => voice.Say(new[] { line }));
    }

    [Test]
    public void TestReveals()
    {
        say("an S full combo, a new best", new VoiceContext(ScoreRank.S, 0.98, FullCombo: true, NewBest: true));
        say("an all perfect", new VoiceContext(ScoreRank.S, 1, FullCombo: true, AllPerfect: true));
        say("an A, just short of S", new VoiceContext(ScoreRank.A, 0.948));
        say("a lost S", new VoiceContext(ScoreRank.A, 0.97, RankLost: true));
        say("a B on a first play", new VoiceContext(ScoreRank.B, 0.85, FirstPlay: true));
        say("a C, third try", new VoiceContext(ScoreRank.C, 0.75, Attempt: 3));
        say("a D at 2 a.m.", new VoiceContext(ScoreRank.D, 0.5, Hour: 2));
        AddStep("stop", () => voice.Stop());
    }

    /// <summary>
    /// She goes from line to line with a moment between, and the subtitle follows her; once done, it goes.
    /// </summary>
    [Test]
    public void TestSheSaysEveryLineInTurn()
    {
        AddStep("an S full combo", () => voice.Say(new[] { "rank-s", "fc", "react-s" }));
        AddAssert("speaking", () => voice.IsSpeaking && voice.Subtitle != null);
        AddUntilStep("done", () => !voice.IsSpeaking);
        AddUntilStep("the subtitle goes", () => voice.Subtitle == null);
    }

    private void say(string name, VoiceContext run) => AddStep(name, () =>
    {
        string[] lines = ResultsVoice.LinesFor(run, new Random()).ToArray();
        said.Text = string.Join("  →  ", lines);
        voice.Say(lines);
    });

    public override void Update()
    {
        base.Update();

        if (subtitle != null)
            subtitle.Text = voice.Subtitle ?? "";
    }
}
