namespace RFG;

using System;
using System.Globalization;
using Godot;
using RFG;
using RszTool;
using RszTool.via;

public static class RszTypeConverter
{
    private static Type baseList = typeof(List<>);

    public static Variant FromRszValue(REField field, object value)
    {
        if (field.RszField.array) {
            var type = value.GetType();
            object[] arr;
            if (type.IsArray) {
                arr = (object[])value;
            } else if (type.IsGenericType && type.GetGenericTypeDefinition() == baseList) {
                arr = (object[])type.GetMethod("ToArray", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public)!.Invoke(value, Array.Empty<object?>())!;
            } else {
                GD.Print("Unhandled array type " + type.FullName);
                arr = Array.Empty<object>();
            }

            if (field.RszField.type == RszFieldType.Object) {
                // TODO
                return new Variant();
            } else {
                return new Godot.Collections.Array(arr.Select(v => FromRszValueSingleValue(field, v)));
            }
        }

        return FromRszValueSingleValue(field, value);
    }

    private static Variant FromRszValueSingleValue(REField field, object value)
    {
        switch (field.RszField.type) {
            case RszFieldType.Sfix:
                return ((sfix)value).v;
            case RszFieldType.Sfix2:
                return new Vector2I(
                    ((Sfix2)value).x.v,
                    ((Sfix2)value).y.v
                );
            case RszFieldType.Sfix3:
                return new Vector3I(
                    ((Sfix3)value).x.v,
                    ((Sfix3)value).y.v,
                    ((Sfix3)value).z.v
                );
            case RszFieldType.Sfix4:
                return new Vector4I(
                    ((Sfix4)value).x.v,
                    ((Sfix4)value).y.v,
                    ((Sfix4)value).z.v,
                    ((Sfix4)value).w.v
                );
            case RszFieldType.Int2:
                return new Vector2I(
                    ((Int2)value).x,
                    ((Int2)value).y
                );
            case RszFieldType.Uint2:
                return new Vector2I(
                    (int)((Uint2)value).x,
                    (int)((Uint2)value).y
                );
            case RszFieldType.Int3:
                return new Vector3I(
                    ((Int3)value).x,
                    ((Int3)value).y,
                    ((Int3)value).z
                );
            case RszFieldType.Uint3:
                return new Vector3I(
                    (int)((Uint3)value).x,
                    (int)((Uint3)value).y,
                    (int)((Uint3)value).z
                );
            case RszFieldType.Int4:
                return new Vector4I(
                    ((Int4)value).x,
                    ((Int4)value).y,
                    ((Int4)value).z,
                    ((Int4)value).w
                );
            case RszFieldType.Uint4:
                return new Vector4I(
                    (int)((Uint4)value).x,
                    (int)((Uint4)value).y,
                    (int)((Uint4)value).z,
                    (int)((Uint4)value).w
                );
            case RszFieldType.S64:
                return (long)value;
            case RszFieldType.U64:
                return (ulong)value;
            case RszFieldType.Data:
                return (byte[])value;
            case RszFieldType.S8:
            case RszFieldType.S16:
            case RszFieldType.S32:
            case RszFieldType.U8:
            case RszFieldType.U16:
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            case RszFieldType.U32:
                return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
            case RszFieldType.F16:
            case RszFieldType.F32:
            case RszFieldType.F64:
                return Convert.ToSingle(value, CultureInfo.InvariantCulture);
            case RszFieldType.String:
                return (value as string)!;
            case RszFieldType.Bool:
                return (bool)value;
            case RszFieldType.Vec2:
            case RszFieldType.Float2:
            case RszFieldType.Point:
                return ((System.Numerics.Vector2)value).ToGodot();
            case RszFieldType.Vec3:
            case RszFieldType.Float3:
            case RszFieldType.Position:
                return ((System.Numerics.Vector3)value).ToGodot();
            case RszFieldType.Vec4:
            case RszFieldType.Float4:
                return ((System.Numerics.Vector4)value).ToGodot();
            case RszFieldType.Quaternion:
                return ((System.Numerics.Quaternion)value).ToGodot();
            case RszFieldType.Color:
                return new Godot.Color(((RszTool.via.Color)value).rgba);
            case RszFieldType.Guid:
            case RszFieldType.Uri:
            case RszFieldType.GameObjectRef:
                return ((Guid)value).ToString();
            case RszFieldType.AABB:
                var min = ((RszTool.via.AABB)value).minpos.ToGodot();
                return new Aabb(
                    min,
                    ((RszTool.via.AABB)value).maxpos.ToGodot() - min
                );
        }

        GD.PrintErr("Unhandled conversion for rsz type " + field.RszField.type + " with value type " + value.GetType().FullName);
        return new Variant();
    }

    public static Vector2 ToGodot(this System.Numerics.Vector2 val) => new Vector2(val.X, val.Y);
    public static Vector3 ToGodot(this System.Numerics.Vector3 val) => new Vector3(val.X, val.Y, val.Z);
    public static Vector4 ToGodot(this System.Numerics.Vector4 val) => new Vector4(val.X, val.Y, val.Z, val.W);
    public static Quaternion ToGodot(this System.Numerics.Quaternion val) => new Quaternion(val.X, val.Y, val.Z, val.W);
}