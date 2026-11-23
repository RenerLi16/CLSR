using System.Collections.Generic;
using UnityEngine;

/// <summary>Arc-length sampled 2D polyline (x, z) with Catmull-Rom smoothing through control points.</summary>
internal sealed class Polyline
{
    public readonly List<Vector2> P = new List<Vector2>();
    public readonly List<float> S = new List<float>();
    public int Count => P.Count;
    public float Length => S.Count > 0 ? S[S.Count - 1] : 0f;

    public static Polyline Smooth(IList<Vector2> ctrl, float step)
    {
        var dense = new List<Vector2>();
        int n = ctrl.Count;
        for (int i = 0; i < n - 1; i++)
        {
            Vector2 p0 = i > 0 ? ctrl[i - 1] : 2f * ctrl[0] - ctrl[1];
            Vector2 p1 = ctrl[i], p2 = ctrl[i + 1];
            Vector2 p3 = i + 2 < n ? ctrl[i + 2] : 2f * ctrl[n - 1] - ctrl[n - 2];
            int sub = Mathf.Max(6, Mathf.CeilToInt(Vector2.Distance(p1, p2) / (step * 0.2f)));
            for (int k = 0; k < sub; k++) dense.Add(CatmullRom(p0, p1, p2, p3, k / (float)sub));
        }
        dense.Add(ctrl[n - 1]);
        return Resample(dense, step);
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    public static Polyline Resample(List<Vector2> pts, float step)
    {
        var cum = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
        float total = cum[pts.Count - 1];
        int count = Mathf.Max(2, Mathf.RoundToInt(total / step) + 1);
        var line = new Polyline();
        int j = 0;
        for (int k = 0; k < count; k++)
        {
            float s = total * k / (count - 1);
            while (j < pts.Count - 2 && cum[j + 1] < s) j++;
            float seg = cum[j + 1] - cum[j];
            float t = seg > 1e-6f ? (s - cum[j]) / seg : 0f;
            line.P.Add(Vector2.Lerp(pts[j], pts[j + 1], Mathf.Clamp01(t)));
            line.S.Add(s);
        }
        line.P[0] = pts[0];
        line.P[count - 1] = pts[pts.Count - 1];
        return line;
    }

    public Vector2 Tangent(int i)
    {
        int a = Mathf.Max(0, i - 1), b = Mathf.Min(Count - 1, i + 1);
        Vector2 d = P[b] - P[a];
        return d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.up;
    }

    /// <summary>Right-hand normal (east when walking north).</summary>
    public Vector2 Normal(int i)
    {
        Vector2 t = Tangent(i);
        return new Vector2(t.y, -t.x);
    }

    public int NearestIndex(Vector2 q)
    {
        int best = 0;
        float bd = float.MaxValue;
        for (int i = 0; i < Count; i++)
        {
            float d = (P[i] - q).sqrMagnitude;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>Distance and side for polylines whose z increases monotonically (river, ridges, escarpment).</summary>
    public float NearestMonotone(Vector2 q, out int index, out float side)
    {
        int lo = 0, hi = Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (P[mid].y < q.y) lo = mid; else hi = mid;
        }
        int best = lo;
        float bd = (P[lo] - q).sqrMagnitude;
        float bdist = Mathf.Sqrt(bd);
        for (int dir = -1; dir <= 1; dir += 2)
        {
            for (int i = dir < 0 ? lo : lo + 1; i >= 0 && i < Count; i += dir)
            {
                float dz = P[i].y - q.y;
                if (dz * dz > bd && Mathf.Abs(dz) > bdist + 2f) break;
                float d = (P[i] - q).sqrMagnitude;
                if (d < bd) { bd = d; best = i; bdist = Mathf.Sqrt(d); }
            }
        }
        // refine on adjacent segments
        float dist = bdist;
        index = best;
        Vector2 foot = P[best];
        for (int s = Mathf.Max(0, best - 1); s < Mathf.Min(Count - 1, best + 1); s++)
        {
            Vector2 a = P[s], b = P[s + 1], ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            Vector2 f = a + ab * t;
            float d = Vector2.Distance(q, f);
            if (d < dist) { dist = d; foot = f; index = t < 0.5f ? s : s + 1; }
        }
        Vector2 tan = Tangent(index);
        float cross = tan.x * (q.y - foot.y) - tan.y * (q.x - foot.x);
        side = cross > 0f ? -1f : 1f;
        return dist;
    }
}

/// <summary>Collects positions, normals, colours and UVs for procedurally built meshes.</summary>
internal sealed class MeshBuilder
{
    public readonly List<Vector3> V = new List<Vector3>();
    public readonly List<Vector3> N = new List<Vector3>();
    public readonly List<Color> C = new List<Color>();
    public readonly List<Vector2> UV = new List<Vector2>();
    public readonly List<List<int>> Sub = new List<List<int>> { new List<int>() };
    public int Current;

    public int VertexCount => V.Count;

    public void UseSubmesh(int i)
    {
        while (Sub.Count <= i) Sub.Add(new List<int>());
        Current = i;
    }

    public int Add(Vector3 p, Vector3 n, Color c, Vector2 uv)
    {
        V.Add(p); N.Add(n); C.Add(c); UV.Add(uv);
        return V.Count - 1;
    }

    public void Tri(int a, int b, int c) { var l = Sub[Current]; l.Add(a); l.Add(b); l.Add(c); }

    public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

    /// <summary>Oriented box with per-face normals; UVs in meters along the box's local axes / uvScale.</summary>
    public void Box(Vector3 center, Vector3 size, Quaternion rot, Color col, float uvScale = 1f, Color? topCol = null)
    {
        Vector3 h = size * 0.5f;
        for (int f = 0; f < 6; f++)
        {
            int axis = f >> 1;
            float sign = (f & 1) == 0 ? 1f : -1f;
            Vector3 n = Vector3.zero; n[axis] = sign;
            int ua = axis == 0 ? 2 : 0, va = axis == 1 ? 2 : 1;
            if (axis == 2) { ua = 0; va = 1; }
            // u along the box's longest in-face axis so wood grain follows the plank
            if (size[va] > size[ua]) { int t = ua; ua = va; va = t; }
            Vector3 du = Vector3.zero; du[ua] = h[ua];
            Vector3 dv = Vector3.zero; dv[va] = h[va];
            Vector3 fc = n * h[axis];
            if (Vector3.Dot(Vector3.Cross(du, dv), n) < 0f) dv = -dv;
            Color c = (topCol.HasValue && axis == 1 && sign > 0f) ? topCol.Value : col;
            Vector3 wn = rot * n;
            int b = VertexCount;
            for (int k = 0; k < 4; k++)
            {
                float su = (k == 0 || k == 3) ? -1f : 1f, sv = k < 2 ? -1f : 1f;
                Vector3 lp = fc + du * su + dv * sv;
                Add(center + rot * lp, wn, c, new Vector2((su * h[ua] + h[ua]) / uvScale, (sv * h[va] + h[va]) / uvScale));
            }
            Tri(b, b + 1, b + 2); Tri(b, b + 2, b + 3);
        }
    }

    /// <summary>Cylinder / log between a and b, optional end caps, UV u around and v along (meters).</summary>
    public void Cylinder(Vector3 a, Vector3 b, float ra, float rb, int sides, Color col, bool caps = true, Color? capCol = null)
    {
        Vector3 axis = b - a;
        float len = axis.magnitude;
        Vector3 dir = axis / Mathf.Max(1e-5f, len);
        Vector3 side1 = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 side2 = Vector3.Cross(dir, side1);
        int b0 = VertexCount;
        for (int i = 0; i <= sides; i++)
        {
            float ang = i / (float)sides * Mathf.PI * 2f;
            Vector3 r = side1 * Mathf.Cos(ang) + side2 * Mathf.Sin(ang);
            float u = i / (float)sides * Mathf.PI * 2f * (ra + rb) * 0.5f;
            Add(a + r * ra, r, col, new Vector2(0f, u));
            Add(b + r * rb, r, col, new Vector2(len, u));
        }
        for (int i = 0; i < sides; i++)
        {
            int p = b0 + i * 2;
            Tri(p, p + 3, p + 1); Tri(p, p + 2, p + 3);
        }
        if (!caps) return;
        Color cc = capCol ?? col;
        for (int end = 0; end < 2; end++)
        {
            Vector3 c = end == 0 ? a : b;
            float rr = end == 0 ? ra : rb;
            Vector3 n = end == 0 ? -dir : dir;
            int center = Add(c, n, cc, new Vector2(0.5f, 0.5f));
            int first = VertexCount;
            for (int i = 0; i <= sides; i++)
            {
                float ang = i / (float)sides * Mathf.PI * 2f;
                Vector3 r = side1 * Mathf.Cos(ang) + side2 * Mathf.Sin(ang);
                Add(c + r * rr, n, cc, new Vector2(0.5f + Mathf.Cos(ang) * 0.5f, 0.5f + Mathf.Sin(ang) * 0.5f));
            }
            for (int i = 0; i < sides; i++)
            {
                if (end == 0) Tri(center, first + i + 1, first + i);
                else Tri(center, first + i, first + i + 1);
            }
        }
    }

    public void Append(MeshBuilder o, Matrix4x4 m)
    {
        int b = VertexCount;
        for (int i = 0; i < o.V.Count; i++)
        {
            V.Add(m.MultiplyPoint3x4(o.V[i]));
            N.Add(m.MultiplyVector(o.N[i]).normalized);
            C.Add(o.C[i]);
            UV.Add(o.UV[i]);
        }
        for (int s = 0; s < o.Sub.Count; s++)
        {
            UseSubmesh(s);
            foreach (int idx in o.Sub[s]) Sub[s].Add(idx + b);
        }
        Current = 0;
    }

    public Mesh ToMesh(string name, bool recalcNormals = false)
    {
        var mesh = new Mesh { name = name };
        if (V.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(V);
        mesh.SetColors(C);
        mesh.SetUVs(0, UV);
        mesh.subMeshCount = Sub.Count;
        for (int s = 0; s < Sub.Count; s++) mesh.SetTriangles(Sub[s], s, false);
        if (recalcNormals) mesh.RecalculateNormals(); else mesh.SetNormals(N);
        mesh.RecalculateBounds();
        return mesh;
    }
}
