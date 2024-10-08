using System;
using Godot;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

public static class Utils
{
    public static void Error(string message)
    {
        OS.Alert(message, "Error!");
        GD.PushError(message);
    }
    public static bool Assert(bool condition, string message)
    {
        if (!condition)
        {
            Error(message);
            throw new Exception();
        }

        return condition;
    }
    
    public static readonly RandomNumberGenerator Rng = new();

    public static Godot.Vector2 To2D(this Godot.Vector3 vec)
    {
        return new Godot.Vector2(vec.X, vec.Z);
    }

    public static Godot.Vector3 To3D(this Godot.Vector2 vec)
    {
        return new Godot.Vector3(vec.X, 0.0f, vec.Y);
    }
    
    public static Vector2 To2D(this Vector3 vec)
    {
        return new Vector2(vec.X, vec.Z);
    }
    
    public static Vector3 To3D(this Vector2 vec)
    {
        return new Vector3(vec.X, 0.0f, vec.Y);
    }

    public static void GlobalPosition(this Node3D spatial, Godot.Vector3 position)
    {
        spatial.GlobalTransform = new Transform3D(spatial.GlobalTransform.Basis, position);
    }

    public static void GlobalPosition(this Node3D spatial, Godot.Vector2 position)
    {
        Godot.Vector3 pos = position.To3D();
        spatial.GlobalTransform = new Transform3D(spatial.GlobalTransform.Basis, pos);
    }

    public static float RandfUnit()
    {
        return Rng.RandfRange(-1.0f, 1.0f);
    }

    public static Godot.Vector2 RandV2()
    {
        return new Godot.Vector2(RandfUnit(), RandfUnit()).Normalized();
    }

    public static double Ease_CubicInOut(double t)
    {
        return t < 0.5f
            ? 4.0f * t * t * t
            : 0.5f * Mathf.Pow(2.0f * t - 2.0f, 3.0f) + 1.0f;
    }
    
    public static double Ease_CubicOut(double t)
    {
        return 1 - Mathf.Pow(1 - t, 3);
    }

    public static Godot.Vector2 RandPointOnEdge(this Rect2 rect)
    {
        Godot.Vector2 r = new(Rng.Randf(), Rng.Randf());
        if (Rng.Randf() > 0.5f)
        {
            r.X = Mathf.Floor(Rng.Randf() + 0.5f);
        }
        else
        {
            r.Y = Mathf.Floor(Rng.Randf() + 0.5f);
        }

        return rect.Position + r * rect.Size;
    }

    public static Vector2 Limit(ref this Vector2 vec, float vMax)
    {
        float length = vec.Length();
        if (length == 0.0f)
        {
            return vec;
        }

        float i = vMax / length;
        i = Mathf.Min(i, 1.0f);
        vec *= i;
        return vec;
    }

    public static Godot.Vector2 ToGodot(this Vector2 vec)
    {
        return new Godot.Vector2(vec.X, vec.Y);
    }
    
    public static Godot.Vector3 ToGodot(this Vector3 vec)
    {
        return new Godot.Vector3(vec.X, vec.Y, vec.Z);
    }
    
    public static Vector2 ToNumerics(this Godot.Vector2 vec)
    {
        return new Vector2(vec.X, vec.Y);
    }
    
    public static Vector3 ToNumerics(this Godot.Vector3 vec)
    {
        return new Vector3(vec.X, vec.Y, vec.Z);
    }
    
    public static float AngleToY(this Vector2 vec)
    {
        return (float) Math.Atan2(vec.X, vec.Y);
    }
    
    public static float AngleToY(this Godot.Vector2 vec)
    {
        return (float) Math.Atan2(vec.X, vec.Y);
    }
    
    public static float AngleTo(this Vector2 v1, Vector2 v2)
    {
        return Mathf.Atan2(v1.Cross(v2), Vector2.Dot(v1, v2));
    }

    public static float Cross(this Vector2 v1, Vector2 v2)
    {
        return (v1.X * v2.Y) - (v1.Y * v2.X);
    }

    public static Vector2 Rot90(this Vector2 v)
    {
        return new Vector2(-v.Y, v.X);
    }

