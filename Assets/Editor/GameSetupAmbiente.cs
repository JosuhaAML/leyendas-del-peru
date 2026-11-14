using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

// Ambientación "de cine" de los niveles: postprocesado (brillo, color, sombras), modelos 3D reales de
// Poly Haven (rocas, troncos, plantas, objetos), niebla baja, rayos de luz, suelo mojado y ruinas talladas.
public static partial class GameSetup
{
    public enum Mood { Selva, Cocha, RioNoche, BosqueNoche, AndesNoche }

    // El shader aditivo de las runas, rayos de luz y círculos rituales siempre va incluido en el juego.
    static void IncludeShader(string name)
    {
        var shader = Shader.Find(name);
        if (shader == null) return;
        var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (settings.Length == 0) return;
        var so = new SerializedObject(settings[0]);
        var list = so.FindProperty("m_AlwaysIncludedShaders");
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------------------------------------------------------- Postprocesado
    static void PostFX(Mood mood)
    {
        IncludeShader("Legacy Shaders/Particles/Additive");
        var cam = Camera.main != null ? Camera.main.gameObject : GameObject.Find("Main Camera");
        var camera = cam.GetComponent<Camera>();
        camera.allowHDR = true;
        var layer = cam.AddComponent<PostProcessLayer>();
        var resources = AssetDatabase.LoadAssetAtPath<PostProcessResources>("Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset");
        if (resources != null) layer.Init(resources);
        layer.volumeTrigger = cam.transform;
        layer.volumeLayer = 1;   // capa "Default"
        layer.antialiasingMode = PostProcessLayer.Antialiasing.FastApproximateAntialiasing;

        EnsureFolder(Root + "/PostFX");
        string path = $"{Root}/PostFX/{mood}.asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<PostProcessProfile>();
        AssetDatabase.CreateAsset(profile, path);

        var bloom = Add<Bloom>(profile);
        var grade = Add<ColorGrading>(profile);
        var vignette = Add<Vignette>(profile);
        var ao = Add<AmbientOcclusion>(profile);
        grade.gradingMode.Override(GradingMode.HighDefinitionRange);
        grade.tonemapper.Override(Tonemapper.ACES);
        ao.mode.Override(AmbientOcclusionMode.MultiScaleVolumetricObscurance);
        ao.intensity.Override(0.7f);
        vignette.smoothness.Override(0.45f);

        switch (mood)
        {
            case Mood.Selva:
                bloom.intensity.Override(1.4f); bloom.threshold.Override(1f);
                grade.temperature.Override(8f); grade.tint.Override(-6f); grade.saturation.Override(18f); grade.contrast.Override(18f);
                grade.postExposure.Override(0.35f);
                vignette.intensity.Override(0.32f);
                break;
            case Mood.Cocha:
                bloom.intensity.Override(1.8f); bloom.threshold.Override(0.95f); bloom.color.Override(new Color(1f, 0.85f, 0.6f));
                grade.temperature.Override(15f); grade.saturation.Override(14f); grade.contrast.Override(20f);
                grade.postExposure.Override(0.3f);
                vignette.intensity.Override(0.35f);
                break;
            case Mood.RioNoche:
                bloom.intensity.Override(3f); bloom.threshold.Override(0.8f); bloom.color.Override(new Color(1f, 0.8f, 0.55f));
                grade.temperature.Override(-12f); grade.tint.Override(6f); grade.saturation.Override(10f); grade.contrast.Override(22f);
                grade.postExposure.Override(0.95f);
                vignette.intensity.Override(0.45f);
                break;
            case Mood.BosqueNoche:
                bloom.intensity.Override(3.5f); bloom.threshold.Override(0.75f); bloom.color.Override(new Color(1f, 0.75f, 0.5f));
                grade.temperature.Override(-18f); grade.tint.Override(14f); grade.saturation.Override(12f); grade.contrast.Override(24f);
                grade.postExposure.Override(0.8f);
                vignette.intensity.Override(0.48f);
                break;
            default: // Andes de noche
                bloom.intensity.Override(2.6f); bloom.threshold.Override(0.85f); bloom.color.Override(new Color(1f, 0.75f, 0.5f));
                grade.temperature.Override(-15f); grade.saturation.Override(5f); grade.contrast.Override(20f);
                grade.postExposure.Override(0.95f);
                vignette.intensity.Override(0.42f);
                break;
        }
        EditorUtility.SetDirty(profile);

        var volGo = new GameObject("Postprocesado");
        volGo.layer = 0;
        var volume = volGo.AddComponent<PostProcessVolume>();
        volume.isGlobal = true;
        volume.priority = 1;
        volume.sharedProfile = profile;
    }

    static T Add<T>(PostProcessProfile profile) where T : PostProcessEffectSettings
    {
        var s = profile.AddSettings<T>();
        s.enabled.Override(true);
        s.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(s, profile);
        return s;
    }

    // ---------------------------------------------------------------- Modelos de Poly Haven
    static GameObject PH(string name) =>
        AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Modelos/PolyHaven/{name}/{name}_1k.gltf")
        ?? AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Modelos/PolyHaven/{name}/{name}.glb");   // árboles aligerados

    // Coloca un modelo real. scale = tamaño; collider = si se puede chocar con él.
    static GameObject Place(string name, Vector3 pos, float yaw, float scale, bool collider = false, Transform parent = null)
    {
        var asset = PH(name);
        if (asset == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0, yaw, 0);
        go.transform.localScale = Vector3.one * scale;
        if (collider)
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        go.isStatic = true;
        return go;
    }

    static bool FreeZone(Vector3 p) => !(p.z < -6f && Mathf.Abs(p.x) < 9f) && new Vector2(p.x, p.z).magnitude > 11f;

    // Esparce modelos en un anillo alrededor de la arena (deja libre el centro y la entrada).
    static void Scatter(System.Random rnd, string[] names, int count, float min, float max, float sMin, float sMax, bool collider = false)
    {
        for (int i = 0; i < count; i++)
        {
            var p = RingPos(rnd, min, max);
            if (!FreeZone(p)) continue;
            Place(names[rnd.Next(0, names.Length)], p, rnd.Next(0, 360), R(rnd, sMin, sMax), collider);
        }
    }

    static readonly string[] JunglePlants = { "fern_02", "calathea_orbifolia_01", "pachira_aquatica_01", "anthurium_botany_01", "shrub_02" };
    static readonly string[] DarkPlants = { "fern_02", "shrub_04", "calathea_orbifolia_01", "fern_02" };
    static readonly string[] Rocks = { "rock_moss_set_01", "rock_moss_set_02", "rock_07", "boulder_01" };
    static readonly string[] Logs = { "dead_tree_trunk", "dead_tree_trunk_02", "tree_stump_01" };

    // Vegetación y rocas reales de cada nivel.
    static void Dress(Mood mood, System.Random rnd)
    {
        switch (mood)
        {
            case Mood.Selva:
                Scatter(rnd, JunglePlants, 110, 11, 46, 1.2f, 2.4f);
                Scatter(rnd, new[] { "grass_medium_01", "grass_medium_02", "moss_01" }, 120, 10, 40, 1f, 1.8f);
                Scatter(rnd, Rocks, 18, 13, 44, 1f, 2.2f, true);
                Scatter(rnd, Logs, 12, 13, 40, 1f, 1.6f, true);
                break;
            case Mood.Cocha:
                // Todo en la orilla lejana: la cocha ocupa el centro (radio LakeRadius).
                Scatter(rnd, JunglePlants, 70, LakeRadius + 1f, 46, 1.2f, 2.2f);
                Scatter(rnd, new[] { "grass_medium_01", "grass_medium_02" }, 90, LakeRadius + 0.5f, 44, 1f, 1.6f);
                Scatter(rnd, Rocks, 14, LakeRadius + 1f, 44, 1f, 2f, true);
                Scatter(rnd, Logs, 8, LakeRadius + 1f, 42, 1f, 1.5f, true);
                break;
            case Mood.RioNoche:
                Scatter(rnd, new[] { "grass_medium_01", "grass_medium_02", "shrub_02" }, 80, 10, 45, 1f, 1.6f);
                Scatter(rnd, new[] { "planter_pot_clay" }, 14, 14, 36, 0.9f, 1.3f);
                Scatter(rnd, Rocks, 10, 13, 44, 0.8f, 1.6f, true);
                break;
            case Mood.BosqueNoche:
                Scatter(rnd, DarkPlants, 90, 11, 46, 1.2f, 2.3f);
                Scatter(rnd, new[] { "moss_01", "grass_medium_02" }, 90, 10, 40, 1f, 1.6f);
                Scatter(rnd, Logs, 16, 13, 42, 1f, 1.7f, true);
                Scatter(rnd, Rocks, 12, 13, 44, 1f, 2f, true);
                break;
            default:
                Scatter(rnd, Rocks, 30, 12, 46, 1.2f, 3f, true);
                Scatter(rnd, new[] { "grass_medium_02" }, 80, 10, 40, 0.8f, 1.3f);
                // Laderas de montaña rocosas de fondo.
                for (int i = 0; i < 6; i++)
                {
                    var p = Quaternion.Euler(0, i * 60 + 20, 0) * Vector3.forward * 70f;
                    Place("mountainside", p, i * 60 + 200, 6f);
                }
                break;
        }
    }

    // ---------------------------------------------------------------- Atmósfera
    static void Mist(Color color, float density)
    {
        var m = new GameObject("Niebla baja").AddComponent<GroundMist>();
        m.color = color;
        m.density = density;
    }

    // Rayos de luz que bajan entre los árboles (planos con brillo aditivo que laten suave).
    static void LightShafts(System.Random rnd, Color color, int count, float tilt)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Texturas/rayo_luz.png");
        string path = $"{Root}/Materiales/Rayo_{ColorUtility.ToHtmlStringRGB(color)}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.mainTexture = tex;
        mat.SetColor("_TintColor", new Color(color.r, color.g, color.b, 0.22f));
        EditorUtility.SetDirty(mat);
        for (int i = 0; i < count; i++)
        {
            var p = RingPos(rnd, 8, 36);
            float h = R(rnd, 16f, 26f);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = "Rayo de luz";
            q.transform.position = p + Vector3.up * h * 0.45f;
            q.transform.rotation = Quaternion.Euler(0, rnd.Next(0, 360), tilt + R(rnd, -6f, 6f));
            q.transform.localScale = new Vector3(R(rnd, 2.5f, 5f), h, 1f);
            q.GetComponent<Renderer>().sharedMaterial = mat;
            q.AddComponent<LightShaft>();
        }
    }

