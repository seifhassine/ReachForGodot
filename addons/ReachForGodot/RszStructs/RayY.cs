namespace RGE;

using System;
using Godot;

public partial class RayY : Resource
{
    [Export] public Vector3 from;
    [Export] public float dir;

    public static implicit operator RayY(RszTool.via.RayY rszValue) => new RayY() {
        from = rszValue.from.ToGodot(),
        dir = rszValue.dir,
    };
}
