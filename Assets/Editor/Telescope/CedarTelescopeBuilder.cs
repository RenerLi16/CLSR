using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the Cedar Valley camp lookout and the telescope dressing of every selectable site:
/// an open A-frame tent with poles at its mouth, a fire ring, supply crates and a flag pole,
/// plus four survivor figure slots (seated in the tent mouth, by the fire, by the crates and
/// walking a loop through the clearing). Figures are saved inactive; the game shows only as
/// many as the simulation says are still at the observed node, during the sighting only.
/// Every site gets the same dressing (mirrored for variety), so props carry no information.
///
/// The lookout height is solved so every survivor slot has a terrain-clear sightline from the
/// one shared camp eye point; slots that still are not clear are moved to clear alternatives.
/// Log: Temp/CedarValleyBuild/telescope/build.txt
/// </summary>
internal static class CedarTelescopeBuilder
{
    private const string ScenePath = "Assets/Scenes/CedarValley.unity";
    private const string Dir = "Assets/Art/CedarValley/Telescope";
    private const string Kit = "Assets/Art/WinterOutpost/";
    private const string KitMats = "Assets/Art/CedarValley/Generated/Materials/";
    internal static string Out => Path.Combine(Directory.GetCurrentDirectory(), "Temp", "CedarValleyBuild", "telescope");
    private static readonly StringBuilder Log = new StringBuilder();

    private const float EyeAboveDeck = 1.55f;
    private const float AimHeight = 1.0f;

    private static readonly List<MeshCollider> temp = new List<MeshCollider>();
    private static readonly HashSet<Collider> ground = new HashSet<Collider>();
    private static readonly List<GameObject> activated = new List<GameObject>();

    [MenuItem("Tools/Cedar Valley/Telescope/Build Lookout And Sites", false, 60)]
    private static void BuildMenu() => Run("build.txt", Build);

    [MenuItem("Tools/Cedar Valley/Telescope/Check Shaders", false, 61)]
    private static void ShadersMenu() => Run("shaders.txt", CheckShaders);

