using System.Collections;
using UnityEngine;

// Entrada de cada jefe, justo después de su retrato. Solo visual (cada PC la reproduce igual);
// el servidor espera Duration segundos más antes de que el jefe empiece a atacar.
//  · Chullachaqui: sale de la tierra entre raíces y hojas.
//  · Yacumama: burbujas y emerge de la cocha.
//  · Bufeo Colorado: se forma de un remolino de destellos rosados y agua.
//  · Tunche: las luces se apagan, silba en la oscuridad y aparece del humo morado.
//  · Pishtaco: sale de una columna de sombras con un destello rojo.
// Al final todos rugen (onda expansiva) y se muestra su nombre en grande.
public class BossEntrance : MonoBehaviour
{
    public const float Duration = 4.2f;
    public static bool Playing => current != null;
    static BossEntrance current;

    string bossName, epithet;
    float introLineAt = -1f;
    float startedAt;

    public static void Play(BossAI boss)
    {
        if (boss == null) return;
        if (current != null) Destroy(current.gameObject);
        current = new GameObject("Entrada jefe").AddComponent<BossEntrance>();
        current.bossName = boss.Data.bossName;
        current.epithet = Epithet(boss.Data.bossName);
        current.startedAt = Time.time;
        current.StartCoroutine(current.Run(boss));
    }

    static string Epithet(string n) =>
        n.Contains("Chulla") ? "El guardián del monte" :
        n.Contains("Yacu") ? "La madre de las aguas" :
        n.Contains("Bufeo") ? "El encantador del río" :
        n.Contains("Tunche") ? "El espíritu que silba en la noche" :
        n.Contains("Pishtaco") ? "El degollador de los Andes" : "";

    static Color Theme(string n) =>
        n.Contains("Chulla") ? new Color(0.45f, 0.8f, 0.25f) :
        n.Contains("Yacu") ? new Color(0.45f, 0.85f, 0.95f) :
        n.Contains("Bufeo") ? new Color(1f, 0.45f, 0.7f) :
        n.Contains("Tunche") ? new Color(0.65f, 0.35f, 1f) :
        new Color(0.9f, 0.15f, 0.1f);

    IEnumerator Run(BossAI boss)
    {
        var visual = boss.transform.Find("Visual");
        var serpent = boss.GetComponentInChildren<SerpentMotion>();
        var animator = boss.GetComponentInChildren<Animator>();
        Vector3 pos = boss.transform.position;
        float height = serpent != null ? 6f : Mathf.Max(3f, boss.HitRadius * 2.2f + 1f);
        Color c = Theme(bossName);
        string n = bossName;

        Vector3 baseLocal = visual != null ? visual.localPosition : Vector3.zero;
        Vector3 baseScale = visual != null ? visual.localScale : Vector3.one;
        bool rises = n.Contains("Chulla");
        bool grows = !rises && serpent == null;

        if (serpent != null) serpent.SnapSubmerged();
        if (visual != null && rises) visual.localPosition = baseLocal + Vector3.down * height * 1.2f;
        if (visual != null && grows) visual.localScale = baseScale * 0.001f;

        Light[] lights = null; float[] intensity = null;
        if (n.Contains("Tunche"))
        {
            lights = FindObjectsByType<Light>();
            intensity = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) intensity[i] = lights[i].intensity;
        }

        CameraFollow.Focus(pos, Duration - 0.3f, serpent != null ? 12f : Mathf.Max(5.5f, boss.HitRadius * 2.2f + 3f), height);

