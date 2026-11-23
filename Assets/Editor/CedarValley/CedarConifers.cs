using UnityEngine;

// All dimensions are in meters. Each form has an authored habit, then a fixed
// seed turns that habit into radial whorls of individually shaped boughs.
internal static class CedarConifers
{
    internal readonly struct Form
    {
        public readonly string Name;
        public readonly float Height, Radius, CrownStart, Droop, Lean, Snow, Green;
        public readonly int Whorls, Boughs;

        public Form(string name, float height, float radius, float crownStart, float droop,
            float lean, float snow, float green, int whorls, int boughs)
        {
            Name = name; Height = height; Radius = radius; CrownStart = crownStart;
            Droop = droop; Lean = lean; Snow = snow; Green = green; Whorls = whorls; Boughs = boughs;
        }
    }

    // Crowns are fuller and carry more snow than the first pass: layered whorls, wide boughs.
    internal static readonly Form[] Forms =
    {
        new("Ridge spruce", 12.4f, 2.45f, .12f, .32f, .16f, .46f, .219f, 9, 7),
        new("Broad woodland pine", 10.2f, 3.85f, .33f, .06f, .34f, .32f, .406f, 7, 8),
        new("Wind-bent sapling", 4.3f, 1.35f, .19f, .09f, .72f, .40f, .594f, 5, 5),
        new("Snow fir", 9.7f, 2.95f, .08f, .19f, .08f, .78f, .406f, 9, 7),
        new("Open-grown old pine", 11.2f, 3.55f, .46f, .02f, .59f, .26f, .219f, 6, 7),
        new("Black spruce", 8.6f, 2.0f, .24f, .38f, .24f, .38f, .219f, 8, 6),
        new("Young balsam", 5.9f, 2.0f, .08f, .13f, .09f, .58f, .594f, 6, 6),
    };

    private static float Rand(int a, int b, int seed) => CedarNoise.Rand(a, b, seed);

    internal static MeshBuilder Build(int variant, int detail, bool simplified = false)
    {
        Form f = Forms[variant];
        int seed = 2100 + variant * 197;
        var mesh = new MeshBuilder();
        Color bark = new(.89f, .85f, .80f, 0f);
        Vector3 Stem(float t) => new(f.Lean * t * t, f.Height * t, -.25f * f.Lean * t * t);
        float radius = f.Radius * (variant == 4 ? .10f : .068f);
        int sides = detail == 0 ? 3 : 5;
        if (detail <= 1)
        {
            Vector3 fork = Stem(.54f);
            mesh.Cylinder(Vector3.down * .28f, fork, radius, radius * .52f, sides, bark, false);
            mesh.Cylinder(fork, Stem(.97f), radius * .52f, .016f, sides, bark, false);
            for (int i = 0; i < mesh.VertexCount; i++) mesh.UV[i] = new Vector2(.09375f, .5f);
        }
        if (detail == 0)
        {
            // Three closed, staggered lobed whorls retain volume from the map camera.
            for (int row = 0; row < 3; row++)
            {
                float t = Mathf.Lerp(f.CrownStart, .71f, row / 2f);
                Vector3 centre = Stem(t);
                Vector3 top = Stem(row == 2 ? 1f : t + .35f);
                Vector3 under = centre - Vector3.up * .18f;
                float width = CrownRadius(variant, f, row / 3f);
                const int points = 5;
                for (int j = 0; j < points; j++)
                {
                    Vector3 Point(int k)
                    {
                        int index = k % points;
                        float angle = (index / (float)points + row * .11f) * Mathf.PI * 2f;
                        float length = width * Mathf.Lerp(.66f, 1.13f, Rand(index, row, seed));
                        return centre + new Vector3(Mathf.Cos(angle) * length, -.12f * (index % 3), Mathf.Sin(angle) * length);
                    }
                    Vector3 a = Point(j), b = Point(j + 1);
                    Face(mesh, top, b, a, f.Green, .88f, f.Snow * .6f);
                    Face(mesh, under, a, b, f.Green, .72f, 0f);
                }
            }
            return mesh;
        }

        // Adjacent whorls rotate so gaps and branch tips never form vertical rows.
        int whorls = simplified ? Mathf.Min(5, f.Whorls) : f.Whorls;
        for (int row = 0; row < whorls; row++)
        {
            float crown = row / (float)whorls;
            float t = Mathf.Lerp(f.CrownStart, .93f, crown);
            int count = (simplified ? Mathf.Min(4, f.Boughs) : f.Boughs) - (row > whorls / 2 ? 1 : 0);
            float spin = row * 2.39996f + .25f * Rand(row, 1, seed);
            for (int j = 0; j < count; j++)
            {
                float angle = spin + (j + .30f * Rand(row, j, seed + 9)) * Mathf.PI * 2f / count;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 across = Vector3.Cross(Vector3.up, direction);
                float length = CrownRadius(variant, f, crown) * Mathf.Lerp(.65f, 1.23f, Rand(row, j, seed + 81));
                if (variant is 2 or 4) length *= Mathf.Lerp(.66f, 1.22f, .5f + direction.x * .5f);
                Vector3 start = Stem(t) + direction * radius * .25f +
                    Vector3.up * ((Rand(row, j, seed + 47) - .5f) * f.Height / f.Whorls * .5f);
                Vector3 tip = start + direction * length +
                    Vector3.up * (length * (.07f - f.Droop) + .32f * Rand(row, j, seed + 15));
                float tone = Mathf.Lerp(.72f, 1.08f, Rand(row, j, seed + 53));
                float snow = f.Snow * Mathf.Lerp(.16f, 1f, Rand(row, j, seed + 27));
                if (detail == 1)
                {
                    Bough(mesh, start, tip, across, length * (simplified ? .58f : .54f), f.Green, tone, snow);
                }
                else
                {
                    // Offset lateral branchlets create gaps inside the crown and fine tips at the edge.
                    Vector3 fork = Vector3.Lerp(start, tip, .48f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 end = Vector3.Lerp(fork, tip, .70f) + across * (side * length * .44f)
                                      + Vector3.up * length * (.18f - f.Droop * .4f);
                        Bough(mesh, fork, end, Vector3.Cross(Vector3.up, (end - fork).normalized),
                            length * .12f, f.Green, tone * 1.03f, snow * .75f);
                    }
                }
            }
        }
        if (detail == 1)
        {
            Vector3 apex = Stem(1f);
            for (int j = 0; j < 3; j++)
            {
                float a = j * 2.0944f;
                Vector3 d = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 basePoint = Stem(.86f) + d * f.Radius * .15f;
                Bough(mesh, basePoint, apex, Vector3.Cross(Vector3.up, (apex - basePoint).normalized),
                    f.Radius * .16f, f.Green, .96f, f.Snow);
            }
        }
        return mesh;
    }

