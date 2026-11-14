using UnityEngine;

// Epílogo después de vencer al Pishtaco (antes de los créditos): el narrador cierra la historia con dos frases,
// sobre los retratos de los jefes y el atardecer andino. Se puede saltar con clic, Esc o Espacio.
public static class Epilogo
{
    public static bool Open { get; private set; }
    public static bool Finished { get; private set; }

    struct Seg { public string text, clip; public float minTime; public int image; }

    // image: 0..4 retrato de ese jefe, 5 = los cinco juntos, 6 = oscuridad, 7 = río con niebla, 8 = atardecer andino
    static readonly Seg[] Segs =
    {
        new Seg { text = "Las cinco leyendas descansan... pero siguen vivas en quienes las cuentan.", clip = "epi_1", minTime = 6f, image = 5 },
        new Seg { text = "La aventura de los héroes... apenas comienza.", clip = "epi_5", minTime = 5.5f, image = 8 },
    };    const float TeaserTime = 7f;

    static int seg;
    static float segStart, segLength;
    static AudioSource voice;

    public static void Show()
    {
        Open = true;
        Finished = false;
        if (voice == null)
        {
            var go = new GameObject("Voz del epilogo");
            Object.DontDestroyOnLoad(go);
            voice = go.AddComponent<AudioSource>();
        }
        StartSeg(0);
    }

    static void StartSeg(int i)
    {
        seg = i;
        segStart = Time.unscaledTime;
        segLength = TeaserTime;
        if (i >= Segs.Length) return;   // tarjeta "CONTINUARÁ"
        var clip = GameAudio.Clip(Segs[i].clip);
        segLength = Mathf.Max(Segs[i].minTime, clip != null ? clip.length + 0.8f : 0f);
        if (clip != null)
        {
            voice.Stop();
            voice.clip = clip;
            voice.volume = GameAudio.Instance != null ? GameAudio.Instance.narratorVolume : 0.9f;
            voice.Play();
        }
    }

    public static void Close()
    {
        if (!Open) return;
        Open = false;
        Finished = true;
        if (voice != null) voice.Stop();
    }

    public static void Draw(float w, Texture2D[] portraits)
    {
        var e = Event.current;
        if ((e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Space)) || e.type == EventType.MouseDown)
        { Close(); e.Use(); return; }

