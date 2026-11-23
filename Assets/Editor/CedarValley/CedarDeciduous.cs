using System.Collections.Generic;
using UnityEngine;

// Leafless, frost-covered broadleaf trees and shrubs for the winter valley forest.
// Each form grows a seeded branch skeleton (trunk -> limbs -> laterals); the woody parts become
// tapered cylinders (frost on their upper faces) and the fine twigs become alpha-tested crown
// cards from the frosted-twig atlas. Detail tiers share one skeleton:
//   far   trunk + a few large limb cards (overview canopy mass),
//   mid   trunk and limbs as wood + one twig card per lateral,
//   close only the additions: lateral wood and crossed twig cards (appended on top of mid).
internal static class CedarDeciduous
{
    internal readonly struct Form
    {
        public readonly string Name;
        public readonly float Height, TrunkRadius, SplitAt, LeaderAngle, CrownRadius, LateralAngle, Lean, Asymmetry, Frost;
        public readonly int Leaders, Laterals, TrunkLaterals, Stems, TwigCell;
        public readonly bool Shrub;

        public Form(string name, float height, float trunkRadius, float splitAt, int leaders, float leaderAngle,
            float crownRadius, int laterals, float lateralAngle, int trunkLaterals, int stems, float lean,
            float asymmetry, float frost, int twigCell, bool shrub = false)
        {
            Name = name; Height = height; TrunkRadius = trunkRadius; SplitAt = splitAt; Leaders = leaders;
            LeaderAngle = leaderAngle; CrownRadius = crownRadius; Laterals = laterals; LateralAngle = lateralAngle;
            TrunkLaterals = trunkLaterals; Stems = stems; Lean = lean; Asymmetry = asymmetry; Frost = frost;
            TwigCell = twigCell; Shrub = shrub;
        }
    }

    internal static readonly Form[] Forms =
    {
        //   name                     H     trunkR split ldrs ldrAng crownR lat latAng trunkLat stems lean  asym frost cell
        new("Frosted oak",           13.5f, .030f, .34f, 3, 38f,  .46f,  7, 52f, 2, 1, .05f, 0f,   .9f, 0),
        new("Frosted birch",         14.5f, .017f, .93f, 1, 4f,   .24f,  5, 38f, 12, 1, .08f, 0f,   .95f, 1),
        new("Frosted maple",         12.0f, .025f, .45f, 3, 30f,  .38f,  7, 46f, 1, 1, .03f, 0f,   .9f, 3),
        new("Young broadleaf",        6.8f, .016f, .55f, 2, 22f,  .26f,  5, 40f, 2, 1, .06f, 0f,   .85f, 1),
        new("Broken old broadleaf",  11.0f, .033f, .40f, 2, 44f,  .40f,  6, 55f, 2, 1, .16f, .65f, .9f, 2),
        new("Riverside alder",        9.2f, .016f, .50f, 2, 18f,  .30f,  5, 44f, 2, 3, .10f, 0f,   .95f, 3),
        new("Frosted hazel shrub",    2.6f, .010f, .05f, 1, 0f,   .45f,  3, 40f, 0, 6, .0f,  0f,   .75f, 6, true),
        new("Bare willow shrub",      1.8f, .009f, .05f, 1, 0f,   .55f,  3, 46f, 0, 8, .0f,  0f,   .6f,  6, true),
    };

    private struct Seg { public Vector3 A, B; public float Ra, Rb; public int Depth; }
    private struct Card { public Vector3 Base, Dir; public float Width, Length, Roll; public int Cell, Tier; }

    private sealed class Skeleton
    {
        public readonly List<Seg> Segs = new List<Seg>();
        public readonly List<Card> Cards = new List<Card>();
        public Vector3 CrownCenter;
        public float CrownSize, CrownBase, CrownTop, CrownRadius;
    }

    private static float Rand(int a, int b, int seed) => CedarNoise.Rand(a, b, seed);

    private static Vector3 Perp(Vector3 d)
    {
        Vector3 p = Vector3.Cross(d, Mathf.Abs(d.y) < 0.95f ? Vector3.up : Vector3.right);
        return p.normalized;
    }

