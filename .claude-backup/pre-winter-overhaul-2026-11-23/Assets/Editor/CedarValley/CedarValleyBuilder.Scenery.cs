using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static partial class CedarValleyBuilder
{
    private static readonly List<Vector4> trees = new List<Vector4>(); // x, y, z, height
    private static readonly Color Timber = new Color(0.9f, 0.86f, 0.8f, 0f);
    private static readonly Color LogCol = new Color(0.82f, 0.78f, 0.74f, 0f);
    private static readonly Color EndGrain = new Color(0.86f, 0.72f, 0.55f, 0f);

    private static float R(int a, int b, int seed) => CedarNoise.Rand(a, b, seed);

    // ================================================================== crossings
    private static void BuildCrossing(BridgeModel b)
    {
        Transform crossings = landscape.Find("Crossings");
        if (crossings == null) crossings = Group("Crossings");
        Transform g = Group(b.Name, crossings);
        var mb = new MeshBuilder();
        var shards = new MeshBuilder();
        Vector3 ax = new Vector3(b.Axis.x, 0f, b.Axis.y);
        Quaternion rot = Quaternion.LookRotation(ax, Vector3.up);
        Vector3 lat = rot * Vector3.right;
        Vector3 origin = new Vector3(b.Center.x, 0f, b.Center.y);
        Vector3 P(float a, float y, float c) => origin + ax * a + lat * c + Vector3.up * y;
        float H = b.HalfSpan, deck = b.DeckY, hw = b.RiverHalfW;
        int seed = b.RiverIndex;
        float stringerBottom = deck - 0.36f;

        // --- deck: individual planks with small irregularities
        mb.UseSubmesh(0);
        const float pitch = 0.245f;
        int count = Mathf.FloorToInt(2f * H / pitch);
        float start = -count * pitch * 0.5f + pitch * 0.5f;
        for (int k = 0; k < count; k++)
        {
            float a = start + k * pitch;
            float len = 2.2f + (R(k, 1, seed) - 0.5f) * 0.14f;
            float off = (R(k, 2, seed) - 0.5f) * 0.07f;
            float yaw = (R(k, 3, seed) - 0.5f) * 2.6f;
            float dy = (R(k, 4, seed) - 0.5f) * 0.014f;
            float tone = 0.8f + 0.28f * R(k, 5, seed);
            if (R(k, 6, seed) > 0.93f) tone *= 0.72f;
            Color c = Timber * tone; c.a = 0f;
            Color top = c; top.a = 0.04f + 0.14f * R(k, 7, seed);
            mb.Box(P(a, deck - 0.03f + dy, off), new Vector3(len, 0.06f, 0.215f), rot * Quaternion.Euler(0f, yaw, 0f), c, 1f, top);
        }
        // snow drifted against the rails, trampled clear down the middle
        for (int side = -1; side <= 1; side += 2)
            for (float a = -H + 0.3f; a < H - 0.3f; a += 0.5f)
            {
                int k = Mathf.RoundToInt(a * 10f) + side * 1000;
                float h = 0.025f + 0.06f * R(k, 8, seed);
                float w = 0.22f + 0.16f * R(k, 9, seed);
                mb.Box(P(a, deck + h * 0.5f - 0.005f, side * (1.06f - w * 0.5f)), new Vector3(w, h, 0.56f), rot, new Color(1, 1, 1, 1));
            }

        // --- handrail posts (timber)
        for (int side = -1; side <= 1; side += 2)
            for (float a = -H + 0.25f; a <= H - 0.2f; a += (2f * H - 0.45f) / Mathf.Max(1, Mathf.Round((2f * H - 0.45f) / 2f)))
                mb.Box(P(a, deck + 0.48f, side * 1.14f), new Vector3(0.11f, 1.02f, 0.11f), rot, Timber * 0.85f, 1f, new Color(1, 1, 1, 0.8f));

        // --- logs: stringers, rails, cribs, sills
        mb.UseSubmesh(1);
        foreach (float c in new[] { -0.78f, 0f, 0.78f })
            mb.Cylinder(P(-H - 0.35f, deck - 0.21f, c), P(H + 0.35f, deck - 0.21f, c), 0.15f, 0.14f, 8, LogCol, true, EndGrain);
        for (int side = -1; side <= 1; side += 2)
        {
            mb.Cylinder(P(-H + 0.2f, deck + 0.97f, side * 1.14f), P(H - 0.2f, deck + 0.97f, side * 1.14f), 0.05f, 0.05f, 6, LogCol * 1.05f, true, EndGrain);
            mb.Cylinder(P(-H + 0.2f, deck + 0.52f, side * 1.14f), P(H - 0.2f, deck + 0.52f, side * 1.14f), 0.04f, 0.04f, 6, LogCol * 0.95f, true, EndGrain);
        }
        for (int e = -1; e <= 1; e += 2)
        {
            float inner = hw + 0.35f, outer = H + 0.25f;
            float bottom = float.MaxValue;
            for (int i = 0; i <= 2; i++)
            for (int j = 0; j <= 2; j++)
            {
                Vector3 q = P(e * Mathf.Lerp(inner, outer, i * 0.5f), 0f, (j - 1) * 1.4f);
                bottom = Mathf.Min(bottom, model.Surface(q.x, q.z));
            }
            bottom = Mathf.Min(bottom, b.IceY) - 0.45f;
            int layers = Mathf.Max(2, Mathf.RoundToInt((stringerBottom - bottom) / 0.27f));
            float lh = (stringerBottom - bottom) / layers;
            // rock fill inside the crib
            mb.Box(P(e * (inner + outer) * 0.5f, (bottom + stringerBottom) * 0.5f, 0f), new Vector3(2.3f, stringerBottom - bottom - 0.05f, outer - inner - 0.25f),
                rot, new Color(0.42f, 0.42f, 0.44f, 0.15f));
            for (int L = 0; L < layers; L++)
            {
                float y = bottom + lh * (L + 0.5f);
                float r = lh * 0.5f;
                if (L % 2 == 0)
                    foreach (float c in new[] { -1.22f, 1.22f })
                        mb.Cylinder(P(e * (inner - 0.12f), y, c), P(e * (outer + 0.12f), y, c), r, r * 0.95f, 7, LogCol * (0.9f + 0.15f * R(L, e + 3, seed)), true, EndGrain);
                else
                    foreach (float a in new[] { inner + 0.14f, outer - 0.14f })
                        mb.Cylinder(P(e * a, y, -1.5f), P(e * a, y, 1.5f), r, r * 0.95f, 7, LogCol * (0.9f + 0.15f * R(L, e + 7, seed)), true, EndGrain);
            }
            // sill log where the approach meets the deck
            mb.Cylinder(P(e * (H + 0.18f), deck - 0.12f, -1.45f), P(e * (H + 0.18f), deck - 0.12f, 1.45f), 0.13f, 0.12f, 7, LogCol, true, EndGrain);

            // shoreline rocks seated at the crib corners, broken shore ice beside the crib
            for (int c = -1; c <= 1; c += 2)
            {
                Vector3 q = P(e * (hw + 0.15f), 0f, c * (1.9f + 0.4f * R(c, e, seed)));
                PlaceKit("26_Shoreline_Rocks", g, new Vector2(q.x, q.z), R(c, e + 9, seed) * 360f, 0.9f + 0.35f * R(c, e + 11, seed), 0.12f, 0.35f);
            }
            for (int s = 0; s < 2; s++)
            {
                float u = (s == 0 ? -1f : 1f) * (4.5f + 3f * R(s, e, seed + 5));
                Vector3 q = origin + new Vector3(b.Along.x, 0f, b.Along.y) * u + ax * e * (hw - 1.1f);
                PlaceKitAt("25_Broken_Ice_Slabs", g, new Vector3(q.x, b.IceY - 0.02f, q.z), R(s, e, seed + 6) * 360f, 0.9f + 0.3f * R(s, e, seed + 7));
            }
            PlaceKit("13_Snow_Stakes", g, PlanXZ(P(e * (H + 4.5f), 0f, 1.9f)), R(e, 3, seed) * 360f, 1f, 0.05f, 0.25f);
        }
        // broken and heaved ice along both shores near the crossing
        Vector3 along = new Vector3(b.Along.x, 0f, b.Along.y);
        for (int k = 0; k < 34; k++)
        {
            float e = R(k, 1, seed + 20) < 0.5f ? -1f : 1f;
            float u = (R(k, 2, seed + 20) - 0.5f) * 20f;
            float inset = 0.25f + 1.8f * R(k, 3, seed + 20) * R(k, 3, seed + 20);
            Vector3 q = origin + along * u + ax * e * (hw - inset);
            float tilt = R(k, 4, seed + 20) < 0.4f ? 14f * R(k, 5, seed + 20) : 3f * R(k, 5, seed + 20);
            CedarMeshes.IceShard(shards, new Vector3(q.x, b.IceY + 0.01f + 0.05f * R(k, 6, seed + 20), q.z), 0.35f + 0.55f * R(k, 7, seed + 20),
                R(k, 8, seed + 20) * 360f, tilt * (R(k, 9, seed + 20) - 0.5f) * 2f, tilt * (R(k, 10, seed + 20) - 0.5f) * 2f, seed * 100 + k);
        }

        // --- mid-span trestle bent where the gap is wide
        float gap = 2f * (hw + 0.35f);
        if (gap > 6.5f)
        {
            mb.UseSubmesh(0);
            float bot = b.IceY - 1.0f, top = stringerBottom;
            foreach (float c in new[] { -1f, 1f })
            {
                mb.Box(P(0f, (bot + top) * 0.5f, c), new Vector3(0.24f, top - bot, 0.24f), rot, Timber * 0.78f, 1f, new Color(1, 1, 1, 0.9f));
                for (int k = 0; k < 6; k++)
                {
                    float ang = k / 6f * 360f + 20f * R(k, 1, seed + 40);
                    Vector3 q = P(0f, 0f, c) + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * (0.45f + 0.25f * R(k, 2, seed + 40));
                    CedarMeshes.IceShard(shards, new Vector3(q.x, b.IceY + 0.02f, q.z), 0.25f + 0.2f * R(k, 3, seed + 40), ang, 12f * R(k, 4, seed + 40), 8f, seed * 200 + k);
                }
            }
            mb.Box(P(0f, top - 0.12f, 0f), new Vector3(2.7f, 0.24f, 0.3f), rot, Timber * 0.8f, 1f, new Color(1, 1, 1, 0.6f));
            float y0 = b.IceY + 0.4f, y1 = top - 0.3f;
            float L = Mathf.Sqrt(4f + (y1 - y0) * (y1 - y0));
            float ang2 = Mathf.Atan2(y1 - y0, 2f) * Mathf.Rad2Deg;
            mb.Box(P(0f, (y0 + y1) * 0.5f, 0f), new Vector3(L, 0.12f, 0.08f), rot * Quaternion.Euler(0f, 0f, ang2), Timber * 0.75f);
            mb.Box(P(0.05f, (y0 + y1) * 0.5f, 0f), new Vector3(L, 0.12f, 0.08f), rot * Quaternion.Euler(0f, 0f, -ang2), Timber * 0.75f);
            Info(b.Name + ": trestle bent added (gap " + gap.ToString("0.0") + " m)");
        }

        Mesh bridge = mb.ToMesh(b.Name + " bridge");
        Mesh ice = shards.ToMesh(b.Name + " shore ice");
        SaveMeshes("Crossings", new[] { bridge, ice });
        MeshObject("Bridge", g, bridge, matTimber, matLogs);
        MeshObject("Broken shore ice", g, ice, matShard);

        // shadow of the deck on the ice (ground shadows would sit under the ice)
        int bs = propShadows.VertexCount;
        Vector3 sOff = new Vector3(shadowDir.x, 0f, shadowDir.y) * (deck - b.IceY) * 0.84f;
        for (int j = 0; j < 3; j++)
        for (int i = 0; i < 3; i++)
        {
            Vector3 q = P((j - 1) * (hw + 0.8f), b.IceY + 0.025f, (i - 1) * 1.7f) + sOff;
            Color c = ShadowTint; c.a = 0.55f;
            propShadows.Add(q, Vector3.up, c, new Vector2(i * 0.5f, j * 0.5f));
        }
        for (int j = 0; j < 2; j++)
        for (int i = 0; i < 2; i++)
        {
            int a = bs + j * 3 + i;
            propShadows.Tri(a, a + 3, a + 4); propShadows.Tri(a, a + 4, a + 1);
        }

        float gA = model.Surface(b.EndA.x, b.EndA.y), gB = model.Surface(b.EndB.x, b.EndB.y);
        Info(b.Name + ": deck " + (2 * H).ToString("0.0") + " m, " + count + " planks, deck top " + deck.ToString("0.00") +
             ", ice " + b.IceY.ToString("0.00") + ", ground at ends " + gA.ToString("0.00") + " / " + gB.ToString("0.00"));
    }

    private static Vector2 PlanXZ(Vector3 p) => new Vector2(p.x, p.z);

    private static GameObject PlaceKitAt(string kitName, Transform parent, Vector3 pos, float yaw, float scale)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + kitName + ".obj");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = Vector3.one * scale;
        go.name = kitName.Substring(3).Replace('_', ' ');
        kitPlaced.Add(go);
        return go;
    }

    // ================================================================== camp
    private static void BuildCamp()
    {
        Transform g = Group("Camp");
        Vector2 C = model.Camp;
        Vector2 P(float deg, float r) => C + new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * r;
        float Face(Vector2 at) { Vector2 d = C - at; return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg; }

        Transform structures = Group("Structures", g), gear = Group("Gear", g);
        var tents = new List<GameObject>();
        GameObject hq = PlaceKit("01_Base_Camp_HQ", structures, P(38f, 11.5f), Face(P(38f, 11.5f)), 1f, 0.06f, 0.55f);
        PlaceKit("22_Weather_Mast", structures, P(57f, 16.5f), 200f, 1f, 0.05f, 0.3f);
        foreach (float ang in new[] { 203f, 247f, 291f, 334f })
        {
            Vector2 at = P(ang, 10.8f);
            tents.Add(PlaceKit("02_Canvas_Expedition_Tent", structures, at, Face(at) + (R((int)ang, 1, 5) - 0.5f) * 10f, 1f, 0.05f, 0.5f));
        }
        PlaceKit("03_Field_Boiler", gear, P(21f, 8.4f), Face(P(21f, 8.4f)) - 25f, 1f, 0.04f, 0.45f);
        PlaceKit("17_Fallen_Timber", gear, P(57f, 8.8f), Face(P(57f, 8.8f)) + 90f, 0.62f, 0.06f, 0.4f);
        PlaceKit("10_Stacked_Supplies", gear, P(140f, 9.3f), Face(P(140f, 9.3f)), 1f, 0.04f, 0.5f);
        PlaceKit("09_Supply_Crate", gear, P(152f, 11.2f), Face(P(152f, 11.2f)) + 14f, 1f, 0.03f, 0.45f);
        PlaceKit("09_Supply_Crate", gear, P(129f, 11.6f), Face(P(129f, 11.6f)) - 22f, 1f, 0.03f, 0.45f);
        PlaceKit("09_Supply_Crate", gear, P(146f, 12.4f), Face(P(146f, 12.4f)) + 61f, 0.92f, 0.03f, 0.45f);
        foreach (Vector3 l in new[] { new Vector3(93f, 6.4f, 0f), new Vector3(190f, 6.9f, 1f), new Vector3(343f, 6.8f, 2f), new Vector3(30f, 13.6f, 3f) })
            PlaceKit("14_Lantern_Post", gear, P(l.x, l.y), Face(P(l.x, l.y)) + 90f, 1f, 0.08f, 0.3f);
        // stone fire ring with ash, charred ends and two log benches, just south of the hub
        var campRocks = new MeshBuilder();
        var campLogs = new MeshBuilder();
        Vector2 fire = P(268f, 5.9f);
        var stone = CedarMeshes.Boulder(31, 0f);
        for (int k = 0; k < 11; k++)
        {
            float ang = k / 11f * Mathf.PI * 2f + 0.2f * R(k, 1, 61);
            Vector2 q = fire + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 0.68f;
            float sz = 0.17f + 0.07f * R(k, 2, 61);
            campRocks.Append(stone, Matrix4x4.TRS(new Vector3(q.x, model.Surface(q.x, q.y) - 0.02f, q.y), Quaternion.Euler(0f, R(k, 3, 61) * 360f, 0f), new Vector3(sz, sz * 0.9f, sz)));
        }
        CedarMeshes.Shadow(propShadows, model, fire, shadowDir, 0.62f, 0.62f, 0.8f, new Color(0.33f, 0.31f, 0.3f));
        float fy = model.Surface(fire.x, fire.y);
        Color charred = new Color(0.32f, 0.27f, 0.24f, 0f);
        campLogs.Cylinder(new Vector3(fire.x - 0.35f, fy + 0.06f, fire.y - 0.1f), new Vector3(fire.x + 0.3f, fy + 0.12f, fire.y + 0.15f), 0.06f, 0.05f, 6, charred, true, new Color(0.15f, 0.13f, 0.12f, 0f));
        campLogs.Cylinder(new Vector3(fire.x - 0.1f, fy + 0.07f, fire.y + 0.35f), new Vector3(fire.x + 0.12f, fy + 0.1f, fire.y - 0.32f), 0.055f, 0.05f, 6, charred, true, new Color(0.15f, 0.13f, 0.12f, 0f));
        foreach (float ang in new[] { 215f, 320f })
        {
            Vector2 bc = fire + new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad)) * 1.85f;
            Vector2 tan = new Vector2(-Mathf.Sin(ang * Mathf.Deg2Rad), Mathf.Cos(ang * Mathf.Deg2Rad));
            Vector2 a = bc - tan * 0.95f, b2 = bc + tan * 0.95f;
            campLogs.Cylinder(new Vector3(a.x, model.Surface(a.x, a.y) + 0.12f, a.y), new Vector3(b2.x, model.Surface(b2.x, b2.y) + 0.12f, b2.y), 0.17f, 0.16f, 8, LogCol, true, EndGrain);
            CedarMeshes.Shadow(propShadows, model, bc + shadowDir * 0.15f, shadowDir, 0.55f, 1.05f, 0.45f, ShadowTint);
        }

        // extra guy lines and wooden pegs on the tent side walls
        var ropes = new MeshBuilder();
        var pegs = new MeshBuilder();
        foreach (GameObject t in tents)
        {
            if (t == null) continue;
            Transform tt = t.transform;
            for (int side = -1; side <= 1; side += 2)
                foreach (float z in new[] { -0.78f, 0.78f })
                {
                    Vector3 a = tt.TransformPoint(new Vector3(side * 1.36f, 1.08f, z));
                    Vector3 s = tt.TransformPoint(new Vector3(side * 2.4f, 0f, z * 1.22f));
                    s.y = model.Surface(s.x, s.z);
                    Vector3 knot = s + Vector3.up * 0.2f;
                    CedarMeshes.Rope(ropes, a, knot, 0.025f, 0.011f, Color.white);
                    Vector3 outward = (s - a); outward.y = 0f; outward.Normalize();
                    Quaternion pr = Quaternion.FromToRotation(Vector3.up, (Vector3.up + outward * 0.35f).normalized);
                    pegs.Box(s + Vector3.up * 0.1f, new Vector3(0.04f, 0.32f, 0.035f), pr, Timber * 0.7f, 1f, new Color(1, 1, 1, 0.5f));
                }
            Vector3 ra = tt.TransformPoint(new Vector3(0f, 1.78f, -1.5f));
            Vector3 rs = tt.TransformPoint(new Vector3(0f, 0f, -2.75f));
            rs.y = model.Surface(rs.x, rs.z);
            CedarMeshes.Rope(ropes, ra, rs + Vector3.up * 0.2f, 0.04f, 0.012f, Color.white);
            pegs.Box(rs + Vector3.up * 0.1f, new Vector3(0.045f, 0.34f, 0.04f), Quaternion.Euler(-18f, tt.eulerAngles.y, 0f), Timber * 0.7f, 1f, new Color(1, 1, 1, 0.5f));
        }
        Mesh ropeMesh = ropes.ToMesh("Camp guy lines"), pegMesh = pegs.ToMesh("Camp tent pegs");

        // trampled snow where people walk
        var walk = new MeshBuilder();
        CedarMeshes.GroundDisc(walk, model, C, 6.4f, 0.9f, 1);
        CedarMeshes.GroundDisc(walk, model, P(36f, 8.2f), 3.4f, 0.75f, 2);
        CedarMeshes.GroundDisc(walk, model, P(140f, 10.2f), 2.6f, 0.7f, 3);
        foreach (float ang in new[] { 203f, 247f, 291f, 334f })
            CedarMeshes.GroundPath(walk, model, new[] { P(ang, 4.5f), P(ang + 2f, 6.6f), P(ang, 8.3f) }, 0.5f, 0.8f, (int)ang);
        var ring = new List<Vector2>();
        for (float ang = 195f; ang <= 342f; ang += 12f) ring.Add(P(ang, 8.1f + 0.4f * Mathf.Sin(ang * 0.1f)));
        CedarMeshes.GroundPath(walk, model, ring, 0.55f, 0.6f, 9);
        CedarMeshes.GroundPath(walk, model, new[] { P(38f, 5f), P(40f, 7.5f), P(38f, 9f) }, 0.65f, 0.85f, 10);
        CedarMeshes.GroundPath(walk, model, new[] { P(21f, 5.5f), P(28f, 7.2f), P(44f, 9.6f), P(56f, 13.8f) }, 0.45f, 0.6f, 11);
        CedarMeshes.GroundPath(walk, model, new[] { P(186f, 5f), P(170f, 7f), P(150f, 9f) }, 0.5f, 0.6f, 12);
        foreach (TrailModel t in model.Trails)
        {
            if (t.From != "Camp") continue;
            var pts = new List<Vector2>();
            for (int i = 0; i < t.Line.Count && t.Line.S[i] < 19f; i += 3) pts.Add(t.Line.P[i]);
            CedarMeshes.GroundPath(walk, model, pts, 1.7f, 0.5f, t.Name.Length);
        }
        CedarMeshes.GroundDisc(walk, model, fire, 2.9f, 0.85f, 13);

        // a loaded freight sled by the supply stack
        var campTimber = new MeshBuilder();
        {
            Vector2 at = P(185f, 14f);
            float yaw = Face(at) + 70f;
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            float gy = model.Surface(at.x, at.y);
            Vector3 o = new Vector3(at.x, gy, at.y);
            Vector3 L(float x, float y, float z) => o + q * new Vector3(x, y, z);
            foreach (float side in new[] { -0.25f, 0.25f })
            {
                campTimber.Box(L(side, 0.04f, 0f), new Vector3(0.05f, 0.05f, 1.9f), q, Timber * 0.7f);
                campTimber.Box(L(side, 0.13f, 1.0f), new Vector3(0.05f, 0.05f, 0.28f), q * Quaternion.Euler(-40f, 0f, 0f), Timber * 0.7f);
                foreach (float z in new[] { -0.7f, 0f, 0.7f }) campTimber.Box(L(side, 0.16f, z), new Vector3(0.04f, 0.2f, 0.04f), q, Timber * 0.75f);
            }
            campTimber.Box(L(0f, 0.28f, 0f), new Vector3(0.58f, 0.05f, 1.7f), q, Timber * 0.95f, 1f, new Color(1, 1, 1, 0.35f));
            campTimber.Box(L(0f, 0.5f, -0.15f), new Vector3(0.5f, 0.38f, 1.05f), q, new Color(0.66f, 0.62f, 0.5f, 0f), 1f, new Color(1, 1, 1, 0.55f));
            CedarMeshes.Shadow(propShadows, model, at + shadowDir * 0.3f, shadowDir, 1.1f, 0.6f, 0.45f, ShadowTint);
        }
        // skis and poles stood upright in the snow between two tents
        {
            Vector2 at = P(224f, 9f);
            float gy = model.Surface(at.x, at.y);
            Vector2 side = new Vector2(Mathf.Cos(314f * Mathf.Deg2Rad), Mathf.Sin(314f * Mathf.Deg2Rad));
            for (int k = 0; k < 2; k++)
            {
                Vector2 p = at + side * (k * 0.32f);
                Quaternion lean = Quaternion.Euler(-7f + 4f * k, 44f + 6f * k, 0f);
                campTimber.Box(new Vector3(p.x, gy + 0.8f, p.y), new Vector3(0.075f, 1.75f, 0.022f), lean, new Color(0.62f, 0.38f, 0.26f, 0f));
                campTimber.Box(new Vector3(p.x + 0.09f, gy + 0.8f, p.y), new Vector3(0.075f, 1.75f, 0.022f), lean * Quaternion.Euler(0f, 0f, 3f), new Color(0.62f, 0.38f, 0.26f, 0f));
                Vector2 pp = p + side * 0.95f;
                campLogs.Cylinder(new Vector3(pp.x, gy - 0.1f, pp.y), new Vector3(pp.x + 0.05f, gy + 1.25f, pp.y + 0.04f), 0.011f, 0.011f, 4, new Color(0.3f, 0.3f, 0.32f, 0f), false);
            }
        }
        // split firewood stacked near the boiler
        {
            Vector2 at = P(15f, 11.8f);
            float yaw = Face(at) + 90f;
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            float gy = model.Surface(at.x, at.y);
            Vector3 o = new Vector3(at.x, gy, at.y);
            int k = 0;
            for (int row = 0; row < 3; row++)
                for (int c = 0; c < 4 - row; c++, k++)
                {
                    float x = (c - (3 - row) * 0.5f) * 0.23f, y = 0.11f + row * 0.19f;
                    Vector3 a = o + q * new Vector3(x, y, -0.6f + 0.05f * R(k, 1, 88)), b = o + q * new Vector3(x, y, 0.6f - 0.05f * R(k, 2, 88));
                    campLogs.Cylinder(a, b, 0.105f, 0.105f, 7, LogCol * (0.85f + 0.2f * R(k, 3, 88)), true, EndGrain);
                }
            CedarMeshes.Shadow(propShadows, model, at + shadowDir * 0.3f, shadowDir, 0.9f, 0.8f, 0.4f, ShadowTint);
        }
        Mesh walkMesh = walk.ToMesh("Camp trampled snow");
        Mesh rockMesh = campRocks.ToMesh("Camp fire ring stones"), logMesh = campLogs.ToMesh("Camp logs and poles");
        Mesh timberMesh = campTimber.ToMesh("Camp sled and skis");
        SaveMeshes("Camp", new[] { ropeMesh, pegMesh, walkMesh, rockMesh, logMesh, timberMesh });
        MeshObject("Fire ring stones", gear, rockMesh, matFireStone);
        MeshObject("Logs, benches and poles", gear, logMesh, matLogs);
        MeshObject("Sled and skis", gear, timberMesh, matTimber);
        MeshObject("Guy lines", gear, ropeMesh, matRope);
        MeshObject("Tent pegs", gear, pegMesh, matTimber);
        MeshObject("Trampled snow", g, walkMesh, matTrampled);
        Info("camp: " + structures.childCount + " structures, " + gear.childCount + " gear items, " + tents.Count + " tents with extra guy lines");
    }

    // ================================================================== forest
    private static void BuildForest()
    {
        Transform g = Group("Forest");
        trees.Clear();
        tierChunks.Clear();
        const float spacingCell = 4f, chunk = 64f;
        var spacing = new Dictionary<Vector2Int, List<(Vector2 point, float radius)>>();
        Vector2Int Cell(Vector2 p) => new(Mathf.FloorToInt(p.x / spacingCell), Mathf.FloorToInt(p.y / spacingCell));
        // Uniform random proposals + variable-radius rejection produce a seeded, non-grid forest.
        // Low-density edges take fewer proposals, and wide mature crowns need more room.
        for (int sample = 0; sample < 42000; sample++)
        {
            float x = Mathf.Lerp(CedarModel.X0 + 2f, CedarModel.X1 - 2f, R(sample, 11, 901));
            float z = Mathf.Lerp(CedarModel.Z0 + 2f, CedarModel.Z1 - 2f, R(sample, 21, 902));
            var p = new Vector2(x, z);
            float f = model.Sample(model.Forest, x, z);
            float establishment = f * f * Mathf.Lerp(.70f, 1.85f, Mathf.SmoothStep(0f, 1f, f * 1.8f));
            if (f < .016f || R(sample, 31, 903) > establishment) continue;
            if (model.Sample(model.TrailDist, x, z) < 4.1f || model.SurfaceNormal(x, z).y < .83f) continue;
            float bank = model.RiverInfo(p, out int section, out float side) -
                         model.River.HalfW[section] - model.BankOffset(section, side);
            if (bank < 5.5f) continue;
            var candidate = new Vector4(x, model.Surface(x, z), z, 0f);
            TreeTier tree = ChooseVariant(candidate, sample, 37);
            float radius = Mathf.Clamp(CedarConifers.Forms[tree.Variant].Radius * tree.Scale * .8f +
                                       Mathf.Lerp(1.1f, .50f, f), 1.35f, 3.45f);
            Vector2Int cell = Cell(p);
            bool clear = true;
            for (int j = -2; j <= 2 && clear; j++)
            for (int i = -2; i <= 2 && clear; i++)
            {
                if (!spacing.TryGetValue(cell + new Vector2Int(i, j), out var neighbors)) continue;
                foreach (var neighbor in neighbors)
                {
                    float separation = radius + neighbor.radius;
                    if ((p - neighbor.point).sqrMagnitude >= separation * separation) continue;
                    clear = false;
                    break;
                }
            }
            if (!clear) continue;
            if (!spacing.TryGetValue(cell, out var occupants)) spacing[cell] = occupants = new();
            occupants.Add((p, radius));
            int key = Mathf.FloorToInt((x - CedarModel.X0) / chunk) + 100 * Mathf.FloorToInt((z - CedarModel.Z0) / chunk);
            trees.Add(new Vector4(x, tree.Pos.y, z, tree.Height));
            if (!tierChunks.TryGetValue(key, out var placements)) tierChunks[key] = placements = new();
            placements.Add((candidate, sample, 37));
        }
        BuildTierChunks(g);

        // sparse woodland on the surrounding slopes (low detail only)
        // Split the surrounding woodland so off-camera slopes are culled, rather than
        // submitting every tree outside the board in one always-visible giant mesh.
        var surround = new Dictionary<Vector2Int, MeshBuilder>();
        int fc = 0;
        for (float z = -230f; z < 440f; z += 6f)
        for (float x = -320f; x < 320f; x += 6f)
        {
            int a = Mathf.RoundToInt(x), b = Mathf.RoundToInt(z);
            float jx = x + (R(a, b, 911) - 0.5f) * 5f, jz = z + (R(a, b, 912) - 0.5f) * 5f;
            if (jx > CedarModel.X0 - 2f && jx < CedarModel.X1 + 2f && jz > CedarModel.Z0 - 2f && jz < CedarModel.Z1 + 2f) continue;
            var point = new Vector2(jx, jz);
            float edge = model.RiverInfo(point, out int k, out float side) - model.River.HalfW[k] - model.BankOffset(k, side);
            float y = CedarMeshes.ApronSurface(model, jx, jz);
            float up = model.SurfaceNormal(jx, jz).y;
            float d = model.WoodlandClimate(point, y, Mathf.Sqrt(Mathf.Max(0f, 1f - up * up)) / Mathf.Max(.1f, up), edge);
            if (R(a, b, 913) > d * d * .68f) continue;
            float h = Mathf.Lerp(7f, 12f, R(a, b, 914));
            int variant = Mathf.Min(CedarConifers.Forms.Length - 1, (int)(R(a, b, 917) * CedarConifers.Forms.Length));
            if (variant is 2 or 6) h *= .55f;
            float scale = h / CedarConifers.Forms[variant].Height;
            var key = new Vector2Int(Mathf.FloorToInt(jx / 128f), Mathf.FloorToInt(jz / 128f));
            if (!surround.TryGetValue(key, out MeshBuilder batch))
                surround[key] = batch = new MeshBuilder();
            batch.Append(farTpl[variant], Matrix4x4.TRS(new Vector3(jx, y - 0.2f, jz),
                Quaternion.Euler(0f, R(a, b, 915) * 360f, 0f), Vector3.one * scale));
            fc++;
        }
        var outskirts = Group("Surrounding woodland", g);
        var surroundMeshes = new List<Mesh>(surround.Count);
        foreach (var (key, batch) in surround)
        {
            Mesh m = batch.ToMesh("Woodland slope " + key.x + ", " + key.y);
            surroundMeshes.Add(m);
            MeshObject(m.name, outskirts, m, matConifer);
        }
        if (surroundMeshes.Count > 0) SaveForestMeshes("ForestSurroundTiers", surroundMeshes);
        Info("forest: " + trees.Count + " trees in " + tierChunks.Count + " tiered chunks; surround " + fc + " trees in " + surround.Count + " batches");
    }

    // ================================================================== boulders
    private static void BuildRocks()
    {
        var rocks = new MeshBuilder();
        var tpl = new MeshBuilder[6];
        for (int i = 0; i < tpl.Length; i++) tpl[i] = CedarMeshes.Boulder(i + 3, 0.85f);
        int count = 0;
        bool Clear(Vector2 p, float trailGap)
        {
            if (p.x < CedarModel.X0 + 2 || p.x > CedarModel.X1 - 2 || p.y < CedarModel.Z0 + 2 || p.y > CedarModel.Z1 - 2) return false;
            if (model.Sample(model.TrailDist, p.x, p.y) < trailGap) return false;
            foreach (var kv in model.Nodes) if (Vector2.Distance(p, kv.Value) < (kv.Key == "Camp" ? 26f : 7f)) return false;
            foreach (BridgeModel b in model.Bridges) if (Vector2.Distance(p, b.Center) < b.HalfSpan + 1.5f) return false;
            return true;
        }
        void Put(Vector2 p, float size, int k, float sink)
        {
            float y = model.Surface(p.x, p.y) - size * sink;
            rocks.Append(tpl[k % tpl.Length], Matrix4x4.TRS(new Vector3(p.x, y, p.y), Quaternion.Euler(0f, R(k, 77, 41) * 360f, 0f),
                new Vector3(size * (0.9f + 0.3f * R(k, 1, 42)), size * (0.7f + 0.3f * R(k, 2, 42)), size)));
            CedarMeshes.Shadow(propShadows, model, p + shadowDir * size * 0.45f, shadowDir, size * 1.25f, size * 1.05f, 0.38f, ShadowTint);
            count++;
        }
        // bank stones: clustered at the toe of the banks, more on the cut (outside) banks
        RiverModel r = model.River;
        for (int i = 0; i < r.Line.Count; i += 3)
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 p0 = r.Line.P[i];
                float cl = CedarNoise.Fbm(r.Line.S[i] / 14f, side * 9f, 951, 2);
                float outside = Mathf.Clamp01(r.Curv[i] * side * 55f);
                if (R(i, side + 5, 952) > cl * 0.8f + outside * 0.35f - 0.2f) continue;
                Vector2 p = p0 + r.Line.Normal(i) * side * (r.HalfW[i] + model.BankOffset(i, side) + 0.1f + 2.2f * R(i, side + 7, 953));
                if (!Clear(p, 2.4f)) continue;
                float sz = 0.3f + 1.0f * R(i, side + 9, 954) * R(i, side + 9, 954);
                Put(p, sz, i + side, 0.25f);
            }
        // talus below the Cliff Pass escarpment
        Polyline esc = model.Escarp;
        for (int i = 0; i < esc.Count; i += 2)
        {
            if (R(i, 1, 961) > 0.55f) continue;
            Vector2 p = esc.P[i] - esc.Normal(i) * (3f + 12f * R(i, 2, 961) * R(i, 2, 961));
            if (!Clear(p, 3f)) continue;
            Put(p, 0.6f + 1.5f * R(i, 3, 961) * R(i, 3, 961), i + 300, 0.2f);
        }
        // wind-scoured stones on the North Ridge crests
        foreach (Polyline crest in new[] { model.RidgeMain, model.RidgeSpur })
            for (int i = 0; i < crest.Count; i += 3)
            {
                if (crest.P[i].y < -10f || R(i, 1, 971 + crest.Count) > 0.42f) continue;
                Vector2 p = crest.P[i] + crest.Normal(i) * ((R(i, 2, 972) - 0.5f) * 16f);
                if (!Clear(p, 2.6f)) continue;
                Put(p, 0.45f + 1.1f * R(i, 3, 973) * R(i, 3, 973), i + 900, 0.3f);
            }
        // loose rock around the Cliff Pass outcrops
        int kn = 0;
        foreach (Knob k in model.Knobs)
        {
            for (int j = 0; j < 12; j++)
            {
                float ang = R(kn, j, 981) * Mathf.PI * 2f;
                Vector2 p = k.P + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * k.R * (0.8f + 0.7f * R(kn, j, 982));
                if (!Clear(p, 2.8f)) continue;
                Put(p, 0.5f + 1.3f * R(kn, j, 983) * R(kn, j, 983), kn * 40 + j + 2000, 0.25f);
            }
            kn++;
        }
        Mesh m = rocks.ToMesh("Boulders");
        SaveMeshes("Rocks", new[] { m });
        MeshObject("Boulders", landscape, m, matRock);
        Info("boulders: " + count);
    }

    private static void BuildShadows()
    {
        Mesh m = propShadows.ToMesh("Prop contact shadows");
        SaveMeshes("Shadows", new[] { m });
        MeshObject("Prop contact shadows", landscape, m, matShadow);
    }

    // ================================================================== checks
    private static void Checks()
    {
        Info("---- checks");
        // trail separation (ignoring the shared site at either end)
        for (int a = 0; a < model.Trails.Count; a++)
        for (int b = a + 1; b < model.Trails.Count; b++)
        {
            TrailModel ta = model.Trails[a], tb = model.Trails[b];
            float best = float.MaxValue;
            for (int i = 0; i < ta.Line.Count; i += 2)
            {
                Vector2 p = ta.Line.P[i];
                if (NearSharedSite(p, ta, tb)) continue;
                for (int j = 0; j < tb.Line.Count; j += 2)
                {
                    Vector2 q = tb.Line.P[j];
                    if (NearSharedSite(q, ta, tb)) continue;
                    best = Mathf.Min(best, Vector2.Distance(p, q));
                }
            }
            if (best < 12f) Info("trail proximity " + ta.Name + " ~ " + tb.Name + ": " + best.ToString("0.0") + " m");
        }
        // trails stay out of the river except on the decks
        foreach (TrailModel t in model.Trails)
        {
            float minEdge = float.MaxValue, maxGrade = 0f, maxCut = 0f;
            Vector2 at = Vector2.zero;
            for (int i = 0; i < t.Line.Count; i++)
            {
                if (t.OnDeck[i]) continue;
                float d = model.RiverInfo(t.Line.P[i], out int k, out _);
                float e = d - model.River.HalfW[k];
                if (t.Bridge != null && Vector2.Distance(t.Line.P[i], t.Bridge.Center) < t.Bridge.HalfSpan + 2f) continue;
                minEdge = Mathf.Min(minEdge, e);
                if (i > 0)
                {
                    float g = Mathf.Abs(t.Y[i] - t.Y[i - 1]) / Mathf.Max(0.01f, t.Line.S[i] - t.Line.S[i - 1]);
                    if (g > maxGrade) { maxGrade = g; at = t.Line.P[i]; }
                }
                maxCut = Mathf.Max(maxCut, Mathf.Abs(model.Sample(model.Natural, t.Line.P[i].x, t.Line.P[i].y) - t.Y[i]));
            }
            Info(t.Name + ": min distance to water edge " + minEdge.ToString("0.0") + " m, max grade " + (maxGrade * 100f).ToString("0") +
                 "% at " + at.ToString("0") + ", max cut/fill " + maxCut.ToString("0.0") + " m");
        }
        // trees near sites (clicks pass through scenery: it has no colliders)
        foreach (var kv in model.Nodes)
        {
            float best = float.MaxValue; int south = 0;
            Vector2 n = kv.Value;
            foreach (Vector4 t in trees)
            {
                Vector2 d = new Vector2(t.x - n.x, t.z - n.y);
                best = Mathf.Min(best, d.magnitude);
                // a tree south of a site (nearer the camera) hides it if within ~1.3 x its height
                if (d.y < 0f && Mathf.Abs(d.x) < 3.5f && -d.y < t.w * 1.28f) south++;
            }
            Info("site " + kv.Key + ": ground " + (kv.Key == "Camp" ? model.CampY : model.NodeGround[kv.Key]).ToString("0.00") +
                 ", nearest tree " + best.ToString("0.0") + " m" + (south > 0 ? ", OCCLUDING TREES " + south : ""));
        }
        // props resting on the ground
        int floating = 0;
        foreach (GameObject go in kitPlaced)
        {
            if (go == null) continue;
            Bounds b = WorldBounds(go);
            float worst = 0f;
            for (int i = 0; i <= 2; i += 2)
            for (int j = 0; j <= 2; j += 2)
            {
                float x = Mathf.Lerp(b.min.x, b.max.x, 0.15f + i * 0.35f), z = Mathf.Lerp(b.min.z, b.max.z, 0.15f + j * 0.35f);
                worst = Mathf.Max(worst, b.min.y - model.Surface(x, z));
            }
            if (worst > 0.06f && !go.name.Contains("Broken Ice")) { floating++; Info("FLOATING " + go.name + " gap " + worst.ToString("0.00")); }
        }
        Info("kit props placed: " + kitPlaced.Count + ", floating: " + floating);
        // every original connection still ends exactly on its two sites
        Transform trails = GameObject.Find("Graph/Trails").transform;
        int bad = 0;
        foreach (TrailModel t in model.Trails)
        {
            LineRenderer lr = trails.Find(t.Name).GetComponent<LineRenderer>();
            Vector3 a = lr.GetPosition(0), b = lr.GetPosition(lr.positionCount - 1);
            float ea = Vector2.Distance(new Vector2(a.x, a.z), model.Nodes[t.From]), eb = Vector2.Distance(new Vector2(b.x, b.z), model.Nodes[t.To]);
            if (ea > 0.05f || eb > 0.05f) { bad++; Info("ENDPOINT MISMATCH " + t.Name + " " + ea.ToString("0.00") + " / " + eb.ToString("0.00")); }
        }
        Info("trails: " + model.Trails.Count + " connections checked, endpoint mismatches " + bad + ", trail objects in scene " + trails.childCount);
    }

    private static bool NearSharedSite(Vector2 p, TrailModel a, TrailModel b)
    {
        foreach (string id in new[] { a.From, a.To })
            if ((id == b.From || id == b.To) && Vector2.Distance(p, model.Nodes[id]) < 16f) return true;
        return false;
    }
}
