using Unity.Netcode;
using UnityEngine;

// Pantalla de carga entre niveles: retrato del jefe, nombre dorado, lugar, leyenda, un consejo
// y una barra con una chakana que gira. Se queda Hold segundos después de cargar para poder leerla
// (el jefe espera ese tiempo extra antes de atacar, ver BossAI).
public class LoadingScreen : MonoBehaviour
{
    public const float Hold = 4.5f;   // tiempo para leer la leyenda y el dato curioso mientras habla el narrador
    public static bool Showing => level >= 0 && (loadedAt < 0f || Time.time < loadedAt + Hold);

    static int level = -1;
    static float startedAt, loadedAt = -1f;
    bool subscribed;

    static readonly string[] Places =
    {
        "La selva baja · Loreto, Amazonía peruana",
        "La cocha encantada · Amazonía peruana",
        "El río de noche · Iquitos, río Amazonas",
        "El bosque de las velas · Ucayali",
        "El pueblo andino · Ayacucho",
    };

    static readonly string[] Legends =
    {
        "Espíritu guardián de la selva baja. Tiene un pie humano y otro de animal, y toma la forma de personas conocidas para perder a los caminantes en el monte.",
        "La \"madre del agua\" (quechua yaku + mama): serpiente gigante de más de 30 metros que vive en ríos y cochas de la Amazonía y succiona a quien se acerca.",
        "El delfín rosado del Amazonas. En noches de luna llena sale del río como un joven elegante vestido de blanco y se lleva a las muchachas al fondo del río.",
        "Alma en pena de la selva que anuncia su llegada con un silbido agudo: si suena lejos, está cerca. Ronda las casas donde a un muerto no lo velaron como se debe.",
        "Figura temida de los Andes: un extraño que recorre los caminos de noche con su cuchillo para robar la grasa de los viajeros.",
    };

    static readonly string[] Tips =
    {
        "Cuando se multiplique, solo el verdadero recibe daño: fíjate cuál pierde vida.",
        "Cuando se sumerja no recibe daño: aléjate de la orilla y espera a que vuelva a salir.",
        "Su canto te encanta y te arrastra al río: ¡tus amigos deben seguir atacando!",
        "Cuando se oiga su silbido, corre junto a una vela: su luz te protege.",
        "Cuando desaparezca en las sombras, júntense: aparece por la espalda.",
    };

    static readonly string[] Travel =
    {
        "Viajando a la selva...", "Viajando a la cocha...", "Viajando al río Amazonas...",
        "Viajando al bosque de las velas...", "Viajando a los Andes...",
    };

    void Update()
    {
        var net = NetworkManager.Singleton;
        if (net == null) return;
        if (!subscribed && net.SceneManager != null && (net.IsClient || net.IsServer))
        {
            net.SceneManager.OnSceneEvent += OnSceneEvent;
            subscribed = true;
        }
        if (subscribed && !net.IsClient && !net.IsServer) { subscribed = false; level = -1; }
    }

    void OnSceneEvent(SceneEvent e)
    {
        var net = NetworkManager.Singleton;
        if (e.SceneEventType == SceneEventType.Load && (e.ClientId == net.LocalClientId || net.IsServer))
        {
            int lv = Progress.LevelOfScene(e.SceneName);
            if (lv >= 0 && (level != lv || loadedAt >= 0f))
            {
                level = lv;
                startedAt = Time.time;
                loadedAt = -1f;
                factIndex = Random.Range(0, 3);
                GameAudio.Narrate("narr_" + lv);   // el narrador cuenta la leyenda
            }
            else if (lv < 0) level = -1;
        }
        else if (e.SceneEventType == SceneEventType.LoadComplete && e.ClientId == net.LocalClientId && level >= 0)
            loadedAt = Mathf.Max(Time.time, startedAt + 0.6f);
    }

    // Para pruebas automáticas.
    public static float LoadedAt => loadedAt;

    // "¿Sabías que...?": datos curiosos de cada mito y su región (se elige uno al azar).
    static int factIndex;
    static readonly string[,] Facts =
    {
        { "El nombre viene del quechua: «chulla» (desigual) y «chaqui» (pie).",
          "Dicen que si te pierdes en el monte, debes ponerte la ropa al revés para confundirlo.",
          "Se le considera el dueño y protector de los animales de la selva." },
        { "«Yacumama» viene del quechua «yaku» (agua) y «mama» (madre).",
          "Los pescadores cuentan que las cochas donde vive nunca se secan.",
          "La leyenda podría estar inspirada en la anaconda, la serpiente más pesada del mundo." },
        { "El bufeo colorado es el delfín rosado del Amazonas, una especie protegida en el Perú.",
          "Según la leyenda, usa sombrero para esconder el espiráculo de su cabeza.",
          "Es el delfín de río más grande del mundo: puede medir más de 2,5 metros." },
        { "La leyenda del Tunche es muy conocida en Loreto, Ucayali y San Martín.",
          "Se dice que si respondes a su silbido, te persigue toda la noche.",
          "Su silbido se parece al canto de un ave nocturna de la selva." },
        { "«Pishtaco» viene del quechua «pishtay»: degollar o cortar en tiras.",
          "Es una de las leyendas más antiguas de los Andes, contada desde la época colonial.",
          "Se le describe como un forastero alto, de barba, con un gran cuchillo." },
    };

