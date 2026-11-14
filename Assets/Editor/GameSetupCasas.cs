using UnityEditor;
using UnityEngine;

// Árboles reales (Poly Haven, aligerados) y casas más realistas: casas ribereñas de bambú sobre pilotes
// con techo de junco, y casas andinas de adobe con zócalo de piedra y techo de tejas. Con objetos alrededor.
public static partial class GameSetup
{
    static readonly string[] RealTrees = { "island_tree_01_lite", "island_tree_02_lite", "island_tree_03_lite", "tree_small_02_lite" };
    static int realTreeCount;
    const int MaxRealTrees = 28;   // cada árbol real pesa ~200 mil triángulos: no más de esto por nivel

    // Coloca un árbol real con la altura pedida. Devuelve false si no hay modelos (usa el árbol de formas simples).
    static bool RealTree(Vector3 pos, System.Random rnd)
    {
        if (PH(RealTrees[0]) == null) return false;
        if (realTreeCount >= MaxRealTrees) return true;   // ya hay suficientes: este hueco queda libre
        realTreeCount++;
        var go = Place(RealTrees[rnd.Next(0, RealTrees.Length)], pos, rnd.Next(0, 360), 1f);
        if (go == null) return false;
        go.name = "Arbol real";
        var b = ObjBounds(go);
        float target = R(rnd, 9f, 15f);
        go.transform.localScale = Vector3.one * (target / Mathf.Max(0.5f, b.size.y));
        b = ObjBounds(go);
        go.transform.position += Vector3.up * (pos.y - b.min.y);   // apoyado en el suelo
        // Tronco que choca (sin chocar con las hojas).
        var col = go.AddComponent<CapsuleCollider>();
        col.center = go.transform.InverseTransformPoint(new Vector3(pos.x, pos.y + 2f, pos.z));
        col.radius = 0.6f / go.transform.localScale.x;
        col.height = 4f / go.transform.localScale.x;
        // Lejos se deja de dibujar (la niebla lo tapa) para que el juego vaya fluido.
        var lod = go.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(0.02f, go.GetComponentsInChildren<Renderer>()) });
        lod.RecalculateBounds();
        return true;
    }

    static Bounds ObjBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static GameObject Box(Transform parent, Vector3 center, Vector3 size, Material mat, Vector3 euler = default, bool collider = false)
    {
        var g = Prim(PrimitiveType.Cube, parent, center, size, mat, keepCollider: collider);
        g.transform.localRotation = Quaternion.Euler(euler);
        return g;
    }

    // Techo a dos aguas a lo largo de X: dos faldones, cumbrera y los triángulos de los extremos.
    static void GableRoof(Transform h, float baseY, float width, float depth, float rise, Material roof, Material gable, Material ridge)
    {
        float half = depth / 2f + 0.5f;                       // con alero
        float slope = Mathf.Atan2(rise, depth / 2f) * Mathf.Rad2Deg;
        float len = Mathf.Sqrt(half * half + rise * rise);
        foreach (var s in new[] { -1f, 1f })
            Box(h, new Vector3(0, baseY + rise / 2f - 0.05f, s * half / 2f), new Vector3(width + 1.2f, 0.14f, len), roof, new Vector3(s * slope, 0, 0));
        Prim(PrimitiveType.Cylinder, h, new Vector3(0, baseY + rise + 0.02f, 0), new Vector3(0.22f, (width + 1.3f) / 2f, 0.22f), ridge)
            .transform.localRotation = Quaternion.Euler(0, 0, 90);
        // Hastiales: un rombo cuya mitad de arriba queda bajo el techo.
        float d = depth / Mathf.Sqrt(2f);
        foreach (var s in new[] { -1f, 1f })
            Box(h, new Vector3(s * (width / 2f - 0.08f), baseY, 0), new Vector3(0.1f, d * (rise * 2f / depth), d), gable, new Vector3(45, 0, 0));
    }

    static void Props(Vector3 pos, Quaternion rot, System.Random rnd, string[] names, int count, float spread)
    {
        for (int i = 0; i < count; i++)
        {
            var p = pos + rot * new Vector3(R(rnd, -spread, spread), 0, R(rnd, -spread * 0.4f, spread * 0.4f));
            Place(names[rnd.Next(0, names.Length)], p, rnd.Next(0, 360), R(rnd, 0.9f, 1.1f), true);
        }
    }

    // ---------------------------------------------------------------- Zona de aparición libre
    // Quita rocas, troncos y objetos sólidos que invadan los 5 m alrededor del punto de aparición
    // (los conjuntos de rocas de Poly Haven miden varios metros y a veces sus bordes llegaban ahí).
    static void ClearSpawnArea()
    {
        var spawn = GameObject.Find("PlayerSpawn");
        if (spawn == null) return;
        var center = spawn.transform.position;
        Physics.SyncTransforms();   // si no, los objetos recién creados dicen estar en (0,0,0)
        const float radius = 5f;
        int removed = 0;
        foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (col == null || col.isTrigger) continue;
            var go = col.gameObject;
            string n = go.name;
            if (n == "Suelo" || n == "Isla" || n == "Agua" || n.StartsWith("Muro invisible") || go.transform.root.name == "Limite del mapa") continue;
            var b = col.bounds;
            var closest = b.ClosestPoint(new Vector3(center.x, b.center.y, center.z));
            if (Vector2.Distance(new Vector2(closest.x, closest.z), new Vector2(center.x, center.z)) > radius) continue;
            if (b.min.y > 2.2f) continue;   // está por encima de la cabeza: no estorba
            Debug.Log($"LEYENDAS: zona de aparición libre, quito {n} (de {go.transform.root.name}) en {b.center} tamaño {b.size} (aparición {center})");
            // Los modelos de Poly Haven son prefabs: hay que "desempaquetarlos" para poder quitar una pieza.
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (root != null) PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Object.DestroyImmediate(go);
            removed++;
        }
        if (removed > 0) Debug.Log($"LEYENDAS: {removed} objetos quitados junto a la aparición en {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
    }

    // ---------------------------------------------------------------- Nivel 2: isla en medio de la cocha
    public const float IslandRadius = 8.5f, LakeRadius = 26f;

    static void Lake(float islandR, float lakeR)
    {
        // Agua: un disco grande sobre el suelo, con sus ondas que fluyen.
        var tint = new Color(0.1f, 0.32f, 0.22f) * 0.85f; tint.a = 1f;
        var waterMat = TexMat("agua", tint, new Vector2(lakeR / 3f, lakeR / 3f), 0.92f);
        waterMat.SetFloat("_BumpScale", 0.6f);
        var water = Prim(PrimitiveType.Cylinder, null, new Vector3(0, 0.04f, 0), new Vector3(lakeR * 2f, 0.02f, lakeR * 2f), waterMat);
        water.name = "Agua";
        water.AddComponent<WaterFlow>();

        // Isla de barro, un poco elevada, con piedras en la orilla y algunas plantas.
        var mud = TexMat("brown_mud_leaves_01", new Color(0.8f, 0.72f, 0.6f), new Vector2(4f, 4f));
        var isle = Prim(PrimitiveType.Cylinder, null, new Vector3(0, 0.15f, 0), new Vector3(islandR * 2f, 0.15f, islandR * 2f), mud);
        isle.name = "Isla";
        isle.AddComponent<MeshCollider>().sharedMesh = isle.GetComponent<MeshFilter>().sharedMesh;
        var shore = TexMat("brown_mud_rocks_01", new Color(0.7f, 0.65f, 0.55f), new Vector2(6f, 1f));
        Prim(PrimitiveType.Cylinder, null, new Vector3(0, 0.08f, 0), new Vector3(islandR * 2f + 1.6f, 0.08f, islandR * 2f + 1.6f), shore);
        var rnd = new System.Random(22);
        for (int i = 0; i < 18; i++)
        {
            float a = i * 20f + R(rnd, -6f, 6f);
            var p = Quaternion.Euler(0, a, 0) * Vector3.forward * (islandR + R(rnd, 0.1f, 0.8f));
            var rock = Prim(PrimitiveType.Sphere, null, p + Vector3.up * 0.15f, new Vector3(R(rnd, 0.7f, 1.6f), R(rnd, 0.4f, 0.8f), R(rnd, 0.7f, 1.4f)),
                TexMat("mossy_rock", new Color(0.8f, 0.8f, 0.75f), Vector2.one));
            rock.transform.rotation = Quaternion.Euler(0, rnd.Next(0, 360), 0);
        }
        for (int i = 0; i < 7; i++)
        {
            float a = i * 51f + R(rnd, -10f, 10f);
            Fern(Quaternion.Euler(0, a, 0) * Vector3.forward * R(rnd, islandR - 2.2f, islandR - 0.8f) + Vector3.up * 0.3f, rnd, new Color(0.2f, 0.5f, 0.2f));
        }
    }

    static readonly string[] RiverProps = { "wooden_barrels_01", "wooden_crate_01", "wooden_crate_02", "wooden_bucket_01", "wicker_basket_01", "wicker_basket_02" };
    static readonly string[] AndesProps = { "ceramic_pot", "wicker_basket_01", "wicker_basket_02", "wooden_stool_01", "wooden_bucket_01" };

    // Casa ribereña amazónica: pilotes, piso de tablas, paredes de bambú, techo de junco, terraza con baranda y escalera.
    static void StiltHouse(Vector3 pos, System.Random rnd)
    {
        var face = Quaternion.LookRotation(new Vector3(-pos.x, 0, -pos.z).sqrMagnitude > 1f ? new Vector3(-pos.x, 0, -pos.z) : Vector3.back);
        var h = new GameObject("Casa ribereña").transform;
        h.SetPositionAndRotation(pos, face);
        var post = TexMat("bark_brown_02", new Color(0.75f, 0.65f, 0.55f), new Vector2(1f, 2f));
        var floor = TexMat("weathered_planks", new Color(0.85f, 0.8f, 0.75f), new Vector2(3f, 2f));
        var bamboo = TexMat("bamboo_wall_02", new Color(0.9f, 0.85f, 0.75f), new Vector2(3f, 1.2f));
        var reed = TexMat("reed_roof_04", new Color(0.85f, 0.8f, 0.7f), new Vector2(3f, 2f));
        var dark = Mat("Interior oscuro", new Color(0.03f, 0.025f, 0.02f));
        const float fy = 2.2f, wh = 2.4f, w = 6f, d = 4.6f;

        // Pilotes (con colisión) y vigas.
        foreach (var x in new[] { -2.8f, 0f, 2.8f })
            foreach (var z in new[] { -2.2f, 2.2f, 3.3f })
                Prim(PrimitiveType.Cylinder, h, new Vector3(x, fy / 2f, z), new Vector3(0.28f, fy / 2f, 0.28f), post, keepCollider: true);
        Box(h, new Vector3(0, fy, 0.55f), new Vector3(w + 0.6f, 0.2f, d + 1.7f), floor, default, true);

        // Paredes de bambú (la del frente con puerta y ventana).
        float wy = fy + 0.1f + wh / 2f;
        Box(h, new Vector3(0, wy, -d / 2f), new Vector3(w, wh, 0.12f), bamboo, default, true);
        foreach (var s in new[] { -1f, 1f }) Box(h, new Vector3(s * w / 2f, wy, 0), new Vector3(0.12f, wh, d), bamboo, default, true);
        Box(h, new Vector3(-(w / 4f + 0.3f), wy, d / 2f), new Vector3(w / 2f - 0.6f, wh, 0.12f), bamboo, default, true);
        Box(h, new Vector3(w / 4f + 0.3f, wy, d / 2f), new Vector3(w / 2f - 0.6f, wh, 0.12f), bamboo, default, true);
        Box(h, new Vector3(0, fy + 0.1f + wh - 0.25f, d / 2f), new Vector3(1.2f, 0.5f, 0.12f), bamboo);
        Box(h, new Vector3(0, fy + 0.1f + (wh - 0.5f) / 2f, d / 2f - 0.3f), new Vector3(1.2f, wh - 0.5f, 0.05f), dark);   // interior por la puerta
        // Ventana con luz cálida y marco.
        var win = Box(h, new Vector3(1.7f, wy + 0.2f, d / 2f + 0.07f), new Vector3(0.9f, 0.7f, 0.03f), Mat("Ventana cálida", new Color(1f, 0.62f, 0.28f), true));
        foreach (var s in new[] { -1f, 1f })
        {
            Box(h, new Vector3(1.7f + s * 0.5f, wy + 0.2f, d / 2f + 0.09f), new Vector3(0.1f, 0.85f, 0.06f), post);
            Box(h, new Vector3(1.7f, wy + 0.2f + s * 0.4f, d / 2f + 0.09f), new Vector3(1.1f, 0.1f, 0.06f), post);
        }
        PointLight(win.transform, new Color(1f, 0.6f, 0.25f), 6f, 1.2f);

        // Techo de junco a dos aguas.
        GableRoof(h, fy + 0.1f + wh, w, d + 1.6f, 1.9f, reed, bamboo, post);

        // Terraza: baranda y escalera.
        for (int i = 0; i < 7; i++)
            Prim(PrimitiveType.Cylinder, h, new Vector3(-3f + i, fy + 0.5f, d / 2f + 1.3f), new Vector3(0.08f, 0.45f, 0.08f), post);
        Box(h, new Vector3(-1.75f, fy + 0.95f, d / 2f + 1.3f), new Vector3(2.6f, 0.08f, 0.08f), post);
        Box(h, new Vector3(1.75f, fy + 0.95f, d / 2f + 1.3f), new Vector3(2.6f, 0.08f, 0.08f), post);
        var ladder = Place("wooden_ladder", h.TransformPoint(new Vector3(0, 0, d / 2f + 2.3f)), face.eulerAngles.y + 180f, 1f, false, h);
        if (ladder != null)
        {
            ladder.transform.localRotation = Quaternion.Euler(-18f, 180f, 0);
            var lb = ObjBounds(ladder);
            ladder.transform.localScale *= (fy + 0.3f) / Mathf.Max(0.5f, lb.size.y);
        }

        // Objetos: en la terraza y en el suelo bajo la casa.
        Place("wicker_basket_01", h.TransformPoint(new Vector3(-2.2f, fy + 0.1f, d / 2f + 0.8f)), rnd.Next(0, 360), 1f, false, h);
        Place("wooden_stool_01", h.TransformPoint(new Vector3(2.4f, fy + 0.1f, d / 2f + 0.8f)), rnd.Next(0, 360), 1f, false, h);
        Props(h.TransformPoint(new Vector3(0, 0, 0)), face, rnd, RiverProps, 3, 3.5f);
        h.gameObject.isStatic = true;
    }

    // Casa andina de adobe: zócalo de piedra, paredes de barro, techo de tejas, puerta de madera y ventana encendida.
    static void AdobeHouse(Vector3 pos, Quaternion rot)
    {
        var rnd = new System.Random(Mathf.RoundToInt(pos.x * 31 + pos.z * 17));
        var h = new GameObject("Casa de adobe").transform;
        h.SetPositionAndRotation(pos, rot);
        var stone = TexMat("rustic_stone_wall", new Color(0.8f, 0.78f, 0.75f), new Vector2(3f, 0.5f));
        var adobe = TexMat("clay_plaster", new Color(0.95f, 0.85f, 0.72f), new Vector2(2f, 1f));
        var tiles = TexMat("clay_roof_tiles", new Color(0.9f, 0.75f, 0.65f), new Vector2(3f, 2f));
        var wood = TexMat("weathered_planks", new Color(0.7f, 0.6f, 0.5f), new Vector2(1f, 1f));
        var beam = TexMat("bark_brown_02", new Color(0.6f, 0.5f, 0.4f), Vector2.one);
        const float w = 6f, d = 4.5f, wh = 2.8f, sb = 0.6f;

        Box(h, new Vector3(0, sb / 2f, 0), new Vector3(w + 0.3f, sb, d + 0.3f), stone, default, true);
        Box(h, new Vector3(0, sb + wh / 2f, 0), new Vector3(w, wh, d), adobe, default, true);
        GableRoof(h, sb + wh, w, d, 1.6f, tiles, adobe, beam);

        // Puerta con marco y escalón.
        Box(h, new Vector3(-1.2f, sb + 0.95f, d / 2f + 0.02f), new Vector3(1f, 1.9f, 0.06f), wood);
        Box(h, new Vector3(-1.2f, sb + 1.95f, d / 2f + 0.05f), new Vector3(1.3f, 0.14f, 0.12f), beam);
        foreach (var s in new[] { -1f, 1f }) Box(h, new Vector3(-1.2f + s * 0.58f, sb + 0.95f, d / 2f + 0.05f), new Vector3(0.12f, 1.95f, 0.12f), beam);
        Box(h, new Vector3(-1.2f, 0.12f, d / 2f + 0.45f), new Vector3(1.4f, 0.24f, 0.6f), stone, default, true);
        // Ventana encendida con marco, alféizar y contraventana abierta.
        Box(h, new Vector3(1.4f, sb + 1.4f, d / 2f + 0.02f), new Vector3(0.75f, 0.7f, 0.03f), Mat("Ventana cálida", new Color(1f, 0.62f, 0.28f), true));
        foreach (var s in new[] { -1f, 1f })
        {
            Box(h, new Vector3(1.4f + s * 0.43f, sb + 1.4f, d / 2f + 0.05f), new Vector3(0.1f, 0.85f, 0.1f), beam);
            Box(h, new Vector3(1.4f, sb + 1.4f + s * 0.4f, d / 2f + 0.05f), new Vector3(0.95f, 0.1f, 0.1f), beam);
        }
        Box(h, new Vector3(1.4f, sb + 0.98f, d / 2f + 0.12f), new Vector3(1.1f, 0.08f, 0.25f), stone);
        Box(h, new Vector3(2.05f, sb + 1.4f, d / 2f + 0.3f), new Vector3(0.4f, 0.75f, 0.05f), wood, new Vector3(0, -70f, 0));
        // Vigas que asoman bajo el alero.
        for (int i = 0; i < 5; i++)
            Box(h, new Vector3(-2.4f + i * 1.2f, sb + wh - 0.1f, d / 2f + 0.25f), new Vector3(0.14f, 0.14f, 0.6f), beam);

        // Objetos junto a la casa: vasijas, canastas, banco; a veces leña con hacha o un fogón.
        Props(h.TransformPoint(new Vector3(0.5f, 0, d / 2f + 1.2f)), rot, rnd, AndesProps, 3, 2.6f);
        if (rnd.NextDouble() < 0.5)
        {
            var logs = h.TransformPoint(new Vector3(w / 2f + 0.9f, 0, 0.5f));
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Cylinder, null, logs + rot * new Vector3(0, 0.15f + (i / 2) * 0.28f, (i % 2) * 0.3f), new Vector3(0.28f, 0.6f, 0.28f), beam)
                    .transform.rotation = rot * Quaternion.Euler(90, 0, 0);
            Place("wooden_axe", logs + Vector3.up * 0.7f, rot.eulerAngles.y + 30f, 1f);
        }
        else Place("stone_fire_pit", h.TransformPoint(new Vector3(-w / 2f - 1.5f, 0, 1.5f)), 0, 1f, true);
        h.gameObject.isStatic = true;
    }
}
