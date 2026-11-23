using System.Collections.Generic;
using UnityEngine;

internal sealed class RiverModel
{
    public Polyline Line;
    public float[] HalfW, Level, Curv;
}

internal sealed class BridgeModel
{
    public string Name;
    public Vector2 Center, Axis, Along; // Axis points from the camp bank to the far bank
    public float HalfSpan, DeckY, IceY, RiverHalfW;
    public int RiverIndex;
    public Vector2 EndA => Center - Axis * HalfSpan;
    public Vector2 EndB => Center + Axis * HalfSpan;
    public Vector2 Local(Vector2 p) { Vector2 d = p - Center; return new Vector2(Vector2.Dot(d, Axis), Vector2.Dot(d, Along)); }
}

internal sealed class TrailModel
{
    public string Name, From, To;
    public Polyline Line;
    public float[] Y;        // trail surface height (top of packed snow, or deck top)
    public bool[] OnDeck;
    public BridgeModel Bridge;
}

internal struct Knob { public Vector2 P; public float H, R; public Knob(float x, float z, float h, float r) { P = new Vector2(x, z); H = h; R = r; } }

/// <summary>
/// The Cedar Valley landscape in world meters (1 unit = 1 m). Everything visual is derived
/// from these fields, so terrain, river, trails, bridges and props always agree.
/// </summary>
internal sealed class CedarModel
{
    public const float X0 = -228f, X1 = 228f, Z0 = -150f, Z1 = 330f, Step = 1.5f;
    public readonly int NX = Mathf.RoundToInt((X1 - X0) / Step) + 1;
    public readonly int NZ = Mathf.RoundToInt((Z1 - Z0) / Step) + 1;

    public float[] Ground;      // final terrain heights (grid vertices)
    public float[] Natural;     // terrain before trail carving
    public Color[] Paint;       // R packed, G rock, B forest shade, A AO
    public float[] Forest;      // tree density 0..1
    public float[] TrailDist;   // distance to nearest trail centreline (capped)
    public float[] Thin;        // thin snow cover: soil and dead grass show through (0..1)
    public float[] Scour;       // wind scour on crests and convex shoulders (0..1)

    public RiverModel River;
    public Polyline RidgeMain, RidgeSpur, Escarp;
    public readonly List<BridgeModel> Bridges = new List<BridgeModel>();
    public readonly List<TrailModel> Trails = new List<TrailModel>();
    public readonly Dictionary<string, Vector2> Nodes = new Dictionary<string, Vector2>();
    public readonly Dictionary<string, float> NodeGround = new Dictionary<string, float>();
    public readonly List<Vector2> ProtectedSites = new List<Vector2>();
    public Vector2 Camp;
    public float CampY;
    public readonly List<Knob> Knobs = new List<Knob>();
    public readonly List<Vector4> Meadows = new List<Vector4>(); // x, z, rx, rz

    // ------------------------------------------------------------------ layout data
    private static readonly Vector2[] RiverCtrl =
    {
        new Vector2(-8, -280), new Vector2(-2, -200), new Vector2(6, -150), new Vector2(20, -102), new Vector2(36, -64),
        new Vector2(40, -37), new Vector2(33, -10), new Vector2(20, 16), new Vector2(14, 42), new Vector2(19, 66),
        new Vector2(33, 89), new Vector2(36, 114), new Vector2(25, 139), new Vector2(9, 161), new Vector2(2, 190),
        new Vector2(3, 220), new Vector2(10, 252), new Vector2(14, 290), new Vector2(10, 330), new Vector2(4, 400),
        new Vector2(-2, 470), new Vector2(-6, 540)
    };

    private static readonly Vector2[] RidgeMainCtrl =
    {
        new Vector2(-40, -34), new Vector2(-56, -4), new Vector2(-67.2f, 12), new Vector2(-82, 40), new Vector2(-96, 64),
        new Vector2(-106.8f, 82), new Vector2(-118, 100), new Vector2(-128, 124), new Vector2(-138, 150), new Vector2(-144.6f, 164),
        new Vector2(-152, 186), new Vector2(-160, 220), new Vector2(-166, 260), new Vector2(-172, 300), new Vector2(-180, 360)
    };
    private static readonly float[] RidgeMainZ = { -34, -2, 12, 40, 64, 82, 100, 124, 150, 164, 186, 220, 260, 300, 360 };
    private static readonly float[] RidgeMainH = { 0, 7, 10, 14, 17, 19, 20, 22, 25, 26, 27, 26, 23, 20, 17 };

    private static readonly Vector2[] RidgeSpurCtrl =
    {
        new Vector2(-106.8f, 82), new Vector2(-106, 96), new Vector2(-107, 115), new Vector2(-108, 135), new Vector2(-106, 160),
        new Vector2(-104, 182), new Vector2(-102.6f, 201), new Vector2(-101, 225), new Vector2(-98, 250), new Vector2(-96, 272)
    };
    private static readonly float[] RidgeSpurZ = { 82, 96, 115, 135, 160, 182, 201, 225, 250, 272 };
    private static readonly float[] RidgeSpurH = { 18.5f, 17, 15.5f, 16, 17.5f, 18.5f, 19.5f, 17, 12, 5 };

    private static readonly Vector2[] EscarpCtrl =
    {
        new Vector2(56, -110), new Vector2(66, -70), new Vector2(78, -30), new Vector2(88, 0), new Vector2(98, 30), new Vector2(104, 60),
        new Vector2(108, 100), new Vector2(113, 140), new Vector2(119, 180), new Vector2(125, 220), new Vector2(132, 260),
        new Vector2(140, 300), new Vector2(150, 360)
    };

