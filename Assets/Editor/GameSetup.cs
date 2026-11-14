using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Genera todo el contenido provisional del juego: materiales, jugador, jefes, menú y 5 niveles.
// Menú "Leyendas > Generar proyecto". Cuando tengas los modelos de Sketchfab, reemplaza
// el hijo "Visual" de cada prefab/jefe por el modelo real.
public static partial class GameSetup
{
    const string Root = "Assets/Juego";
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    [MenuItem("Leyendas/Generar proyecto")]
    public static void Generate()
    {
        foreach (var f in new[] { "", "/Materiales", "/Prefabs", "/Escenas", "/Jefes" }) EnsureFolder(Root + f);

        // Reimporta la interfaz con los ajustes de UIImport (sin escalar ni comprimir).
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/UI" }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);

        var player = BuildPlayerPrefab();
        var warning = BuildAreaWarningPrefab();

        var scenes = new List<string>
        {
            BuildMenu(player),
            BuildLevel1(warning),
            BuildLevel2(warning),
            BuildLevel3(warning),
            BuildLevel4(warning),
            BuildLevel5(warning),
            BuildVictory(),
            BuildLobby(),
        };
        EditorBuildSettings.scenes = scenes.ConvertAll(s => new EditorBuildSettingsScene(s, true)).ToArray();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(scenes[0]);
        Debug.Log("LEYENDAS: proyecto generado");
    }

    public static void RunBatch()
    {
        Generate();
        EditorApplication.Exit(0);
    }

    // ---------- Prefabs ----------

    static GameObject BuildPlayerPrefab()
    {
        var root = new GameObject("Jugador");
        var cc = root.AddComponent<CharacterController>();
        cc.center = new Vector3(0, 1, 0);
        cc.height = 2f;
        cc.radius = 0.4f;
        root.AddComponent<NetworkObject>();
        root.AddComponent<OwnerNetworkTransform>();
        root.AddComponent<Health>();
        root.AddComponent<PlayerController>();
        root.AddComponent<PlayerAbilities>();

        var visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        Prim(PrimitiveType.Capsule, visual, new Vector3(0, 1, 0), Vector3.one, Mat("Jugador", Color.white));
        Prim(PrimitiveType.Cube, visual, new Vector3(0, 1.5f, 0.4f), new Vector3(0.35f, 0.15f, 0.3f), Mat("Jugador", Color.white));

        // Un modelo por clase (Assets/Modelos/<Clase>/<Clase>.fbx). PlayerController muestra el de su clase.
        foreach (PlayerRole role in System.Enum.GetValues(typeof(PlayerRole)))
        {
            var holder = new GameObject("Rol_" + role).transform;
            holder.SetParent(visual, false);
            if (!RealModel(holder, role.ToString(), 1.9f)) { Object.DestroyImmediate(holder.gameObject); continue; }
            // Esqueleto automático para que camine, mueva los brazos y ataque.
            var style = role == PlayerRole.Arquero ? HeroRig.Style.Bow
                : role == PlayerRole.Chaman || role == PlayerRole.Sanador ? HeroRig.Style.Staff : HeroRig.Style.Melee;
            if (holder.childCount > 0 && holder.GetChild(0).GetComponentInChildren<Animator>() == null)
                AutoRig.Build(holder.GetChild(0).gameObject, holder, style, role.ToString());
        }

        return SavePrefab(root, "Jugador");
    }

    static GameObject BuildAreaWarningPrefab()
    {
        var root = Prim(PrimitiveType.Cylinder, null, Vector3.zero, new Vector3(1, 0.02f, 1), Mat("AvisoArea", new Color(1f, 0.1f, 0.1f), true));
        root.name = "AvisoArea";
        return SavePrefab(root, "AvisoArea");
    }

    static GameObject SavePrefab(GameObject go, string name)
    {
        string path = $"{Root}/Prefabs/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        if (prefab.TryGetComponent(out NetworkObject no)) RefreshNetworkId(no);
        return prefab;
    }

    // ---------- Escenas ----------

    static string BuildMenu(GameObject playerPrefab)
    {
        NewScene(new Color(0.55f, 0.35f, 0.25f), new Color(0.9f, 0.7f, 0.5f), new Color(1f, 0.9f, 0.75f), 1.1f, false);
        Ground(new Color(0.55f, 0.45f, 0.35f), 4f, "Piedra");
        Spawn(new Vector3(0, 0, -4));

        // Cinco tótems: uno por cada leyenda que espera.
        Color[] colors = { new Color(0.45f, 0.3f, 0.15f), new Color(0.2f, 0.45f, 0.2f), new Color(0.95f, 0.45f, 0.65f), new Color(0.2f, 0.2f, 0.25f), new Color(0.9f, 0.9f, 0.85f) };
        for (int i = 0; i < 5; i++)
        {
            var pos = Quaternion.Euler(0, -60 + i * 30, 0) * Vector3.forward * 10f;
            Prim(PrimitiveType.Cube, null, pos + Vector3.up * 2f, new Vector3(1.2f, 4f, 1.2f), Mat("Totem" + i, colors[i]));
        }

        var netGo = new GameObject("NetworkManager");
        var nm = netGo.AddComponent<NetworkManager>();
        var transport = netGo.AddComponent<UnityTransport>();
        nm.NetworkConfig = new NetworkConfig { NetworkTransport = transport, PlayerPrefab = playerPrefab, EnableSceneManagement = true };
        netGo.AddComponent<LevelManager>();
        var ui = new SerializedObject(netGo.AddComponent<NetworkUI>());
        string[] bosses = { "Chullachaqui", "Yacumama", "Bufeo Colorado", "Tunche", "Pishtaco" };
        var fondos = ui.FindProperty("fondos");
        fondos.arraySize = bosses.Length;
        for (int i = 0; i < bosses.Length; i++) fondos.GetArrayElementAtIndex(i).objectReferenceValue = Portrait(bosses[i]);
        ui.ApplyModifiedPropertiesWithoutUndo();
        netGo.AddComponent<HUD>();
        netGo.AddComponent<GameAudio>();
        netGo.AddComponent<Effects>();
        netGo.AddComponent<PauseMenu>();
        netGo.AddComponent<LoadingScreen>();

        return SaveScene("Menu");
    }

    // Nivel 1 — Selva baja de Loreto: selva espesa de día, niebla verde.
    static string BuildLevel1(GameObject warning)
    {
        NewScene(new Color(0.55f, 0.7f, 0.55f), new Color(0.35f, 0.5f, 0.35f), new Color(1f, 0.95f, 0.8f), 1.1f, true, 0.02f);
        levelPalms = true; levelVines = true;
        Ground(new Color(0.25f, 0.35f, 0.15f), 10f, "Tierra selva");
        Spawn(new Vector3(0, 0, -18));
        var rnd = new System.Random(1);
        for (int i = 0; i < 70; i++) Tree(RingPos(rnd, 14, 48), rnd, new Color(0.15f, 0.45f, 0.15f));
        for (int i = 0; i < 45; i++) Bush(RingPos(rnd, 9, 45), rnd, new Color(0.15f, 0.45f, 0.15f));
        for (int i = 0; i < 12; i++) FallenLog(RingPos(rnd, 12, 40), rnd);
        for (int i = 0; i < 18; i++) Rock(RingPos(rnd, 12, 44), rnd);

        var boss = BossRoot("Chullachaqui", warning, 1.6f);
        var v = boss.Find("Visual");
        if (!RealModel(v, "Chullachaqui", 4.5f)) Chullachaqui(v);
        BossData("Chullachaqui", "Espíritu guardián de la selva baja (Loreto, Ucayali, Madre de Dios). Tiene un pie humano y otro de animal, y toma la forma de personas conocidas para perder a los caminantes en el monte.",
            7500, 3.5f, 14, 1.8f, 2, 35, 5, 14, 2.2f, 0.3f, 1.5f);
        Ambience(AmbienceType.Lluvia);
        Atmosphere(Mood.Selva);
        return SaveScene("Nivel1_Chullachaqui");
    }