    public static Vector2 NormalizeSafe(this Vector2 vec)
    {
        if (Mathf.Abs(vec.X) < 2e-10f && Mathf.Abs(vec.Y) < 2e-10f) // TODO: is this right?
            return Vector2.Zero;
        return Vector2.Normalize(vec);
    }

    public static Vector2 SetMag(ref this Vector2 vec, float vMag)
    {
        vec = vec.NormalizeSafe() * vMag;
        return vec;
    }

    public static Vector2 ParallelComponent(this Vector2 vec, Vector2 unitBasis)
    {
        float projection = Vector2.Dot(vec, unitBasis);
        return unitBasis * projection;
    }

    public static Vector2 PerpendicularComponent(this Vector2 vec, Vector2 unitBasis)
    {
        return vec - vec.ParallelComponent(unitBasis);
    }
    
    public static Vector2 LocaliseDirection(Vector2 globalDirection, Vector2 forward, Vector2 side)
    {
        return new Vector2(Vector2.Dot(globalDirection, side),
            Vector2.Dot(globalDirection, forward));
    }
    
    public static Vector2 LocalisePosition(Vector2 globalPosition, Vector2 localPosition, Vector2 forward, Vector2 side)
    {
        Vector2 globalOffset = globalPosition - localPosition;
        return LocaliseDirection(globalOffset, forward, side);
    }
    
    public static Vector2 LocaliseOffset(Vector2 globalOffset, Vector2 forward, Vector2 side)
    {
        return LocaliseDirection(globalOffset, forward, side);
    }

    public static Vector2 GlobaliseOffset(Vector2 localOffset, Vector2 forward, Vector2 side)
    {
        return localOffset.X * side + localOffset.Y * forward;
    }

    public static void Line(Vector3 p1, Vector3 p2, Color col, ref int v, ref int i, Godot.Vector3[] verts, Color[] cols, int[] indices)
    {
        cols[v] = col;
        verts[v] = p1.ToGodot();
        indices[i++] = v++;
        cols[v] = col;
        verts[v] = p2.ToGodot();
        indices[i++] = v++;
    }
    
    public static void Circle(Vector3 pos, int segments, float radius, Color col, ref int v, ref int i, Godot.Vector3[] verts, Color[] cols, int[] indices)
    {
        for (int s = 0; s < segments; s++)
        {
            cols[v + s] = col;
            float rad = Mathf.Pi * 2.0f * ((float) s / segments);
            Vector3 vert = pos + new Vector3(Mathf.Sin(rad), 0.0f, Mathf.Cos(rad)) * radius;
            verts[v + s] = vert.ToGodot();
            indices[i++] = v + s;
            indices[i++] = v + (s + 1) % segments;
        }

        v += segments;
    }
    
    public static void CircleArc(Vector3 pos, int segments, float radius, float arcDeg, Vector2 heading, Color col, ref int v, ref int i, Godot.Vector3[] verts, Color[] cols, int[] indices)
    {
        if (arcDeg >= 360.0f)
        {
            Circle(pos, segments, radius, col,ref v, ref i, verts, cols, indices);
            return;
        }
        
        float segmentArc = Mathf.DegToRad(arcDeg / (segments - 1));
        float headingAngle = heading.AngleTo(-Vector2.UnitY) - Mathf.DegToRad(arcDeg * 0.5f);
        cols[v] = col;
        verts[v] = pos.ToGodot();
        v++;
        for (int s = 0; s < segments; s++)
        {
            cols[v + s] = col;
            float rad = headingAngle + segmentArc * s;
            Vector3 vert = pos + new Vector3(Mathf.Sin(rad), 0.0f, Mathf.Cos(rad)) * radius;
            verts[v + s] = vert.ToGodot();
            indices[i++] = (v - 1 + (segments + s - 1) % segments);
            indices[i++] = (v - 1 + (segments + s) % segments);
        }
        v += segments;
    }

    public static Godot.Vector2 Vector2FromString(string s)
    {
        string[] split = s.Split('(')[1].Split(')')[0].Split(',');
        return new Godot.Vector2(split[0].ToFloat(), split[1].ToFloat());
    }
    
    public static float Remap(float x, float a, float b, float c, float d)
    {
        return (((x - a) / (b - a)) * (d - c)) + c;
    }
    
    public static bool Null(this Object instance)
    {
        return instance == null;// || instance.native == IntPtr.Zero;
    }
}