    private static Skeleton Grow(int variant)
    {
        Form f = Forms[variant];
        int seed = 3100 + variant * 211;
        var sk = new Skeleton();
        int counter = 0;
        float crownR = f.CrownRadius * f.Height;
        Vector3 leanDir = new Vector3(Mathf.Cos(variant * 1.7f), 0f, Mathf.Sin(variant * 1.7f));

        // a limb: a few bent segments, with laterals (depth + 1) along it and twig cards on laterals
        void Limb(Vector3 start, Vector3 dir, float length, float radius, int depth, float lateralLen)
        {
            int segs = depth == 0 ? 4 : depth == 1 ? 3 : 2;
            Vector3 p = start;
            Vector3 d = dir.normalized;
            float r = radius;
            int id = counter++;
            for (int s = 0; s < segs; s++)
            {
                Vector3 jitter = new Vector3(Rand(id, s, seed) - .5f, (Rand(id, s + 9, seed) - .5f) * .4f, Rand(id, s + 19, seed) - .5f);
                d = (d + jitter * (depth == 0 ? .12f : .32f)).normalized;
                if (depth >= 2) d = (d + Vector3.down * .10f).normalized; // fine laterals droop slightly
                if (depth == 1) d = (d + Vector3.up * .06f).normalized;
                Vector3 q = p + d * length / segs;
                float r1 = Mathf.Lerp(radius, radius * (depth == 0 ? .55f : .25f), (s + 1f) / segs);
                sk.Segs.Add(new Seg { A = p, B = q, Ra = r, Rb = r1, Depth = depth });

                if (depth == 1 && s >= 1 || depth == 1 && segs == 1)
                {
                    int lat = Mathf.Max(1, Mathf.RoundToInt(f.Laterals / (float)(segs - 1)));
                    for (int k = 0; k < lat; k++)
                    {
                        int lid = counter++;
                        float t = (k + Rand(lid, 1, seed)) / lat;
                        Vector3 o = Vector3.Lerp(p, q, t);
                        float spin = lid * 2.39996f;
                        Vector3 side = Quaternion.AngleAxis(spin * Mathf.Rad2Deg, d) * Perp(d);
                        if (f.Asymmetry > 0f && Vector3.Dot(side, leanDir) < -.2f && Rand(lid, 2, seed) < f.Asymmetry) continue;
                        float ang = f.LateralAngle * Mathf.Lerp(.75f, 1.2f, Rand(lid, 3, seed));
                        Vector3 cd = (d * Mathf.Cos(ang * Mathf.Deg2Rad) + side * Mathf.Sin(ang * Mathf.Deg2Rad)).normalized;
                        float len = lateralLen * Mathf.Lerp(.7f, 1.15f, Rand(lid, 4, seed)) * Mathf.Lerp(1f, .6f, (s + t) / segs);
                        Limb(o, cd, len, Mathf.Max(.012f, r1 * .5f), 2, 0f);
                    }
                }
                p = q;
                r = r1;
            }
            if (depth == 2)
            {
                // twig cards along the outer part of each lateral; close tier crosses them
                float w = length * 1.35f;
                sk.Cards.Add(new Card { Base = Vector3.Lerp(start, p, .18f), Dir = (p - start).normalized, Width = w, Length = length * 1.15f,
                                        Roll = Rand(id, 31, seed) * 180f, Cell = f.TwigCell == 6 ? 6 : (f.TwigCell + (id & 1) * 2) % 4, Tier = 1 });
                sk.Cards.Add(new Card { Base = Vector3.Lerp(start, p, .3f), Dir = (p - start).normalized, Width = w * .85f, Length = length,
                                        Roll = Rand(id, 31, seed) * 180f + 90f, Cell = f.TwigCell == 6 ? 6 : (f.TwigCell + 1) % 4, Tier = 2 });
            }
            else if (depth == 1)
            {
                // one broad card per limb carries the canopy mass at overview distance
                sk.Cards.Add(new Card { Base = Vector3.Lerp(start, p, .32f), Dir = (p - start).normalized, Width = Mathf.Max(lateralLen * 2.1f, length * .8f),
                                        Length = length * .95f + lateralLen * .4f, Roll = Rand(id, 41, seed) * 180f, Cell = f.TwigCell == 6 ? 6 : f.TwigCell, Tier = 0 });
            }
        }

        for (int stem = 0; stem < f.Stems; stem++)
        {
            float sa = stem * 2.39996f + Rand(stem, 1, seed);
            Vector3 baseOff = f.Stems > 1 ? new Vector3(Mathf.Cos(sa), 0f, Mathf.Sin(sa)) * f.Height * (f.Shrub ? .05f : .03f) : Vector3.zero;
            Vector3 stemDir = (Vector3.up + leanDir * f.Lean + baseOff.normalized * (f.Stems > 1 ? (f.Shrub ? .55f : .22f) : 0f)).normalized;
            float h = f.Height * (f.Stems > 1 ? Mathf.Lerp(.72f, 1f, Rand(stem, 2, seed)) : 1f);
            float trunkR = f.TrunkRadius * f.Height * (f.Stems > 1 ? .7f : 1f);
            if (f.Shrub)
            {
                // shrubs: thin stems, each a limb (depth 1) with laterals carrying shrub cards
                Limb(baseOff - Vector3.up * .05f, stemDir, h, trunkR, 1, crownR * .55f);
                continue;
            }
            Vector3 split = baseOff + stemDir * h * f.SplitAt;
            Limb(baseOff - Vector3.up * .3f, stemDir, h * f.SplitAt + .3f, trunkR, 0, 0f);
            // laterals off the trunk below the crown split (the main structure for birch)
            for (int k = 0; k < f.TrunkLaterals; k++)
            {
                int lid = counter++;
                float t = Mathf.Lerp(.32f, .98f, (k + Rand(lid, 1, seed) * .6f) / Mathf.Max(1, f.TrunkLaterals));
                Vector3 o = baseOff + stemDir * h * f.SplitAt * t;
                float spin = lid * 2.39996f;
                Vector3 side = new Vector3(Mathf.Cos(spin), 0f, Mathf.Sin(spin));
                if (f.Asymmetry > 0f && Vector3.Dot(side, leanDir) < -.2f && Rand(lid, 2, seed) < f.Asymmetry) continue;
                float up = Mathf.Lerp(.5f, 1.4f, t) * (f.Leaders == 1 ? 1f : .7f);
                Vector3 cd = (side + Vector3.up * up).normalized;
                float len = crownR * Mathf.Lerp(1.1f, .45f, t) * Mathf.Lerp(.8f, 1.15f, Rand(lid, 3, seed));
                Limb(o, cd, len, trunkR * Mathf.Lerp(.45f, .25f, t), 1, crownR * .42f);
            }
            for (int l = 0; l < f.Leaders; l++)
            {
                if (f.Leaders == 1) break;
                int lid = counter++;
                float spin = l * Mathf.PI * 2f / f.Leaders + Rand(lid, 1, seed) * .7f;
                Vector3 side = new Vector3(Mathf.Cos(spin), 0f, Mathf.Sin(spin));
                if (f.Asymmetry > 0f && Vector3.Dot(side, leanDir) < -.3f) side = (side + leanDir).normalized;
                float ang = f.LeaderAngle * Mathf.Lerp(.75f, 1.25f, Rand(lid, 2, seed));
                Vector3 ld = (stemDir * Mathf.Cos(ang * Mathf.Deg2Rad) + side * Mathf.Sin(ang * Mathf.Deg2Rad)).normalized;
                float len = (h - h * f.SplitAt) / Mathf.Max(.4f, ld.y) * Mathf.Lerp(.82f, 1.02f, Rand(lid, 3, seed));
                Limb(split, ld, len, trunkR * .62f, 1, crownR * .55f);
            }
        }
        // crown centre (for card normals) and extent
        Vector3 sum = Vector3.zero; int n = 0;
        float top = 0f;
        foreach (Card c in sk.Cards) { sum += c.Base + c.Dir * c.Length * .5f; n++; top = Mathf.Max(top, (c.Base + c.Dir * c.Length).y); }
        sk.CrownCenter = n > 0 ? sum / n : Vector3.up * f.Height * .6f;
        sk.CrownSize = Mathf.Max(1f, crownR);
        // crown envelope from the branch skeleton: fork height, top, widest reach from the axis
        float rad = 0f, tp = 0f;
        foreach (Seg sg in sk.Segs)
        {
            if (sg.Depth == 0) continue;
            tp = Mathf.Max(tp, sg.B.y);
            Vector2 d = new Vector2(sg.B.x - sk.CrownCenter.x, sg.B.z - sk.CrownCenter.z);
            rad = Mathf.Max(rad, d.magnitude);
        }
        sk.CrownTop = Mathf.Max(tp, top) + .3f;
        sk.CrownBase = f.Shrub ? 0f : Mathf.Min(f.Height * f.SplitAt * .8f, f.Height * .32f);
        if (f.Leaders == 1 && !f.Shrub) sk.CrownBase = f.Height * .26f;
        sk.CrownRadius = Mathf.Max(.6f, rad * 1.02f);
        return sk;
    }