    // Serpiente con cuerpo generado por código (SerpentMotion) y cabeza con mandíbula móvil.
    static void Serpent(Transform visual)
    {
        var scales = Mat("Yacumama escamas", Color.white);
        scales.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Modelos/Yacumama/Yacumama_Escamas.png");
        scales.SetFloat("_Glossiness", 0.45f);

        var body = new GameObject("Cuerpo");
        body.transform.SetParent(visual, false);
        body.AddComponent<MeshFilter>();
        body.AddComponent<MeshRenderer>().sharedMaterial = scales;
        var motion = body.AddComponent<SerpentMotion>();

        // Escamas sacadas de la ilustración de la Yacumama (con relieve) y púas a lo largo del lomo.
        var realScales = TexMat("yacumama_escamas", new Color(0.95f, 0.9f, 0.8f), new Vector2(2f, 1f), 0.5f);
        body.GetComponent<MeshRenderer>().sharedMaterial = realScales;
        var spikeMat = TexMat("yacumama_escamas", new Color(0.45f, 0.38f, 0.25f), new Vector2(1f, 1f), 0.3f);
        var moss = TexMat("forest_leaves_02", new Color(0.4f, 0.65f, 0.2f), Vector2.one, 0.05f);
        var spikes = new List<Transform>();
        for (int i = 0; i < 16; i++)
        {
            var sp = new GameObject("Pua").transform;
            sp.SetParent(body.transform, false);
            Prim(PrimitiveType.Capsule, sp, new Vector3(0, 0.7f, 0), new Vector3(0.35f, 0.9f, 0.35f), spikeMat);
            Prim(PrimitiveType.Sphere, sp, new Vector3(0, 0.3f, -0.4f), new Vector3(0.1f, 1.1f, 1.2f), spikeMat);   // aleta
            if (i % 2 == 0) Prim(PrimitiveType.Cylinder, sp, new Vector3(0.3f, -0.2f, 0), new Vector3(0.05f, 0.8f, 0.05f), moss);   // musgo colgando
            spikes.Add(sp);
        }
        motion.spikes = spikes.ToArray();

        var head = new GameObject("Cabeza").transform;
        head.SetParent(body.transform, false);
        head.localScale = Vector3.one * 1.3f;
        // Cabeza 3D monstruosa (TripoSR, desde la ilustración). Si no está, la cabeza de figuras de antes.
        var headModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Modelos/YacumamaCabeza/YacumamaCabeza.glb");
        if (headModel != null)
        {
            var hm = (GameObject)PrefabUtility.InstantiatePrefab(headModel, head);
            hm.transform.localPosition = new Vector3(0, 0.3f, 1.2f);
            hm.transform.localRotation = Quaternion.identity;
            hm.transform.localScale = Vector3.one * 3.4f;
            var headTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Modelos/YacumamaCabeza/YacumamaCabeza_Textura.png");
            if (headTex != null)
            {
                var hmat = Mat("Yacumama cabeza", Color.white);
                hmat.mainTexture = headTex;
                hmat.SetFloat("_Glossiness", 0.35f);
                foreach (var r in hm.GetComponentsInChildren<Renderer>()) r.sharedMaterials = System.Array.ConvertAll(r.sharedMaterials, _ => hmat);
            }
            // Brillo amarillo de los ojos.
            var glowGo = new GameObject("Brillo ojos");
            glowGo.transform.SetParent(head, false);
            glowGo.transform.localPosition = new Vector3(0, 1f, 2.4f);
            var l = glowGo.AddComponent<Light>();
            l.color = new Color(1f, 0.75f, 0.2f);
            l.range = 7f;
            l.intensity = 2.5f;
            motion.head = head;
            return;
        }
        Prim(PrimitiveType.Sphere, head, new Vector3(0, 0.1f, 0.9f), new Vector3(1.7f, 1.1f, 2.8f), scales);
        Prim(PrimitiveType.Sphere, head, new Vector3(0, 0.55f, 0.3f), new Vector3(1.4f, 0.5f, 1.4f), scales);   // ceja
        var jaw = new GameObject("Mandibula").transform;
        jaw.SetParent(head, false);
        jaw.localPosition = new Vector3(0, -0.25f, -0.3f);
        Prim(PrimitiveType.Sphere, jaw, new Vector3(0, -0.2f, 1.2f), new Vector3(1.4f, 0.45f, 2.4f), scales);
        var mouth = Mat("Boca", new Color(0.45f, 0.08f, 0.1f));
        Prim(PrimitiveType.Sphere, head, new Vector3(0, -0.2f, 1.3f), new Vector3(1.2f, 0.25f, 2.1f), mouth);
        var eye = Mat("Ojos dorados", new Color(1f, 0.75f, 0.1f), true);
        Prim(PrimitiveType.Sphere, head, new Vector3(-0.7f, 0.4f, 1.3f), Vector3.one * 0.32f, eye);
        Prim(PrimitiveType.Sphere, head, new Vector3(0.7f, 0.4f, 1.3f), Vector3.one * 0.32f, eye);
        var fang = Mat("Colmillos", new Color(0.95f, 0.9f, 0.75f));
        foreach (var x in new[] { -0.45f, 0.45f })
        {
            var f = Prim(PrimitiveType.Capsule, head, new Vector3(x, -0.45f, 2.0f), new Vector3(0.12f, 0.3f, 0.12f), fang);
            f.transform.localRotation = Quaternion.Euler(10, 0, 0);
        }
        motion.head = head;
    }

    // Versión provisional con figuras, por si no está el modelo 3D.
    static void Chullachaqui(Transform v)
    {
        var skin = Mat("Chullachaqui piel", new Color(0.45f, 0.35f, 0.2f));
        Prim(PrimitiveType.Capsule, v, new Vector3(0, 1.4f, 0), new Vector3(1.2f, 1.2f, 1.2f), skin);
        Prim(PrimitiveType.Cylinder, v, new Vector3(0, 2.9f, 0), new Vector3(2.4f, 0.05f, 2.4f), Mat("Sombrero", new Color(0.6f, 0.5f, 0.3f)));
        Prim(PrimitiveType.Cylinder, v, new Vector3(0, 3.2f, 0), new Vector3(1f, 0.3f, 1f), Mat("Sombrero", Color.white));
        Prim(PrimitiveType.Sphere, v, new Vector3(-0.25f, 2.3f, 0.5f), Vector3.one * 0.2f, Mat("Ojos rojos", Color.red, true));
        Prim(PrimitiveType.Sphere, v, new Vector3(0.25f, 2.3f, 0.5f), Vector3.one * 0.2f, Mat("Ojos rojos", Color.red, true));
        // El pie desigual: uno humano y uno de animal.
        Prim(PrimitiveType.Cube, v, new Vector3(-0.3f, 0.1f, 0.2f), new Vector3(0.3f, 0.2f, 0.6f), skin);
        Prim(PrimitiveType.Cube, v, new Vector3(0.3f, 0.1f, -0.2f), new Vector3(0.2f, 0.2f, 0.6f), Mat("Pezuña", new Color(0.15f, 0.1f, 0.05f)));
    }

    // Pone el modelo real (Assets/Modelos/<nombre>/<nombre>.glb o .fbx) dentro de "Visual",
    // escalado a la altura indicada y apoyado en el suelo. Devuelve false si no existe.
    static bool RealModel(Transform visual, string name, float height)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Modelos/{name}/{name}.fbx")
                 ?? AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Modelos/{name}/{name}.glb");
        if (asset == null) return false;