        float t = Time.unscaledTime - segStart;
        if (t >= segLength)
        {
            if (seg + 1 >= Segs.Length) { Close(); return; }   // sin tarjeta final: termina tras la última frase
            StartSeg(seg + 1);
            t = 0f;
        }
        float fadeIn = Mathf.Clamp01(t / 0.8f), fadeOut = Mathf.Clamp01((segLength - t) / 0.8f);
        float a = Mathf.Min(fadeIn, fadeOut);

        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, w, 720), Texture2D.whiteTexture);

        if (seg >= Segs.Length) { DrawTeaser(w, t, a); return; }

        // Imagen de fondo con un acercamiento lento (efecto documental).
        int img = Segs[seg].image;
        float zoom = 1.04f + 0.06f * (t / segLength);
        if (img == 5 && portraits != null)
        {
            // Los cinco retratos en fila.
            float pw = w / 5f;
            for (int i = 0; i < 5 && i < portraits.Length; i++)
                if (portraits[i] != null)
                {
                    GUI.color = new Color(1, 1, 1, 0.55f * a);
                    Bestiario.Cover(new Rect(i * pw, 0, pw, 720), portraits[i]);
                }
        }
        else if (img <= 4 && portraits != null)
        {
            // Chullachaqui → Yacumama → Tunche, uno tras otro mientras habla.
            int[] order = { 0, 1, 3 };
            float part = segLength / order.Length;
            int k = Mathf.Clamp((int)(t / part), 0, order.Length - 1);
            float pa = Mathf.Min(Mathf.Clamp01((t - k * part) / 0.6f), Mathf.Clamp01(((k + 1) * part - t) / 0.6f), a);
            var p = portraits.Length > order[k] ? portraits[order[k]] : null;
            if (p != null)
            {
                GUI.color = new Color(1, 1, 1, 0.6f * pa);
                Bestiario.Cover(new Rect(-(zoom - 1f) * w / 2f, -(zoom - 1f) * 360f, w * zoom, 720 * zoom), p);
            }
        }
        else if (img == 7)
        {
            // Río de noche con niebla azul (México): bandas que se mueven despacio.
            for (int i = 0; i < 6; i++)
            {
                float y = 380 + i * 50 + Mathf.Sin(Time.unscaledTime * 0.4f + i) * 12f;
                GUI.color = new Color(0.35f, 0.5f, 0.8f, 0.12f * a);
                GUI.DrawTexture(new Rect(-100 + Mathf.Sin(Time.unscaledTime * 0.2f + i * 2f) * 80f, y, w + 200, 70), UIStyle.Tex("brillo"));
            }
            GUI.color = new Color(0.85f, 0.9f, 1f, 0.15f * a);
            GUI.DrawTexture(new Rect(w * 0.62f, 90, 120, 120), UIStyle.Tex("brillo"));   // luna
        }
        else if (img == 8)
        {
            var bg = UIStyle.Tex("fondo_clases");
            if (bg != null)
            {
                GUI.color = new Color(1, 1, 1, 0.55f * a);
                Bestiario.Cover(new Rect(-(zoom - 1f) * w / 2f, -(zoom - 1f) * 360f, w * zoom, 720 * zoom), bg);
            }
            // Los cinco héroes de pie en la plaza, apareciendo uno tras otro (el Guerrero al centro, más adelante).
            string[] heroes = { "Arquero", "Guardian", "Guerrero", "Chaman", "Sanador" };
            float[] xs = { 0.18f, 0.34f, 0.5f, 0.66f, 0.82f };
            float[] hs = { 380f, 410f, 450f, 410f, 380f };
            int[] appear = { 2, 1, 0, 3, 4 };
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < heroes.Length; i++)
                {
                    bool center = i == 2;
                    if ((pass == 0) == center) continue;   // el del centro se dibuja al final (adelante)
                    var tex = UIStyle.Tex("heroe_" + heroes[i]);
                    if (tex == null) continue;
                    float ha = a * Mathf.Clamp01((t - 0.3f - appear[i] * 0.35f) / 0.7f);
                    float h = hs[i], hw = h * tex.width / tex.height;
                    float bob = Mathf.Sin(Time.unscaledTime * 1.6f + i) * 2f;
                    float bottom = center ? 600f : 575f;
                    GUI.color = new Color(1, 1, 1, ha);
                    GUI.DrawTexture(new Rect(w * xs[i] - hw / 2f, bottom - h + bob, hw, h), tex);
                }
            // Sombra bajo el subtítulo para que se lea sobre los héroes.
            GUI.color = new Color(0, 0, 0, 0.55f * a);
            GUI.DrawTexture(new Rect(0, 570, w, 150), Texture2D.whiteTexture);
        }
        GUI.color = new Color(1, 1, 1, a);
        GUI.DrawTexture(new Rect(0, 0, w, 720), UIStyle.Tex("vineta"));

        // Subtítulo de la narración.
        bool cry = Segs[seg].clip == "epi_llanto";
        var style = new GUIStyle(UIStyle.Text)
        {
            fontSize = cry ? 30 : 23, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter, wordWrap = true,
            normal = { textColor = cry ? new Color(0.7f, 0.82f, 1f) : UIStyle.Cream },
        };
        GUI.color = new Color(1, 1, 1, a);
        GUI.Label(new Rect(w / 2 - 460, cry ? 300 : 560, 920, 110), Segs[seg].text, style);
        GUI.Label(new Rect(w - 300, 14, 270, 22), "Clic, Esc o Espacio para saltar", new GUIStyle(UIStyle.Small) { fontSize = 12, alignment = TextAnchor.MiddleRight });
        GUI.color = Color.white;
    }

    // "CONTINUARÁ..." y el adelanto de la próxima parte.
    static void DrawTeaser(float w, float t, float a)
    {
        for (int i = 0; i < 4; i++)
        {
            GUI.color = new Color(0.3f, 0.45f, 0.8f, 0.08f * a);
            GUI.DrawTexture(new Rect(-50 + Mathf.Sin(Time.unscaledTime * 0.3f + i) * 60f, 420 + i * 60, w + 100, 80), UIStyle.Tex("brillo"));
        }
        float a1 = a * Mathf.Clamp01(t / 1f), a2 = a * Mathf.Clamp01((t - 1.5f) / 1f), a3 = a * Mathf.Clamp01((t - 3f) / 1f);
        GUI.color = new Color(1, 1, 1, a1);
        GUI.Label(new Rect(0, 210, w, 90), "CONTINUARÁ...", new GUIStyle(UIStyle.Title) { fontSize = 64, alignment = TextAnchor.MiddleCenter });
        GUI.color = new Color(1, 1, 1, a2);
        UIStyle.Separator(new Rect(w / 2 - 200, 300, 400, 18));
        GUI.Label(new Rect(0, 325, w, 40), "Próximamente: LEYENDAS DE LATINOAMÉRICA",
            new GUIStyle(UIStyle.Title) { fontSize = 28, alignment = TextAnchor.MiddleCenter });
        GUI.color = new Color(1, 1, 1, a3);
        GUI.Label(new Rect(0, 380, w, 60), "La Llorona (México) · y muchas leyendas más de nuestra América",
            new GUIStyle(UIStyle.Text) { fontSize = 21, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.75f, 0.85f, 1f) } });
        GUI.color = Color.white;
    }
}
