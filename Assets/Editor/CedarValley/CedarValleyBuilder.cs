using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Rebuilds the Cedar Valley landscape at real-world scale (1 unit = 1 m) from <see cref="CedarModel"/>.
/// Graph objects (sites, trails, signs, storage markers) keep their identity and XZ positions;
/// they are only re-seated on the new ground. Scenery lives under World/Landscape and has no colliders.
/// </summary>
internal static partial class CedarValleyBuilder
{
    public const string ScenePath = "Assets/Scenes/CedarValley.unity";
    private const string Gen = "Assets/Art/CedarValley/Generated";
    private const string MeshDir = Gen + "/Meshes";
    private const string MatDir = Gen + "/Materials";
    private const string Kit = "Assets/Art/WinterOutpost/";
    public static string OutDir => Path.Combine(Directory.GetCurrentDirectory(), "Temp", "CedarValleyBuild");
    private static readonly StringBuilder Log = new StringBuilder();

    // Original Y of label anchors and storage markers, kept relative to the ground they stood on.
    private static readonly Dictionary<string, float> OriginalY = new Dictionary<string, float>
    {
        { "Sign_North_Ridge", 46.16f }, { "Sign_West_Woods", 28.75f }, { "Sign_River_Road", -1.15f },
        { "Sign_Cliff_Pass", 34.63f }, { "Storage_N3", 27.74f }, { "Storage_W2", 8.09f }
    };

    private static CedarModel model;
    private static Transform landscape;
    private static Vector2 shadowDir = new Vector2(-0.5f, 0.866f);
    private static readonly Color ShadowTint = new Color(0.56f, 0.62f, 0.74f, 1f);
    private static MeshBuilder propShadows;
    private static readonly List<GameObject> kitPlaced = new List<GameObject>();

    [MenuItem("Tools/Cedar Valley/Rebuild Landscape", false, 1)]
    private static void RebuildMenu() => Run("build.txt", () => Rebuild(false));

    [MenuItem("Tools/Cedar Valley/Rebuild Landscape (regenerate textures)", false, 2)]
    private static void RebuildTexMenu() => Run("build.txt", () => Rebuild(true));

    public static void RebuildLandscapeBatch()
    {
        EditorSceneManager.OpenScene(ScenePath);
        Run("build.txt", () => Rebuild(false));
    }

    [MenuItem("Tools/Cedar Valley/Rebuild Forest and Map Overlays", false, 3)]
    private static void RebuildForestMenu() => Run("forestbuild.txt", RebuildForestAndOverlays);

    // Do not regenerate the terrain, reseat nodes, or regenerate trails for this visual-only pass.
    private static void RebuildForestAndOverlays()
    {
        var scene = Application.isBatchMode
            ? EditorSceneManager.OpenScene(ScenePath)
            : EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath || scene.isDirty)
            throw new InvalidOperationException("Open and save Cedar Valley before rebuilding its forest.");
        landscape = GameObject.Find("World/Landscape")?.transform;
        Transform graph = GameObject.Find("Graph/Nodes")?.transform;
        if (landscape == null || graph == null)
            throw new InvalidOperationException("The authored landscape and graph must already exist.");

        var nodes = new Dictionary<string, Vector2>();
        foreach (Transform t in graph)
        {
            string id = t.name == "N00_Camp" ? "Camp" : t.name.Replace("Node_", "");
            nodes.Add(id, new Vector2(t.position.x, t.position.z));
        }
        model = new CedarModel();
        model.Build(nodes, ProtectedGraphSites(graph.parent));
        Light sun = UnityEngine.Object.FindAnyObjectByType<Light>();
        if (sun != null)
        {
            Vector3 f = sun.transform.forward;
            shadowDir = new Vector2(f.x, f.z).normalized;
        }
        meshAssets.Clear();
        CedarTextures.LoadAll();
        Materials();
        PrepareForestLod();