    void OnGUI()
    {
        if (!Showing) return;
        GUI.depth = -10;
        float scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float w = Screen.width / scale;
        float t = Time.time - startedAt;
        float fade = loadedAt < 0f ? 1f : Mathf.Clamp01((loadedAt + Hold - Time.time) / 0.4f);
        float fadeIn = Mathf.Clamp01(t / 0.3f);
        float a = Mathf.Min(fade, fadeIn);

        GUI.color = new Color(0.047f, 0.031f, 0.024f, a);
        GUI.DrawTexture(new Rect(0, 0, w, 720), Texture2D.whiteTexture);

        // Retrato a la izquierda que se funde con el fondo.
        float pw = Mathf.Min(760f, w * 0.58f);
        var portraits = NetworkManager.Singleton.GetComponent<NetworkUI>()?.Retratos;
        GUI.color = new Color(1, 1, 1, a);
        if (portraits != null && level < portraits.Length && portraits[level] != null)
        {
            // Zoom lento ("efecto Ken Burns").
            float z = 1f + t * 0.015f;
            HUD.DrawCover(new Rect(-(z - 1) * pw / 2, -(z - 1) * 360, pw * z, 720 * z), portraits[level]);
        }
        GUI.DrawTexture(new Rect(pw - 300, 0, 322, 720), Tex("fundido"));
        GUI.color = new Color(0.047f, 0.031f, 0.024f, a);
        GUI.DrawTexture(new Rect(pw + 20, 0, w - pw, 720), Texture2D.whiteTexture);
        GUI.color = new Color(1, 1, 1, a);
        UIStyle.ScreenCorners(w, 110);

        float cw = Mathf.Min(560f, w - pw - 40), x = Mathf.Max(pw - 60, w - cw - 50), mid = x + cw / 2;
        GUI.DrawTexture(new Rect(w / 2 - 110, 4, 220, 100), Tex("logo"));

        var center = new GUIStyle(UIStyle.Text) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(x, 122, cw, 30), $"NIVEL {level + 1}", new GUIStyle(center) { fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = UIStyle.Gold } });
        UIStyle.Separator(new Rect(mid - 210, 126, 120, 22));
        UIStyle.Separator(new Rect(mid + 90, 126, 120, 22));
        var name = Tex("nombre_" + level);
        if (name != null)
        {
            float nh = 120f * Mathf.Min(1.15f, (cw + 40) / name.width);
            float nw = name.width * nh / 120f;
            GUI.DrawTexture(new Rect(mid - nw / 2 + 14, 200 - nh / 2, nw, nh), name);
        }
        GUI.Label(new Rect(x, 252, cw, 30), Places[level],
            new GUIStyle(center) { fontSize = 20, fontStyle = FontStyle.Italic, normal = { textColor = new Color(0.86f, 0.9f, 0.86f) } });
        UIStyle.Separator(new Rect(x + 20, 286, cw - 40, 22));
        GUI.Label(new Rect(x + 10, 310, cw - 20, 90), Legends[level], new GUIStyle(UIStyle.Text) { fontSize = 17, wordWrap = true });
        GUI.Label(new Rect(x + 10, 392, cw - 20, 48), $"<color=#f2c25c><b>¿Sabías que...?</b></color> <i>{Facts[level, factIndex]}</i>",
            new GUIStyle(UIStyle.Text) { fontSize = 15, wordWrap = true, richText = true });

        // Consejo con cinta dorada arriba.
        var tip = new Rect(x, 452, cw, 108);
        UIStyle.Panel(tip);
        var ribbon = new Rect(mid - 90, tip.y - 18, 180, 36);
        GUI.Box(ribbon, GUIContent.none, UIStyle.Button);
        GUI.Label(ribbon, "CONSEJO", new GUIStyle(UIStyle.Title) { fontSize = 19 });
        GUI.Label(new Rect(tip.x + 26, tip.y + 24, cw - 52, 74), Tips[level],
            new GUIStyle(center) { fontSize = 17, fontStyle = FontStyle.Italic, wordWrap = true });

        // Barra de progreso con brillo: avanza mientras carga y se completa al terminar.
        float progress = loadedAt < 0f ? 0.85f * (1f - Mathf.Exp(-t * 1.5f))
            : Mathf.Lerp(0.85f, 1f, Mathf.Clamp01((Time.time - loadedAt) / 1.2f));
        var bar = new Rect(Mathf.Min(x, pw - 120), 606, w - Mathf.Min(x, pw - 120) - 50, 30);
        GUI.color = new Color(1f, 0.75f, 0.3f, a * (0.35f + 0.15f * Mathf.Sin(t * 4f)));
        GUI.DrawTexture(new Rect(bar.x, bar.y - 30, bar.width * progress + 40, bar.height + 60), Tex("brillo"));
        GUI.color = new Color(1, 1, 1, a);
        UIStyle.Bar(bar, progress, new Color(1f, 0.78f, 0.3f));
        float px = bar.x + 6 + (bar.width - 12) * progress;
        var pivot = new Vector2(px, bar.center.y);
        var m = GUI.matrix;
        GUIUtility.RotateAroundPivot(t * 90f, pivot * scale);
        GUI.DrawTexture(new Rect(px - 22, bar.center.y - 22, 44, 44), Tex("chakana"));
        GUI.matrix = m;
        float bm = bar.center.x;
        UIStyle.Separator(new Rect(bm - 260, 650, 100, 18));
        UIStyle.Separator(new Rect(bm + 160, 650, 100, 18));
        GUI.Label(new Rect(bm - 160, 644, 320, 30), $"{Travel[level]}   <color=#f2c25c><b>{Mathf.RoundToInt(progress * 100)} %</b></color>",
            new GUIStyle(center) { fontSize = 19, fontStyle = FontStyle.Italic, richText = true });
        GUI.color = Color.white;
    }

    static Texture2D Tex(string n) => Resources.Load<Texture2D>("UI/" + n);
}
