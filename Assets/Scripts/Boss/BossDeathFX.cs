using System.Collections;
using UnityEngine;

// Muerte épica de cada jefe. Corre en todas las PCs (solo visual), con tiempo real para que la cámara lenta no la alargue de más.
//  · Chullachaqui: lo atrapan raíces del suelo y se hunde entre hojas.
//  · Yacumama: remolino en la cocha y un géiser final.
//  · Bufeo Colorado: se deshace en destellos rosados que suben.
//  · Tunche: todas las luces se apagan como velas, humo morado y vuelve una sola llama dorada.
//  · Pishtaco: se vuelve ceniza que se lleva el viento y una luz dorada sale de su pecho.
// Al final, una runa dorada de "sellado" bajo el jefe.
public class BossDeathFX : MonoBehaviour
{
    public const float Duration = 4.6f;   // el panel "¡Nivel superado!" espera esto

    public static void Play(BossAI boss, Transform visual, string bossName, bool serpent)
    {
        var go = new GameObject("Muerte jefe");
        var fx = go.AddComponent<BossDeathFX>();
        fx.StartCoroutine(fx.Run(boss.transform, visual, bossName ?? "", serpent, boss.HitRadius));
    }

    IEnumerator Run(Transform boss, Transform visual, string name, bool serpent, float hitRadius)
    {
        Vector3 pos = boss.position;
        float height = serpent ? 6f : Mathf.Max(3f, hitRadius * 2.2f + 1f);
        Color gold = new Color(1f, 0.8f, 0.35f);

        SlowMo.Play(0.3f, 1.4f);
        HUD.Flash(new Color(1f, 0.92f, 0.7f, 0.45f), 0.7f);
        CameraFollow.Focus(pos, Duration - 0.4f, serpent ? 12f : Mathf.Max(5.5f, hitRadius * 2.2f + 3f), height);
        Effects.Burst(pos + Vector3.up * height * 0.5f, gold, 60, 10f, 0.6f, 1.2f);
        AbilityFX.RingAt(pos + Vector3.up * 0.1f, gold, 10f, 1.2f);

        // Columna de luz que crece y se apaga.
        var light = new GameObject("Luz muerte").AddComponent<Light>();
        light.transform.position = pos + Vector3.up * height * 0.6f;
        light.type = LightType.Point;
        light.range = 18f;
        light.color = ThemeColor(name);
        light.intensity = 0f;

        Vector3 startScale = visual != null ? visual.localScale : Vector3.one;
        Vector3 startLocal = visual != null ? visual.localPosition : Vector3.zero;
        Light[] sceneLights = null; float[] lightIntensity = null;
        if (name.Contains("Tunche"))
        {
            sceneLights = FindObjectsByType<Light>();
            lightIntensity = new float[sceneLights.Length];
            for (int i = 0; i < sceneLights.Length; i++) lightIntensity[i] = sceneLights[i] != null ? sceneLights[i].intensity : 0f;
        }

        float t = 0f, nextPuff = 0f;
        int roots = 0;
        while (t < Duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01((t - 1.2f) / 2.4f);   // 0→1 mientras "se va"
            light.intensity = Mathf.Sin(Mathf.Clamp01(t / Duration) * Mathf.PI) * 6f;

            if (t >= nextPuff)
            {
                nextPuff = t + 0.18f;
                Vector3 body = pos + Vector3.up * Random.Range(0.3f, height) + Random.insideUnitSphere * hitRadius * 0.6f;
                if (name.Contains("Chulla"))
                {
                    Effects.Burst(body, new Color(0.35f, 0.65f, 0.2f), 14, 4f, 0.35f, 1.4f, 0.4f);
                    if (t > 0.6f && roots < 7) { StartCoroutine(Root(pos, hitRadius, roots)); roots++; }
                }
                else if (name.Contains("Yacu"))
                {
                    float a = t * 400f;
                    var ring = pos + Quaternion.Euler(0, a, 0) * Vector3.forward * Mathf.Lerp(7f, 2f, k);
                    Effects.Burst(new Vector3(ring.x, 0.2f, ring.z), new Color(0.7f, 0.9f, 0.95f, 0.9f), 16, 4f, 0.45f, 0.9f, 1.2f);
                    if (Random.value < 0.4f) AbilityFX.RingAt(pos + Vector3.up * 0.05f, new Color(0.4f, 0.75f, 0.8f), Mathf.Lerp(9f, 3f, k), 0.8f);
                }
                else if (name.Contains("Bufeo"))
                    Effects.Burst(body, new Color(1f, 0.5f, 0.7f), 18, 2.5f, 0.3f, 1.8f, -0.6f);
                else if (name.Contains("Tunche"))
                    Effects.Burst(body, new Color(0.35f, 0.2f, 0.45f, 0.8f), 10, 1.5f, 1.1f, 2f, -0.25f);
                else   // Pishtaco y cualquier otro: ceniza al viento
                {
                    Effects.Burst(body + Vector3.right * 0.5f, new Color(0.45f, 0.42f, 0.4f), 16, 3f, 0.3f, 1.8f, -0.15f);
                    if (t > 1f && t < 1.3f) Effects.Burst(pos + Vector3.up * height * 0.6f, gold, 40, 12f, 0.5f, 1f);
                }
            }

            // El cuerpo se va: se hunde (Chullachaqui), se encoge (Bufeo, Pishtaco, Tunche). La Yacumama ya se hunde sola.
            if (visual != null && !serpent)
            {
                float e = k * k;
                if (name.Contains("Chulla")) visual.localPosition = startLocal + Vector3.down * e * height * 1.1f;
                else visual.localScale = startScale * Mathf.Lerp(1f, name.Contains("Pishtaco") ? 0.05f : 0.01f, e);
                if (name.Contains("Bufeo") || name.Contains("Tunche")) visual.localPosition = startLocal + Vector3.up * e * 1.5f;
            }

            // Tunche: todas las luces se apagan como velas... y luego vuelven poco a poco.
            if (sceneLights != null)
            {
                float dim = t < 2.2f ? Mathf.Clamp01(1f - (t - 0.6f) / 1.2f) : Mathf.Clamp01((t - 3.2f) / 1.2f);
                for (int i = 0; i < sceneLights.Length; i++)
                    if (sceneLights[i] != null && sceneLights[i] != light) sceneLights[i].intensity = lightIntensity[i] * Mathf.Max(0.08f, dim);
                if (t > 2.4f && t < 2.5f) Effects.Burst(pos + Vector3.up * 1.2f, new Color(1f, 0.75f, 0.3f), 30, 2f, 0.4f, 1.5f, -0.5f);
            }

            if (name.Contains("Yacu") && t > 2.6f && t < 2.65f)
            {
                // Géiser final.
                Effects.Burst(pos + Vector3.up * 0.3f, new Color(0.75f, 0.92f, 1f), 120, 16f, 0.7f, 1.6f, 1.6f);
                CameraFollow.Shake(0.6f, 0.6f);
            }
            if (t > 3.3f && t < 3.35f)
            {
                // Sello final: runa dorada y estallido de luz.
                AbilityFX.RuneWarning(pos, serpent ? 6f : Mathf.Max(3.5f, hitRadius * 2f), 1.4f, gold);
                Effects.Burst(pos + Vector3.up * 1f, gold, 70, 8f, 0.5f, 1.4f, -0.2f);
                AbilityFX.RingAt(pos + Vector3.up * 0.1f, gold, 12f, 1.2f);
            }
            yield return null;
        }

        if (sceneLights != null)
            for (int i = 0; i < sceneLights.Length; i++) if (sceneLights[i] != null) sceneLights[i].intensity = lightIntensity[i];
        if (visual != null && !serpent) visual.gameObject.SetActive(false);   // ya se fue del todo
        Destroy(light.gameObject);
        Destroy(gameObject);
    }