    private static readonly Dictionary<int, Skeleton> cache = new Dictionary<int, Skeleton>();

    /// <summary>Wood and crown-card templates for one tier (0 far, 1 mid, 2 close additions only).</summary>
    internal static void Build(int variant, int tier, out MeshBuilder wood, out MeshBuilder cards)
    {
        if (!cache.TryGetValue(variant, out Skeleton sk)) cache[variant] = sk = Grow(variant);
        Form f = Forms[variant];
        wood = new MeshBuilder();
        cards = new MeshBuilder();
        Color barkCol = new Color(.82f, .78f, .74f, 0f);
        foreach (Seg s in sk.Segs)
        {
            bool include = tier == 0 ? s.Depth == 0 && !f.Shrub : tier == 1 ? s.Depth <= 1 : s.Depth == 2;
            if (!include) continue;
            int sides = s.Depth == 0 ? (tier == 0 ? 3 : 5) : 3;
            if (s.Ra < .012f && s.Depth == 2 && tier == 2 && f.Shrub) continue;
            int b0 = wood.VertexCount;
            wood.Cylinder(s.A, s.B, s.Ra, s.Rb, sides, barkCol, false);
            // frost: upper faces of limbs, heavier toward the fine wood; trunks stay mostly dark
            float frost = f.Frost * (s.Depth == 0 ? .28f : s.Depth == 1 ? .7f : .9f);
            for (int i = b0; i < wood.VertexCount; i++)
            {
                Color c = wood.C[i];
                c.a = frost * Mathf.SmoothStep(0f, 1f, (wood.N[i].y + .15f) / .85f);
                wood.C[i] = c;
            }
        }
        foreach (Card c in sk.Cards)
        {
            bool include = tier == 1 ? c.Tier == 1 : tier == 2 && c.Tier == 2;
            if (!include) continue;
            AddCard(cards, sk, c, f);
        }
        if (tier <= 1 && !f.Shrub)
        {
            // Whole-crown cards: three crossed side views and one view from above give the crown
            // its frosted mass at every angle the map camera can take; the lacy lateral cards
            // and the limbs add depth inside it at mid and close zoom.
            int cell = CrownCells[Mathf.Min(variant, CrownCells.Length - 1)];
            float k = tier == 0 ? 1f : .94f;
            float h = (sk.CrownTop - sk.CrownBase) * k, w = sk.CrownRadius * 2.1f * k;
            Vector3 b = new Vector3(sk.CrownCenter.x, sk.CrownBase, sk.CrownCenter.z);
            for (int j = 0; j < 3; j++)
                AddCard(cards, sk, new Card { Base = b, Dir = Vector3.up, Width = w, Length = h, Roll = j * 60f + variant * 17f, Cell = cell, Tier = 0 }, f);
            if (tier == 0)
            {
                float topY = sk.CrownBase + (sk.CrownTop - sk.CrownBase) * .62f;
                AddFlat(cards, sk, new Vector3(sk.CrownCenter.x, topY, sk.CrownCenter.z), sk.CrownRadius * 1.5f, variant * 41f, f);
            }
        }
    }