    internal static void Run(string file, Action action)
    {
        Log.Clear();
        Log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + " " + file);
        try { action(); }
        catch (Exception e) { Log.AppendLine("EXCEPTION " + e); Debug.LogException(e); }
        finally
        {
            EndGround();
            Directory.CreateDirectory(Out);
            File.WriteAllText(Path.Combine(Out, file), Log.ToString());
        }
    }

    private static void Info(string s) => Log.AppendLine(s);

    // ================================================================== shaders
    private static void CheckShaders()
    {
        foreach (string name in new[] { "Hidden/CLSR/Telescope Depth Of Field", "CLSR/Winter Lit", "CLSR/Winter Foliage" })
        {
            Shader s = Shader.Find(name);
            if (s == null) { Info("MISSING shader " + name); continue; }
            ShaderMessage[] msgs = ShaderUtil.GetShaderMessages(s);
            Info($"{name}: supported {s.isSupported}, errors {ShaderUtil.ShaderHasError(s)}, messages {msgs.Length}");
            foreach (ShaderMessage m in msgs) Info($"  {m.severity} {m.platform} line {m.line}: {m.message}");
        }
    }

    // ================================================================== ground sampling
    private static void BeginGround()
    {
        EndGround();
        Transform land = GameObject.Find("World/Landscape")?.transform;
        if (land == null) throw new InvalidOperationException("World/Landscape not found");
        foreach (MeshFilter mf in land.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.gameObject.name.StartsWith("Cedar terrain ") || mf.sharedMesh == null) continue;
            for (Transform t = mf.transform; t != null; t = t.parent)
                if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); activated.Add(t.gameObject); }
            var c = mf.gameObject.AddComponent<MeshCollider>();
            c.sharedMesh = mf.sharedMesh;
            temp.Add(c);
            ground.Add(c);
        }
        Physics.SyncTransforms();
        Info("ground colliders: " + temp.Count);
    }

    private static void EndGround()
    {
        ClearPropColliders();
        foreach (MeshCollider c in temp) if (c != null) UnityEngine.Object.DestroyImmediate(c);
        temp.Clear();
        ground.Clear();
        foreach (GameObject g in activated) if (g != null) g.SetActive(false);
        activated.Clear();
    }

    private static float Ground(float x, float z)
    {
        float best = float.NaN;
        foreach (RaycastHit h in Physics.RaycastAll(new Vector3(x, 600f, z), Vector3.down, 1200f, ~0, QueryTriggerInteraction.Ignore))
            if (ground.Contains(h.collider) && (float.IsNaN(best) || h.point.y > best)) best = h.point.y;
        return best;
    }

    private static Vector3 OnGround(Vector3 p, float above = 0f) => new Vector3(p.x, Ground(p.x, p.z) + above, p.z);

    private static bool TerrainBlocks(Vector3 eye, Vector3 target)
    {
        Vector3 d = target - eye;
        float len = d.magnitude;
        foreach (RaycastHit h in Physics.RaycastAll(eye, d / len, len - 0.25f, ~0, QueryTriggerInteraction.Ignore))
            if (ground.Contains(h.collider)) return true;
        return false;
    }

    // Terrain or one of this site's props (tent, crates, fire ring) between the eye and a point.
    private static readonly HashSet<Collider> props = new HashSet<Collider>();
    private static bool Blocks(Vector3 eye, Vector3 target, Collider ignore = null)
    {
        Vector3 d = target - eye;
        float len = d.magnitude;
        foreach (RaycastHit h in Physics.RaycastAll(eye, d / len, len - 0.25f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider != ignore && (ground.Contains(h.collider) || props.Contains(h.collider))) return true;
        return false;
    }

    // A box around a prop group, oriented with the group (contact shadows left out), so the
    // sightline test is no more conservative than the props themselves.
    private static Collider AddPropCollider(Transform t)
    {
        var go = new GameObject("temp blocker");
        go.transform.SetPositionAndRotation(t.position, t.rotation);
        Bounds local = LocalBounds(t, go.transform);
        var box = go.AddComponent<BoxCollider>();
        box.center = local.center;
        box.size = local.size;
        props.Add(box);
        Physics.SyncTransforms();
        return box;
    }

    internal static Bounds LocalBounds(Transform group, Transform frame)
    {
        bool any = false;
        var b = new Bounds();
        foreach (MeshFilter mf in group.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || mf.gameObject.name == "Contact shadow") continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                Vector3 q = frame.InverseTransformPoint(mf.transform.TransformPoint(c));
                if (!any) { b = new Bounds(q, Vector3.zero); any = true; } else b.Encapsulate(q);
            }
        }
        return b;
    }

    private static void ClearPropColliders()
    {
        foreach (Collider c in props) if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
        props.Clear();
        Physics.SyncTransforms();
    }

    private static Bounds CedarBounds(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>();
        Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.zero);
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ================================================================== site layout
    private sealed class Layout
    {
        public string Id;
        public Vector3 Center;      // ground point at the node
        public Vector3 Forward, Right;
        public float Mirror;        // +1 or -1
        public float FarScale = 1f; // compresses the far clearing toward the site centre (crest sites)
        public Vector3 W(float x, float z, float up = 0f)
        {
            if (z > 2f) z = 2f + (z - 2f) * FarScale;
            Vector3 p = Center + Right * (x * Mirror) + Forward * z;
            return OnGround(p, up);
        }
    }

    // candidate local positions (x, z) per slot kind; the first clear one is used
    private static readonly Vector2[] SeatedSpots = { new Vector2(-2.55f, -3.85f) };
    private static readonly Vector2[] FireSpots = { new Vector2(1.75f, -0.15f), new Vector2(1.6f, -1.2f), new Vector2(0.3f, -1.4f) };
    private static readonly Vector2[] CrateSpots = { new Vector2(-2.2f, 6.6f), new Vector2(-2.5f, 4.9f), new Vector2(-1.2f, 6.1f), new Vector2(-1.4f, 5.4f), new Vector2(-2.0f, 4.4f) };
    private static readonly Vector2[] WalkLoop =
    {
        new Vector2(-1.3f, 5.3f), new Vector2(1.0f, 4.8f), new Vector2(2.9f, 5.2f), new Vector2(3.7f, 6.4f),
        new Vector2(2.6f, 7.5f), new Vector2(0.2f, 7.6f), new Vector2(-1.6f, 6.9f), new Vector2(-2.0f, 6.0f)
    };
    // used where the far clearing cannot be seen (crest sites): a loop across the middle of the site
    private static readonly Vector2[] MidLoop =
    {
        new Vector2(0.2f, 1.5f), new Vector2(2.1f, 1.1f), new Vector2(3.8f, 1.6f), new Vector2(3.4f, 2.6f),
        new Vector2(1.4f, 2.9f), new Vector2(-0.5f, 2.4f)
    };
    private static readonly float[] FarScales = { 1f, 0.8f, 0.65f, 0.5f, 0.38f };

    private static int Hash(string id)
    {
        int h = 17;
        foreach (char c in id) h = h * 31 + c;
        return Mathf.Abs(h);
    }

    // ================================================================== build
    private static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open Assets/Scenes/CedarValley.unity first (active: " + scene.path + ")");
        Directory.CreateDirectory(Dir + "/Materials");
        Directory.CreateDirectory(Dir + "/Meshes");
        AssetDatabase.Refresh();

        GameObject world = GameObject.Find("World");
        Transform old = world.transform.Find("Telescope");
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        var root = new GameObject("Telescope").transform;
        root.SetParent(world.transform, false);

        BeginGround();
        Transform campNode = GameObject.Find("Graph/Nodes/N00_Camp").transform;
        Vector3 camp = OnGround(campNode.position);
        Vector2 towerXZ = new Vector2(camp.x, camp.z) + new Vector2(Mathf.Cos(100f * Mathf.Deg2Rad), Mathf.Sin(100f * Mathf.Deg2Rad)) * 14f;
        Vector3 towerBase = OnGround(new Vector3(towerXZ.x, 0f, towerXZ.y));
        Info($"camp ground {camp}, tower base {towerBase}");

        var nodes = GameObject.Find("Graph/Nodes").GetComponentsInChildren<RescueNode>(true)
            .Where(n => n.gameObject.name != "N00_Camp" && n.nodeId != "Camp").OrderBy(n => n.gameObject.name).ToList();

        Layout MakeLayout(RescueNode n, Vector3 eye)
        {
            Vector3 c = OnGround(n.transform.position);
            Vector3 f = c - eye; f.y = 0f; f.Normalize();
            return new Layout { Id = n.gameObject.name.Replace("Node_", ""), Center = c, Forward = f,
                Right = Vector3.Cross(Vector3.up, f), Mirror = Hash(n.gameObject.name) % 2 == 0 ? 1f : -1f };
        }

        // ---- lookout height: the lowest deck from which every site has a layout whose survivor
        // slots (heads and chests) are clear of terrain; crest sites may pull their far clearing in
        List<(string, Vector3)> SlotPoints(Layout l)
        {
            var pts = new List<(string, Vector3)>
            {
                ("seated head", l.W(SeatedSpots[0].x, SeatedSpots[0].y, 0.75f)),
                ("fire chest", l.W(FireSpots[0].x, FireSpots[0].y, 1.3f)),
                ("crates chest", l.W(CrateSpots[0].x, CrateSpots[0].y, 1.3f)),
            };
            foreach (Vector2 w in WalkLoop) pts.Add(("walk chest", l.W(w.x, w.y, 1.3f)));
            return pts;
        }
        float ClearScale(RescueNode n, Vector3 eye)
        {
            Layout l = MakeLayout(n, eye);
            foreach (float k in FarScales)
            {
                l.FarScale = k;
                if (SlotPoints(l).All(q => !TerrainBlocks(eye, q.Item2))) return k;
            }
            return -1f;
        }
        float bestDeck = 6f;
        int bestFailing = int.MaxValue;
        var table = new StringBuilder("deck height -> sites with no terrain-clear layout:");
        for (float deck = 6f; deck <= 30f; deck += 2f)
        {
            Vector3 e = towerBase + Vector3.up * (deck + EyeAboveDeck);
            var failing = nodes.Where(n => ClearScale(n, e) < 0f).Select(n => n.gameObject.name.Replace("Node_", "")).ToList();
            table.Append($" {deck}m:{(failing.Count == 0 ? "none" : string.Join("/", failing))}");
            if (failing.Count < bestFailing) { bestFailing = failing.Count; bestDeck = deck; }
            if (failing.Count == 0) break;
        }
        Info(table.ToString());
        Info($"lookout deck height {bestDeck} m (sites without a clear layout: {bestFailing})");
        Vector3 eyePos = towerBase + Vector3.up * (bestDeck + EyeAboveDeck);

        // ---- materials and meshes
        var m = new Mats();
        Mesh tentMesh = TentMesh();
        Mesh blob = BlobMesh();
        var shadowMat = AssetDatabase.LoadAssetAtPath<Material>(KitMats + "CVW_CastShadow.mat");
        void Blob(Transform parent, Vector3 at, Quaternion rot, float length, float width)
        {
            if (shadowMat == null) return;
            var g = MeshObj(parent, "Contact shadow", blob, shadowMat, Vector3.zero, Quaternion.identity, Vector3.one);
            g.transform.SetPositionAndRotation(at + Vector3.up * 0.03f, rot);
            SetWorldScale(g.transform, new Vector3(width, 1f, length));
            g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---- lookout tower
        Transform tower = new GameObject("Camp lookout").transform;
        tower.SetParent(root, false);
        tower.position = towerBase;
        BuildTower(tower, towerBase, bestDeck, m);
        var lookout = tower.gameObject.AddComponent<TelescopeLookout>();
        var eyeT = new GameObject("Telescope eye").transform;
        eyeT.SetParent(tower, false);
        Vector3 mean = Vector3.zero;
        foreach (RescueNode n in nodes) { Vector3 d = n.transform.position - eyePos; d.y = 0; mean += d.normalized; }
        eyeT.SetPositionAndRotation(eyePos, Quaternion.LookRotation(mean.normalized) * Quaternion.Euler(4f, 0f, 0f));
        lookout.eye = eyeT;
        lookout.restingFieldOfView = 50f;
        lookout.depthOfFieldShader = AssetDatabase.LoadAssetAtPath<Shader>(Dir + "/TelescopeDepthOfField.shader");
        if (lookout.depthOfFieldShader == null) Info("WARNING depth of field shader not found");

        // ---- sites
        Transform sitesRoot = new GameObject("Sites").transform;
        sitesRoot.SetParent(root, false);
        var jackets = new[] { m.Orange, m.Red, m.Blue, m.Yellow, m.Teal };
        foreach (RescueNode n in nodes)
        {
            Layout l = MakeLayout(n, eyePos);
            float scale = ClearScale(n, eyePos);
            l.FarScale = scale > 0f ? scale : 1f;
            if (scale < 0f) Info($"  {l.Id}: some survivor slots stay terrain-blocked at every layout");
            else if (scale < 1f) Info($"  {l.Id}: far clearing pulled in to {scale:0.00} of its depth to stay in sight");
            var siteGo = new GameObject("Site " + l.Id);
            siteGo.transform.SetParent(sitesRoot, false);
            siteGo.transform.SetPositionAndRotation(l.Center, Quaternion.LookRotation(l.Forward));
            var site = siteGo.AddComponent<TelescopeSite>();
            site.nodeId = l.Id;
            int hash = Hash(l.Id);

            var aim = new GameObject("Aim").transform;
            aim.SetParent(siteGo.transform, false);
            aim.position = l.W(0.2f, 1.2f, AimHeight);
            site.aim = aim;
            site.frameHeight = 7f;
            site.focusRange = 16f;
            site.clipClearance = 13f;

            // tent: open A-frame, mouth toward camp, poles at the mouth
            Vector3 tentC = l.W(-2.55f, -2.6f);
            Quaternion facing = Quaternion.LookRotation(l.Forward);
            var tent = new GameObject("Tent").transform;
            tent.SetParent(siteGo.transform, false);
            tent.SetPositionAndRotation(tentC, facing);
            MeshObj(tent, "Canvas", tentMesh, m.Canvas, Vector3.zero, Quaternion.identity, Vector3.one);
            Transform frontPole = null;
            foreach (float zEnd in new[] { -1.3f, 1.3f })
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 a = tent.TransformPoint(new Vector3(side * 1.08f, 0f, zEnd));
                Vector3 b = tent.TransformPoint(new Vector3(0f, 1.48f, zEnd));
                Transform pole = Beam(tent, zEnd < 0 ? "Front pole" : "Back pole", a, b, 0.035f, m.DarkTimber);
                if (zEnd < 0 && side < 0) frontPole = pole;
            }
            Beam(tent, "Ridge pole", tent.TransformPoint(new Vector3(0f, 1.5f, -1.42f)), tent.TransformPoint(new Vector3(0f, 1.5f, 1.42f)), 0.03f, m.DarkTimber);
            Prim(PrimitiveType.Cube, tent, "Ground sheet", tent.TransformPoint(new Vector3(0f, 0.015f, 0.05f)), facing, new Vector3(1.9f, 0.03f, 2.55f), m.GroundSheet);
            Prim(PrimitiveType.Cylinder, tent, "Bedroll", tent.TransformPoint(new Vector3(0.55f, 0.11f, 0.4f)), facing * Quaternion.Euler(90f, 0f, 0f), new Vector3(0.22f, 0.45f, 0.22f), m.Bedroll);
            site.nearDetail = tent;
            Blob(tent, tentC, facing, 3.1f, 2.7f);

            // flag pole at the same depth as the tent mouth, on the other side
            var flag = new GameObject("Flag pole").transform;
            flag.SetParent(siteGo.transform, false);
            Vector3 fp = l.W(3.3f, -3.9f);
            flag.position = fp;
            Beam(flag, "Pole", fp - Vector3.up * 0.2f, fp + Vector3.up * 2.7f, 0.035f, m.OldTimber);
            Prim(PrimitiveType.Cube, flag, "Flag", fp + Vector3.up * 2.42f + l.Right * (0.3f * l.Mirror), facing, new Vector3(0.62f, 0.38f, 0.015f), m.Oxide);

            // fire ring
            var fire = new GameObject("Fire ring").transform;
            fire.SetParent(siteGo.transform, false);
            Vector3 fc = l.W(0.9f, -0.6f);
            fire.position = fc;
            for (int k = 0; k < 9; k++)
            {
                float a = k / 9f * Mathf.PI * 2f;
                Vector3 sp = OnGround(fc + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.48f, 0.04f);
                float sz = 0.17f + 0.05f * ((hash >> k) & 1);
                Prim(PrimitiveType.Sphere, fire, "Stone", sp, Quaternion.Euler(0f, k * 40f, 0f), new Vector3(sz, sz * 0.75f, sz), m.Slate);
            }
            Beam(fire, "Log", fc + new Vector3(-0.3f, 0.08f, -0.1f), fc + new Vector3(0.28f, 0.12f, 0.12f), 0.06f, m.DarkTimber);
            Beam(fire, "Log", fc + new Vector3(-0.08f, 0.09f, 0.3f), fc + new Vector3(0.1f, 0.1f, -0.3f), 0.055f, m.DarkTimber);

            // supply crates in the far clearing
            var crates = new GameObject("Crates").transform;
            crates.SetParent(siteGo.transform, false);
            Vector3 cr = l.W(-3.1f, 5.8f);
            crates.SetPositionAndRotation(cr, facing);
            GameObject crate = Kit09(crates, cr, Quaternion.LookRotation(l.Forward) * Quaternion.Euler(0f, 12f, 0f));
            Kit09(crates, l.W(-3.7f, 6.6f), Quaternion.LookRotation(l.Forward) * Quaternion.Euler(0f, -31f, 0f));
            site.farDetail = crates;
            Blob(crates, cr + l.Right * (-0.3f * l.Mirror) + l.Forward * 0.4f, facing, 1.9f, 1.4f);

            // survivor slots
            var survivorsRoot = new GameObject("Survivors").transform;
            survivorsRoot.SetParent(siteGo.transform, false);
            Vector3 Pick(Vector2[] spots, float chestUp, string what)
            {
                foreach (Vector2 s in spots)
                {
                    Vector3 p = l.W(s.x, s.y);
                    if (!Blocks(eyePos, p + Vector3.up * chestUp) && !Blocks(eyePos, p + Vector3.up * 1.7f)) return p;
                }
                Info($"  {l.Id}: no clear spot for {what}; using the first");
                return l.W(spots[0].x, spots[0].y);
            }
            Vector3 seatedAt = tent.TransformPoint(new Vector3(-0.12f, 0f, -0.82f));
            Collider tentBlock = AddPropCollider(tent);
            AddPropCollider(crates);
            if (TerrainBlocks(eyePos, seatedAt + Vector3.up * 0.75f)) Info($"  {l.Id}: seated head in the tent mouth is terrain-blocked");
            Vector3 fireAt = Pick(FireSpots, 1.3f, "fire stander");
            Vector3 crateAt = Pick(CrateSpots, 1.3f, "crate stander");
            Vector3[] walk = null;
            foreach (Vector2[] loop in new[] { WalkLoop, MidLoop })
            {
                foreach (float dx in new[] { 0f, 1.2f, 2.4f, -1.2f })
                {
                    Vector3[] path = loop.Select(w => l.W(w.x + dx, w.y)).ToArray();
                    if (path.All(q => !Blocks(eyePos, q + Vector3.up * 1.0f) && !Blocks(eyePos, q + Vector3.up * 1.7f)))
                    {
                        walk = path;
                        if (dx != 0f || loop == MidLoop)
                            Info($"  {l.Id}: walk loop {(loop == MidLoop ? "across the middle of the site" : "")} moved {dx} m sideways to stay in clear view");
                        break;
                    }
                }
                if (walk != null) break;
            }
            if (walk == null)
            {
                walk = WalkLoop.Select(w => l.W(w.x, w.y)).ToArray();
                Info($"  {l.Id}: walk loop partly blocked from the lookout");
            }

            var kinds = new List<SurvivorFigure.Pose> { SurvivorFigure.Pose.Seated, SurvivorFigure.Pose.Walking, SurvivorFigure.Pose.Standing, SurvivorFigure.Pose.Standing };
            var spotsFor = new List<int> { 0, 1, 2, 3 };
            // per-site fill order: which slot appears first when only one or two survivors remain
            for (int i = spotsFor.Count - 1; i > 0; i--)
            {
                int j = (hash / (i + 3)) % (i + 1);
                (spotsFor[i], spotsFor[j]) = (spotsFor[j], spotsFor[i]);
            }
            foreach (int slot in spotsFor)
            {
                Material jacket = jackets[(hash + slot * 2) % jackets.Length];
                SurvivorFigure fig;
                switch (slot)
                {
                    case 0:
                        fig = Figure(survivorsRoot, "Seated in tent", SurvivorFigure.Pose.Seated, seatedAt, Quaternion.LookRotation(-l.Forward), jacket, m, hash);
                        break;
                    case 1:
                        fig = Figure(survivorsRoot, "Walking in clearing", SurvivorFigure.Pose.Walking, walk[0], Quaternion.LookRotation(walk[1] - walk[0]), jacket, m, hash);
                        fig.path = walk;
                        break;
                    case 2:
                        Vector3 toFire = fc - fireAt; toFire.y = 0;
                        fig = Figure(survivorsRoot, "Standing by fire", SurvivorFigure.Pose.Standing, fireAt,
                            Quaternion.LookRotation(Vector3.Slerp(toFire.normalized, -l.Forward, 0.35f)), jacket, m, hash);
                        break;
                    default:
                        fig = Figure(survivorsRoot, "Standing by crates", SurvivorFigure.Pose.Standing, crateAt,
                            Quaternion.LookRotation(Quaternion.Euler(0f, 35f * l.Mirror, 0f) * -l.Forward), jacket, m, hash);
                        break;
                }
                fig.phase = ((hash >> slot) % 97) / 97f;
                site.survivors.Add(fig);
                fig.gameObject.SetActive(false);
            }

            foreach (SurvivorFigure f in site.survivors)
            {
                bool sit = f.pose == SurvivorFigure.Pose.Seated;
                Blob(f.transform, f.transform.position + f.transform.forward * (sit ? 0.25f : 0f), f.transform.rotation, sit ? 1.0f : 0.7f, sit ? 0.75f : 0.6f);
            }

            // framing: centre on everything that matters here and fit the narrowest supported
            // screen shape (4:3) with a margin
            var keys = new List<Vector3>();
            foreach (SurvivorFigure f in site.survivors)
            {
                IEnumerable<Vector3> feet = f.pose == SurvivorFigure.Pose.Walking ? f.path : new[] { f.transform.position };
                foreach (Vector3 q in feet) { keys.Add(q); keys.Add(q + Vector3.up * (f.pose == SurvivorFigure.Pose.Seated ? 1.05f : 1.85f)); }
            }
            foreach (float zEnd in new[] { -1.3f, 1.3f })
                foreach (float side in new[] { -1.1f, 1.1f }) keys.Add(tent.TransformPoint(new Vector3(side, 0f, zEnd)));
            keys.Add(tent.TransformPoint(new Vector3(0f, 1.5f, -1.3f)));
            keys.Add(fp); keys.Add(fp + Vector3.up * 2.7f);
            Bounds cb = CedarBounds(crates);
            keys.Add(cb.min); keys.Add(cb.max);
            site.frameHeight = FitFrame(eyePos, aim, keys);

            ClearPropColliders();
            lookout.sites.Add(site);
            float dist = Vector3.Distance(eyePos, aim.position);
            float fov = CLSR.Presentation.TelescopeOptics.FovForFrame(site.frameHeight, dist);
            Info($"site {l.Id}: aim {dist:0.0} m, frame {site.frameHeight:0.0} m, fov {fov:0.00} deg, person {1.72f / site.frameHeight * 1080f:0} px tall at 1080p, " +
                 $"clip at {dist - site.clipClearance:0.0} m, mirror {l.Mirror}, fill order {string.Join(",", spotsFor)}");
        }

        EndGround();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save Cedar Valley.");
        AssetDatabase.SaveAssets();
        Info("saved scene with lookout and " + lookout.sites.Count + " telescope sites");
        CheckShaders();
    }

    // ================================================================== pieces
    private sealed class Mats
    {
        public Material Canvas, DarkTimber, OldTimber, Slate, Oxide, GroundSheet, Bedroll;
        public Material Orange, Red, Blue, Yellow, Teal, Trousers, Skin, Boots, Pack, Hat;

        public Mats()
        {
            Canvas = Kit("Canvas"); DarkTimber = Kit("DarkTimber"); OldTimber = Kit("OldTimber");
            Slate = Kit("Slate"); Oxide = Kit("OxideRed");
            Texture snow = Canvas != null ? Canvas.GetTexture("_SnowTex") : null;
            Material M(string n, Color c) => Make(n, c, snow);
            GroundSheet = M("GroundSheet", new Color(.22f, .27f, .22f));
            Bedroll = M("Bedroll", new Color(.42f, .30f, .22f));
            Orange = M("JacketOrange", new Color(.86f, .40f, .12f));
            Red = M("JacketRed", new Color(.70f, .13f, .12f));
            Blue = M("JacketBlue", new Color(.15f, .32f, .62f));
            Yellow = M("JacketYellow", new Color(.86f, .70f, .16f));
            Teal = M("JacketTeal", new Color(.10f, .52f, .52f));
            Trousers = M("Trousers", new Color(.16f, .17f, .21f));
            Skin = M("Skin", new Color(.80f, .60f, .48f));
            Boots = M("Boots", new Color(.10f, .08f, .07f));
            Pack = M("Pack", new Color(.24f, .26f, .20f));
            Hat = M("Hat", new Color(.20f, .20f, .24f));
        }

        private static Material Kit(string n) => AssetDatabase.LoadAssetAtPath<Material>(KitMats + "CVW_Kit_" + n + ".mat");

        private static Material Make(string n, Color c, Texture snow)
        {
            string path = Dir + "/Materials/CVW_Telescope_" + n + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("CLSR/Winter Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find("CLSR/Winter Lit");
            mat.SetColor("_Color", c);
            mat.SetFloat("_UseVertexColor", 0f);
            mat.SetFloat("_AutoSnow", 0f);
            mat.SetFloat("_Wrap", .25f);
            if (snow != null) mat.SetTexture("_SnowTex", snow);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }

    private static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
        g.name = name;
        g.transform.SetParent(parent, true);
        g.transform.SetPositionAndRotation(pos, rot);
        SetWorldScale(g.transform, scale);
        g.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return g;
    }

    private static void SetWorldScale(Transform t, Vector3 world)
    {
        Vector3 p = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(world.x / p.x, world.y / p.y, world.z / p.z);
    }

    private static Transform Beam(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material mat)
    {
        Vector3 d = b - a;
        GameObject g = Prim(PrimitiveType.Cylinder, parent, name, (a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized),
            new Vector3(radius * 2f, d.magnitude * 0.5f, radius * 2f), mat);
        return g.transform;
    }

    private static GameObject MeshObj(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos, Quaternion localRot, Vector3 localScale)
    {
        var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = localRot;
        g.transform.localScale = localScale;
        g.GetComponent<MeshFilter>().sharedMesh = mesh;
        g.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return g;
    }

    private static GameObject Kit09(Transform parent, Vector3 pos, Quaternion rot)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "09_Supply_Crate.obj");
        if (src == null) { Info("missing kit crate"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        go.name = "Supply crate";
        go.transform.SetPositionAndRotation(pos - Vector3.up * 0.03f, rot);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                var w = AssetDatabase.LoadAssetAtPath<Material>(KitMats + "CVW_Kit_" + mats[i].name + ".mat");
                if (w != null) mats[i] = w;
            }
            r.sharedMaterials = mats;
        }
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
        return go;
    }

    // Re-aims at the angular centre of the key points and returns the frame height (m at the aim
    // distance) that shows all of them on a 4:3 screen with a margin.
    private static float FitFrame(Vector3 eye, Transform aim, List<Vector3> keys)
    {
        float d = Vector3.Distance(eye, aim.position);
        float vExt = 0f, hExt = 0f;
        for (int iter = 0; iter < 4; iter++)
        {
            Quaternion q = Quaternion.LookRotation(aim.position - eye, Vector3.up), inv = Quaternion.Inverse(q);
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (Vector3 k in keys)
            {
                Vector3 v = inv * (k - eye);
                float ax = Mathf.Atan2(v.x, v.z), ay = Mathf.Atan2(v.y, v.z);
                x0 = Mathf.Min(x0, ax); x1 = Mathf.Max(x1, ax); y0 = Mathf.Min(y0, ay); y1 = Mathf.Max(y1, ay);
            }
            float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
            vExt = y1 - y0; hExt = x1 - x0;
            aim.position = eye + q * new Vector3(Mathf.Tan(cx), Mathf.Tan(cy), 1f).normalized * d;
        }
        float need = Mathf.Max(vExt, hExt / 1.3f) * 1.16f;
        return Mathf.Max(6f, 2f * d * Mathf.Tan(need * 0.5f));
    }

    // Flat contact-shadow quad for the Winter Cast Shadow material (smooth half of its atlas).
    private static Mesh BlobMesh()
    {
        string path = Dir + "/Meshes/TelescopeContactShadow.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool create = mesh == null;
        if (create) mesh = new Mesh();
        mesh.Clear();
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var tri = new List<int>();
        const int n = 3;
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            float u = i / (n - 1f), w = j / (n - 1f);
            v.Add(new Vector3(u - 0.5f, 0f, w - 0.5f));
            uv.Add(new Vector2(u * 0.5f, w));
            col.Add(new Color(0.5f, 0.56f, 0.68f, 0.75f));
        }
        for (int j = 0; j < n - 1; j++)
        for (int i = 0; i < n - 1; i++)
        {
            int a = j * n + i;
            tri.AddRange(new[] { a, a + n, a + n + 1, a, a + n + 1, a + 1 });
        }
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetColors(col); mesh.SetTriangles(tri, 0);
        mesh.SetNormals(Enumerable.Repeat(Vector3.up, v.Count).ToList());
        mesh.RecalculateBounds();
        if (create) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // Open A-frame tent, 2.6 m long, mouth at -Z. Both faces of the canvas are modelled.
    private static Mesh TentMesh()
    {
        string path = Dir + "/Meshes/TelescopeTent.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool create = mesh == null;
        if (create) mesh = new Mesh();
        mesh.Clear();
        const float hw = 1.08f, h = 1.48f, hl = 1.3f;
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var uv = new List<Vector2>();
        var tri = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 nn = Vector3.Cross(b - a, d - a).normalized;
            for (int side = 0; side < 2; side++)
            {
                int i = v.Count;
                v.AddRange(new[] { a, b, c, d });
                Vector3 k = side == 0 ? nn : -nn;
                n.AddRange(new[] { k, k, k, k });
                uv.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                if (side == 0) tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                else tri.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
        }
        Vector3 L0 = new Vector3(-hw, 0, -hl), L1 = new Vector3(-hw, 0, hl), R0 = new Vector3(hw, 0, -hl), R1 = new Vector3(hw, 0, hl);
        Vector3 T0 = new Vector3(0, h, -hl - 0.06f), T1 = new Vector3(0, h, hl + 0.06f);
        Quad(L0, T0, T1, L1);   // left slope
        Quad(R1, T1, T0, R0);   // right slope
        Quad(L1, T1, T1, R1);   // back wall (degenerate quad = triangle)
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tri, 0);
        mesh.RecalculateBounds();
        if (create) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static void BuildTower(Transform tower, Vector3 basePos, float deck, Mats m)
    {
        const float top = 1.25f, bottom = 2.1f;
        Vector3 Leg(float sx, float sz, float y, float half) => basePos + new Vector3(sx * half, y, sz * half);
        float[] signs = { -1f, 1f };
        foreach (float sx in signs)
        foreach (float sz in signs)
            Beam(tower, "Leg", Leg(sx, sz, -0.6f, bottom), Leg(sx, sz, deck, top), 0.16f, m.OldTimber);
        // cross-bracing every few metres on all four faces
        int levels = Mathf.Max(2, Mathf.RoundToInt(deck / 4f));
        for (int i = 0; i < levels; i++)
        {
            float y0 = deck * i / levels, y1 = deck * (i + 1) / levels;
            float h0 = Mathf.Lerp(bottom, top, y0 / deck), h1 = Mathf.Lerp(bottom, top, y1 / deck);
            foreach (float s in signs)
            {
                Beam(tower, "Brace", basePos + new Vector3(-h0, y0, s * h0), basePos + new Vector3(h1, y1, s * h1), 0.07f, m.DarkTimber);
                Beam(tower, "Brace", basePos + new Vector3(h0, y0, s * h0), basePos + new Vector3(-h1, y1, s * h1), 0.07f, m.DarkTimber);
                Beam(tower, "Brace", basePos + new Vector3(s * h0, y0, -h0), basePos + new Vector3(s * h1, y1, h1), 0.07f, m.DarkTimber);
                Beam(tower, "Brace", basePos + new Vector3(s * h0, y0, h0), basePos + new Vector3(s * h1, y1, -h1), 0.07f, m.DarkTimber);
                Beam(tower, "Girt", basePos + new Vector3(-h1, y1, s * h1), basePos + new Vector3(h1, y1, s * h1), 0.08f, m.DarkTimber);
                Beam(tower, "Girt", basePos + new Vector3(s * h1, y1, -h1), basePos + new Vector3(s * h1, y1, h1), 0.08f, m.DarkTimber);
            }
        }
        Prim(PrimitiveType.Cube, tower, "Deck", basePos + Vector3.up * (deck + 0.06f), Quaternion.identity, new Vector3(3.1f, 0.14f, 3.1f), m.OldTimber);
        foreach (float s in signs)
        {
            Beam(tower, "Rail", basePos + new Vector3(-1.5f, deck + 1.0f, s * 1.5f), basePos + new Vector3(1.5f, deck + 1.0f, s * 1.5f), 0.045f, m.OldTimber);
            Beam(tower, "Rail", basePos + new Vector3(s * 1.5f, deck + 1.0f, -1.5f), basePos + new Vector3(s * 1.5f, deck + 1.0f, 1.5f), 0.045f, m.OldTimber);
            foreach (float t in signs)
                Beam(tower, "Rail post", basePos + new Vector3(s * 1.5f, deck, t * 1.5f), basePos + new Vector3(s * 1.5f, deck + 1.05f, t * 1.5f), 0.05f, m.OldTimber);
        }
        // the telescope itself: a brass-and-black tube on a post at the eye point
        Prim(PrimitiveType.Cylinder, tower, "Telescope post", basePos + Vector3.up * (deck + 0.6f), Quaternion.identity, new Vector3(0.08f, 0.6f, 0.08f), m.DarkTimber);
        // ladder up the south face
        Vector3 l0 = basePos + new Vector3(-0.35f, 0f, -bottom - 0.25f), l1 = basePos + new Vector3(-0.35f, deck, -top - 0.05f);
        Vector3 r0 = l0 + Vector3.right * 0.55f, r1 = l1 + Vector3.right * 0.55f;
        Beam(tower, "Ladder rail", l0, l1, 0.035f, m.DarkTimber);
        Beam(tower, "Ladder rail", r0, r1, 0.035f, m.DarkTimber);
        for (float y = 0.4f; y < deck; y += 0.45f)
        {
            float t = y / deck;
            Beam(tower, "Rung", Vector3.Lerp(l0, l1, t), Vector3.Lerp(r0, r1, t), 0.022f, m.OldTimber);
        }
    }

    // A survivor: hooded parka, trousers, boots, mittens, small pack. Feet at the root, faces +Z.
    private static SurvivorFigure Figure(Transform parent, string name, SurvivorFigure.Pose pose, Vector3 at, Quaternion facing,
        Material jacket, Mats m, int hash)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.SetPositionAndRotation(at, facing);
        var fig = root.gameObject.AddComponent<SurvivorFigure>();
        fig.pose = pose;
        bool seated = pose == SurvivorFigure.Pose.Seated;

        Transform Node(string n, Transform p, Vector3 local)
        {
            var t = new GameObject(n).transform;
            t.SetParent(p, false);
            t.localPosition = local;
            return t;
        }
        void Part(Transform p, string n, PrimitiveType type, Vector3 local, Vector3 size, Material mat, Vector3? euler = null)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = n;
            g.transform.SetParent(p, false);
            g.transform.localPosition = local;
            g.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            g.transform.localScale = size;
            g.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        Transform pelvis = Node("Pelvis", root, new Vector3(0f, seated ? 0.24f : 0.95f, 0f));
        Transform torso = Node("Torso", pelvis, new Vector3(0f, 0.02f, 0f));
        Part(torso, "Parka", PrimitiveType.Capsule, new Vector3(0f, 0.33f, 0f), new Vector3(0.44f, 0.36f, 0.30f), jacket);
        Part(torso, "Parka hem", PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(0.42f, 0.06f, 0.30f), jacket);
        if (hash % 3 != 0) Part(torso, "Pack", PrimitiveType.Cube, new Vector3(0f, 0.38f, -0.2f), new Vector3(0.30f, 0.40f, 0.15f), m.Pack);
        Transform head = Node("Head", torso, new Vector3(0f, 0.68f, 0f));
        Part(head, "Face", PrimitiveType.Sphere, new Vector3(0f, 0.12f, 0.01f), new Vector3(0.21f, 0.24f, 0.21f), m.Skin);
        Part(head, "Hood", PrimitiveType.Sphere, new Vector3(0f, 0.15f, -0.035f), new Vector3(0.27f, 0.29f, 0.26f), jacket);
        Part(head, "Hat", PrimitiveType.Sphere, new Vector3(0f, 0.25f, 0f), new Vector3(0.2f, 0.12f, 0.2f), m.Hat);
        Transform armL = Node("Arm L", torso, new Vector3(-0.25f, 0.56f, 0f));
        Transform armR = Node("Arm R", torso, new Vector3(0.25f, 0.56f, 0f));
        foreach (Transform arm in new[] { armL, armR })
        {
            Part(arm, "Sleeve", PrimitiveType.Capsule, new Vector3(0f, -0.29f, 0f), new Vector3(0.13f, 0.31f, 0.13f), jacket);
            Part(arm, "Mitten", PrimitiveType.Sphere, new Vector3(0f, -0.6f, 0.01f), new Vector3(0.11f, 0.12f, 0.11f), m.Boots);
        }
        Transform legL = Node("Leg L", pelvis, new Vector3(-0.1f, 0f, 0f));
        Transform legR = Node("Leg R", pelvis, new Vector3(0.1f, 0f, 0f));
        foreach (Transform leg in new[] { legL, legR })
        {
            Part(leg, "Trouser", PrimitiveType.Capsule, new Vector3(0f, -0.45f, 0f), new Vector3(0.16f, 0.46f, 0.16f), m.Trousers);
            Part(leg, "Boot", PrimitiveType.Cube, new Vector3(0f, -0.89f, 0.05f), new Vector3(0.14f, 0.12f, 0.26f), m.Boots);
        }
        if (seated)
        {
            legL.localRotation = Quaternion.Euler(-84f, -6f, 0f);
            legR.localRotation = Quaternion.Euler(-80f, 7f, 0f);
            armL.localRotation = Quaternion.Euler(-38f, 0f, 8f);
            armR.localRotation = Quaternion.Euler(-34f, 0f, -8f);
        }
        else if (pose == SurvivorFigure.Pose.Standing)
        {
            armL.localRotation = Quaternion.Euler(-18f, 0f, 6f);
            armR.localRotation = Quaternion.Euler(-18f, 0f, -6f);
        }
        fig.pelvis = pelvis; fig.torso = torso; fig.head = head;
        fig.armL = armL; fig.armR = armR; fig.legL = legL; fig.legR = legR;
        return fig;
    }
}