    /// <summary>
    /// Close-range needle spray cards (alpha-tested) laid along each bough of the full mid crown,
    /// snowier toward the top; they break the solid bough silhouettes into needles and snow clumps.
    /// </summary>
    internal static MeshBuilder BuildSprays(int variant)
    {
        Form f = Forms[variant];
        int seed = 2100 + variant * 197;
        var mesh = new MeshBuilder();
        Vector3 Stem(float t) => new(f.Lean * t * t, f.Height * t, -.25f * f.Lean * t * t);
        float radius = f.Radius * (variant == 4 ? .10f : .068f);
        for (int row = 0; row < f.Whorls; row++)
        {
            float crown = row / (float)f.Whorls;
            float t = Mathf.Lerp(f.CrownStart, .93f, crown);
            int count = f.Boughs - (row > f.Whorls / 2 ? 1 : 0);
            float spin = row * 2.39996f + .25f * Rand(row, 1, seed);
            for (int j = 0; j < count; j++)
            {
                float angle = spin + (j + .30f * Rand(row, j, seed + 9)) * Mathf.PI * 2f / count;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float length = CrownRadius(variant, f, crown) * Mathf.Lerp(.65f, 1.23f, Rand(row, j, seed + 81));
                if (variant is 2 or 4) length *= Mathf.Lerp(.66f, 1.22f, .5f + direction.x * .5f);
                Vector3 start = Stem(t) + direction * radius * .25f +
                    Vector3.up * ((Rand(row, j, seed + 47) - .5f) * f.Height / f.Whorls * .5f);
                Vector3 tip = start + direction * length +
                    Vector3.up * (length * (.07f - f.Droop) + .32f * Rand(row, j, seed + 15));
                Vector3 up = (tip - start).normalized;
                Vector3 across = Vector3.Cross(Vector3.up, direction).normalized;
                across = (across + Vector3.up * (Rand(row, j, seed + 5) - .5f) * .5f).normalized;
                float w = length * .95f, l = length * 1.18f;
                Vector3 b0 = start + up * length * .02f + Vector3.up * length * .12f;
                Vector3[] p = { b0 - across * w * .5f, b0 + across * w * .5f, b0 + across * w * .5f + up * l, b0 - across * w * .5f + up * l };
                Rect r = CedarWinterTextures.Cell(crown > .45f || f.Snow > .5f ? 5 : 4);
                Vector2[] uv = { new(r.xMin, r.yMin), new(r.xMax, r.yMin), new(r.xMax, r.yMax), new(r.xMin, r.yMax) };
                int b = mesh.VertexCount;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 axis = Stem(Mathf.Clamp01(p[k].y / f.Height));
                    Vector3 n = (p[k] - axis + Vector3.up * f.Radius * .6f).normalized;
                    mesh.Add(p[k], n, new Color(1f, 1f, 1f, f.Snow * .3f), uv[k]);
                }
                mesh.Tri(b, b + 2, b + 1);
                mesh.Tri(b, b + 3, b + 2);
            }
        }
        return mesh;
    }

    private static float CrownRadius(int variant, Form f, float crown)
    {
        float taper = Mathf.Pow(1f - crown, variant is 1 or 4 ? .63f : .92f);
        float baseShape = variant is 1 or 4 ? Mathf.Lerp(.73f, 1f, Mathf.Sin(crown * Mathf.PI)) : 1f;
        return f.Radius * taper * baseShape;
    }

    private static void Bough(MeshBuilder mesh, Vector3 start, Vector3 tip, Vector3 across,
        float width, float uv, float tint, float snow)
    {
        Vector3 mid = Vector3.Lerp(start, tip, .43f);
        Vector3 left = mid + across * width, right = mid - across * width * .82f;
        Vector3 raised = mid + Vector3.up * width * .69f;
        Face(mesh, start, raised, left, uv, tint, snow);
        Face(mesh, left, raised, tip, uv, tint, snow);
        Face(mesh, tip, raised, right, uv, tint * .91f, snow * .64f);
        Face(mesh, right, raised, start, uv, tint * .91f, snow * .64f);
        Face(mesh, start, left, right, uv, tint * .75f, 0f);
        Face(mesh, left, tip, right, uv, tint * .75f, 0f);
    }

    private static void Face(MeshBuilder mesh, Vector3 a, Vector3 b, Vector3 c, float uv, float tint, float snow)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        Color color = new(tint * .91f, tint, tint * .92f, snow * Mathf.SmoothStep(0f, 1f, (normal.y - .13f) * 1.6f));
        var sample = new Vector2(uv, .5f);
        mesh.Tri(mesh.Add(a, normal, color, sample), mesh.Add(b, normal, color, sample), mesh.Add(c, normal, color, sample));
    }
}
