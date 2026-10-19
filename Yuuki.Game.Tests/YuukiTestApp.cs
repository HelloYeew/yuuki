// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Reflection;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;
using Yuuki.Game.Resources;

namespace Yuuki.Game.Tests;

public partial class YuukiTestApp : TestBrowserApp
{
    public YuukiTestApp(Assembly testAssembly = null!) : base(testAssembly)
    {
    }

    public override void Load()
    {
        base.Load();

        var resourceStorage = new EmbeddedResourceStorage(YuukiResourceAssembly.ResourceAssembly, "Yuuki.Game.Resources");
        var fontStore = resourceStorage.GetStorageForDirectory("Fonts");

        FontStore.AddFontFamily(fontStore, "Quicksand");
        FontStore.AddFontFamily(fontStore, "Nunito", hasItalics: true);
        FontStore.AddFontFamily(fontStore, "MPLUSRounded1c", script: FontScript.Japanese);
        FontStore.AddFontFamily(fontStore, "Sarabun", script: FontScript.Thai);

        Add(new CursorContainer());
    }

    protected override Assembly ResourceAssembly => typeof(YuukiTestApp).Assembly;
    protected override string ResourceRootNamespace => "Yuuki.Game.Tests.Resources";

    protected override IEnumerable<(Assembly Assembly, string RootNamespace)> AdditionalResourceAssemblies
        => [(YuukiResourceAssembly.ResourceAssembly, "Yuuki.Game.Resources")];
}
