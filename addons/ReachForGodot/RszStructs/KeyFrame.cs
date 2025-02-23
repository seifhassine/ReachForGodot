namespace RGE;

using Godot;

public partial class KeyFrame : Resource
{
    [Export] public float value;
    [Export] public uint time_type;
    [Export] public uint inNormal;
    [Export] public uint outNormal;

    public static implicit operator KeyFrame(RszTool.via.KeyFrame rszValue) => new KeyFrame() {
        value = rszValue.value,
        time_type = rszValue.time_type,
        inNormal = rszValue.inNormal,
        outNormal = rszValue.outNormal,
    };

    public RszTool.via.KeyFrame ToRsz() => new() {
        value = value,
        time_type = time_type,
        inNormal = inNormal,
        outNormal = outNormal,
    };
}