    // Suelo mojado: el suelo refleja la luz (más brillo) y hay charcos que brillan con las llamas.
    static void Wet(float gloss, int puddles, System.Random rnd)
    {
        var ground = GameObject.Find("Suelo");
        if (ground != null)
        {
            var m = ground.GetComponent<Renderer>().sharedMaterial;
            m.SetFloat("_Glossiness", gloss);
            EditorUtility.SetDirty(m);
        }
        var puddle = TexMat("agua", new Color(0.12f, 0.13f, 0.14f), new Vector2(2f, 2f), 0.97f);
        for (int i = 0; i < puddles; i++)
        {
            var p = RingPos(rnd, 3, 30);
            float s = R(rnd, 1.5f, 4f);
            var d = Prim(PrimitiveType.Cylinder, null, p + Vector3.up * 0.015f, new Vector3(s, 0.005f, s * R(rnd, 0.5f, 1f)), puddle);
            d.transform.rotation = Quaternion.Euler(0, rnd.Next(0, 360), 0);
            d.name = "Charco";
        }
    }

    // ---------------------------------------------------------------- Cocha (nivel 2): cascada, victorias regias y flores
    static void Waterfall(Vector3 pos, float yaw, float width, float height)
    {
        var root = new GameObject("Cascada").transform;
        root.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var rock = TexMat("mossy_rock", new Color(0.7f, 0.72f, 0.65f), new Vector2(3f, 3f));
        // Acantilado de roca con musgo a los lados y detrás.
        Prim(PrimitiveType.Cube, root, new Vector3(0, height / 2f, 1.5f), new Vector3(width + 10f, height, 3f), rock);
        for (int i = 0; i < 6; i++)
            Prim(PrimitiveType.Sphere, root, new Vector3((i % 2 == 0 ? -1 : 1) * (width / 2f + 1.5f + i * 0.4f), height * (0.2f + i * 0.12f), 0.5f),
                Vector3.one * (3f + i % 3), rock);
        // Agua cayendo.
        var waterMat = TexMat("agua", new Color(0.75f, 0.9f, 0.95f), new Vector2(2f, 4f), 0.9f);
        var water = Prim(PrimitiveType.Quad, root, new Vector3(0, height / 2f, -0.1f), new Vector3(width, height, 1f), waterMat);
        water.transform.localRotation = Quaternion.Euler(0, 180, 0);
        var fx = root.gameObject.AddComponent<WaterfallFX>();
        fx.water = water.GetComponent<Renderer>();
        fx.width = width;
        Place("pachira_aquatica_01", pos + Quaternion.Euler(0, yaw, 0) * new Vector3(-width / 2f - 3f, 0, -2f), yaw, 2.2f);
        Place("pachira_aquatica_01", pos + Quaternion.Euler(0, yaw, 0) * new Vector3(width / 2f + 3f, 0, -2f), yaw + 90, 2f);
    }

