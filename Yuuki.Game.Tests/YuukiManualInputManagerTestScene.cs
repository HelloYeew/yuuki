// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Testing;

namespace Yuuki.Game.Tests;

public abstract class YuukiManualInputManagerTestScene : ManualInputManagerTestScene
{
    [SetUp]
    public void ResetAndSetup()
    {
        AddStep("clear scene", () =>
        {
            TestContent.Clear();
        });
        AddStep("reset input", ResetInput);
    }
}