    public static readonly Vector2 CrossingLowerTarget = new Vector2(39.5f, -40f); // Camp -> C1
    public static readonly Vector2 CrossingUpperTarget = new Vector2(24.5f, 8f);   // Camp -> R1

    // ------------------------------------------------------------------ build
    public void Build(Dictionary<string, Vector2> nodes, IEnumerable<Vector2> protectedSites = null)
    {
        if (protectedSites != null) ProtectedSites.AddRange(protectedSites);
        foreach (var kv in nodes) Nodes[kv.Key] = kv.Value;
        Camp = Nodes["Camp"];
        BuildRiver();
        RidgeMain = Polyline.Smooth(RidgeMainCtrl, 1f);
        RidgeSpur = Polyline.Smooth(RidgeSpurCtrl, 1f);
        Escarp = Polyline.Smooth(EscarpCtrl, 1f);
        Knobs.Add(new Knob(80, 17, 7, 8)); Knobs.Add(new Knob(104, 27, 8, 8.5f));
        Knobs.Add(new Knob(117, 71, 7, 7.5f)); Knobs.Add(new Knob(139, 84, 6.5f, 7));
        Knobs.Add(new Knob(151, 116, 8, 8)); Knobs.Add(new Knob(162, 68, 11, 12)); Knobs.Add(new Knob(167, 160, 9, 11));
        Knobs.Add(new Knob(128, 112, 5, 6));
        Meadows.Add(new Vector4(-38, 62, 12, 9)); Meadows.Add(new Vector4(-46, 94, 10, 8));
        Meadows.Add(new Vector4(-57, 148, 13, 9)); Meadows.Add(new Vector4(-31, 147, 9, 7));
        Meadows.Add(new Vector4(-62, 192, 11, 8)); Meadows.Add(new Vector4(-14, 8, 10, 8));
        Meadows.Add(new Vector4(70, 120, 12, 9)); Meadows.Add(new Vector4(85, 200, 10, 12));
        BuildBridges();
        BuildNatural();
        BuildTrails();
        CarveTrails();
        SculptTerrain();
        BuildPaint();
    }

    private void BuildRiver()
    {
        var r = new RiverModel { Line = Polyline.Smooth(RiverCtrl, 1f) };
        int n = r.Line.Count;
        r.HalfW = new float[n]; r.Level = new float[n]; r.Curv = new float[n];
        int sLower = r.Line.NearestIndex(CrossingLowerTarget), sUpper = r.Line.NearestIndex(CrossingUpperTarget);
        for (int i = 0; i < n; i++)
        {
            float z = r.Line.P[i].y, s = r.Line.S[i];
            float hw = 6.6f + 1.6f * (CedarNoise.Value(s / 45f, 1.7f, 91) - 0.5f) * 2f;
            hw += 6.2f * CedarNoise.Gauss((z - 108f) / 24f);   // broad pool around the big bend
            hw += 2.4f * CedarNoise.Gauss((z + 62f) / 12f);    // eddy pool below camp
            hw += 2.0f * CedarNoise.Gauss((z - 205f) / 20f);
            hw = Mathf.Lerp(hw, 4.5f, CedarNoise.Gauss((i - sLower) / 10f));  // riffle at the lower crossing
            hw = Mathf.Lerp(hw, 4.9f, CedarNoise.Gauss((i - sUpper) / 10f));  // narrows at the upper crossing
            r.HalfW[i] = hw;
            r.Level[i] = -1.2f + 0.03f * (z + 60f);
        }
        for (int i = 0; i < n; i++)
        {
            int a = Mathf.Max(0, i - 10), b = Mathf.Min(n - 1, i + 10);
            Vector2 ta = r.Line.Tangent(a), tb = r.Line.Tangent(b);
            float ang = Mathf.Atan2(ta.x * tb.y - ta.y * tb.x, Vector2.Dot(ta, tb));
            r.Curv[i] = ang / Mathf.Max(1f, r.Line.S[b] - r.Line.S[a]); // +: turning left
        }
        River = r;
    }

    private void BuildBridges()
    {
        Bridges.Add(MakeBridge("Lower crossing", CrossingLowerTarget));
        Bridges.Add(MakeBridge("Upper crossing", CrossingUpperTarget));
    }

    private BridgeModel MakeBridge(string name, Vector2 target)
    {
        int i = River.Line.NearestIndex(target);
        Vector2 along = River.Line.Tangent(i);
        Vector2 axis = River.Line.Normal(i); // east = away from camp
        float hw = River.HalfW[i];
        return new BridgeModel
        {
            Name = name, RiverIndex = i, Center = River.Line.P[i], Axis = axis, Along = along,
            RiverHalfW = hw, HalfSpan = hw + 2.3f, IceY = River.Level[i], DeckY = River.Level[i] + 1.6f
        };
    }

    // ------------------------------------------------------------------ height function
    public float RiverInfo(Vector2 q, out int k, out float side) => River.Line.NearestMonotone(q, out k, out side);

    // The relief follows the existing valley. The authored bridge spans and approach roads
    // still use the original river widths, so local bank work cannot move a crossing.
    public float BankOffset(int index, float side)
    {
        float s = River.Line.S[index];
        float free = 1f;
        foreach (BridgeModel bridge in Bridges)
            free = Mathf.Min(free, S01((Mathf.Abs(s - River.Line.S[bridge.RiverIndex]) - 10f) / 16f));
        return free * (2.3f * SignedNoise(s / 31f, side * 3.7f, 148) +
                       0.8f * SignedNoise(s / 9f, side * 6.1f, 149));
    }

    private static float SignedNoise(float x, float z, int seed) => 2f * CedarNoise.Fbm(x, z, seed, 3) - 1f;