    // Victoria regia: hoja enorme con borde levantado y, a veces, su flor rosada o blanca.
    static void VictoriaRegia(Vector3 pos, System.Random rnd)
    {
        float s = R(rnd, 1.5f, 3f);
        var leaf = TexMat("forest_leaves_02", new Color(0.35f, 0.62f, 0.22f), Vector2.one * 2f, 0.4f);
        var rim = TexMat("forest_leaves_02", new Color(0.55f, 0.3f, 0.2f), Vector2.one, 0.3f);
        Prim(PrimitiveType.Cylinder, null, pos + Vector3.up * 0.05f, new Vector3(s, 0.02f, s), leaf);
        Prim(PrimitiveType.Cylinder, null, pos + Vector3.up * 0.1f, new Vector3(s * 1.04f, 0.06f, s * 1.04f), rim).transform.localScale = new Vector3(s * 1.04f, 0.05f, s * 1.04f);
        if (rnd.NextDouble() < 0.5)
        {
            var petal = Mat(rnd.NextDouble() < 0.5 ? "Flor regia rosada" : "Flor regia blanca",
                rnd.NextDouble() < 0.5 ? new Color(1f, 0.6f, 0.75f) : new Color(1f, 0.97f, 0.92f));
            var center = pos + Quaternion.Euler(0, rnd.Next(0, 360), 0) * Vector3.forward * s * 0.25f + Vector3.up * 0.25f;
            for (int i = 0; i < 8; i++)
            {
                var p = Prim(PrimitiveType.Sphere, null, center + Quaternion.Euler(0, i * 45, 0) * Vector3.forward * 0.22f, new Vector3(0.18f, 0.12f, 0.4f), petal);
                p.transform.rotation = Quaternion.Euler(-30, i * 45, 0);
            }
            Prim(PrimitiveType.Sphere, null, center + Vector3.up * 0.08f, Vector3.one * 0.18f, Mat("Centro flor", new Color(1f, 0.85f, 0.3f)));
        }
    }

