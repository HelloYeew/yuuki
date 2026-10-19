// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Reactive;
using Sakura.Framework.Testing;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestRevertToDefaultButton : TestScene
{
    private Reactive<int> setting = null!;
    private RevertToDefaultButton button = null!;

    [Test]
    public void TestShowsOffDefaultAndReverts()
    {
        AddStep("add button", () =>
        {
            Clear();
            setting = new Reactive<int>(750);
            Add(button = RevertToDefaultButton.For(setting));
            button.Anchor = Anchor.Centre;
            button.Origin = Anchor.Centre;
        });

        AddAssert("hidden at the default", () => button.Alpha == 0 && !button.Enabled.Value);
        AddStep("move off the default", () => setting.Value = 900);
        AddUntilStep("shows", () => button.Alpha > 0 && button.Enabled.Value);
        AddStep("click", () => button.Action());
        AddAssert("back to the default", () => setting.Value == 750);
        AddUntilStep("hides again", () => !button.Enabled.Value);
    }

    [Test]
    public void TestStartsShownWhenAlreadyOffDefault()
    {
        AddStep("add button for a changed setting", () =>
        {
            Clear();
            setting = new Reactive<int>(0) { Value = -44 };
            Add(button = RevertToDefaultButton.For(setting));
        });

        AddAssert("shown straight away, no fade", () => button.Alpha == 1 && button.Enabled.Value);
    }

    [Test]
    public void TestCustomDefault()
    {
        var keys = new List<char>();
        char[] defaults = { 'D', 'F', 'J', 'K' };

        AddStep("add button for a layout", () =>
        {
            Clear();
            keys.Clear();
            keys.AddRange(defaults);
            Add(button = new RevertToDefaultButton(() => keys.SequenceEqual(defaults), () =>
            {
                keys.Clear();
                keys.AddRange(defaults);
            }));
        });

        AddAssert("hidden on the default layout", () => !button.Enabled.Value);
        AddStep("rebind a key", () => keys[0] = 'A');
        AddUntilStep("shows", () => button.Enabled.Value);
        AddStep("click", () => button.Action());
        AddAssert("layout restored", () => keys.SequenceEqual(defaults));
    }
}
