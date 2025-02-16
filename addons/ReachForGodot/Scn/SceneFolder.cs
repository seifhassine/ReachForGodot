namespace RFG;

using System;
using System.Diagnostics;
using Godot;
using RszTool;

[GlobalClass, Tool]
public partial class SceneFolder : RszContainerNode
{
    public Node? FolderContainer { get; private set; }

    [ExportToolButton("Regenerate tree")]
    private Callable BuildTreeButton => Callable.From(BuildTree);

    [ExportToolButton("Regenerate tree + Children")]
    private Callable BuildFullTreeButton => Callable.From(BuildTreeDeep);

    public override void Clear()
    {
        FolderContainer = null;
        base.Clear();
    }

    public void AddFolder(SceneFolder folder)
    {
        if (FolderContainer == null) {
            AddChild(FolderContainer = new Node() { Name = "Folders" });
            FolderContainer.Owner = this;
        }
        FolderContainer.AddChild(folder);
        folder.Owner = Owner ?? this;
    }

    public void BuildTree()
    {
        using var conv = new GodotScnConverter(ReachForGodot.GetAssetConfig(Game!)!, false);
        conv.GenerateSceneTree(this);
    }

    public void BuildTreeDeep()
    {
        using var conv = new GodotScnConverter(ReachForGodot.GetAssetConfig(Game!)!, true);
        conv.GenerateSceneTree(this);
    }
}