    // Raíz que sale del suelo y se curva sobre el Chullachaqui.
    IEnumerator Root(Vector3 center, float radius, int index)
    {
        var mat = new Material(Shader.Find("Standard")) { color = new Color(0.28f, 0.2f, 0.12f) };
        var root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(root.GetComponent<Collider>());
        root.GetComponent<Renderer>().sharedMaterial = mat;
        float a = index * 51f;
        var basePos = center + Quaternion.Euler(0, a, 0) * Vector3.forward * (radius + 2.2f);
        root.transform.position = basePos;
        root.transform.rotation = Quaternion.LookRotation(Vector3.up) * Quaternion.Euler(0, 0, a);
        Effects.Burst(basePos, new Color(0.35f, 0.27f, 0.18f), 20, 3f, 0.4f, 0.8f, 1f, flat: true);
        Destroy(root, 3.6f);   // tiempo de juego (con la cámara lenta dura un poco más)
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.7f)
        {
            float h = Mathf.Lerp(0.1f, 1.6f + (index % 3) * 0.35f, t);
            root.transform.localScale = new Vector3(0.35f, h, 0.35f);
            // Se inclina hacia el jefe como si lo atrapara.
            var toCenter = (center - basePos).normalized;
            root.transform.rotation = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, toCenter, 0.3f * t));
            root.transform.position = basePos + root.transform.up * h * 0.9f;
            yield return null;
        }
    }

    static Color ThemeColor(string name) =>
        name.Contains("Chulla") ? new Color(0.5f, 1f, 0.4f) :
        name.Contains("Yacu") ? new Color(0.4f, 0.9f, 1f) :
        name.Contains("Bufeo") ? new Color(1f, 0.5f, 0.75f) :
        name.Contains("Tunche") ? new Color(0.7f, 0.4f, 1f) :
        new Color(1f, 0.8f, 0.4f);
}
