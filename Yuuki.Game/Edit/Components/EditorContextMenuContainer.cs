// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Edit.Components;

public partial class EditorContextMenuContainer : ContextMenuContainer
{
    protected override Menu CreateMenu() => new YuukiMenu(FlowDirection.Vertical);
}
