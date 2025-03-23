namespace ReaGE;

using Godot;

[GlobalClass, Tool, Icon("res://addons/ReachForGodot/icons/gear.png")]
public partial class GameObject : Node3D, ISerializationListener, ICloneable
{
    [Export] public SupportedGame Game { get; set; }
    [Export] public string Uuid { get; set; } = "00000000-0000-0000-0000-000000000000";
    [Export] public string? Prefab { get; set; }
    [Export] public string OriginalName { get; set; } = string.Empty;
    [Export] public REObject? Data { get; set; }
    private Godot.Collections.Array<REComponent> _components = null!;
    [Export]
    public Godot.Collections.Array<REComponent> Components {
        get => _components;
        set {
            _components = value;
            foreach (var comp in value) {
                if (comp != null) {
                    comp.GameObject = this;
                    comp.Game = Game;
                    if (comp.IsEmpty && !string.IsNullOrEmpty(comp.Classname)) {
                        comp.ResetProperties();
                    }
                }
            }
        }
    }

    public Guid ObjectGuid => System.Guid.TryParse(Uuid, out var guid) ? guid : Guid.Empty;
    public SceneFolder? ParentFolder => this.FindNodeInParents<SceneFolder>();

    public IEnumerable<GameObject> Children => this.FindChildrenByType<GameObject>();
    public IEnumerable<GameObject> AllChildren => this.FindChildrenByType<GameObject>().SelectMany(c => new[] { c }.Concat(c.AllChildren));
    public IEnumerable<GameObject> AllChildrenIncludingSelf => new[] { this }.Concat(AllChildren);

    public string Path => this is PrefabNode pfb
            ? pfb.Asset?.AssetFilename ?? SceneFilePath
            : Owner is SceneFolder scn
                ? $"{scn.Path}:/{scn.GetPathTo(this)}"
                : Owner is PrefabNode pfbParent
                    ? $"{pfbParent.Path}:/{pfbParent.GetPathTo(this)}"
                    : Owner != null ? Owner.GetPathTo(this) : Name;

    private static readonly REFieldAccessor UpdateField = new REFieldAccessor("Update").WithConditions("v2");
    private static readonly REFieldAccessor DrawField = new REFieldAccessor("Draw").WithConditions("v3");

    public override void _EnterTree()
    {
        Components ??= new();
        if (Game == SupportedGame.Unknown) {
            Game = (GetParent() as GameObject)?.Game ?? (GetParent() as SceneFolder)?.Game ?? SupportedGame.Unknown;
        }
        if (Game != SupportedGame.Unknown && Data == null) {
            Data = new REObject(Game, "via.GameObject");
            Data.ResetProperties();
        }
        UpdateComponentGameObjects();
    }

    public void Clear()
    {
        this.ClearChildren();
        Components?.Clear();
    }

    public int GetChildDeduplicationIndex(string name, GameObject? relativeTo)
    {
        int i = 0;
        foreach (var child in Children) {
            if (child.OriginalName == name) {
                if (relativeTo == child) {
                    return i;
                }
                i++;
            }
        }
        return i;
    }

    public GameObject? GetChild(string name, int deduplicationIndex)
    {
        var dupesFound = 0;
        foreach (var child in Children) {
            if (child.OriginalName == name) {
                if (dupesFound >= deduplicationIndex) {
                    return child;
                }

                dupesFound++;
            }
        }

        return null;
    }

    public void PreExport()
    {
        if (Data == null) {
            Data = new REObject(Game, "via.GameObject");
            Data.ResetProperties();
            Data.SetField(DrawField, true);
            Data.SetField(UpdateField, true);
            Data.SetField(Data.TypeInfo.Fields[4], -1f);
        }

        if (string.IsNullOrEmpty(OriginalName)) {
            OriginalName = Name;
        }

        Data.SetField(Data.TypeInfo.Fields[0], OriginalName);
        if (!HasComponent("via.Transform")) {
            Components ??= new();
            var tr = new RETransformComponent(Game, "via.Transform") { GameObject = this, ResourceName = "via.Transform" };
            tr.ResetProperties();
            Components.Insert(0, tr);
        }

        foreach (var comp in Components) {
            comp.PreExport();
        }

        foreach (var child in Children) {
            child.PreExport();
        }
    }