    private float Relief(Vector2 p)
    {
        float distance = RiverInfo(p, out int river, out _) - River.HalfW[river];
        float away = S01((distance - 13f) / 27f);
        if (away <= 0f) return 0f;

        float x = p.x, z = p.y;
        float wx = x + 19f * SignedNoise(x / 72f, z / 72f, 171);
        float wz = z + 22f * SignedNoise(x / 88f, z / 88f, 184);
        float west = RidgeDistance(p, out float crest);
        float east = EscarpSigned(p, out float faceWidth, out float faceHeight);
        float ridgeFace = CedarNoise.Gauss(west / 49f) * S01((crest - 4f) / 14f);
        float scarpFace = CedarNoise.Gauss((east - faceWidth * 0.27f) / 36f) * (faceHeight / 15f);
        float foothill = SignedNoise(wx / 73f, wz / 91f, 195) * 4.8f;
        // Broad spurs and gullies cross the smooth contours. The first pass used tighter, deeper
        // ridged noise that read as folded clay; these are wider, shallower and fewer, so the
        // slopes keep coherent large forms while the authored ridges and scarp dominate.
        float ribs = CedarNoise.Ridged(wx / 74f, (wz + wx * .64f) / 49f, 211);
        float gullies = CedarNoise.Ridged(wx / 66f, (wz - wx * .48f) / 41f, 277);
        float geology = ridgeFace * (ribs * 8.5f - gullies * gullies * 3.6f - 2.4f) +
                        scarpFace * (gullies * 7.5f - ribs * ribs * 4.2f - 1.6f);
        float drifts = .24f * SignedNoise((wx + wz * .30f) / 17f, wz / 12f, 281);
        float high = S01((Mathf.Abs(x) - 135f) / 75f) * SignedNoise(wx / 58f, wz / 79f, 289) * 7f;
        return away * (foothill + geology + drifts + high);
    }

    public float OuterSurface(float x, float z)
    {
        var p = new Vector2(x, z);
        return NaturalAt(x, z) + Relief(p) + BankRelief(p);
    }

    private void SculptTerrain()
    {
        for (int j = 0; j < NZ; j++)
        for (int i = 0; i < NX; i++)
        {
            int index = Idx(i, j);
            var p = new Vector2(GX(i), GZ(j));
            // Leave the complete trail bench, camp, bridge approaches and marker pads intact.
            float mask = S01((TrailDist[index] - 4.55f) / 9f);
            mask *= S01((Vector2.Distance(p, Camp) - 31f) / 12f);
            foreach (var node in Nodes) mask *= S01((Vector2.Distance(p, node.Value) - 8f) / 8f);
            foreach (Vector2 site in ProtectedSites) mask *= S01((Vector2.Distance(p, site) - 7f) / 7f);
            foreach (BridgeModel bridge in Bridges) mask *= S01((Vector2.Distance(p, bridge.Center) - 18f) / 11f);

            Ground[index] += mask * (Relief(p) + BankRelief(p));
        }

        // Every fourth core vertex coincides with an outer-grid vertex.
        for (int j = 0; j + 4 < NZ; j += 4)
        for (int i = 1; i < 4; i++)
        {
            Ground[Idx(0, j + i)] = Mathf.Lerp(Ground[Idx(0, j)], Ground[Idx(0, j + 4)], i / 4f);
            Ground[Idx(NX - 1, j + i)] = Mathf.Lerp(Ground[Idx(NX - 1, j)], Ground[Idx(NX - 1, j + 4)], i / 4f);
        }
        for (int i = 0; i + 4 < NX; i += 4)
        for (int j = 1; j < 4; j++)
        {
            Ground[Idx(i + j, 0)] = Mathf.Lerp(Ground[Idx(i, 0)], Ground[Idx(i + 4, 0)], j / 4f);
            Ground[Idx(i + j, NZ - 1)] = Mathf.Lerp(Ground[Idx(i, NZ - 1)], Ground[Idx(i + 4, NZ - 1)], j / 4f);
        }
    }

    private float BankRelief(Vector2 p)
    {
        float d = RiverInfo(p, out int k, out float side);
        float bank = BankOffset(k, side);
        float e = d - River.HalfW[k];
        float s = River.Line.S[k];
        float cut = .66f * (S01((e - bank) / 3.8f) - S01(e / 3.8f));
        float terrace = .33f * CedarNoise.Gauss((e - bank - 2.4f) / 1.7f) * SignedNoise(s / 13f, side * 4f, 293);
        float channel = -.5f * S01(-e / 3f) * (1f + SignedNoise(s / 27f, side * 5f, 294));
        return cut + terrace + channel;
    }

    private static float Lerp01(float[] zs, float[] hs, float z)
    {
        if (z <= zs[0]) return hs[0];
        for (int i = 1; i < zs.Length; i++)
            if (z <= zs[i]) return Mathf.Lerp(hs[i - 1], hs[i], Mathf.InverseLerp(zs[i - 1], zs[i], z));
        return hs[hs.Length - 1];
    }

    private static float S01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    public float RidgeDistance(Vector2 q, out float crest)
    {
        float dm = RidgeMain.NearestMonotone(q, out int im, out _);
        float ds = RidgeSpur.NearestMonotone(q, out int isp, out _);
        crest = Mathf.Max(Lerp01(RidgeMainZ, RidgeMainH, RidgeMain.P[im].y) * (dm < ds ? 1f : 0f),
                          Lerp01(RidgeSpurZ, RidgeSpurH, RidgeSpur.P[isp].y) * (ds <= dm ? 1f : 0f));
        return Mathf.Min(dm, ds);
    }

