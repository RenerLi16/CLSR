using System.Collections.Generic;
using UnityEngine;

// Procedural geometry for the Cedar Valley landscape. All meshes are built in world meters.
internal static class CedarMeshes
{
    // ------------------------------------------------------------------ terrain
    public const int ChunkCellsX = 76, ChunkCellsZ = 80;

    public static List<Mesh> TerrainChunks(CedarModel m, bool coarse = false)
    {
        var meshes = new List<Mesh>();
        int cellsX = m.NX - 1, cellsZ = m.NZ - 1;
        for (int cz = 0; cz < cellsZ; cz += ChunkCellsZ)
        for (int cx = 0; cx < cellsX; cx += ChunkCellsX)
        {
            int ex = Mathf.Min(cellsX, cx + ChunkCellsX), ez = Mathf.Min(cellsZ, cz + ChunkCellsZ);
            var mb = new MeshBuilder();
            if (coarse)
            {
                BuildCoarseChunk(m, mb, cx, cz, ex, ez);
                meshes.Add(mb.ToMesh("Cedar overview terrain " + (cx / ChunkCellsX) + "-" + (cz / ChunkCellsZ)));
                continue;
            }
            int w = ex - cx + 1;
            for (int j = cz; j <= ez; j++)
            for (int i = cx; i <= ex; i++)
            {
                int id = m.Idx(i, j);
                mb.Add(new Vector3(m.GX(i), m.Ground[id], m.GZ(j)), GridNormal(m, i, j), m.Paint[id], new Vector2(m.Thin[id], m.Scour[id]));
            }
            for (int j = 0; j < ez - cz; j++)
            for (int i = 0; i < ex - cx; i++)
            {
                int v00 = j * w + i, v10 = v00 + 1, v01 = v00 + w, v11 = v01 + 1;
                mb.Tri(v00, v01, v11);
                mb.Tri(v00, v11, v10);
            }
            meshes.Add(mb.ToMesh("Cedar terrain " + (cx / ChunkCellsX) + "-" + (cz / ChunkCellsZ)));
        }
        return meshes;
    }