    static void LagoonDressing()
    {
        var rnd = new System.Random(222);
        Waterfall(new Vector3(-6, 0, LakeRadius + 3f), 180, 9f, 16f);   // cae a la cocha desde la orilla
        for (int i = 0; i < 26; i++)
        {
            // En el agua, fuera del camino de la Yacumama (que nada a ~12,5 m de la isla).
            var p = Quaternion.Euler(0, R(rnd, 0f, 360f), 0) * Vector3.forward * R(rnd, 17f, LakeRadius - 2f) + Vector3.up * 0.06f;
            VictoriaRegia(p, rnd);
        }
        // Flores rojas en la orilla (las raíces oscuras parecían escombros).
        for (int i = 0; i < 16; i++)
            Place("anthurium_botany_01", Quaternion.Euler(0, R(rnd, 0f, 360f), 0) * Vector3.forward * R(rnd, LakeRadius + 0.5f, LakeRadius + 4f), rnd.Next(0, 360), R(rnd, 1.6f, 2.4f));
    }

    // Ruinas mirando hacia el centro de la arena.
    static void RuinsFacing(Vector3 pos, System.Random rnd, bool big) =>
        Ruins(pos, Quaternion.LookRotation(new Vector3(-pos.x, 0, -pos.z)).eulerAngles.y, rnd, big);