    private static float RidgeProfile(float d, float h, float w) =>
        h * (0.55f * CedarNoise.Gauss(d / w) + 0.25f * CedarNoise.Gauss(d / (2.6f * w)) + 0.2f * CedarNoise.Gauss(d / 8f));

    public float EscarpSigned(Vector2 q, out float wc, out float hc)
    {
        float d = Escarp.NearestMonotone(q, out _, out float side);
        float sd = d * side + 4.5f * (CedarNoise.Fbm(q.y / 22f, 0.5f, 301, 3) - 0.5f) * 2f;
        wc = 17f + 26f * CedarNoise.Gauss((q.y - 24f) / 15f);
        hc = 15f * S01((q.y + 85f) / 50f);
        return sd;
    }

    public float NaturalAt(float x, float z)
    {
        var q = new Vector2(x, z);
        float d = RiverInfo(q, out int k, out float side);
        float w = River.HalfW[k], L = River.Level[k], s = River.Line.S[k];
        float outside = Mathf.Clamp(River.Curv[k] * side * 55f, -1f, 1f);
        float bankH = Mathf.Max(0.6f, 1.25f + 0.45f * (CedarNoise.Value(s / 26f + side * 7f, 3.1f, 101) - 0.5f) * 2f + 0.55f * outside);
        float bankW = Mathf.Lerp(3.0f, 1.5f, Mathf.Clamp01(outside)) + Mathf.Max(0f, -outside) * 2.6f
                      + 0.8f * CedarNoise.Value(s / 18f, side * 5f, 102);
        float e = d - w;
        // Away from the water, river-derived values blend to smooth regional ones so that
        // switching between two bends as the "nearest" river section never leaves a seam.
        float far = S01((e - 6f) / 24f);
        L = Mathf.Lerp(L, -1.2f + 0.03f * (z + 60f), far);
        bankH = Mathf.Lerp(bankH, 1.25f, far);
        bankW = Mathf.Lerp(bankW, 3f, far);
        float eF = Mathf.Lerp(e, d - 7f, far);
        float h;
        if (e < 0f) h = L - 0.25f - 0.75f * S01(-e / 2.5f);
        else h = L - 0.25f + (bankH + 0.25f) * S01(eF / bankW) + 0.03f * Mathf.Max(0f, eF - bankW);
        float dv = Mathf.Max(0f, d - 25f);
        h += 0.018f * dv + 0.00036f * dv * dv;
        float away = Mathf.Clamp01((eF - 6f) / 26f);
        h += (side < 0f ? 2.4f : 1.5f) * (CedarNoise.Fbm(x / 58f + 3.3f, z / 58f, 201, 4) - 0.5f) * 2f * away;
        h += 0.45f * (CedarNoise.Fbm(x / 11f, z / 11f, 202, 3) - 0.5f) * 2f * Mathf.Clamp01((e - 1.5f) / 5f);

        // North Ridge: a main crest plus the spur toward N4; flanks steeper on the west side.
        float dm = RidgeMain.NearestMonotone(q, out int im, out float sm);
        float ds = RidgeSpur.NearestMonotone(q, out int isp, out float ss);
        float hm = Lerp01(RidgeMainZ, RidgeMainH, RidgeMain.P[im].y);
        float hs = Lerp01(RidgeSpurZ, RidgeSpurH, RidgeSpur.P[isp].y);
        float ridge = Mathf.Max(RidgeProfile(dm, hm, sm < 0f ? 16f : 24f), RidgeProfile(ds, hs, ss < 0f ? 18f : 20f));
        h += ridge * Mathf.Clamp01((eF - 4f) / 20f);

        // Cliff Pass: escarpment band with a ramped gully where the pass climbs, upper bench beyond.
        float sd = EscarpSigned(q, out float wc, out float hc);
        h += hc * S01((sd + wc * 0.5f) / wc);
        if (sd > 0f) h += 0.09f * sd + 0.0006f * sd * sd + 2.2f * (CedarNoise.Fbm(x / 40f, z / 40f, 303, 3) - 0.5f) * 2f * S01(sd / 20f);
        else h += 1.8f * (hc / 15f) * CedarNoise.Gauss((sd + 5f) / 7f);

        foreach (Knob kn in Knobs)
        {
            Vector2 dq = q - kn.P;
            float ang = Mathf.Atan2(dq.y, dq.x);
            float stretch = 1f + 0.35f * Mathf.Cos(2f * ang + kn.H);
            float r = dq.magnitude * stretch * (1f + 0.3f * (CedarNoise.Fbm(x / 5f, z / 5f, 311, 3) - 0.5f));
            float dome = CedarNoise.Gauss(r / (kn.R * 0.78f));
            h += kn.H * dome * (0.85f + 0.3f * CedarNoise.Fbm(x / 3.5f, z / 3.5f, 312, 2));
        }

        h += 55f * S01((-x - 235f) / 110f) + 55f * S01((x - 235f) / 110f) + 42f * S01((z - 340f) / 150f);
        return h;
    }

    // ------------------------------------------------------------------ grids
    public int Idx(int i, int j) => j * NX + i;
    public float GX(int i) => X0 + i * Step;
    public float GZ(int j) => Z0 + j * Step;