        Transform old = landscape.Find("Forest");
        int index = old != null ? old.GetSiblingIndex() : landscape.childCount;
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        BuildForest();
        landscape.Find("Forest").SetSiblingIndex(index);
        SetupMapOverlays();
        SetupAtmosphere();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save Cedar Valley.");
        AssetDatabase.SaveAssets();
        Info("Saved forest and map overlays. Terrain and graph were not rebuilt.");
    }

    public static void Run(string logName, Action action)
    {
        Log.Clear();
        Log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + " " + logName);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { action(); }
        catch (Exception e)
        {
            Log.AppendLine("EXCEPTION " + e);
            if (Application.isBatchMode) throw;
            Debug.LogException(e);
        }
        finally
        {
            Log.AppendLine("elapsed " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
            if (Application.isBatchMode) Debug.Log(Log.ToString());
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, logName), Log.ToString());
        }
    }

    public static void Info(string s) => Log.AppendLine(s);

    // ================================================================== rebuild
    private static void Rebuild(bool regenTextures)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) { Info("ABORT: active scene is " + scene.path); return; }
        meshAssets.Clear(); // every rebuild rewrites its mesh assets from scratch
        Info("scene dirty before build: " + scene.isDirty);

        GameObject world = GameObject.Find("World"), graph = GameObject.Find("Graph");
        Transform legacy = world.transform.Find("Landscape (legacy)");
        Transform current = world.transform.Find("Landscape");
        bool alreadySeated = legacy != null;
        if (legacy == null)
        {
            current.name = "Landscape (legacy)";
            current.gameObject.SetActive(false);
            legacy = current;
            current = null;
        }
        if (current != null) UnityEngine.Object.DestroyImmediate(current.gameObject);

        SetupWinterLighting();

        var nodes = new Dictionary<string, Vector2>();
        var nodeObjects = new Dictionary<string, Transform>();
        foreach (Transform t in graph.transform.Find("Nodes"))
        {
            string id = t.name == "N00_Camp" ? "Camp" : t.name.Replace("Node_", "");
            nodes[id] = new Vector2(t.position.x, t.position.z);
            nodeObjects[id] = t;
        }
        Info("nodes: " + nodes.Count);

        model = new CedarModel();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        model.Build(nodes, ProtectedGraphSites(graph.transform));
        Info("model built in " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s; grid " + model.NX + "x" + model.NZ + ", camp y " + model.CampY.ToString("0.00"));

        if (regenTextures || AssetDatabase.LoadAssetAtPath<Texture2D>(CedarTextures.Folder + "/CV_TrailBed.png") == null)
            CedarTextures.GenerateAll();
        else CedarTextures.LoadAll();
        Materials();
        PrepareForestLod();

        landscape = new GameObject("Landscape").transform;
        landscape.SetParent(world.transform, false);
        landscape.SetSiblingIndex(0);
        propShadows = new MeshBuilder();
        kitPlaced.Clear();

        BuildTerrain();
        BuildRiver();
        BuildTrailbeds();
        foreach (BridgeModel b in model.Bridges) BuildCrossing(b);
        BuildCamp();
        BuildForest();
        BuildRocks();
        BuildShadows();

        if (!alreadySeated)
        {
            ReseatGraph(graph.transform, nodeObjects, legacy);
            UpdateTrails(graph.transform);
        }
        RemapKitMaterials();
        StripColliders(landscape);
        MarkStatic(landscape);
        Checks();
        SetupMapOverlays();
        SetupAtmosphere();
        SoftenTrailMarkings();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Info("saved scene");
    }

    private static IEnumerable<Vector2> ProtectedGraphSites(Transform graph)
    {
        foreach (string group in new[] { "Signs", "Structures" })
        {
            Transform sites = graph.Find(group);
            if (sites == null) continue;
            foreach (Transform site in sites)
                yield return new Vector2(site.position.x, site.position.z);
        }
    }

    private static void SetupWinterLighting()
    {
        // Soft winter daylight: a cool sky dome plus bright snow bounce from below, so shade
        // keeps its detail; a low, slightly warm sun from behind the valley (upper left on
        // screen) draws long shadows toward the lower right.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.60f, .66f, .76f);
        RenderSettings.ambientEquatorColor = new Color(.54f, .58f, .65f);
        RenderSettings.ambientGroundColor = new Color(.47f, .49f, .53f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.fog = false;

        Light sun = RenderSettings.sun != null ? RenderSettings.sun : GameObject.Find("Directional Light")?.GetComponent<Light>();
        if (sun == null) return;
        sun.transform.rotation = Quaternion.Euler(24f, 122f, 0f);
        sun.color = new Color(1f, .925f, .81f);
        sun.intensity = .84f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = .55f;
        Vector3 forward = sun.transform.forward;
        shadowDir = new Vector2(forward.x, forward.z).normalized;
        Info("winter sun " + forward + "; terrain and soft contact shadows share this direction");
    }

    /// <summary>Adds the Cedar Valley screen-constant overlay component (runtime, game assembly) once.</summary>
    private static void SetupMapOverlays()
    {
        Type t = Type.GetType("MapOverlayScaler, Assembly-CSharp");
        if (t == null) { Info("WARNING: MapOverlayScaler type not found"); return; }
        GameObject host = GameObject.Find("Map overlays");
        if (host == null) host = new GameObject("Map overlays");
        if (host.GetComponent(t) == null) host.AddComponent(t);
        Info("map overlays: screen-constant site markers and labels enabled");
    }

    // ================================================================== materials
    private static Material matFireStone, matGround, matIce, matTrailbed, matTrampled, matSpruce, matRock, matShadow, matTimber, matLogs, matShard, matMarking, matRope;

    private static Material Mat(string name, string shader)
    {
        Directory.CreateDirectory(MatDir);
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader sh = Shader.Find(shader);
        if (sh == null) throw new Exception("shader missing: " + shader);
        if (m == null) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.enableInstancing = true;
        return m;
    }

    private static Texture2D KitTex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(Kit + "Textures/" + n + ".png");

    private static void Materials()
    {
        matGround = Mat("CV_Ground", "CLSR/Cedar Ground");
        matGround.SetTexture("_SnowTex", CedarTextures.FreshSnow); matGround.SetFloat("_SnowTile", 8f);
        matGround.SetTexture("_PackedTex", CedarTextures.PackedSnow); matGround.SetFloat("_PackedTile", 4f);
        matGround.SetTexture("_RockTex", CedarTextures.Rock); matGround.SetFloat("_RockTile", 6f);
        matGround.SetTexture("_MacroTex", CedarTextures.Macro); matGround.SetFloat("_MacroTile", 180f);
        matGround.SetFloat("_MacroStrength", .13f);
        matGround.SetColor("_SnowColor", new Color(.90f, .93f, .95f));
        matGround.SetColor("_ForestTint", new Color(.64f, .72f, .76f));

        matIce = Mat("CV_RiverIce", "CLSR/Cedar River Ice");
        matIce.SetTexture("_MainTex", CedarTextures.Ice);
        matIce.SetTexture("_MacroTex", CedarTextures.Macro);
        SnowOn(matIce);

        matTrailbed = Mat("CV_Trailbed", "CLSR/Ground Decal");
        matTrailbed.SetTexture("_MainTex", CedarTextures.TrailBed); matTrailbed.SetFloat("_MainWorld", 0f);
        matTrailbed.SetColor("_Color", new Color(.91f, .88f, .81f, 1f));

        matTrampled = Mat("CV_TrampledSnow", "CLSR/Ground Decal");
        matTrampled.SetTexture("_MainTex", CedarTextures.PackedSnow); matTrampled.SetFloat("_MainWorld", 1f); matTrampled.SetFloat("_MainTile", 4f);
        matTrampled.SetColor("_Color", new Color(0.96f, 0.97f, 1f, 0.92f));

        matSpruce = Mat("CV_Spruce", "CLSR/Snow Detail");
        matSpruce.SetTexture("_MainTex", CedarTextures.Needles); matSpruce.SetTextureScale("_MainTex", new Vector2(3f, 4f));
        SnowOn(matSpruce);

        matRock = Mat("CV_Rock", "CLSR/Snow Detail");
        matRock.SetTexture("_MainTex", CedarTextures.Rock); matRock.SetFloat("_MainWorld", 1f); matRock.SetFloat("_MainTile", 3f);
        SnowOn(matRock); matRock.SetFloat("_AutoSnow", 0.85f); matRock.SetFloat("_AutoSnowStart", 0.72f);

        matFireStone = Mat("CV_FireRingStone", "CLSR/Snow Detail");
        matFireStone.SetTexture("_MainTex", CedarTextures.Rock); matFireStone.SetFloat("_MainWorld", 1f); matFireStone.SetFloat("_MainTile", 2f);
        matFireStone.SetColor("_Color", new Color(0.72f, 0.7f, 0.68f)); SnowOn(matFireStone); matFireStone.SetFloat("_AutoSnow", 0f);

        matShadow = Mat("CV_ContactShadow", "CLSR/Contact Shadow");
        matShadow.SetTexture("_MainTex", CedarTextures.Blob); matShadow.SetFloat("_Strength", 1f);

        matTimber = Mat("CV_BridgeTimber", "CLSR/Snow Detail");
        matTimber.SetTexture("_MainTex", KitTex("OldTimber")); SnowOn(matTimber);
        matLogs = Mat("CV_BridgeLogs", "CLSR/Snow Detail");
        matLogs.SetTexture("_MainTex", KitTex("PineBark")); SnowOn(matLogs);
        matLogs.SetFloat("_AutoSnow", 0.6f); matLogs.SetFloat("_AutoSnowStart", 0.8f);

        matShard = Mat("CV_IceShards", "CLSR/Snow Detail");
        matShard.SetTexture("_MainTex", CedarTextures.Ice); matShard.SetFloat("_MainWorld", 1f); matShard.SetFloat("_MainTile", 4f);
        SnowOn(matShard);

        matMarking = Mat("CV_TrailMarking", "Sprites/Default");
        matMarking.color = Color.white;

        matRope = AssetDatabase.LoadAssetAtPath<Material>(Kit + "Materials/Hemp.mat");
        AssetDatabase.SaveAssets();
        WinterMaterials(); // the winter art pass replaces every landscape material above
    }

    private static void SnowOn(Material m)
    {
        m.SetTexture("_SnowTex", CedarTextures.FreshSnow);
        m.SetFloat("_SnowTile", 8f);
    }

    // ================================================================== helpers
    private static readonly Dictionary<string, string> meshAssets = new Dictionary<string, string>();

    /// <summary>Saves meshes into one container asset per group (replacing any previous build).</summary>
    private static void SaveMeshes(string group, IList<Mesh> meshes)
    {
        Directory.CreateDirectory(MeshDir);
        string path = MeshDir + "/CV_" + group + ".asset";
        if (!meshAssets.ContainsKey(path))
        {
            AssetDatabase.DeleteAsset(path);
            meshAssets[path] = path;
            AssetDatabase.CreateAsset(meshes[0], path);
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], path);
        }
        else foreach (Mesh m in meshes) AssetDatabase.AddObjectToAsset(m, path);
    }

    private static void SaveForestMeshes(string group, IList<Mesh> meshes)
    {
        Directory.CreateDirectory(MeshDir);
        string path = MeshDir + "/CV_" + group + ".asset";
        if (!meshAssets.ContainsKey(path))
        {
            AssetDatabase.DeleteAsset(path);
            Type packType = Type.GetType("CedarForestMeshPack, Assembly-CSharp");
            if (packType == null) throw new InvalidOperationException("CedarForestMeshPack is missing.");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance(packType), path);
            meshAssets[path] = path;
        }
        foreach (Mesh mesh in meshes)
        {
            if (mesh.vertexCount == 0) { UnityEngine.Object.DestroyImmediate(mesh); continue; }
            var colors = new List<Color32>();
            mesh.GetColors(colors);
            mesh.SetColors(colors);
            MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
            AssetDatabase.AddObjectToAsset(mesh, path);
        }
    }

    private static GameObject MeshObject(string name, Transform parent, Mesh mesh, params Material[] mats)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go;
    }

    private static Transform Group(string name, Transform parent = null)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent != null ? parent : landscape, false);
        return t;
    }

    /// <summary>Places a Winter Outpost kit model so its whole footprint rests on (or slightly into) the ground.</summary>
    private static GameObject PlaceKit(string kitName, Transform parent, Vector2 xz, float yaw, float scale = 1f, float sink = 0.04f, float shadow = 0.5f)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + kitName + ".obj");
        if (src == null) { Info("missing kit model " + kitName); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        go.transform.localScale = Vector3.one * scale;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.position = new Vector3(xz.x, 0f, xz.y);
        Bounds b = WorldBounds(go);
        float minG = float.MaxValue, maxG = float.MinValue;
        for (int i = 0; i <= 2; i++)
        for (int j = 0; j <= 2; j++)
        {
            float x = Mathf.Lerp(b.min.x, b.max.x, i * 0.5f), z = Mathf.Lerp(b.min.z, b.max.z, j * 0.5f);
            float g = CedarModelSurface(x, z);
            minG = Mathf.Min(minG, g); maxG = Mathf.Max(maxG, g);
        }
        float pivotOffset = b.min.y - go.transform.position.y;
        go.transform.position = new Vector3(xz.x, minG - pivotOffset - sink, xz.y);
        go.name = kitName.Substring(3).Replace('_', ' ');
        kitPlaced.Add(go);
        if (shadow > 0f)
        {
            float k = 1f / Mathf.Tan(Mathf.Deg2Rad * (biome != null ? biome.shadowSunElevation : 24f));
            float footprint = Mathf.Max(b.size.x, b.size.z);
            Vector2 c = new Vector2(b.center.x, b.center.z) + shadowDir * b.size.y * k * 0.42f;
            CedarMeshes.Shadow(propShadows, model, c, shadowDir, footprint * 0.55f + b.size.y * k * 0.45f,
                footprint * 0.55f, shadow * 0.9f, ShadowTint);
        }
        return go;
    }

    private static float CedarModelSurface(float x, float z) => model.Surface(x, z);

    public static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    private static void StripColliders(Transform root)
    {
        int n = 0;
        foreach (Collider c in root.GetComponentsInChildren<Collider>(true)) { UnityEngine.Object.DestroyImmediate(c); n++; }
        Info("colliders removed from scenery: " + n);
    }

    // Kit props share materials, so static batching merges their draw calls in the web build.
    // Generated meshes are already combined per material and stay non-static.
    private static void MarkStatic(Transform root)
    {
        foreach (GameObject go in kitPlaced)
        {
            if (go == null) continue;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        }
    }

    // ================================================================== terrain + river
    private static void BuildTerrain()
    {
        var t = Group("Terrain");
        List<Mesh> chunks = CedarMeshes.TerrainChunks(model);
        List<Mesh> coarse = CedarMeshes.TerrainChunks(model, coarse: true);
        Mesh apron = CedarMeshes.Apron(model);
        var all = new List<Mesh>(chunks) { apron };
        SaveMeshes("Terrain", all);
        SaveMeshes("OverviewTerrain", coarse);
        var detail = Group("Detailed terrain", t);
        var overview = Group("Overview terrain", t);
        long fineTriangles = 0, coarseTriangles = 0;
        foreach (Mesh m in chunks) { MeshObject(m.name, detail, m, matGround); fineTriangles += m.GetIndexCount(0) / 3; }
        foreach (Mesh m in coarse) { MeshObject(m.name, overview, m, matGround); coarseTriangles += m.GetIndexCount(0) / 3; }
        detail.gameObject.SetActive(false);
        MeshObject("Terrain surround", t, apron, matGround);
        Info($"terrain: {chunks.Count} chunks, {coarseTriangles} overview / {fineTriangles} detail triangles (+ {apron.GetIndexCount(0) / 3} surround)");
    }

    private static void BuildRiver()
    {
        Mesh ice = CedarMeshes.RiverIce(model);
        SaveMeshes("River", new[] { ice });
        MeshObject("Frozen Cedar River", landscape, ice, matIce);
        float minW = 99f, maxW = 0f;
        foreach (float w in model.River.HalfW) { minW = Mathf.Min(minW, w); maxW = Mathf.Max(maxW, w); }
        Info("river: length " + model.River.Line.Length.ToString("0") + " m, width " + (2 * minW).ToString("0.0") + "-" + (2 * maxW).ToString("0.0") + " m");
    }

    private static void BuildTrailbeds()
    {
        var all = new MeshBuilder();
        foreach (TrailModel t in model.Trails)
        {
            Mesh m = CedarMeshes.Trailbed(model, t);
            var mb = new MeshBuilder();
            mb.V.AddRange(m.vertices); mb.N.AddRange(m.normals); mb.C.AddRange(m.colors); mb.UV.AddRange(m.uv);
            mb.Sub[0].AddRange(m.triangles);
            all.Append(mb, Matrix4x4.identity);
            UnityEngine.Object.DestroyImmediate(m);
        }
        Mesh beds = all.ToMesh("Packed snow trailbeds");
        SaveMeshes("Trailbeds", new[] { beds });
        MeshObject("Packed snow trailbeds", landscape, beds, matTrailbed);
    }

    // ================================================================== graph objects
    private static float LegacyGround(Transform legacy, float x, float z)
    {
        Transform terrain = legacy.Find("Cedar snow terrain");
        if (terrain == null) return 0f;
        Mesh mesh = terrain.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] v = mesh.vertices;
        int[] tri = mesh.triangles;
        Matrix4x4 m = terrain.localToWorldMatrix;
        for (int i = 0; i < tri.Length; i += 3)
        {
            Vector3 a = m.MultiplyPoint3x4(v[tri[i]]), b = m.MultiplyPoint3x4(v[tri[i + 1]]), c = m.MultiplyPoint3x4(v[tri[i + 2]]);
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(d) < 1e-6f) continue;
            float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
            float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
            float l3 = 1f - l1 - l2;
            if (l1 >= -1e-4f && l2 >= -1e-4f && l3 >= -1e-4f) return l1 * a.y + l2 * b.y + l3 * c.y;
        }
        return 0f;
    }

    private static void ReseatGraph(Transform graph, Dictionary<string, Transform> nodes, Transform legacy)
    {
        foreach (var kv in nodes)
        {
            Vector3 p = kv.Value.position;
            float g = kv.Key == "Camp" ? model.CampY : model.NodeGround[kv.Key];
            kv.Value.position = new Vector3(p.x, g + 0.6f, p.z);
        }
        foreach (string group in new[] { "Signs", "Structures" })
        {
            Transform gt = graph.Find(group);
            if (gt == null) continue;
            foreach (Transform t in gt)
            {
                if (!OriginalY.TryGetValue(t.name, out float y0)) { Info("no original Y for " + t.name); continue; }
                Vector3 p = t.position;
                float offset = y0 - LegacyGround(legacy, p.x, p.z);
                t.position = new Vector3(p.x, model.Surface(p.x, p.z) + offset, p.z);
                Info(t.name + " offset above ground " + offset.ToString("0.00"));
            }
        }
    }

    private static void UpdateTrails(Transform graph)
    {
        Transform trails = graph.Find("Trails");
        var marking = new Color(0.44f, 0.50f, 0.58f, 0.42f);
        foreach (TrailModel t in model.Trails)
        {
            Transform tr = trails.Find(t.Name);
            if (tr == null) { Info("MISSING trail object " + t.Name); continue; }
            for (int i = tr.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(tr.GetChild(i).gameObject);
            LineRenderer lr = tr.GetComponent<LineRenderer>();
            if (lr == null) lr = tr.gameObject.AddComponent<LineRenderer>();
            tr.rotation = Quaternion.Euler(90f, 0f, 0f);
            var pts = new List<Vector3>();
            for (int i = 0; i < t.Line.Count; i++)
            {
                if (i % 2 == 1 && i != t.Line.Count - 1) continue;
                Vector2 p = t.Line.P[i];
                float y = t.OnDeck[i] ? t.Bridge.DeckY - 0.16f : model.Surface(p.x, p.y) + 0.06f;
                pts.Add(new Vector3(p.x, y, p.y));
            }
            lr.useWorldSpace = true;
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            lr.alignment = LineAlignment.TransformZ;
            lr.widthMultiplier = 1f;
            lr.widthCurve = AnimationCurve.Constant(0f, 1f, 0.3f);
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.startColor = marking; lr.endColor = marking;
            lr.sharedMaterial = matMarking;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.loop = false;
            Info(t.Name + ": " + t.Line.Length.ToString("0") + " m, " + pts.Count + " pts" + (t.Bridge != null ? " via " + t.Bridge.Name : ""));
        }
    }
}
