namespace RGE;

using System.Threading.Tasks;
using Godot;
using RszTool;

[GlobalClass, Tool]
public partial class REComponentPlaceholder : REComponent
{
    public REComponentPlaceholder() { }
    public REComponentPlaceholder(SupportedGame game, string classname) : base(game, classname) {}

    public override Task Setup(IRszContainerNode root, REGameObject gameObject, RszInstance rsz, RszImportType importType) => Task.CompletedTask;
}