    private void BuildNatural()
    {
        Natural = new float[NX * NZ];
        for (int j = 0; j < NZ; j++)
        for (int i = 0; i < NX; i++)
            Natural[Idx(i, j)] = NaturalAt(GX(i), GZ(j));

        // Camp clearing: a level, slightly crowned bench set back from the river.
        CampY = Sample(Natural, Camp.x, Camp.y);
        foreach (var kv in Nodes) NodeGround[kv.Key] = kv.Key == "Camp" ? CampY : Sample(Natural, kv.Value.x, kv.Value.y);
        for (int j = 0; j < NZ; j++)
        for (int i = 0; i < NX; i++)
        {
            var p = new Vector2(GX(i), GZ(j));
            int id = Idx(i, j);
            float rc = Vector2.Distance(p, Camp);
            if (rc < 30f)
            {
                float flat = CampY + 0.06f * (CedarNoise.Fbm(p.x / 5f, p.y / 5f, 401, 2) - 0.5f) - 0.004f * rc;
                Natural[id] = Mathf.Lerp(Natural[id], flat, S01((30f - rc) / 12f));
            }
            foreach (var kv in Nodes)
            {
                if (kv.Key == "Camp") continue;
                float rn = Vector2.Distance(p, kv.Value);
                if (rn < 8f) Natural[id] = Mathf.Lerp(Natural[id], NodeGround[kv.Key], S01((8f - rn) / 4.5f));
            }
        }
    }