        var controller = BossAnimSetup.Setup(name);
        asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Modelos/{name}/{name}.fbx") ?? asset;

        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, visual);
        if (controller != null)
        {
            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            var boss = visual.GetComponentInParent<BossAI>();
            if (boss != null)
            {
                var ai = new SerializedObject(boss);
                ai.FindProperty("animator").objectReferenceValue = animator;
                ai.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        // Los FBX de Mixamo pueden perder la textura: si hay <nombre>_Textura.png, se aplica a mano.
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Modelos/{name}/{name}_Textura.png");
        if (texture != null)
        {
            var skin = Mat(name + " modelo", Color.white);
            skin.mainTexture = texture;
            skin.SetFloat("_Glossiness", 0.1f);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.sharedMaterials = System.Array.ConvertAll(r.sharedMaterials, _ => skin);
        }

        var bounds = RendererBounds(model);
        model.transform.localScale *= height / bounds.size.y;
        bounds = RendererBounds(model);
        model.transform.position += new Vector3(visual.position.x - bounds.center.x, visual.position.y - bounds.min.y, visual.position.z - bounds.center.z);
        // Ajuste fino en juego: apoya los pies reales en el suelo (la caja de límites no es exacta).
        if (visual.GetComponentInParent<BossAI>() != null) model.AddComponent<BossGrounder>().groundRef = visual.parent != null ? visual.parent : visual;
        return true;
    }

    static Bounds RendererBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    // Nivel 2 — Cocha amazónica: laguna con árboles caídos, la madre del agua.
    static string BuildLevel2(GameObject warning)
    {
        NewScene(new Color(0.5f, 0.6f, 0.55f), new Color(0.3f, 0.45f, 0.4f), new Color(1f, 0.85f, 0.65f), 1f, true, 0.015f);
        levelPalms = true; levelVines = true;
        // Una isla de barro pequeña en medio de una gran cocha: los héroes no pueden huir y la Yacumama
        // nada en círculos alrededor, atacando desde el agua.
        Ground(new Color(0.3f, 0.3f, 0.15f), 10f, "Barro");
        Lake(IslandRadius, LakeRadius);
        Spawn(new Vector3(0, 0.4f, -3f));
        Boundary(IslandRadius + 0.4f);   // muro invisible al borde de la isla: no se puede entrar al agua
        var rnd = new System.Random(2);
        for (int i = 0; i < 45; i++) Tree(RingPos(rnd, LakeRadius + 3f, 48), rnd, new Color(0.2f, 0.5f, 0.2f));
        for (int i = 0; i < 26; i++) Bush(RingPos(rnd, LakeRadius + 2f, 45), rnd, new Color(0.2f, 0.5f, 0.2f));
        for (int i = 0; i < 22; i++)
        {
            float a = i * 360f / 22f + rnd.Next(-6, 6);
            Reeds(Quaternion.Euler(0, a, 0) * Vector3.forward * (LakeRadius - R(rnd, 0.5f, 2f)), rnd);
        }

        // La Yacumama rodea la isla nadando (BossAI la mueve en círculo) y ataca desde el agua.
        var boss = BossRoot("Yacumama", warning, 2.5f);
        boss.position = new Vector3(0, 0, 12.5f);
        Serpent(boss.Find("Visual"));
        BossData("Yacumama", "La \"madre del agua\" (quechua yaku + mama): serpiente gigante de más de 30 metros que vive en ríos y cochas de la Amazonía y succiona a quien se acerca.",
            7700, 0f, 16, 7.5f, 1.6f, 42, 7, 9, 2f, 0.3f, 1.5f);
        Ambience(AmbienceType.Lluvia);
        Atmosphere(Mood.Cocha);
        return SaveScene("Nivel2_Yacumama");
    }

    // Nivel 3 — Pueblo ribereño del Amazonas cerca de Iquitos, de noche en fiesta.
    static string BuildLevel3(GameObject warning)
    {
        NewScene(new Color(0.08f, 0.1f, 0.2f), new Color(0.2f, 0.2f, 0.35f), new Color(0.6f, 0.65f, 0.9f), 0.35f, true, 0.02f);
        levelPalms = true; levelVines = false;
        Ground(new Color(0.3f, 0.25f, 0.18f), 10f, "Orilla");
        Water(new Vector3(0, 0.02f, 32), new Vector3(10, 1, 3), new Color(0.35f, 0.25f, 0.2f));
        Spawn(new Vector3(0, 0, -18));
        var rnd = new System.Random(3);
        for (int i = 0; i < 8; i++)
        {
            float x = -35 + i * 10;
            StiltHouse(new Vector3(x, 0, i % 2 == 0 ? -30 : 20), rnd);
            Lantern(new Vector3(x + 4, 0, -10 + (i % 3) * 8));
        }
        for (int i = 0; i < 25; i++) Tree(RingPos(rnd, 40, 50), rnd, new Color(0.1f, 0.3f, 0.15f));
        Dock(new Vector3(-18, 0, 24), 0); Dock(new Vector3(16, 0, 24), 10);
        for (int i = 0; i < 4; i++) Canoe(new Vector3(-30 + i * 17, 0, 23 + (i % 2) * 2), 80 + i * 25);
        for (int i = 0; i < 10; i++) Reeds(new Vector3(-45 + i * 10 + rnd.Next(-3, 3), 0, 22 + rnd.Next(0, 3)), rnd);

        var boss = BossRoot("Bufeo Colorado", warning, 1.6f);
        var v = boss.Find("Visual");
        if (!RealModel(v, "Bufeo Colorado", 4.5f))
        {
            var pink = Mat("Bufeo rosado", new Color(0.95f, 0.5f, 0.7f));
            Prim(PrimitiveType.Capsule, v, new Vector3(0, 1.6f, 0), new Vector3(1.4f, 1.6f, 1.4f), Mat("Traje blanco", new Color(0.95f, 0.95f, 0.95f)));
            Prim(PrimitiveType.Sphere, v, new Vector3(0, 3.7f, 0.2f), new Vector3(1f, 1f, 1.1f), pink);
            Prim(PrimitiveType.Capsule, v, new Vector3(0, 3.6f, 1f), new Vector3(0.35f, 0.6f, 0.35f), pink).transform.localRotation = Quaternion.Euler(90, 0, 0);
            Prim(PrimitiveType.Cylinder, v, new Vector3(0, 4.3f, 0), new Vector3(1.6f, 0.05f, 1.6f), Mat("Sombrero blanco", Color.white));
        }
        BossData("Bufeo Colorado", "El delfín rosado del Amazonas. En noches de luna llena sale del río como un joven elegante vestido de blanco, con sombrero para esconder su espiráculo, y se lleva a las muchachas al fondo del río.",
            10900, 4f, 22, 1.8f, 1.8f, 50, 6, 11, 2f, 0.35f, 1.6f);
        Ambience(AmbienceType.Luciernagas);
        Atmosphere(Mood.RioNoche);
        return SaveScene("Nivel3_BufeoColorado");
    }

    // Nivel 4 — Bosque nocturno con una casa de velorio: el silbido del Tunche.
    static string BuildLevel4(GameObject warning)
    {
        NewScene(new Color(0.04f, 0.07f, 0.06f), new Color(0.2f, 0.26f, 0.22f), new Color(0.45f, 0.55f, 0.7f), 0.35f, true, 0.03f);
        levelPalms = false; levelVines = true;
        Ground(new Color(0.12f, 0.15f, 0.08f), 10f, "Tierra oscura");
        Spawn(new Vector3(0, 0, -18));
        var rnd = new System.Random(4);
        for (int i = 0; i < 80; i++) Tree(RingPos(rnd, 14, 48), rnd, new Color(0.06f, 0.15f, 0.08f));
        for (int i = 0; i < 40; i++) Bush(RingPos(rnd, 12, 45), rnd, new Color(0.06f, 0.15f, 0.08f));
        StiltHouse(new Vector3(0, 0, 22), rnd);
        for (int i = 0; i < 6; i++) Candle(Quaternion.Euler(0, i * 60, 0) * Vector3.forward * 9f);
        for (int i = 0; i < 9; i++) Cross(new Vector3(-8 + i * 2f + rnd.Next(-1, 1), 0, 15 + rnd.Next(0, 4)), rnd);
        for (int i = 0; i < 10; i++) FallenLog(RingPos(rnd, 14, 35), rnd);

        var boss = BossRoot("Tunche", warning, 1.5f);
        var v = boss.Find("Visual");
        if (!RealModel(v, "Tunche", 5f))
        {
            var shadow = Mat("Tunche sombra", new Color(0.08f, 0.1f, 0.08f));
            Prim(PrimitiveType.Capsule, v, new Vector3(0, 2.2f, 0), new Vector3(1.3f, 2.2f, 1.3f), shadow);
            Prim(PrimitiveType.Sphere, v, new Vector3(0, 4.6f, 0), Vector3.one * 1.1f, shadow);
            Prim(PrimitiveType.Sphere, v, new Vector3(-0.25f, 4.7f, 0.45f), Vector3.one * 0.18f, Mat("Ojos verdes", new Color(0.4f, 1f, 0.4f), true));
            Prim(PrimitiveType.Sphere, v, new Vector3(0.25f, 4.7f, 0.45f), Vector3.one * 0.18f, Mat("Ojos verdes", new Color(0.4f, 1f, 0.4f), true));
        }
        BossData("Tunche", "Alma en pena de la selva que anuncia su llegada con un silbido agudo: si suena lejos, está cerca. Ronda las casas donde a un muerto no lo velaron como se debe.",
            12100, 4.5f, 24, 1.8f, 1.8f, 55, 7, 10, 1.8f, 0.3f, 1.7f);
        Ambience(AmbienceType.Luciernagas);
        Atmosphere(Mood.BosqueNoche);
        return SaveScene("Nivel4_Tunche");
    }

    // Nivel 5 — Pueblo andino de noche (Ayacucho/Huancavelica): luna llena, adobe y cerros nevados.
    static string BuildLevel5(GameObject warning)
    {
        NewScene(new Color(0.05f, 0.08f, 0.15f), new Color(0.15f, 0.2f, 0.3f), new Color(0.65f, 0.75f, 1f), 0.5f, true, 0.008f);
        levelPalms = false; levelVines = false;
        Ground(new Color(0.35f, 0.33f, 0.3f), 10f, "Piedra andina");
        Spawn(new Vector3(0, 0, -18));
        var rnd = new System.Random(5);
        for (int i = 0; i < 12; i++)
        {
            var pos = RingPos(rnd, 18, 32);
            if (pos.z < -8f && Mathf.Abs(pos.x) < 14f) pos.x += Mathf.Sign(pos.x) * 14f;  // deja libre la entrada
            AdobeHouse(pos, Quaternion.LookRotation(-new Vector3(pos.x, 0, pos.z)));
        }
        for (int i = 0; i < 8; i++)
        {
            var pos = Quaternion.Euler(0, i * 45 + rnd.Next(-10, 10), 0) * Vector3.forward * 90f;
            float s = 40 + rnd.Next(0, 30);
            Prim(PrimitiveType.Sphere, null, pos, new Vector3(s * 1.4f, s, s * 1.4f), TexMat("mossy_rock", new Color(0.55f, 0.55f, 0.6f), new Vector2(8f, 4f)));
            Prim(PrimitiveType.Sphere, null, pos + Vector3.up * s * 0.38f, new Vector3(s * 0.6f, s * 0.3f, s * 0.6f), TexMat("snow_02", new Color(0.95f, 0.97f, 1f), new Vector2(2f, 1f)));
        }
        for (int i = 0; i < 30; i++) Rock(RingPos(rnd, 10, 40), rnd);
        for (int i = 0; i < 10; i++) { float a = i * 36f + 18f; if (a > 140f && a < 220f) continue; /* entrada libre */ Pirca(Quaternion.Euler(0, a - 12f, 0) * Vector3.forward * 15f, Quaternion.Euler(0, a + 12f, 0) * Vector3.forward * 15f); }
        for (int i = 0; i < 8; i++) Torch(Quaternion.Euler(0, i * 45f + 22f, 0) * Vector3.forward * 16.5f);
        Prim(PrimitiveType.Sphere, null, new Vector3(-60, 70, 150), Vector3.one * 14f, Mat("Luna", new Color(0.95f, 0.95f, 0.85f), true));

        var boss = BossRoot("Pishtaco", warning, 1.6f);
        var v = boss.Find("Visual");
        if (!RealModel(v, "Pishtaco", 5f))
        {
            var robe = Mat("Pishtaco poncho", new Color(0.85f, 0.83f, 0.78f));
            Prim(PrimitiveType.Capsule, v, new Vector3(0, 2.5f, 0), new Vector3(1.6f, 2.5f, 1.6f), robe);
            Prim(PrimitiveType.Sphere, v, new Vector3(0, 5.2f, -0.1f), new Vector3(1.4f, 1.5f, 1.4f), robe);
            Prim(PrimitiveType.Sphere, v, new Vector3(0, 5.1f, 0.35f), new Vector3(0.9f, 1f, 0.6f), Mat("Capucha sombra", new Color(0.05f, 0.03f, 0.03f)));
            Prim(PrimitiveType.Cube, v, new Vector3(1.1f, 2.6f, 0.8f), new Vector3(0.1f, 0.1f, 1.4f), Mat("Cuchillo", new Color(0.8f, 0.85f, 0.9f)));
        }
        BossData("Pishtaco", "Figura temida de los Andes (Ayacucho, Huancavelica, Cusco): un extraño que recorre los caminos de noche con su cuchillo para robar la grasa de los viajeros.",
            12400, 4.5f, 28, 1.8f, 1.6f, 65, 6, 9, 1.6f, 0.3f, 2f);
        Ambience(AmbienceType.Nieve);
        Atmosphere(Mood.AndesNoche);
        return SaveScene("Nivel5_Pishtaco");
    }

    static string BuildVictory()
    {
        NewScene(new Color(0.95f, 0.6f, 0.35f), new Color(0.8f, 0.6f, 0.5f), new Color(1f, 0.85f, 0.6f), 1.2f, false);
        Ground(new Color(0.5f, 0.45f, 0.3f), 4f, "Piedra");
        Spawn(new Vector3(0, 0, -4));
        Prim(PrimitiveType.Sphere, null, new Vector3(0, 12, 40), Vector3.one * 12f, Mat("Inti", new Color(1f, 0.8f, 0.2f), true));
        return SaveScene("Victoria");
    }

    // "Sala": el menú al que vuelve el grupo sin desconectarse (sin NetworkManager propio).
    static string BuildLobby()
    {
        NewScene(new Color(0.55f, 0.35f, 0.25f), new Color(0.9f, 0.7f, 0.5f), new Color(1f, 0.9f, 0.75f), 1.1f, false);
        Ground(new Color(0.55f, 0.45f, 0.35f), 4f, "Piedra");
        Spawn(new Vector3(0, 0, -4));
        return SaveScene(LevelManager.LobbyScene);
    }

    // ---------- Jefes ----------

    static Transform pendingBoss;

    static Transform BossRoot(string name, GameObject warning, float hitRadius)
    {
        var root = new GameObject("Jefe_" + name);
        root.transform.position = new Vector3(0, 0, 10);
        root.transform.rotation = Quaternion.Euler(0, 180, 0);
        var col = root.AddComponent<CapsuleCollider>();
        col.radius = hitRadius;
        col.height = 5f;
        col.center = new Vector3(0, 2.5f, 0);
        root.AddComponent<NetworkObject>();
        root.AddComponent<NetworkTransform>();
        root.AddComponent<Health>();
        var ai = root.AddComponent<BossAI>();
        ai.HitRadius = hitRadius;
        var mechanic = root.AddComponent<BossMechanics>();
        mechanic.type = name switch
        {
            "Chullachaqui" => MechanicType.Clones,
            "Yacumama" => MechanicType.Sumergirse,
            "Bufeo Colorado" => MechanicType.Encantamiento,
            "Tunche" => MechanicType.Silbido,
            "Pishtaco" => MechanicType.Sombras,
            _ => MechanicType.Ninguna,
        };
        mechanic.interval = 20f;
        var so = new SerializedObject(ai);
        so.FindProperty("areaWarningPrefab").objectReferenceValue = warning;
        so.ApplyModifiedPropertiesWithoutUndo();
        new GameObject("Visual").transform.SetParent(root.transform, false);
        pendingBoss = root.transform;
        return root.transform;
    }

    // Imagen del jefe en Assets/Arte/Jefes/<nombre>.png (sin redimensionar, para que no se deforme).
    static Texture2D Portrait(string name)
    {
        string path = $"Assets/Arte/Jefes/{name}.png";
        if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
            (importer.npotScale != TextureImporterNPOTScale.None || importer.mipmapEnabled))
        {
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void BossData(string name, string legend, float hp, float speed, float meleeDmg, float meleeRange, float meleeCd,
        float areaDmg, float areaRadius, float areaCd, float warnTime, float enrageAt, float enrageMult)
    {
        string path = $"{Root}/Jefes/{name}.asset";
        var data = AssetDatabase.LoadAssetAtPath<BossData>(path);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<BossData>();
            AssetDatabase.CreateAsset(data, path);
        }
        data.bossName = name;
        data.leyenda = legend;
        data.maxHealth = hp;
        data.moveSpeed = speed;
        data.meleeDamage = meleeDmg;
        data.meleeRange = meleeRange;
        data.meleeCooldown = meleeCd;
        data.areaDamage = areaDmg;
        data.areaRadius = areaRadius;
        data.areaCooldown = areaCd;
        data.areaWarningTime = warnTime;
        data.enrageAt = enrageAt;
        data.enrageDamageMultiplier = enrageMult;
        data.retrato = Portrait(name);
        EditorUtility.SetDirty(data);

        var so = new SerializedObject(pendingBoss.GetComponent<BossAI>());
        so.FindProperty("data").objectReferenceValue = data;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------- Escenario ----------

    static void NewScene(Color sky, Color ambient, Color sun, float sunIntensity, bool fog, float fogDensity = 0.01f)
    {
        realTreeCount = 0;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cam = new GameObject("Main Camera");
        cam.tag = "MainCamera";
        var c = cam.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.Skybox;
        c.backgroundColor = sky;
        c.farClipPlane = 400f;
        cam.AddComponent<AudioListener>();
        cam.AddComponent<CameraFollow>();
        cam.transform.position = new Vector3(0, 6, -28);
        cam.transform.LookAt(new Vector3(0, 2, 5));

        var light = new GameObject("Sol / Luna").AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = sun;
        light.intensity = sunIntensity;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(45, -30, 0);

        // Cielo con degradado (skybox procedural de Unity): de día brilla, de noche casi negro azulado.
        var skyMat = new Material(Shader.Find("Skybox/Procedural"));
        skyMat.SetColor("_SkyTint", Color.Lerp(sky, Color.white, 0.25f));
        skyMat.SetColor("_GroundColor", ambient);
        skyMat.SetFloat("_Exposure", Mathf.Lerp(0.18f, 1.25f, Mathf.Clamp01(sunIntensity / 1.1f)));
        skyMat.SetFloat("_AtmosphereThickness", sunIntensity < 0.6f ? 0.6f : 1.1f);
        skyMat.SetFloat("_SunSize", sunIntensity < 0.6f ? 0.02f : 0.045f);
        string skyPath = $"{Root}/Materiales/Cielo_{ColorUtility.ToHtmlStringRGB(sky)}.mat";
        AssetDatabase.DeleteAsset(skyPath);
        AssetDatabase.CreateAsset(skyMat, skyPath);
        RenderSettings.skybox = skyMat;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambient;
        RenderSettings.fog = fog;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = sky;
        RenderSettings.fogDensity = fogDensity;
    }

    static void Ground(Color color, float size, string matName)
    {
        // Suelo con textura real repetida cada ~4 m, teñida con el color del nivel para mantener su ambiente.
        var tint = Color.Lerp(Color.white, color * 1.6f, 0.45f);
        tint.a = 1f;
        var g = Prim(PrimitiveType.Plane, null, Vector3.zero, new Vector3(size, 1, size),
            TexMat(GroundTexture(matName), tint, Vector2.one * size * 2.5f), keepCollider: true);
        g.name = "Suelo";
        g.isStatic = true;
    }

    static void Water(Vector3 pos, Vector3 scale, Color color)
    {
        // Agua con ondas (relieve generado por código) que fluyen con WaterFlow, y brillo de reflejo.
        var tint = color * 0.85f;
        tint.a = 1f;
        var mat = TexMat("agua", tint, new Vector2(scale.x * 1.5f, scale.z * 1.5f), 0.92f);
        mat.SetFloat("_BumpScale", 0.6f);
        var w = Prim(PrimitiveType.Plane, null, pos, scale, mat);
        w.name = "Agua";
        w.AddComponent<WaterFlow>();
    }

    // Muelle de tablas sobre pilotes, entrando al agua.
    static void Dock(Vector3 pos, float yaw)
    {
        var dock = new GameObject("Muelle").transform;
        dock.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var planks = TexMat("brown_planks_05", new Color(0.85f, 0.75f, 0.65f), new Vector2(1f, 4f));
        var post = TexMat("bark_brown_02", new Color(0.7f, 0.6f, 0.5f), Vector2.one);
        Prim(PrimitiveType.Cube, dock, new Vector3(0, 0.45f, 4f), new Vector3(2.4f, 0.15f, 8f), planks);
        for (int i = 0; i < 4; i++)
            foreach (var x in new[] { -1.1f, 1.1f })
                Prim(PrimitiveType.Cylinder, dock, new Vector3(x, 0.2f, 1f + i * 2.2f), new Vector3(0.2f, 0.6f, 0.2f), post);
    }

    // Canoa de madera (peque-peque) varada en la orilla.
    static void Canoe(Vector3 pos, float yaw)
    {
        var c = Prim(PrimitiveType.Sphere, null, pos + Vector3.up * 0.2f, new Vector3(1.1f, 0.45f, 4.5f),
            TexMat("brown_planks_05", new Color(0.65f, 0.45f, 0.3f), new Vector2(1f, 3f)));
        c.transform.rotation = Quaternion.Euler(0, yaw, 0);
        c.name = "Canoa";
        Prim(PrimitiveType.Cylinder, null, pos + Quaternion.Euler(0, yaw, 0) * new Vector3(0.9f, 0.5f, 0.5f), new Vector3(0.08f, 1.2f, 0.08f),
            TexMat("bark_brown_02", new Color(0.7f, 0.6f, 0.5f), Vector2.one)).transform.rotation = Quaternion.Euler(70, yaw + 20, 0);
    }

    // Juncos y totoras en la orilla del agua.
    static void Reeds(Vector3 pos, System.Random rnd)
    {
        var mat = TexMat("forest_leaves_02", new Color(0.55f, 0.75f, 0.3f), new Vector2(1f, 3f), 0.05f);
        for (int i = 0; i < 9; i++)
        {
            float h = R(rnd, 0.8f, 1.8f);
            var r = Prim(PrimitiveType.Cylinder, null, pos + new Vector3(R(rnd, -1f, 1f), h * 0.5f, R(rnd, -1f, 1f)), new Vector3(0.05f, h * 0.5f, 0.05f), mat);
            r.transform.rotation = Quaternion.Euler(R(rnd, -12f, 12f), 0, R(rnd, -12f, 12f));
        }
    }

    // Hojas de nenúfar (victoria regia) flotando.
    static void LilyPads(Vector3 center, System.Random rnd, float spread)
    {
        var mat = TexMat("forest_leaves_02", new Color(0.4f, 0.7f, 0.25f), Vector2.one, 0.3f);
        for (int i = 0; i < 14; i++)
        {
            float s = R(rnd, 0.8f, 1.8f);
            Prim(PrimitiveType.Cylinder, null, center + new Vector3(R(rnd, -spread, spread), 0.04f, R(rnd, -spread * 0.4f, spread * 0.4f)),
                new Vector3(s, 0.01f, s), mat);
        }
    }

    // Tronco caído con musgo y hongos.
    static void FallenLog(Vector3 pos, System.Random rnd)
    {
        if (pos.z < -6f && Mathf.Abs(pos.x) < 9f) return;
        float len = R(rnd, 3f, 6f);
        var log = Prim(PrimitiveType.Cylinder, null, pos + Vector3.up * 0.35f, new Vector3(0.7f, len * 0.5f, 0.7f),
            TexMat("bark_brown_02", new Color(0.65f, 0.6f, 0.5f), new Vector2(2f, 3f)), keepCollider: true);
        log.transform.rotation = Quaternion.Euler(90, rnd.Next(0, 180), 0);
        var mush = Mat("Hongo", new Color(0.85f, 0.6f, 0.35f));
        for (int i = 0; i < 3; i++)
            Prim(PrimitiveType.Sphere, null, pos + log.transform.up * R(rnd, -len * 0.4f, len * 0.4f) + Vector3.up * 0.75f,
                new Vector3(0.35f, 0.12f, 0.35f), mush);
    }

    // Cruz de madera (velorio del Tunche).
    static void Cross(Vector3 pos, System.Random rnd)
    {
        var wood = TexMat("brown_planks_05", new Color(0.6f, 0.5f, 0.42f), Vector2.one);
        var cross = new GameObject("Cruz").transform;
        cross.SetPositionAndRotation(pos, Quaternion.Euler(R(rnd, -8f, 8f), rnd.Next(0, 360), R(rnd, -8f, 8f)));
        Prim(PrimitiveType.Cube, cross, new Vector3(0, 0.9f, 0), new Vector3(0.15f, 1.8f, 0.12f), wood);
        Prim(PrimitiveType.Cube, cross, new Vector3(0, 1.35f, 0), new Vector3(0.9f, 0.14f, 0.12f), wood);
        Prim(PrimitiveType.Cube, null, pos + Vector3.up * 0.05f, new Vector3(1.1f, 0.12f, 2f), TexMat("forest_ground_04", new Color(0.6f, 0.55f, 0.5f), Vector2.one))
            .transform.rotation = cross.rotation;
    }

    // Pirca: muro bajo de piedras apiladas típico de los Andes.
    static void Pirca(Vector3 from, Vector3 to)
    {
        var mid = (from + to) / 2f;
        var len = Vector3.Distance(from, to);
        var wall = Prim(PrimitiveType.Cube, null, mid + Vector3.up * 0.5f, new Vector3(0.8f, 1f, len),
            TexMat("rustic_stone_wall", new Color(0.85f, 0.8f, 0.75f), new Vector2(len / 3f, 0.6f)), keepCollider: true);
        wall.transform.rotation = Quaternion.LookRotation(to - from);
        wall.name = "Pirca";
    }

    // Antorcha con fuego que parpadea (Andes).
    static void Torch(Vector3 pos)
    {
        Prim(PrimitiveType.Cylinder, null, pos + Vector3.up * 1.1f, new Vector3(0.12f, 1.1f, 0.12f), TexMat("bark_brown_02", new Color(0.6f, 0.5f, 0.4f), Vector2.one));
        var fire = Prim(PrimitiveType.Sphere, null, pos + Vector3.up * 2.35f, new Vector3(0.3f, 0.45f, 0.3f), Mat("Fuego", new Color(1f, 0.55f, 0.15f), true));
        PointLight(fire.transform, new Color(1f, 0.55f, 0.2f), 10f, 2.2f);
        fire.AddComponent<Flicker>();
    }

    static void Spawn(Vector3 pos)
    {
        var s = new GameObject("PlayerSpawn");
        s.transform.position = pos;
        Boundary(46f);
    }

    // Muro invisible en círculo para que nadie salga del escenario ni se caiga por el borde del suelo.
    static void Boundary(float radius)
    {
        var root = new GameObject("Limite del mapa").transform;
        const int segments = 40;
        float side = 2f * Mathf.PI * radius / segments + 1f;
        for (int i = 0; i < segments; i++)
        {
            var wall = new GameObject("Muro invisible");
            wall.transform.SetParent(root, false);
            float a = i * 360f / segments;
            wall.transform.position = Quaternion.Euler(0, a, 0) * Vector3.forward * radius + Vector3.up * 5f;
            wall.transform.rotation = Quaternion.Euler(0, a, 0);
            wall.AddComponent<BoxCollider>().size = new Vector3(side, 10f, 1f);
        }
        // Suelo de respaldo grueso bajo el suelo visible (por si algo atraviesa el plano).
        var floor = new GameObject("Suelo de respaldo");
        floor.transform.SetParent(root, false);
        floor.transform.position = Vector3.down * 0.5f;
        floor.AddComponent<BoxCollider>().size = new Vector3(110f, 1f, 110f);
    }

    // Vegetación del nivel que se está armando (la ajusta cada BuildLevel antes de plantar árboles).
    static bool levelPalms, levelVines;

    static float R(System.Random rnd, float min, float max) => min + (float)rnd.NextDouble() * (max - min);

    static void Tree(Vector3 pos, System.Random rnd, Color leaves)
    {
        if (pos.z < -6f && Mathf.Abs(pos.x) < 9f) return;  // camino libre entre la entrada y el jefe
        if (levelPalms && rnd.NextDouble() < 0.3) { Palm(pos, rnd, leaves); return; }
        if (RealTree(pos, rnd)) return;   // árbol real de Poly Haven (si están los modelos)
        float h = 6 + (float)rnd.NextDouble() * 10;
        var tree = new GameObject("Arbol").transform;
        tree.position = pos;
        tree.rotation = Quaternion.Euler((float)rnd.NextDouble() * 6f - 3f, rnd.Next(0, 360), (float)rnd.NextDouble() * 6f - 3f);
        var bark = TexMat("bark_brown_02", new Color(0.85f, 0.75f, 0.65f), new Vector2(2f, Mathf.Round(h * 0.3f)));
        // Tronco en dos tramos (más ancho abajo) y raíces que salen del suelo.
        Prim(PrimitiveType.Cylinder, tree, new Vector3(0, h * 0.25f, 0), new Vector3(0.95f, h * 0.25f, 0.95f), bark, keepCollider: true);
        Prim(PrimitiveType.Cylinder, tree, new Vector3(0, h * 0.72f, 0), new Vector3(0.65f, h * 0.24f, 0.65f), bark);
        for (int i = 0; i < 3; i++)
        {
            var root = Prim(PrimitiveType.Cube, tree, Quaternion.Euler(0, i * 120, 0) * new Vector3(0, 0.3f, 0.7f), new Vector3(0.35f, 0.5f, 1.4f), bark);
            root.transform.localRotation = Quaternion.Euler(-25f, i * 120, 0);
        }
        // Copa de varias "nubes" de hojas en dos tonos.
        // Follaje: textura de hojas teñida de verde (en dos tonos) para que la copa no se vea lisa.
        var dark = TexMat("forest_leaves_02", Foliage(leaves, 0f), new Vector2(3f, 2f), 0.05f);
        var light = TexMat("forest_leaves_02", Foliage(leaves, 0.25f), new Vector2(3f, 2f), 0.05f);
        float s = 3.5f + (float)rnd.NextDouble() * 3f;
        int blobs = 3 + rnd.Next(0, 3);
        for (int i = 0; i < blobs; i++)
        {
            var offset = new Vector3((float)rnd.NextDouble() * 2.4f - 1.2f, (float)rnd.NextDouble() * 1.4f, (float)rnd.NextDouble() * 2.4f - 1.2f) * s * 0.4f;
            float k = 0.7f + (float)rnd.NextDouble() * 0.5f;
            Prim(PrimitiveType.Sphere, tree, new Vector3(0, h, 0) + offset, new Vector3(s * k, s * 0.55f * k, s * k), i % 2 == 0 ? dark : light);
        }

        // Ramas que salen del tronco hacia arriba y afuera, cada una con su mata de hojas.
        int branches = 3 + rnd.Next(0, 3);
        for (int i = 0; i < branches; i++)
        {
            float yaw = i * 360f / branches + R(rnd, -25f, 25f);
            float at = h * R(rnd, 0.55f, 0.85f);
            float len = R(rnd, 2.2f, 3.6f);
            var dir = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(R(rnd, 35f, 60f), 0, 0) * Vector3.up;
            var b = Prim(PrimitiveType.Cylinder, tree, new Vector3(0, at, 0) + dir * len * 0.5f, new Vector3(0.28f, len * 0.5f, 0.28f), bark);
            b.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            var tip = new Vector3(0, at, 0) + dir * len;
            float k = R(rnd, 1.6f, 2.6f);
            Prim(PrimitiveType.Sphere, tree, tip + Vector3.up * 0.3f, new Vector3(k * 1.3f, k * 0.7f, k * 1.3f), i % 2 == 0 ? light : dark);

            // Lianas colgando de la rama.
            if (levelVines && rnd.NextDouble() < 0.7)
            {
                var vine = TexMat("bark_brown_02", new Color(0.35f, 0.5f, 0.25f), new Vector2(1f, 4f));
                for (int v = 0; v < 2; v++)
                {
                    float vl = R(rnd, 2f, Mathf.Min(at - 0.5f, 6f));
                    var vp = Vector3.Lerp(new Vector3(0, at, 0), tip, R(rnd, 0.4f, 1f)) + new Vector3(R(rnd, -0.4f, 0.4f), -vl * 0.5f, R(rnd, -0.4f, 0.4f));
                    Prim(PrimitiveType.Cylinder, tree, vp, new Vector3(0.06f, vl * 0.5f, 0.06f), vine);
                    Prim(PrimitiveType.Sphere, tree, vp - Vector3.up * vl * 0.5f, new Vector3(0.35f, 0.5f, 0.35f), light);
                }
            }
        }
        tree.gameObject.isStatic = true;
    }

    // Palmera: tronco anillado que se curva y hojas largas que caen en abanico.
    static void Palm(Vector3 pos, System.Random rnd, Color leaves)
    {
        var palm = new GameObject("Palmera").transform;
        palm.position = pos;
        palm.rotation = Quaternion.Euler(0, rnd.Next(0, 360), 0);
        var bark = TexMat("bark_brown_02", new Color(0.8f, 0.72f, 0.6f), new Vector2(1f, 2f));
        float h = R(rnd, 7f, 11f);
        int segs = 6;
        float lean = R(rnd, 3f, 7f);
        Vector3 p = Vector3.zero;
        for (int i = 0; i < segs; i++)
        {
            float segH = h / segs;
            var dir = Quaternion.Euler(lean * i, 0, 0) * Vector3.up;
            var mid = p + dir * segH * 0.5f;
            float w = Mathf.Lerp(0.55f, 0.35f, i / (float)segs);
            var s = Prim(PrimitiveType.Cylinder, palm, mid, new Vector3(w, segH * 0.52f, w), bark, keepCollider: i == 0);
            s.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            p += dir * segH;
        }
        var frond = TexMat("forest_leaves_02", Foliage(leaves, 0.3f), new Vector2(3f, 1f), 0.05f);
        int fronds = 7 + rnd.Next(0, 3);
        for (int i = 0; i < fronds; i++)
        {
            float yaw = i * 360f / fronds + R(rnd, -10f, 10f);
            float len = R(rnd, 3.2f, 4.2f);
            var rot = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(R(rnd, 100f, 125f), 0, 0);   // hacia afuera y cayendo
            var f = Prim(PrimitiveType.Sphere, palm, p + rot * Vector3.up * len * 0.45f, new Vector3(0.9f, len * 0.5f, 0.12f), frond);
            f.transform.localRotation = rot;
        }
        Prim(PrimitiveType.Sphere, palm, p, Vector3.one * 0.7f, TexMat("bark_brown_02", new Color(0.5f, 0.45f, 0.3f), Vector2.one));
        palm.gameObject.isStatic = true;
    }

    // Helecho: hojas largas que salen del centro hacia afuera.
    static void Fern(Vector3 pos, System.Random rnd, Color leaves)
    {
        var fern = new GameObject("Helecho").transform;
        fern.position = pos;
        var mat = TexMat("forest_leaves_02", Foliage(leaves, 0.4f), new Vector2(2f, 1f), 0.05f);
        int n = 6 + rnd.Next(0, 4);
        for (int i = 0; i < n; i++)
        {
            float len = R(rnd, 0.9f, 1.5f);
            var rot = Quaternion.Euler(0, i * 360f / n + R(rnd, -15f, 15f), 0) * Quaternion.Euler(R(rnd, 45f, 70f), 0, 0);
            var leaf = Prim(PrimitiveType.Sphere, fern, rot * Vector3.up * len * 0.45f, new Vector3(0.35f, len * 0.5f, 0.06f), mat);
            leaf.transform.localRotation = rot;
        }
        fern.gameObject.isStatic = true;
    }

    // La textura de hojas es marrón: se tiñe con un verde fuerte (más claro según 'light').
    static Color Foliage(Color leaves, float light)
    {
        var c = Color.Lerp(leaves, new Color(0.6f, 0.8f, 0.25f), light) * 2.2f;
        return new Color(Mathf.Min(c.r * 0.7f, 1f), Mathf.Min(c.g, 1f), Mathf.Min(c.b * 0.6f, 1f), 1f);
    }

    // Clima del nivel (lluvia, luciérnagas o nieve), ver LevelAmbience.
    static void Ambience(AmbienceType type)
    {
        new GameObject("Clima").AddComponent<LevelAmbience>().type = type;
    }

    static void Bush(Vector3 pos, System.Random rnd, Color leaves)
    {
        if (pos.z < -6f && Mathf.Abs(pos.x) < 9f) return;
        var bush = new GameObject("Arbusto").transform;
        bush.position = pos;
        var mat = TexMat("forest_leaves_02", Foliage(leaves, 0.15f), new Vector2(2f, 1f), 0.05f);
        for (int i = 0; i < 3; i++)
        {
            float k = 0.8f + (float)rnd.NextDouble() * 0.8f;
            Prim(PrimitiveType.Sphere, bush, new Vector3((float)rnd.NextDouble() * 1.6f - 0.8f, 0.4f * k, (float)rnd.NextDouble() * 1.6f - 0.8f),
                new Vector3(1.6f * k, 1f * k, 1.6f * k), mat);
        }
        bush.gameObject.isStatic = true;
        // Helechos alrededor de cada arbusto.
        for (int i = 0; i < 2; i++)
            Fern(pos + new Vector3(R(rnd, -2.5f, 2.5f), 0, R(rnd, -2.5f, 2.5f)), rnd, leaves);
    }

    static void Rock(Vector3 pos, System.Random rnd)
    {
        if (pos.z < -6f && Mathf.Abs(pos.x) < 9f) return;
        float k = 0.6f + (float)rnd.NextDouble() * 1.6f;
        var rock = Prim(PrimitiveType.Sphere, null, pos + Vector3.up * 0.3f * k, new Vector3(1.6f * k, 0.9f * k, 1.3f * k),
            TexMat("mossy_rock", new Color(0.85f, 0.85f, 0.8f), new Vector2(2f, 1f)), keepCollider: true);
        rock.transform.rotation = Quaternion.Euler(rnd.Next(-15, 15), rnd.Next(0, 360), rnd.Next(-15, 15));
        rock.name = "Roca";
        rock.isStatic = true;
    }

    // StiltHouse y AdobeHouse están en GameSetupCasas.cs.

    static void Lantern(Vector3 pos)
    {
        var wood = TexMat("bark_brown_02", new Color(0.7f, 0.6f, 0.5f), new Vector2(1f, 2f));
        Prim(PrimitiveType.Cylinder, null, pos + Vector3.up * 1.5f, new Vector3(0.12f, 1.5f, 0.12f), wood, keepCollider: true);
        Prim(PrimitiveType.Cube, null, pos + new Vector3(0.3f, 3f, 0), new Vector3(0.7f, 0.08f, 0.08f), wood);
        // Farol de madera real colgando del brazo, con la llama encendida adentro.
        Place("wooden_lantern_01", pos + new Vector3(0.6f, 2.35f, 0), 0, 1.4f);
        var glow = Prim(PrimitiveType.Sphere, null, pos + new Vector3(0.6f, 2.6f, 0), new Vector3(0.15f, 0.22f, 0.15f), Mat("Farol", new Color(1f, 0.6f, 0.2f), true));
        PointLight(glow.transform, new Color(1f, 0.6f, 0.25f), 12f, 2.5f);
        glow.AddComponent<Flicker>();
    }

    static void Candle(Vector3 pos)
    {
        // "Vela" es el nombre que busca la mecánica del Tunche; el círculo marca la zona segura (3,5 m).
        var group = new GameObject("Altar de velas").transform;
        group.position = pos;
        var wax = Mat("Cera", new Color(0.95f, 0.88f, 0.72f));
        // La cera brilla un poco con la luz de la llama (cálida, aunque la luna sea azul).
        wax.EnableKeyword("_EMISSION");
        wax.SetColor("_EmissionColor", new Color(0.55f, 0.38f, 0.18f));
        var clay = TexMat("old_stone_wall", new Color(0.7f, 0.5f, 0.38f), new Vector2(2f, 0.5f));
        // Base de barro y tres velas de distinta altura con chorros de cera.
        // Cuenco de madera real (Poly Haven) como base; si no está, una base de barro.
        if (Place("wooden_bowl_01", pos, 0, 4.5f, false, group) == null)
            Prim(PrimitiveType.Cylinder, group, new Vector3(0, 0.08f, 0), new Vector3(1.1f, 0.08f, 1.1f), clay);
        // Pétalos rojos esparcidos alrededor, como en una ofrenda.
        var red = Mat("Petalo rojo", new Color(0.75f, 0.08f, 0.08f));
        for (int i = 0; i < 18; i++)
        {
            var pp = Quaternion.Euler(0, i * 20 + (i % 4) * 9, 0) * Vector3.forward * (0.6f + (i % 3) * 0.22f);
            var pe = Prim(PrimitiveType.Sphere, group, pp + Vector3.up * 0.03f, new Vector3(0.12f, 0.025f, 0.08f), red);
            pe.transform.localRotation = Quaternion.Euler(0, i * 37, 0);
        }
        float[] heights = { 0.9f, 0.6f, 0.45f };
        Vector3[] offsets = { Vector3.zero, new Vector3(0.32f, 0, 0.18f), new Vector3(-0.28f, 0, 0.25f) };
        var flameMat = Mat("Llama", new Color(1f, 0.75f, 0.3f), true);
        for (int i = 0; i < 3; i++)
        {
            float hh = heights[i];
            var c = Prim(PrimitiveType.Cylinder, group, offsets[i] + Vector3.up * (0.16f + hh * 0.5f), new Vector3(0.22f - i * 0.03f, hh * 0.5f, 0.22f - i * 0.03f), wax);
            if (i == 0) c.name = "Vela";
            // chorros de cera derretida
            for (int d = 0; d < 3; d++)
            {
                var drip = Quaternion.Euler(0, d * 120 + i * 40, 0) * Vector3.forward * (0.1f - i * 0.012f);
                Prim(PrimitiveType.Capsule, group, offsets[i] + drip + Vector3.up * (0.16f + hh * (0.55f + d * 0.12f)), new Vector3(0.05f, 0.1f + d * 0.04f, 0.05f), wax);
            }
            var flame = Prim(PrimitiveType.Sphere, group, offsets[i] + Vector3.up * (0.24f + hh), new Vector3(0.11f, 0.2f, 0.11f), flameMat);
            flame.AddComponent<Flicker>().amount = 0.35f;
            if (i == 0) PointLight(flame.transform, new Color(1f, 0.68f, 0.32f), 9f, 2.2f);
        }
        // Flores de cempasúchil (marigold) alrededor, como en los velorios andinos.
        var petal = Mat("Flor cempasuchil", new Color(1f, 0.55f, 0.1f));
        for (int i = 0; i < 10; i++)
        {
            var fp = Quaternion.Euler(0, i * 36 + (i % 3) * 7, 0) * Vector3.forward * (0.85f + (i % 2) * 0.3f);
            Prim(PrimitiveType.Sphere, group, fp + Vector3.up * 0.08f, new Vector3(0.18f, 0.1f, 0.18f), petal);
        }
        // Círculo ritual de luz dorada (zona segura): brilla, late y gira despacio (CandleGlow).
        var circleMat = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
        circleMat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Texturas/circulo_ritual.png");
        string cmPath = $"{Root}/Materiales/Circulo_ritual.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(cmPath) == null) AssetDatabase.CreateAsset(circleMat, cmPath);
        circleMat = AssetDatabase.LoadAssetAtPath<Material>(cmPath);
        circleMat.SetColor("_TintColor", new Color(1f, 0.72f, 0.3f, 0.85f));
        EditorUtility.SetDirty(circleMat);
        var circle = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(circle.GetComponent<Collider>());
        circle.name = "Zona segura";
        circle.transform.SetParent(group, false);
        circle.transform.localPosition = Vector3.up * 0.03f;
        circle.transform.localRotation = Quaternion.Euler(90, 0, 0);
        circle.transform.localScale = Vector3.one * 7.4f;
        circle.GetComponent<Renderer>().sharedMaterial = circleMat;
        group.gameObject.AddComponent<CandleGlow>();
        return;
    }

    static void PointLight(Transform parent, Color color, float range, float intensity)
    {
        var l = new GameObject("Luz").AddComponent<Light>();
        l.transform.SetParent(parent, false);
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
    }

    static Vector3 RingPos(System.Random rnd, float min, float max)
    {
        float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
        float r = min + (float)rnd.NextDouble() * (max - min);
        return new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
    }

    // ---------- Utilidades ----------

    static GameObject Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material mat, bool keepCollider = false)
    {
        var go = GameObject.CreatePrimitive(type);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // Material con textura de Poly Haven (Assets/Texturas/<tex>_color.jpg + _relieve.jpg como mapa de normales).
    // tint oscurece/tiñe para mantener el ambiente de cada nivel; tiling = repeticiones de la textura.
    static Material TexMat(string tex, Color tint, Vector2 tiling, float gloss = 0.1f)
    {
        string key = "tex_" + tex + tint + tiling;
        if (mats.TryGetValue(key, out var cached) && cached != null) return cached;
        string ext = System.IO.File.Exists($"Assets/Texturas/{tex}_color.jpg") ? "jpg" : "png";
        var color = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Texturas/{tex}_color.{ext}");
        if (color == null) return Mat(tex, tint);
        string normalPath = $"Assets/Texturas/{tex}_relieve.{ext}";
        if (AssetImporter.GetAtPath(normalPath) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap)
        {
            ni.textureType = TextureImporterType.NormalMap;
            ni.SaveAndReimport();
        }
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);

        string path = $"{Root}/Materiales/T_{tex}_{ColorUtility.ToHtmlStringRGB(tint)}_{tiling.x:0.#}x{tiling.y:0.#}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = tint;
        mat.mainTexture = color;
        mat.mainTextureScale = tiling;
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", 1.2f);
            mat.EnableKeyword("_NORMALMAP");
        }
        mat.SetFloat("_Glossiness", gloss);
        EditorUtility.SetDirty(mat);
        mats[key] = mat;
        return mat;
    }

    // Textura del suelo según el nivel (por el nombre del material de suelo).
    static string GroundTexture(string matName) => matName switch
    {
        "Tierra selva" => "forest_leaves_02",
        "Barro" => "brown_mud_leaves_01",
        "Orilla" => "brown_mud_rocks_01",
        "Tierra oscura" => "forest_ground_04",
        "Piedra andina" => "snow_02",
        _ => "stony_dirt_path",
    };

    static Material Mat(string name, Color color, bool emissive = false)
    {
        string key = name + color + emissive;
        if (mats.TryGetValue(key, out var cached) && cached != null) return cached;

        string safe = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars()));
        string path = $"{Root}/Materiales/{safe}_{ColorUtility.ToHtmlStringRGB(color)}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = color;
        mat.SetFloat("_Glossiness", 0.15f);
        if (emissive)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(mat);
        mats[key] = mat;
        return mat;
    }

    static string SaveScene(string name)
    {
        string path = $"{Root}/Escenas/{name}.unity";
        ClearSpawnArea();   // que nadie aparezca dentro de una roca
        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, path);
        // Los NetworkObject colocados en escena necesitan su ID global una vez guardada la escena.
        foreach (var no in Object.FindObjectsByType<NetworkObject>()) RefreshNetworkId(no);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, path);
        return path;
    }

    static void RefreshNetworkId(NetworkObject no)
    {
        typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(no, null);
        EditorUtility.SetDirty(no);
        var hash = typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(no);
        Debug.Log($"LEYENDAS: {no.name} GlobalObjectIdHash={hash}");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int i = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
    }
}