    // Todo el ambiente de un nivel de una vez.
    static void Atmosphere(Mood mood)
    {
        var rnd = new System.Random(100 + (int)mood);
        PostFX(mood);
        Dress(mood, rnd);
        switch (mood)
        {
            case Mood.Selva:
                Mist(new Color(0.78f, 0.88f, 0.78f), 0.9f);
                LightShafts(rnd, new Color(1f, 0.92f, 0.65f), 10, 14f);
                Wet(0.45f, 6, rnd);
                RuinsFacing(new Vector3(-26, 0, 30), rnd, true);
                RuinsFacing(new Vector3(30, 0, 24), rnd, false);
                break;
            case Mood.Cocha:
                LagoonDressing();
                Mist(new Color(0.85f, 0.88f, 0.8f), 1.6f);
                LightShafts(rnd, new Color(1f, 0.85f, 0.55f), 12, 16f);
                RuinsFacing(new Vector3(34, 0, 42), rnd, true);
                RuinsFacing(new Vector3(-36, 0, 36), rnd, false);
                break;
            case Mood.RioNoche:
                Mist(new Color(0.35f, 0.4f, 0.55f), 1f);
                LightShafts(rnd, new Color(0.6f, 0.7f, 1f), 4, 18f);
                Wet(0.55f, 8, rnd);
                break;
            case Mood.BosqueNoche:
                Mist(new Color(0.45f, 0.42f, 0.62f), 1.6f);
                LightShafts(rnd, new Color(0.6f, 0.7f, 1f), 6, 20f);
                Wet(0.62f, 10, rnd);
                RuinsFacing(new Vector3(-24, 0, 22), rnd, false);
                RuinsFacing(new Vector3(26, 0, 14), rnd, false);
                break;
            default:
                Mist(new Color(0.6f, 0.65f, 0.8f), 0.8f);
                RuinsFacing(new Vector3(0, 0, 40), rnd, true);
                break;
        }
    }

    // Ruinas de piedra con relieves andinos tallados (cara de piedra y friso con llama).
    static void Ruins(Vector3 pos, float yaw, System.Random rnd, bool big)
    {
        var root = new GameObject("Ruinas").transform;
        root.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var cara = TexMat("relieve_cara", Color.white, Vector2.one, 0.15f);
        var llama = TexMat("relieve_llama", Color.white, Vector2.one, 0.15f);
        var block = TexMat("mossy_rock", new Color(0.8f, 0.78f, 0.7f), new Vector2(1f, 1f));
        float k = big ? 1.6f : 1f;
        // Estela con la cara tallada al centro.
        Prim(PrimitiveType.Cube, root, new Vector3(0, 2.4f * k, 0), new Vector3(2.6f, 4.8f, 1f) * k, cara, keepCollider: true);
        Prim(PrimitiveType.Cube, root, new Vector3(0, 5f * k, 0), new Vector3(3.2f, 0.5f, 1.3f) * k, block);
        // Muro con friso de llamas a los lados, uno roto.
        Prim(PrimitiveType.Cube, root, new Vector3(-3.4f * k, 1.4f * k, 0.3f), new Vector3(3.6f, 2.8f, 0.9f) * k, llama, keepCollider: true);
        var broken = Prim(PrimitiveType.Cube, root, new Vector3(3.3f * k, 0.9f * k, 0.4f), new Vector3(3.2f, 1.8f, 0.9f) * k, llama, keepCollider: true);
        broken.transform.localRotation = Quaternion.Euler(0, 0, 4f);
        // Escalinata y bloques caídos con musgo.
        for (int i = 0; i < 3; i++)
            Prim(PrimitiveType.Cube, root, new Vector3(0, 0.15f + i * 0.3f, 1.4f - i * 0.4f) * k, new Vector3(4.5f - i * 0.6f, 0.3f, 1f) * k, block, keepCollider: true);
        for (int i = 0; i < 4; i++)
        {
            var b = Prim(PrimitiveType.Cube, root, new Vector3(R(rnd, -6f, 6f), 0.4f, R(rnd, 1.5f, 4f)) * k, Vector3.one * R(rnd, 0.6f, 1.2f) * k, block, keepCollider: true);
            b.transform.localRotation = Quaternion.Euler(R(rnd, -20f, 20f), rnd.Next(0, 360), R(rnd, -20f, 20f));
        }
        // Plantas que crecen entre las piedras.
        Place("fern_02", pos + Quaternion.Euler(0, yaw, 0) * new Vector3(-1.8f, 0, 1.2f) * k, rnd.Next(0, 360), 1.6f);
        Place("anthurium_botany_01", pos + Quaternion.Euler(0, yaw, 0) * new Vector3(2.2f, 0, 1.5f) * k, rnd.Next(0, 360), 1.8f);
        root.gameObject.isStatic = true;
    }
}