    // Decimate 2x2 patches only where the omitted samples stay close to the surface.
    // Boundary and neighboring full-detail samples are shared so neither tier has cracks.
    private static void BuildCoarseChunk(CedarModel m, MeshBuilder mesh, int cx, int cz, int ex, int ez)
    {
        int nx = (ex - cx) / 2, nz = (ez - cz) / 2;
        var fine = new bool[nx, nz];
        float Height(int x, int z) => m.Ground[m.Idx(x, z)];
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            int x = cx + i * 2, z = cz + j * 2;
            float a = Height(x, z), b = Height(x, z + 2), c = Height(x + 2, z + 2), d = Height(x + 2, z);
            int center = m.Idx(x + 1, z + 1);
            Vector2 point = new(m.GX(x + 1), m.GZ(z + 1));
            float river = m.RiverInfo(point, out int riverIndex, out _) - m.River.HalfW[riverIndex];
            const float tolerance = .22f;
            fine[i, j] = river < 12f || m.TrailDist[center] < 8f ||
                         Mathf.Abs(Height(x, z + 1) - (a + b) * .5f) > tolerance ||
                         Mathf.Abs(Height(x + 1, z + 2) - (b + c) * .5f) > tolerance ||
                         Mathf.Abs(Height(x + 2, z + 1) - (c + d) * .5f) > tolerance ||
                         Mathf.Abs(Height(x + 1, z) - (d + a) * .5f) > tolerance ||
                         Mathf.Abs(m.Ground[center] - (a + c) * .5f) > tolerance;
        }
        var vertices = new Dictionary<int, int>();
        int Vertex(int x, int z)
        {
            int id = m.Idx(x, z);
            if (vertices.TryGetValue(id, out int index)) return index;
            index = mesh.Add(new Vector3(m.GX(x), m.Ground[id], m.GZ(z)), GridNormal(m, x, z),
                m.Paint[id], new Vector2(m.Thin[id], m.Scour[id]));
            vertices.Add(id, index);
            return index;
        }
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            int x = cx + i * 2, z = cz + j * 2;
            if (fine[i, j])
            {
                for (int v = z; v < z + 2; v++)
                for (int u = x; u < x + 2; u++)
                    mesh.Quad(Vertex(u, v), Vertex(u, v + 1), Vertex(u + 1, v + 1), Vertex(u + 1, v));
                continue;
            }
            int a = Vertex(x, z), b = Vertex(x, z + 2), c = Vertex(x + 2, z + 2), d = Vertex(x + 2, z);
            bool left = i == 0 || fine[i - 1, j], top = j == nz - 1 || fine[i, j + 1];
            bool right = i == nx - 1 || fine[i + 1, j], bottom = j == 0 || fine[i, j - 1];
            if (!left && !top && !right && !bottom) { mesh.Quad(a, b, c, d); continue; }
            int middle = Vertex(x + 1, z + 1);
            void Edge(int from, int to, bool split, int mx, int mz)
            {
                if (!split) { mesh.Tri(middle, from, to); return; }
                int p = Vertex(mx, mz);
                mesh.Tri(middle, from, p);
                mesh.Tri(middle, p, to);
            }
            Edge(a, b, left, x, z + 1);
            Edge(b, c, top, x + 1, z + 2);
            Edge(c, d, right, x + 2, z + 1);
            Edge(d, a, bottom, x + 1, z);
        }
    }

    private static Vector3 GridNormal(CedarModel m, int i, int j)
    {
        int a = Mathf.Max(0, i - 1), b = Mathf.Min(m.NX - 1, i + 1), c = Mathf.Max(0, j - 1), d = Mathf.Min(m.NZ - 1, j + 1);
        float gx = (m.Ground[m.Idx(b, j)] - m.Ground[m.Idx(a, j)]) / ((b - a) * CedarModel.Step);
        float gz = (m.Ground[m.Idx(i, d)] - m.Ground[m.Idx(i, c)]) / ((d - c) * CedarModel.Step);
        return new Vector3(-gx, 1f, -gz).normalized;
    }

    private const float ApronX = -348f, ApronZ = -270f, ApronStep = 6f;

    private static float ApronHeight(CedarModel m, float x, float z) =>
        x >= CedarModel.X0 && x <= CedarModel.X1 && z >= CedarModel.Z0 && z <= CedarModel.Z1
            ? m.Sample(m.Ground, x, z) : m.OuterSurface(x, z);

    public static float ApronSurface(CedarModel m, float x, float z)
    {
        float a = ApronX + Mathf.Floor((x - ApronX) / ApronStep) * ApronStep;
        float b = ApronZ + Mathf.Floor((z - ApronZ) / ApronStep) * ApronStep;
        float u = (x - a) / ApronStep, v = (z - b) / ApronStep;
        float h = ApronHeight(m, a, b), diagonal = ApronHeight(m, a + ApronStep, b + ApronStep);
        return v > u ? h + v * (ApronHeight(m, a, b + ApronStep) - h) + u * (diagonal - ApronHeight(m, a, b + ApronStep))
                     : h + u * (ApronHeight(m, a + ApronStep, b) - h) + v * (diagonal - ApronHeight(m, a + ApronStep, b));
    }

    /// <summary>Coarse surround stitched to the shared core border.</summary>
    public static Mesh Apron(CedarModel m)
    {
        const float x0 = ApronX, x1 = 348f, z0 = ApronZ, z1 = 510f, st = ApronStep;
        int nx = Mathf.RoundToInt((x1 - x0) / st) + 1, nz = Mathf.RoundToInt((z1 - z0) / st) + 1;
        var mb = new MeshBuilder();
        var h = new float[nx * nz];
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            float x = x0 + i * st, z = z0 + j * st;
            h[j * nx + i] = ApronHeight(m, x, z);
        }
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            float x = x0 + i * st, z = z0 + j * st;
            float hx = h[j * nx + Mathf.Min(nx - 1, i + 1)] - h[j * nx + Mathf.Max(0, i - 1)];
            float hz = h[Mathf.Min(nz - 1, j + 1) * nx + i] - h[Mathf.Max(0, j - 1) * nx + i];
            Vector3 n = new Vector3(-hx, 2f * st, -hz).normalized;
            float slope = Mathf.Sqrt(hx * hx + hz * hz) / (2f * st);
            float rock = Mathf.Clamp01((slope - 0.6f) / 0.35f);
            float edge = m.RiverInfo(new Vector2(x, z), out int k, out _) - m.River.HalfW[k];
            float forest = m.WoodlandClimate(new Vector2(x, z), h[j * nx + i], slope, edge);
            Color paint = new(0f, rock, forest, 1f - .09f * forest);
            // outside the board: high ground is wind-scoured, steep sunny slopes thin out
            Vector2 extra = new Vector2(Mathf.Clamp01(forest * .2f) * CedarNoise.Fbm(x / 13f, z / 13f, 623, 3) * 1.3f,
                                        Mathf.Clamp01((h[j * nx + i] - 26f) / 40f) * .6f);
            if (x >= CedarModel.X0 && x <= CedarModel.X1 && z >= CedarModel.Z0 && z <= CedarModel.Z1)
            {
                int u = Mathf.RoundToInt((x - CedarModel.X0) / CedarModel.Step);
                int v = Mathf.RoundToInt((z - CedarModel.Z0) / CedarModel.Step);
                n = GridNormal(m, u, v);
                paint = m.Paint[m.Idx(u, v)];
                extra = new Vector2(m.Thin[m.Idx(u, v)], m.Scour[m.Idx(u, v)]);
            }
            mb.Add(new Vector3(x, h[j * nx + i], z), n, paint, extra);
        }
        for (int j = 0; j < nz - 1; j++)
        for (int i = 0; i < nx - 1; i++)
        {
            float x = x0 + (i + .5f) * st, z = z0 + (j + .5f) * st;
            if (x > CedarModel.X0 && x < CedarModel.X1 && z > CedarModel.Z0 && z < CedarModel.Z1) continue;
            int v00 = j * nx + i, v10 = v00 + 1, v01 = v00 + nx, v11 = v01 + 1;
            mb.Tri(v00, v01, v11);
            mb.Tri(v00, v11, v10);
        }
        return mb.ToMesh("Cedar terrain apron");
    }

    // ------------------------------------------------------------------ river ice
    public static Mesh RiverIce(CedarModel m)
    {
        RiverModel r = m.River;
        var mb = new MeshBuilder();
        float[] fr = { -1f, -0.9f, -0.72f, -0.4f, 0f, 0.4f, 0.72f, 0.9f, 1f };
        int cols = fr.Length, rows = 0;
        for (int i = 0; i < r.Line.Count; i += 2)
        {
            Vector2 p = r.Line.P[i], n = r.Line.Normal(i);
            float hw = r.HalfW[i], s = r.Line.S[i];
            float lowerB = 1e9f;
            foreach (BridgeModel b in m.Bridges) lowerB = Mathf.Min(lowerB, Mathf.Abs(i - b.RiverIndex));
            float nearBridge = CedarNoise.Gauss(lowerB / 14f);
            for (int c = 0; c < cols; c++)
            {
                float f = fr[c];
                float off = Mathf.Abs(f) >= 1f ? hw + 1.4f : Mathf.Abs(f) >= 0.9f ? hw + 0.25f : hw * Mathf.Abs(f);
                off = off * Mathf.Sign(f) + m.BankOffset(i, Mathf.Sign(f)) * f * Mathf.Abs(f);
                Vector2 q = p + n * off;
                float side = f < 0 ? -1f : 1f;
                // shelf ice: width of the snowed-over edge varies along each bank independently
                float shelf = Mathf.Lerp(0.18f, 0.55f, CedarNoise.Fbm(s / 23f, side * 3f, 701, 3));
                float a = Mathf.Abs(f);
                float snow;
                // the outer edge is fully snowed-over shelf ice, so it meets the bank snow without a visible seam
                if (a >= 0.9f) snow = 1f;
                else if (a > 1f - shelf) snow = Mathf.Lerp(0.3f, 1f, Mathf.InverseLerp(1f - shelf, 0.9f, a));
                else snow = 0.1f + 0.35f * Mathf.Clamp01((CedarNoise.Fbm(q.x / 11f, q.y / 11f, 702, 3) - 0.45f) * 2.5f);
                snow *= 1f - 0.55f * nearBridge * (a < 0.9f ? 1f : 0.3f); // thin refrozen ice where the flow is fast
                float clear = 0.86f + 0.14f * CedarNoise.Fbm(q.x / 14f, q.y / 14f, 703, 2) - 0.12f * nearBridge;
                Color col = new Color(clear * 0.92f, clear * 0.98f, clear, Mathf.Clamp01(snow));
                float y = r.Level[i] + Mathf.SmoothStep(0f, .095f, Mathf.InverseLerp(.62f, 1f, a));
                mb.Add(new Vector3(q.x, y, q.y), Vector3.up, col, new Vector2(f * hw, s));
            }
            rows++;
        }
        for (int j = 0; j < rows - 1; j++)
        for (int c = 0; c < cols - 1; c++)
        {
            int a = j * cols + c, b = a + 1, d = a + cols, e = d + 1;
            // columns run from the west (left) bank to the east bank, rows run north
            mb.Tri(a, d, e);
            mb.Tri(a, e, b);
        }
        return mb.ToMesh("Frozen Cedar River ice");
    }

    // ------------------------------------------------------------------ trail beds
    public static Mesh Trailbed(CedarModel m, TrailModel t)
    {
        var mb = new MeshBuilder();
        float[] fr = { -1f, -0.62f, -0.25f, 0f, 0.25f, 0.62f, 1f };
        int cols = fr.Length;
        int prevRow = -1;
        for (int i = 0; i < t.Line.Count; i++)
        {
            bool skip = t.OnDeck[i];
            if (skip) { prevRow = -1; continue; }
            Vector2 p = t.Line.P[i], n = t.Line.Normal(i);
            float s = t.Line.S[i];
            float hw = 1.48f * (0.88f + 0.24f * CedarNoise.Fbm(s / 6f, 0.3f, 711, 2));
            if (t.Bridge != null)
            {
                Vector2 local = t.Bridge.Local(p);
                if (Mathf.Abs(local.y) < 4f)
                    hw = Mathf.Lerp(1.12f, hw, Mathf.InverseLerp(0f, 7f, Mathf.Abs(local.x) - t.Bridge.HalfSpan));
            }
            int row = mb.VertexCount;
            for (int c = 0; c < cols; c++)
            {
                float f = fr[c];
                float edgeNoise = CedarNoise.Fbm(s / 1.7f, f * 4f, 712, 2);
                float off = f * hw * (Mathf.Abs(f) >= 1f ? 0.92f + 0.3f * edgeNoise : 1f);
                Vector2 q = p + n * off;
                float alpha = Mathf.Abs(f) >= 1f ? 0f : Mathf.Abs(f) > 0.5f ? 0.45f + 0.4f * edgeNoise : 0.95f;
                float tone = 0.94f + 0.1f * CedarNoise.Fbm(q.x / 3f, q.y / 3f, 713, 2);
                float y = m.Surface(q.x, q.y) + 0.025f;
                mb.Add(new Vector3(q.x, y, q.y), m.SurfaceNormal(q.x, q.y), new Color(tone, tone, tone * 1.02f, alpha),
                    new Vector2(0.5f + off / (2f * hw), s / 2.4f));
            }
            if (prevRow >= 0)
                for (int c = 0; c < cols - 1; c++)
                {
                    int a = prevRow + c, b = a + 1, d = row + c, e = d + 1;
                    mb.Tri(a, d, e);
                    mb.Tri(a, e, b);
                }
            prevRow = row;
        }
        return mb.ToMesh(t.Name + " trailbed");
    }

    /// <summary>Soft, irregular trampled-snow path for camp foot traffic (ground decal).</summary>
    public static void GroundPath(MeshBuilder mb, CedarModel m, IList<Vector2> ctrl, float halfWidth, float strength, int seed)
    {
        Polyline line = Polyline.Smooth(ctrl, 0.5f);
        float[] fr = { -1f, -0.5f, 0f, 0.5f, 1f };
        int prev = -1;
        for (int i = 0; i < line.Count; i++)
        {
            Vector2 p = line.P[i], n = line.Normal(i);
            float s = line.S[i];
            float endFade = Mathf.Clamp01(s / 1.2f) * Mathf.Clamp01((line.Length - s) / 1.2f);
            float hw = halfWidth * (0.8f + 0.4f * CedarNoise.Fbm(s / 2.5f, seed, 721, 2));
            int row = mb.VertexCount;
            foreach (float f in fr)
            {
                Vector2 q = p + n * f * hw;
                float a = (Mathf.Abs(f) >= 1f ? 0f : Mathf.Abs(f) >= 0.5f ? 0.55f : 1f) * strength * Mathf.Lerp(0.35f, 1f, endFade);
                mb.Add(new Vector3(q.x, m.Surface(q.x, q.y) + 0.02f, q.y), Vector3.up, new Color(1f, 1f, 1f, a), new Vector2(0f, 0f));
            }
            if (prev >= 0)
                for (int c = 0; c < fr.Length - 1; c++)
                {
                    int a = prev + c, b = a + 1, d = row + c, e = d + 1;
                    mb.Tri(a, d, e); mb.Tri(a, e, b);
                }
            prev = row;
        }
    }

    /// <summary>Irregular trampled disc (ground decal) draped on the terrain.</summary>
    public static void GroundDisc(MeshBuilder mb, CedarModel m, Vector2 c, float radius, float strength, int seed)
    {
        const int seg = 28, rings = 4;
        int center = mb.Add(new Vector3(c.x, m.Surface(c.x, c.y) + 0.02f, c.y), Vector3.up, new Color(1, 1, 1, strength), Vector2.zero);
        int first = mb.VertexCount;
        for (int r = 1; r <= rings; r++)
        for (int k = 0; k < seg; k++)
        {
            float ang = k / (float)seg * Mathf.PI * 2f;
            float wob = 0.8f + 0.4f * CedarNoise.Fbm(Mathf.Cos(ang) * 1.5f + seed, Mathf.Sin(ang) * 1.5f, 731, 2);
            float rr = radius * r / rings * (r == rings ? wob : Mathf.Lerp(1f, wob, 0.5f));
            Vector2 q = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
            float a = strength * (r == rings ? 0f : r == rings - 1 ? 0.5f : 1f);
            mb.Add(new Vector3(q.x, m.Surface(q.x, q.y) + 0.02f, q.y), Vector3.up, new Color(1, 1, 1, a), Vector2.zero);
        }
        for (int k = 0; k < seg; k++) mb.Tri(center, first + (k + 1) % seg, first + k);
        for (int r = 0; r < rings - 1; r++)
        for (int k = 0; k < seg; k++)
        {
            int a = first + r * seg + k, b = first + r * seg + (k + 1) % seg, d = a + seg, e = b + seg;
            mb.Tri(a, b, e); mb.Tri(a, e, d);
        }
    }

    // ------------------------------------------------------------------ trees
    public static MeshBuilder SpruceTemplate(int seed, bool hi)
    {
        var mb = new MeshBuilder();
        float rnd(int k) => CedarNoise.Rand(seed, k, 801);
        int tiers = hi ? 5 : 3;
        int seg = hi ? 7 : 6;
        float snowiness = Mathf.Lerp(0.35f, 0.8f, rnd(1));
        Color needle = Color.Lerp(new Color(0.16f, 0.26f, 0.21f), new Color(0.25f, 0.34f, 0.27f), rnd(2)) * Mathf.Lerp(0.9f, 1.1f, rnd(3));
        needle.a = 0f;
        float baseR = Mathf.Lerp(0.13f, 0.17f, rnd(4));
        // trunk
        if (hi) mb.Cylinder(new Vector3(0, -0.05f, 0), new Vector3(0, 0.22f, 0), 0.028f, 0.02f, 5, new Color(0.45f, 0.36f, 0.3f, 0f), false);
        float crownBase = hi ? 0.1f : 0.12f;
        for (int t = 0; t < tiers; t++)
        {
            float f = t / (float)tiers;
            float yb = crownBase + f * (1f - crownBase) * 0.86f;
            float yt = t == tiers - 1 ? 1f : yb + (1f - crownBase) * 0.86f / tiers * 1.75f;
            float r = baseR * (1f - f * 0.78f) * (0.92f + 0.16f * rnd(10 + t));
            float rot = rnd(20 + t) * Mathf.PI * 2f;
            float droop = r * 0.18f;
            int ring0 = mb.VertexCount;
            for (int k = 0; k < seg; k++)
            {
                float ang = rot + k / (float)seg * Mathf.PI * 2f;
                float rr = r * (0.88f + 0.24f * rnd(100 + t * 16 + k));
                Vector3 radial = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Vector3 nrm = (radial * (yt - yb) + Vector3.up * r * 1.4f).normalized;
                Color c = needle; c.a = Mathf.Clamp01(snowiness * (0.75f + 0.5f * rnd(200 + t * 16 + k)));
                mb.Add(radial * rr + Vector3.up * (yb - droop), nrm, c, new Vector2(k / (float)seg, 0f));
            }
            int ring1 = -1;
            if (hi)
            {
                ring1 = mb.VertexCount;
                float ym = yb + 0.42f * (yt - yb);
                for (int k = 0; k < seg; k++)
                {
                    float ang = rot + (k + 0.5f) / seg * Mathf.PI * 2f;
                    Vector3 radial = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 nrm = (radial * (yt - yb) + Vector3.up * r).normalized;
                    Color c = needle; c.a = snowiness * 0.25f * rnd(300 + t * 16 + k);
                    mb.Add(radial * r * 0.5f + Vector3.up * ym, nrm, c, new Vector2((k + 0.5f) / seg, 0.45f));
                }
            }
            Color ac = needle; ac.a = snowiness * 0.7f;
            int apex = mb.Add(Vector3.up * yt, Vector3.up, ac, new Vector2(0.5f, 1f));
            for (int k = 0; k < seg; k++)
            {
                int k1 = (k + 1) % seg;
                if (hi)
                {
                    mb.Tri(ring0 + k, ring1 + k, ring0 + k1);
                    mb.Tri(ring0 + k1, ring1 + k, ring1 + k1);
                    mb.Tri(ring1 + k, apex, ring1 + k1);
                }
                else mb.Tri(ring0 + k, apex, ring0 + k1);
            }
        }
        return mb;
    }

    // ------------------------------------------------------------------ contact shadows
    /// <summary>
    /// Adds a soft elliptical shadow draped over the terrain. Long shadows get a denser grid so they
    /// follow the ground. kind 0 = smooth falloff, 1 = dappled (leafless crowns) from the atlas.
    /// </summary>
    public static void Shadow(MeshBuilder mb, CedarModel m, Vector2 center, Vector2 dir, float lenR, float widR, float strength, Color tint, int kind = 0)
    {
        Vector2 side = new Vector2(dir.y, -dir.x);
        int nj = lenR > 7f ? 7 : lenR > 3f ? 5 : 3, ni = widR > 3f ? 5 : 3;
        int b = mb.VertexCount;
        for (int j = 0; j < nj; j++)
        for (int i = 0; i < ni; i++)
        {
            float u = i / (ni - 1f), v = j / (nj - 1f);
            Vector2 q = center + dir * (v * 2f - 1f) * lenR + side * (u * 2f - 1f) * widR;
            Color c = tint; c.a = strength;
            mb.Add(new Vector3(q.x, m.Surface(q.x, q.y) + 0.03f, q.y), Vector3.up, c, new Vector2((kind + u) * 0.5f, v));
        }
        for (int j = 0; j < nj - 1; j++)
        for (int i = 0; i < ni - 1; i++)
        {
            int a = b + j * ni + i, c1 = a + 1, d = a + ni, e = d + 1;
            mb.Tri(a, d, e); mb.Tri(a, e, c1);
        }
    }

    // ------------------------------------------------------------------ rocks
    public static MeshBuilder Boulder(int seed, float snow)
    {
        var mb = new MeshBuilder();
        const int lat = 5, lon = 8;
        float rnd(int k) => CedarNoise.Rand(seed, k, 811);
        var idx = new int[lat + 1, lon];
        for (int a = 0; a <= lat; a++)
        for (int o = 0; o < lon; o++)
        {
            float th = a / (float)lat * Mathf.PI * 0.62f; // flattened, partly buried dome
            float ph = o / (float)lon * Mathf.PI * 2f;
            float r = 0.8f + 0.4f * rnd(a * 16 + o);
            Vector3 dir = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
            Vector3 p = new Vector3(dir.x * r * 1.2f, dir.y * r * 0.75f - 0.18f, dir.z * r);
            float sn = Mathf.Clamp01((dir.y - 0.55f) * 3f) * snow;
            float g = 0.8f + 0.25f * rnd(200 + a * 16 + o);
            idx[a, o] = mb.Add(p, dir, new Color(g, g, g * 1.03f, sn), new Vector2(ph, th));
        }
        for (int a = 0; a < lat; a++)
        for (int o = 0; o < lon; o++)
        {
            int o1 = (o + 1) % lon;
            mb.Tri(idx[a, o], idx[a, o1], idx[a + 1, o1]);
            mb.Tri(idx[a, o], idx[a + 1, o1], idx[a + 1, o]);
        }
        return mb;
    }

    /// <summary>Thin, irregular ice shard plate (5-7 sides), local origin at its centre.</summary>
    public static void IceShard(MeshBuilder mb, Vector3 c, float radius, float yaw, float tiltX, float tiltZ, int seed)
    {
        int sides = 5 + (int)(CedarNoise.Rand(seed, 1, 821) * 3f);
        Quaternion q = Quaternion.Euler(tiltX, yaw, tiltZ);
        const float th = 0.07f;
        var top = new Vector3[sides];
        for (int k = 0; k < sides; k++)
        {
            float ang = (k + 0.3f * CedarNoise.Rand(seed, 10 + k, 821)) / sides * Mathf.PI * 2f;
            float r = radius * (0.6f + 0.5f * CedarNoise.Rand(seed, 30 + k, 821));
            top[k] = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
        }
        float snow = CedarNoise.Rand(seed, 50, 821) * 0.5f;
        Color tc = new Color(0.98f, 1f, 1f, snow), sc = new Color(0.82f, 0.92f, 0.98f, 0f);
        int ct = mb.Add(c + q * new Vector3(0, th, 0), q * Vector3.up, tc, new Vector2(0.5f, 0.5f));
        int rt = mb.VertexCount;
        for (int k = 0; k < sides; k++) mb.Add(c + q * (top[k] + Vector3.up * th), q * Vector3.up, tc, new Vector2(top[k].x, top[k].z));
        for (int k = 0; k < sides; k++) mb.Tri(ct, rt + (k + 1) % sides, rt + k);
        for (int k = 0; k < sides; k++)
        {
            Vector3 a = top[k], b = top[(k + 1) % sides];
            Vector3 n = q * new Vector3((a + b).x, 0, (a + b).z).normalized;
            int v0 = mb.Add(c + q * (a + Vector3.up * th), n, sc, new Vector2(0, 0));
            int v1 = mb.Add(c + q * (b + Vector3.up * th), n, sc, new Vector2(1, 0));
            int v2 = mb.Add(c + q * b, n, sc, new Vector2(1, 1));
            int v3 = mb.Add(c + q * a, n, sc, new Vector2(0, 1));
            mb.Tri(v0, v1, v2); mb.Tri(v0, v2, v3);
        }
    }

    // ------------------------------------------------------------------ rope
    public static void Rope(MeshBuilder mb, Vector3 a, Vector3 b, float sag, float radius, Color col)
    {
        const int segs = 6;
        Vector3 prev = a;
        for (int s = 1; s <= segs; s++)
        {
            float t = s / (float)segs;
            Vector3 p = Vector3.Lerp(a, b, t) + Vector3.down * sag * 4f * t * (1f - t);
            mb.Cylinder(prev, p, radius, radius, 4, col, false);
            prev = p;
        }
    }
}
