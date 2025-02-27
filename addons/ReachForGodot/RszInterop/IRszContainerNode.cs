namespace RGE;

using System;
using Godot;

public interface IRszContainerNode
{
    public SupportedGame Game { get; set; }
    public AssetReference? Asset { get; set; }
    public REResource[]? Resources { get; set; }

    public bool IsEmpty { get; }

    public void AddGameObject(REGameObject gameObject, Node? parent)
    {
        if (parent != null) {
            parent.AddUniqueNamedChild(gameObject);
        } else {
            (this as Node)?.AddUniqueNamedChild(gameObject);
        }

        gameObject.Owner = this as Node;
    }

    public T? FindResource<T>(string? filepath) where T : REResource
    {
        if (Resources == null || string.IsNullOrEmpty(filepath)) return null;
        foreach (var res in Resources) {
            if (res is T cast && cast.Asset?.IsSameAsset(filepath) == true) {
                return cast;
            }
        }
        return null;
    }
}