        bool roared = false, emerged = false;
        float nextFx = 0f;
        while (true)
        {
            float t = Time.time - startedAt;
            if (t >= Duration || boss == null) break;
            // 0→1 mientras aparece (de 0,4 s a 1,9 s).
            float k = Mathf.Clamp01((t - 0.4f) / 1.5f);
            float e = 1f - Mathf.Pow(1f - k, 3f);

            if (visual != null && rises) visual.localPosition = baseLocal + Vector3.down * height * 1.2f * (1f - e);
            if (visual != null && grows) visual.localScale = baseScale * Mathf.Max(0.001f, e);

            if (t >= nextFx && t < 2f)
            {
                nextFx = t + 0.12f;
                var around = pos + Quaternion.Euler(0, t * 500f, 0) * Vector3.forward * (boss.HitRadius + 1f);
                if (rises)
                {
                    Effects.Burst(pos + Vector3.up * 0.2f, new Color(0.4f, 0.3f, 0.2f), 18, 4f, 0.5f, 0.8f, 1.2f, flat: true);
                    Effects.Burst(around + Vector3.up, c, 8, 3f, 0.3f, 1.2f, 0.4f);
                }
                else if (serpent != null)
                {
                    var b = pos + Random.insideUnitSphere * 3f; b.y = 0.15f;
                    Effects.Burst(b, new Color(0.8f, 0.95f, 1f, 0.9f), 10, 2f, 0.35f, 0.7f, -0.3f);
                    if (!emerged && t > 1f) { emerged = true; serpent.SetSubmerged(false); }
                    if (emerged) Effects.Burst(new Vector3(pos.x, 0.2f, pos.z), new Color(0.75f, 0.92f, 1f), 30, 7f, 0.5f, 1f, 1.5f);
                }
                else if (n.Contains("Bufeo"))
                {
                    Effects.Burst(around + Vector3.up * (0.5f + k * 3f), c, 12, 2f, 0.3f, 1f, -0.4f);
                    Effects.Burst(new Vector3(pos.x, 0.15f, pos.z), new Color(0.7f, 0.85f, 1f), 12, 4f, 0.4f, 0.6f, 1f, flat: true);
                }
                else if (n.Contains("Tunche"))
                    Effects.Burst(pos + Vector3.up * Random.Range(0.3f, height), new Color(0.3f, 0.15f, 0.4f, 0.85f), 10, 1.5f, 1.2f, 1.6f, -0.2f);
                else
                {
                    Effects.Burst(pos + Vector3.up * Random.Range(0f, height * 1.4f), new Color(0.05f, 0.04f, 0.06f, 0.9f), 14, 2.5f, 1f, 1.2f, -0.4f);
                    if (t > 1.6f && t < 1.75f) Effects.Burst(pos + Vector3.up * height * 0.8f, c, 30, 6f, 0.35f, 0.6f);
                }
            }

            // Tunche: oscuridad total al aparecer y la luz vuelve con el rugido.
            if (lights != null)
            {
                float dim = t < 2.2f ? Mathf.Clamp01(1f - t / 0.6f) : Mathf.Clamp01((t - 2.2f) / 0.8f);
                for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].intensity = intensity[i] * Mathf.Max(0.1f, dim);
            }

            // Rugido: onda expansiva, polvo y temblor.
            if (!roared && t >= 2.1f)
            {
                roared = true;
                if (animator != null) animator.SetTrigger("Enrage");
                if (serpent != null) serpent.Play("Enrage");
                GameAudio.Play("rugido");
                boss.Voice("_voz", 1f, 0f);
                introLineAt = Time.time + 0.9f;   // tras el rugido, dice su frase de entrada
                CameraFollow.Shake(0.7f, 0.9f);
                AbilityFX.RingAt(pos + Vector3.up * 0.1f, c, 14f, 1.1f);
                AbilityFX.RingAt(pos + Vector3.up * 0.1f, new Color(1f, 0.85f, 0.5f), 9f, 0.8f);
                Effects.Burst(pos + Vector3.up * 0.3f, new Color(0.55f, 0.48f, 0.38f), 60, 9f, 0.7f, 1f, 0.5f, flat: true);
                HUD.Flash(new Color(c.r, c.g, c.b, 0.25f), 0.4f);
            }
            if (introLineAt > 0f && Time.time >= introLineAt) { introLineAt = -1f; BossDialogue.Say(boss, "intro", true); }
            yield return null;
        }
        if (introLineAt > 0f) BossDialogue.Say(boss, "intro", true);

        if (visual != null) { visual.localPosition = baseLocal; visual.localScale = baseScale; }
        if (serpent != null) serpent.SetSubmerged(false);
        if (lights != null) for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].intensity = intensity[i];
        if (current == this) current = null;
        Destroy(gameObject);
    }

    // Nombre y título del jefe en grande (lo llama HUD mientras dura la entrada).
    public static void DrawBanner(float w)
    {
        if (current == null) return;
        float t = Time.time - current.startedAt - 1.7f;
        if (t < 0f) return;
        float a = Mathf.Clamp01(t * 3f) * Mathf.Clamp01((Duration - 1.7f - t) * 2.5f);
        float pop = 1f + 0.25f * Mathf.Pow(1f - Mathf.Clamp01(t * 4f), 2f);
        var col = Theme(current.bossName);

        GUI.color = new Color(0f, 0f, 0f, 0.55f * a);
        GUI.DrawTexture(new Rect(0, 470, w, 160), Texture2D.whiteTexture);
        GUI.color = new Color(col.r, col.g, col.b, 0.5f * a);
        GUI.DrawTexture(new Rect(w / 2 - 420, 430, 840, 240), UIStyle.Tex("brillo"));
        GUI.color = new Color(1f, 1f, 1f, a);
        var name = new GUIStyle(UIStyle.Title) { fontSize = Mathf.RoundToInt(64 * pop), alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(0, 478, w, 90), current.bossName.ToUpper(), name);
        UIStyle.Separator(new Rect(w / 2 - 220, 566, 440, 18));
        var sub = new GUIStyle(UIStyle.Text) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic };
        GUI.Label(new Rect(0, 584, w, 36), current.epithet, sub);
        GUI.color = Color.white;
    }
}