    public float Sample(float[] grid, float x, float z)
    {
        float fx = Mathf.Clamp((x - X0) / Step, 0f, NX - 1.001f), fz = Mathf.Clamp((z - Z0) / Step, 0f, NZ - 1.001f);
        int i = (int)fx, j = (int)fz;
        float tx = fx - i, tz = fz - j;
        float a = grid[Idx(i, j)], b = grid[Idx(i + 1, j)], c = grid[Idx(i, j + 1)], d = grid[Idx(i + 1, j + 1)];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    /// <summary>Height of the rendered terrain surface, matching the mesh triangulation exactly.</summary>
    public float Surface(float x, float z)
    {
        if (x < X0 || x > X1 || z < Z0 || z > Z1) return OuterSurface(x, z);
        float fx = Mathf.Clamp((x - X0) / Step, 0f, NX - 1.0001f), fz = Mathf.Clamp((z - Z0) / Step, 0f, NZ - 1.0001f);
        int i = (int)fx, j = (int)fz;
        float tx = fx - i, tz = fz - j;
        float v00 = Ground[Idx(i, j)], v10 = Ground[Idx(i + 1, j)], v01 = Ground[Idx(i, j + 1)], v11 = Ground[Idx(i + 1, j + 1)];
        if (tz > tx) return v00 + tz * (v01 - v00) + tx * (v11 - v01);
        return v00 + tx * (v10 - v00) + tz * (v11 - v10);
    }

    public Vector3 SurfaceNormal(float x, float z)
    {
        const float e = 0.75f;
        float hx = Surface(x + e, z) - Surface(x - e, z), hz = Surface(x, z + e) - Surface(x, z - e);
        return new Vector3(-hx, 2f * e, -hz).normalized;
    }

    // ------------------------------------------------------------------ trails
    private static List<Vector2> V(params float[] xz)
    {
        var l = new List<Vector2>();
        for (int i = 0; i < xz.Length; i += 2) l.Add(new Vector2(xz[i], xz[i + 1]));
        return l;
    }

    private List<Vector2> AlongLine(Polyline line, Vector2 from, Vector2 to, float spacing, float wiggle, int seed)
    {
        int a = line.NearestIndex(from), b = line.NearestIndex(to);
        var pts = new List<Vector2> { from };
        int dir = b >= a ? 1 : -1;
        int step = Mathf.Max(1, Mathf.RoundToInt(spacing));
        for (int i = a + dir * step; dir > 0 ? i < b - step * 0.6f : i > b + step * 0.6f; i += dir * step)
        {
            float off = wiggle * Mathf.Sin(line.S[i] / 17f + seed) + wiggle * 0.5f * (CedarNoise.Value(line.S[i] / 9f, seed, 501) - 0.5f);
            pts.Add(line.P[i] + line.Normal(i) * off);
        }
        pts.Add(to);
        return pts;
    }

    private List<Vector2> AlongBank(Vector2 from, Vector2 to, float setback, int seed)
    {
        int a = River.Line.NearestIndex(from), b = River.Line.NearestIndex(to);
        var pts = new List<Vector2> { from };
        int dir = b >= a ? 1 : -1;
        for (int i = a + dir * 13; dir > 0 ? i < b - 9 : i > b + 9; i += dir * 13)
        {
            float off = River.HalfW[i] + setback + 1.6f * Mathf.Sin(River.Line.S[i] / 21f + seed);
            pts.Add(River.Line.P[i] + River.Line.Normal(i) * off);
        }
        pts.Add(to);
        return pts;
    }

    private void AddTrail(string from, string to, List<Vector2> ctrl, BridgeModel bridge = null)
    {
        var t = new TrailModel { Name = "Trail_" + from + "_" + to, From = from, To = to, Line = Polyline.Smooth(ctrl, 0.75f), Bridge = bridge };
        Trails.Add(t);
    }

    private List<Vector2> WithBridge(List<Vector2> before, BridgeModel b, List<Vector2> after)
    {
        var l = new List<Vector2>(before);
        l.Add(b.EndA - b.Axis * 4f);
        l.Add(b.EndA);
        l.Add(b.EndB);
        l.Add(b.EndB + b.Axis * 4f);
        l.AddRange(after);
        return l;
    }

    private void BuildTrails()
    {
        Vector2 camp = Camp;
        Vector2 N(string id) => Nodes[id];
        BridgeModel lower = Bridges[0], upper = Bridges[1];

        AddTrail("Camp", "N1", new List<Vector2> { camp, new Vector2(-12, -41.5f), new Vector2(-25, -36), new Vector2(-37, -28.5f), new Vector2(-47, -18), new Vector2(-55.5f, -6.5f), new Vector2(-62, 3.5f), N("N1") });
        AddTrail("N1", "N2", AlongLine(RidgeMain, N("N1"), N("N2"), 13, 1.4f, 3));
        AddTrail("N2", "N3", AlongLine(RidgeMain, N("N2"), N("N3"), 13, 1.4f, 5));
        AddTrail("N2", "N4", AlongLine(RidgeSpur, N("N2"), N("N4"), 13, 1.4f, 7));

        AddTrail("Camp", "W1", new List<Vector2> { camp, new Vector2(-6.5f, -32), new Vector2(-13, -20), new Vector2(-17, -6), new Vector2(-22, 8), new Vector2(-25, 20), new Vector2(-28, 30), N("W1") });
        AddTrail("W1", "W2", new List<Vector2> { N("W1"), new Vector2(-38, 50), new Vector2(-35, 63), new Vector2(-43, 74), new Vector2(-41, 88), new Vector2(-49, 99), N("W2") });
        AddTrail("W2", "W3", new List<Vector2> { N("W2"), new Vector2(-51, 126), new Vector2(-58, 140), new Vector2(-55, 156), new Vector2(-63, 170), new Vector2(-59, 186), new Vector2(-66, 201), N("W3") });
        AddTrail("W2", "W4", new List<Vector2> { N("W2"), new Vector2(-44, 118), new Vector2(-40, 131), new Vector2(-31, 140), new Vector2(-30, 153), new Vector2(-23, 162), N("W4") });

        AddTrail("Camp", "R1", WithBridge(new List<Vector2> { camp, new Vector2(3, -33), new Vector2(7, -20), new Vector2(10.5f, -8.5f) }, upper,
            new List<Vector2> { new Vector2(33.5f, 19.5f), N("R1") }), upper);
        AddTrail("R1", "R2", AlongBank(N("R1"), N("R2"), 8.5f, 1));
        AddTrail("R2", "R3", AlongBank(N("R2"), N("R3"), 8.5f, 2));
        AddTrail("R2", "R4", new List<Vector2> { N("R2"), new Vector2(65, 98), new Vector2(73, 108), new Vector2(77, 121), new Vector2(84, 133), new Vector2(87, 146), N("R4") });
        AddTrail("R4", "R5", new List<Vector2> { N("R4"), new Vector2(93, 170), new Vector2(88, 185), new Vector2(91, 200), new Vector2(86, 214), new Vector2(82, 230), N("R5") });

        AddTrail("Camp", "C1", WithBridge(new List<Vector2> { camp, new Vector2(12, -45.5f), new Vector2(22, -44.5f) }, lower,
            new List<Vector2> { new Vector2(57, -31), new Vector2(64, -17), N("C1") }), lower);
        AddTrail("C1", "C2", new List<Vector2> { N("C1"), new Vector2(77, 6), new Vector2(86, 10), new Vector2(92, 18), new Vector2(88, 27), new Vector2(96, 34), new Vector2(106, 40), new Vector2(114, 46), N("C2") });
        AddTrail("C2", "C3", new List<Vector2> { N("C2"), new Vector2(130, 64), new Vector2(126, 78), new Vector2(134, 92), new Vector2(142, 104), new Vector2(138, 118), new Vector2(146, 132), N("C3") });

        // Heights: follow the natural ground smoothly, then lay the deck and its approaches.
        foreach (TrailModel t in Trails)
        {
            int n = t.Line.Count;
            var raw = new float[n];
            for (int i = 0; i < n; i++) raw[i] = Sample(Natural, t.Line.P[i].x, t.Line.P[i].y);
            t.Y = new float[n];
            t.OnDeck = new bool[n];
            const int k = 11;
            for (int i = 0; i < n; i++)
            {
                float sum = 0f, wsum = 0f;
                for (int o = -k; o <= k; o++)
                {
                    int j = Mathf.Clamp(i + o, 0, n - 1);
                    float w = CedarNoise.Gauss(o / (k * 0.55f));
                    sum += raw[j] * w; wsum += w;
                }
                t.Y[i] = sum / wsum;
            }
            // pin the ends to the site pads
            float y0 = NodeGround[t.From], y1 = NodeGround[t.To];
            for (int i = 0; i < n; i++)
            {
                float s = t.Line.S[i], rem = t.Line.Length - s;
                t.Y[i] = Mathf.Lerp(t.Y[i], y0, S01((5f - s) / 5f));
                t.Y[i] = Mathf.Lerp(t.Y[i], y1, S01((5f - rem) / 5f));
            }
            // limit the grade to a walkable 35%; carving then benches the trail into the slope
            const float maxRise = 0.35f * 0.75f;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 1; i < n; i++) t.Y[i] = Mathf.Clamp(t.Y[i], t.Y[i - 1] - maxRise, t.Y[i - 1] + maxRise);
                for (int i = n - 2; i >= 0; i--) t.Y[i] = Mathf.Clamp(t.Y[i], t.Y[i + 1] - maxRise, t.Y[i + 1] + maxRise);
            }
            if (t.Bridge != null)
            {
                BridgeModel b = t.Bridge;
                for (int i = 0; i < n; i++)
                {
                    Vector2 l = b.Local(t.Line.P[i]);
                    float beyond = Mathf.Abs(l.x) - b.HalfSpan;
                    if (Mathf.Abs(l.y) > 3f) continue;
                    if (beyond <= 0f) { t.Y[i] = b.DeckY; t.OnDeck[i] = true; }
                    else if (beyond < 7f) t.Y[i] = Mathf.Lerp(b.DeckY, t.Y[i], S01(beyond / 7f));
                }
            }
        }
    }

    private void CarveTrails()
    {
        Ground = (float[])Natural.Clone();
        TrailDist = new float[NX * NZ];
        var trailH = new float[NX * NZ];
        for (int i = 0; i < TrailDist.Length; i++) TrailDist[i] = 99f;
        const float R = 5f;
        foreach (TrailModel t in Trails)
        {
            for (int s = 0; s < t.Line.Count - 1; s++)
            {
                if (t.OnDeck[s] && t.OnDeck[s + 1]) continue;
                Vector2 a = t.Line.P[s], b = t.Line.P[s + 1];
                int i0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - R - X0) / Step), i1 = Mathf.CeilToInt((Mathf.Max(a.x, b.x) + R - X0) / Step);
                int j0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - R - Z0) / Step), j1 = Mathf.CeilToInt((Mathf.Max(a.y, b.y) + R - Z0) / Step);
                for (int j = Mathf.Max(0, j0); j <= Mathf.Min(NZ - 1, j1); j++)
                for (int i = Mathf.Max(0, i0); i <= Mathf.Min(NX - 1, i1); i++)
                {
                    var p = new Vector2(GX(i), GZ(j));
                    Vector2 ab = b - a;
                    float tt = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    float d = Vector2.Distance(p, a + ab * tt);
                    int id = Idx(i, j);
                    if (d >= TrailDist[id]) continue;
                    if (t.Bridge != null)
                    {
                        Vector2 l = t.Bridge.Local(p);
                        if (Mathf.Abs(l.x) < t.Bridge.HalfSpan - 1.4f && Mathf.Abs(l.y) < 6f) continue; // keep the channel open under the deck
                    }
                    TrailDist[id] = d;
                    trailH[id] = Mathf.Lerp(t.Y[s], t.Y[s + 1], tt);
                }
            }
        }
        for (int id = 0; id < Ground.Length; id++)
        {
            float d = TrailDist[id];
            if (d > 4.6f) continue;
            float bed = trailH[id] - 0.05f;
            Ground[id] = Mathf.Lerp(bed, Natural[id], S01((d - 1.3f) / 3.3f));
        }
    }

    // ------------------------------------------------------------------ paint + forest
    public float Slope(int i, int j)
    {
        int a = Mathf.Max(0, i - 1), b = Mathf.Min(NX - 1, i + 1), c = Mathf.Max(0, j - 1), d = Mathf.Min(NZ - 1, j + 1);
        float gx = (Ground[Idx(b, j)] - Ground[Idx(a, j)]) / ((b - a) * Step);
        float gz = (Ground[Idx(i, d)] - Ground[Idx(i, c)]) / ((d - c) * Step);
        return Mathf.Sqrt(gx * gx + gz * gz);
    }

    public float MeadowMask(Vector2 p)
    {
        float m = 0f;
        foreach (Vector4 md in Meadows)
        {
            float dx = (p.x - md.x) / md.z, dz = (p.y - md.y) / md.w;
            float r = Mathf.Sqrt(dx * dx + dz * dz) * (1f + 0.25f * (CedarNoise.Fbm(p.x / 7f, p.y / 7f, 601, 2) - 0.5f));
            m = Mathf.Max(m, S01((1.15f - r) / 0.35f));
        }
        return m;
    }

    public float WoodlandClimate(Vector2 p, float elevation, float slope, float riverEdge)
    {
        float x = p.x, z = p.y;
        float wx = x + 36f * SignedNoise(x / 100f, z / 100f, 621);
        float wz = z + 32f * SignedNoise(x / 95f, z / 95f, 632);
        float canopy = CedarNoise.Fbm(wx / 63f, wz / 71f, 641, 4);
        float gaps = CedarNoise.Fbm(wx / 20f, wz / 23f, 658, 3);
        float stands = S01((canopy + .23f * (gaps - .5f) - .30f) / .36f);
        float openings = Mathf.Lerp(.22f, 1f, S01((gaps - .22f) / .28f));
        float moisture = Mathf.Lerp(.57f, 1f, CedarNoise.Gauss((riverEdge - 38f) / 86f));
        float altitude = Mathf.Lerp(1f, .18f, S01((elevation - 29f) / 48f));
        float rooting = 1f - S01((slope - .34f) / .39f);
        return (.025f + .95f * stands * openings) * moisture * altitude * rooting *
               S01((riverEdge - 3.5f) / 8f);
    }

    private void BuildPaint()
    {
        Paint = new Color[NX * NZ];
        Forest = new float[NX * NZ];
        Thin = new float[NX * NZ];
        Scour = new float[NX * NZ];
        Vector2 sunward = new Vector2(-.86f, .5f); // horizontal direction toward the winter sun
        for (int j = 0; j < NZ; j++)
        for (int i = 0; i < NX; i++)
        {
            int id = Idx(i, j);
            var p = new Vector2(GX(i), GZ(j));
            float slope = Slope(i, j);
            float d = RiverInfo(p, out int k, out float side);
            float e = d - River.HalfW[k] - BankOffset(k, side);
            float rd = RidgeDistance(p, out float crest);
            float sd = EscarpSigned(p, out float wc, out float hc);
            float n1 = CedarNoise.Fbm(p.x / 9f, p.y / 9f, 611, 3);

            // exposed rock: steep faces, wind-scoured ridge crest, the cliff band and knobs
            float exposure = CedarNoise.Ridged((p.x + p.y * .38f) / 29f, Ground[id] / 8f + p.y / 59f, 615);
            float rock = S01((slope - Mathf.Lerp(.57f, .32f, exposure)) / .52f);
            if (e < 4f) rock = Mathf.Max(rock * .68f, S01((slope - .29f) / .63f) * .62f);
            float crestExposure = S01((crest - 10f) / 15f) * S01((12f - rd) / 9f);
            float cliffExposure = CedarNoise.Gauss((sd - wc * .19f) / 16f) * (hc / 15f);
            rock = Mathf.Max(rock, Mathf.Max(crestExposure, cliffExposure) * S01((exposure - .30f) / .58f) * .82f);
            rock *= Mathf.Lerp(.54f, 1f, S01((n1 + exposure * .32f - .30f) / .5f));
            rock *= Mathf.Lerp(.08f, 1f, S01((TrailDist[id] - 1f) / 3f));
            rock = Mathf.Clamp01(rock);

            // forest density
            float f = WoodlandClimate(p, Ground[id], slope, e);
            f *= 1f - .88f * Mathf.Max(rock, crestExposure);
            f *= 1f - .91f * MeadowMask(p);
            f *= S01((Vector2.Distance(p, Camp) - 30f) / 12f);
            foreach (var kv in Nodes)
            {
                if (kv.Key == "Camp") continue;
                Vector2 dn = p - kv.Value;
                if (dn.y < 0f) dn.y *= 0.55f; // keep the ground south of sites (toward the camera) open
                f *= S01((dn.magnitude - 12f) / 6f);
            }
            f *= S01((TrailDist[id] - 3.2f) / 2.4f);
            foreach (BridgeModel b in Bridges) f *= S01((Vector2.Distance(p, b.Center) - 14f) / 6f);
            foreach (Vector2 site in ProtectedSites) f *= S01((Vector2.Distance(p, site) - 7f) / 8f);
            Forest[id] = Mathf.Clamp01(f);

            float packed = 0f;
            float rc = Vector2.Distance(p, Camp);
            if (rc < 22f) packed = Mathf.Max(packed, 0.32f * S01((22f - rc) / 8f) * (0.6f + 0.8f * n1));
            packed = Mathf.Max(packed, .52f * S01((3.25f - TrailDist[id]) / 2.5f));
            packed = Mathf.Max(packed, .30f * CedarNoise.Gauss((e - .8f) / 2.2f) * (0.4f + .6f * n1));
            Paint[id] = new Color(Mathf.Clamp01(packed), rock, 0f, 1f);

            // thin snow where sun-facing slopes melt out, trail shoulders are trampled, meadow
            // grass and bank edges poke through, and around camp; always patchy, never a band
            int ia = Mathf.Max(0, i - 1), ib = Mathf.Min(NX - 1, i + 1), ja = Mathf.Max(0, j - 1), jb = Mathf.Min(NZ - 1, j + 1);
            float gx = (Ground[Idx(ib, j)] - Ground[Idx(ia, j)]) / ((ib - ia) * Step);
            float gz = (Ground[Idx(i, jb)] - Ground[Idx(i, ja)]) / ((jb - ja) * Step);
            float sunFacing = Vector2.Dot(new Vector2(-gx, -gz), sunward);
            float thin = Mathf.Max(S01((sunFacing - .3f) / .5f) * .35f, .62f * CedarNoise.Gauss((TrailDist[id] - 3.4f) / 1.6f));
            thin = Mathf.Max(thin, .55f * MeadowMask(p));
            thin = Mathf.Max(thin, .5f * CedarNoise.Gauss((e - 1.6f) / 2.2f));
            thin = Mathf.Max(thin, .45f * S01((24f - rc) / 8f) * S01((rc - 6f) / 6f));
            thin *= 1f - .8f * rock; // exposed rock shows rock and snow, not soil
            float patchy = CedarNoise.Fbm(p.x / 13f, p.y / 13f, 623, 3);
            Thin[id] = Mathf.Clamp01(thin * Mathf.Lerp(.35f, 1.35f, patchy)) * (1f - .7f * packed);
            Scour[id] = Mathf.Clamp01(Mathf.Max(crestExposure * .95f, S01((Ground[id] - 24f) / 30f) * .45f));
        }
        // forest-floor shade follows a softened density field; AO darkens concave ground
        for (int j = 0; j < NZ; j++)
        for (int i = 0; i < NX; i++)
        {
            float sum = 0f; int cnt = 0;
            for (int dj = -2; dj <= 2; dj++)
            for (int di = -2; di <= 2; di++)
            {
                int a = Mathf.Clamp(i + di, 0, NX - 1), b = Mathf.Clamp(j + dj, 0, NZ - 1);
                sum += Forest[Idx(a, b)]; cnt++;
            }
            int id = Idx(i, j);
            float shade = Mathf.Clamp01(sum / cnt * 1.15f);
            float lap = 0f;
            if (i > 0 && j > 0 && i < NX - 1 && j < NZ - 1)
                lap = (Ground[Idx(i - 1, j)] + Ground[Idx(i + 1, j)] + Ground[Idx(i, j - 1)] + Ground[Idx(i, j + 1)]) * 0.25f - Ground[id];
            float ao = 1f - 0.09f * shade - Mathf.Clamp01(lap * 0.7f) * 0.12f;
            // convex shoulders (negative Laplacian) are the wind-exposed ones
            Scour[id] = Mathf.Clamp01(Mathf.Max(Scour[id], S01((-lap - .06f) / .3f) * .55f));
            Thin[id] = Mathf.Clamp01(Mathf.Max(Thin[id], shade * .22f * CedarNoise.Fbm(GX(i) / 7f, GZ(j) / 7f, 624, 2)));
            Color c = Paint[id];
            c.b = shade;
            c.a = ao;
            Paint[id] = c;
        }
    }
}
