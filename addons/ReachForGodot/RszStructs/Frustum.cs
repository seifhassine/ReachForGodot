namespace RGE;

using System;
using Godot;

public partial class Frustum : Resource
{
    [Export] public Plane? plane0;
    [Export] public Plane? plane1;
    [Export] public Plane? plane2;
    [Export] public Plane? plane3;
    [Export] public Plane? plane4;
    [Export] public Plane? plane5;

    public static implicit operator Frustum(RszTool.via.Frustum rszValue) => new Frustum() {
        plane0 = (Plane)rszValue.plane0,
        plane1 = (Plane)rszValue.plane1,
        plane2 = (Plane)rszValue.plane2,
        plane3 = (Plane)rszValue.plane3,
        plane4 = (Plane)rszValue.plane4,
        plane5 = (Plane)rszValue.plane5,
    };
}