    public void AddComponent(REComponent component)
    {
        Components ??= new();
        Components.Add(component);
    }

    object ICloneable.Clone() => this.Clone();
    public GameObject Clone()
    {
        var clone = RecursiveClone();
        return clone;
    }

    private GameObject RecursiveClone()
    {
        // If it looks stupid that we're doing this manually instead of calling Duplicate(), that's because it is.
        // The issue is that Godot's Duplicate somehow modifies the original node's data to point to the clone.
        // Maybe it's just non-exported fields? Either way, doing it manually to be more reliable.
        var clone = CloneSelf();
        foreach (var child in GetChildren()) {
            if (child is ICloneable cloneable) {
                var childClone = (Node)cloneable.Clone();
                clone.AddChild(childClone);
            } else {
                clone.AddChild(child.Duplicate());
            }
        }
        return clone;
    }

    private GameObject CloneSelf()
    {
        var clone = new GameObject() {
            Name = Name,
            Game = Game,
            OriginalName = OriginalName,
            Uuid = Guid.NewGuid().ToString(),
            Data = Data?.Duplicate(true) as REObject,
            Components = new Godot.Collections.Array<REComponent>(),
            Prefab = Prefab,
        };
        clone.Transform = Transform;
        foreach (var comp in Components) {
            clone.Components.Add(comp.Clone(clone));
        }
        return clone;
    }

    public REComponent? GetComponent(string classname)
    {
        return Components?.FirstOrDefault(x => x.Classname == classname);
    }

    public TComponent? GetComponent<TComponent>() where TComponent : REComponent
    {
        return Components?.OfType<TComponent>().FirstOrDefault();
    }

    public REComponent? GetComponentInChildren(string classname)
    {
        return Components?.FirstOrDefault(x => x.Classname == classname)
            ?? AllChildren.Select(ch => ch.GetComponent(classname)).FirstOrDefault(c => c != null);
    }

    public TComponent? GetComponentInChildren<TComponent>() where TComponent : REComponent
    {
        return GetComponent<TComponent>()
            ?? AllChildren.Select(ch => ch.GetComponent<TComponent>()).FirstOrDefault(c => c != null);
    }

    public bool HasComponent(string classname)
    {
        return Components?.Any(x => x.Classname == classname) == true;
    }

    public override string ToString()
    {
        if (GetParent() is GameObject parent) {
            var dedupId = parent.GetChildDeduplicationIndex(OriginalName, this);
            if (dedupId > 0) return $"{OriginalName} #{dedupId}";
        }

        return OriginalName;
    }

    public void OnBeforeSerialize()
    {
    }

    public void OnAfterDeserialize()
    {
        UpdateComponentGameObjects();
    }

    public Aabb CalculateBounds()
    {
        Aabb bounds = new Aabb();
        Vector3 origin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);

        Components ??= new();
        UpdateComponentGameObjects();
        foreach (var vis in Components.OfType<IVisualREComponent>()) {
            var compBounds = vis.GetBounds();
            if (compBounds.Size.IsZeroApprox()) {
                origin = compBounds.Position;
            } else {
                bounds = bounds.Size.IsZeroApprox() ? compBounds : bounds.Merge(compBounds);
            }
        }

        foreach (var child in Children) {
            var childBounds = child.CalculateBounds();
            if (!childBounds.Size.IsZeroApprox()) {
                var transformedChildBounds = Transform * childBounds;
                bounds = bounds.Size.IsZeroApprox() ? transformedChildBounds : bounds.Merge(transformedChildBounds);
            }
        }

        if (bounds.Size.IsZeroApprox()) {
            if (!bounds.Position.IsZeroApprox()) {
                return bounds;
            }

            return new Aabb(origin.X == float.MaxValue ? Position : origin, Vector3.Zero);
        }

        return bounds;
    }

    private void UpdateComponentGameObjects()
    {
        foreach (var comp in Components) {
            comp.GameObject = this;
        }
    }
}