    private static readonly int[] CrownCells = { 8, 9, 10, 9, 8, 10 };

    // A horizontal crown card (the crown seen from above), slightly domed by its normals.
    private static void AddFlat(MeshBuilder mb, Skeleton sk, Vector3 c, float size, float yaw, Form f)
    {
        Rect r = CedarWinterTextures.Cell(11);
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        Vector3[] p = { c + q * new Vector3(-size, 0, -size) * .5f, c + q * new Vector3(size, 0, -size) * .5f,
                        c + q * new Vector3(size, 0, size) * .5f, c + q * new Vector3(-size, 0, size) * .5f };
        Vector2[] uv = { new(r.xMin, r.yMin), new(r.xMax, r.yMin), new(r.xMax, r.yMax), new(r.xMin, r.yMax) };
        int b = mb.VertexCount;
        for (int i = 0; i < 4; i++)
            mb.Add(p[i], ((p[i] - sk.CrownCenter).normalized + Vector3.up * 1.4f).normalized, new Color(1f, 1f, 1f, f.Frost * .08f), uv[i]);
        mb.Tri(b, b + 2, b + 1);
        mb.Tri(b, b + 3, b + 2);
    }

    private static void AddCard(MeshBuilder mb, Skeleton sk, Card c, Form f)
    {
        Vector3 up = c.Dir.normalized;
        Vector3 across = Quaternion.AngleAxis(c.Roll, up) * Perp(up);
        Rect r = CedarWinterTextures.Cell(c.Cell);
        float inset = .004f;
        Vector3 p0 = c.Base - across * c.Width * .5f, p1 = c.Base + across * c.Width * .5f;
        Vector3 p2 = p1 + up * c.Length, p3 = p0 + up * c.Length;
        Vector3[] p = { p0, p1, p2, p3 };
        Vector2[] uv =
        {
            new Vector2(r.xMin + inset, r.yMin + inset), new Vector2(r.xMax - inset, r.yMin + inset),
            new Vector2(r.xMax - inset, r.yMax - inset), new Vector2(r.xMin + inset, r.yMax - inset)
        };
        int b = mb.VertexCount;
        for (int i = 0; i < 4; i++)
        {
            Vector3 n = (p[i] - sk.CrownCenter).normalized;
            if (f.Shrub) n = (n + Vector3.up).normalized;
            n = (n + Vector3.up * .35f).normalized;
            mb.Add(p[i], n, new Color(1f, 1f, 1f, f.Frost * .08f), uv[i]);
        }
        mb.Tri(b, b + 2, b + 1);
        mb.Tri(b, b + 3, b + 2);
    }

    /// <summary>Approximate crown radius for spacing and shadows.</summary>
    internal static float CrownRadius(int variant) => Forms[variant].CrownRadius * Forms[variant].Height;
}
