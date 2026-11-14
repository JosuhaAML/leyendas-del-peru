using System.Collections;
using UnityEngine;

// Efectos visuales hechos por código (sin assets): estallidos de partículas y proyectiles con estela.
// Son solo visuales y corren en cada PC. Vive junto al NetworkManager.
public class Effects : MonoBehaviour
{
    static Effects instance;
    static Material softMaterial;

    void Awake()
    {
        instance = this;
        Health.Changed += OnHealthChanged;
    }

    void OnDestroy() => Health.Changed -= OnHealthChanged;

    // Chispas al recibir daño, destellos al curar y explosión cuando muere un jefe.
    void OnHealthChanged(Health h, float delta)
    {
        bool boss = h.GetComponent<BossAI>() != null;
        var center = h.transform.position + Vector3.up * (boss ? 3f : 1.3f);
        if (delta < 0f && !h.IsDead) StartCoroutine(HitFlash(h, boss));
        if (delta < 0f && !h.IsDead && !boss) h.GetComponent<PlayerController>()?.PlayAction("Hit");
        if (delta > 0f)
            Burst(center, new Color(0.5f, 1f, 0.5f), 18, 1.5f, 0.3f, 1f, -0.4f);
        else if (boss && h.IsDead)
            Burst(center, new Color(0.45f, 0.15f, 0.6f), 120, 9f, 0.9f, 1.6f, 0.3f);
        else
            Burst(center, boss ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.25f, 0.2f), boss ? 14 : 10, 5f, 0.25f, 0.45f, 1f);
    }

    // Al recibir daño: destello rojo en el modelo y un leve "encogerse" por el golpe.
    static MaterialPropertyBlock flashBlock;
    readonly System.Collections.Generic.HashSet<Health> flashing = new System.Collections.Generic.HashSet<Health>();

    IEnumerator HitFlash(Health h, bool boss)
    {
        if (h == null || flashing.Contains(h)) yield break;
        flashing.Add(h);
        flashBlock ??= new MaterialPropertyBlock();
        var visual = h.transform.Find("Visual");
        var renderers = (visual != null ? visual : h.transform).GetComponentsInChildren<Renderer>();
        Vector3 baseScale = visual != null ? visual.localScale : Vector3.one;
        const float duration = 0.16f;
        for (float t = 0f; t < duration && h != null; t += Time.deltaTime)
        {
            float k = 1f - t / duration;
            flashBlock.SetColor("_Color", Color.Lerp(Color.white, new Color(1f, 0.35f, 0.3f), k));
            foreach (var r in renderers) if (r != null && !(r is ParticleSystemRenderer)) r.SetPropertyBlock(flashBlock);
            if (visual != null) visual.localScale = baseScale * (1f - (boss ? 0.03f : 0.06f) * Mathf.Sin(t / duration * Mathf.PI));
            yield return null;
        }
        foreach (var r in renderers) if (r != null && !(r is ParticleSystemRenderer)) r.SetPropertyBlock(null);
        if (visual != null) visual.localScale = baseScale;
        flashing.Remove(h);
    }

    // Material de partículas: un punto suave y redondo (textura generada aquí mismo).
    public static Material SoftMaterial()
    {
        if (softMaterial != null) return softMaterial;
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f - 0.5f, n / 2f - 0.5f)) / (n / 2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
            }
        tex.Apply();
        softMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        return softMaterial;
    }

    // Estallido de partículas. flat = anillo a ras del suelo (polvo, agua).
    public static void Burst(Vector3 position, Color color, int count, float speed, float size, float life,
        float gravity = 0f, bool flat = false)
    {
        var go = new GameObject("Efecto");
        go.transform.position = position;
        if (flat) go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.2f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = color;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = flat ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Sphere;
        shape.radius = flat ? 0.8f : 0.25f;

        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        var colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        colorOverLife.color = fade;

        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

        go.GetComponent<ParticleSystemRenderer>().material = SoftMaterial();
        ps.Play();
        Destroy(go, life + 0.5f);
    }

    // Proyectil que vuela de 'from' a 'to' con estela; al llegar, chispas del mismo color.
    public static void Projectile(Vector3 from, Vector3 to, Color color, bool arrow)
    {
        if (instance != null) instance.StartCoroutine(instance.Fly(from, to, color, arrow));
    }

    IEnumerator Fly(Vector3 from, Vector3 to, Color color, bool arrow)
    {
        var go = GameObject.CreatePrimitive(arrow ? PrimitiveType.Cube : PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = arrow ? new Vector3(0.06f, 0.06f, 1f) : Vector3.one * 0.45f;
        var body = new Material(Shader.Find("Sprites/Default")) { color = arrow ? new Color(0.55f, 0.38f, 0.2f) : color };
        go.GetComponent<Renderer>().material = body;

        var trail = go.AddComponent<TrailRenderer>();
        trail.material = SoftMaterial();
        trail.time = arrow ? 0.12f : 0.25f;
        trail.startWidth = arrow ? 0.18f : 0.4f;
        trail.endWidth = 0f;
        trail.startColor = arrow ? new Color(Mathf.Lerp(color.r, 1f, 0.4f), Mathf.Lerp(color.g, 1f, 0.4f), Mathf.Lerp(color.b, 1f, 0.4f), 0.9f) : color;
        trail.endColor = new Color(color.r, color.g, color.b, 0f);

        if (!arrow)
        {
            var light = go.AddComponent<Light>();
            light.color = color;
            light.range = 5f;
            light.intensity = 2.5f;
        }

        float speed = arrow ? 45f : 26f;
        float duration = Mathf.Max(0.08f, Vector3.Distance(from, to) / speed);
        go.transform.rotation = Quaternion.LookRotation(to - from);
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            // Las flechas suben un poco en arco; los orbes van rectos.
            go.transform.position = Vector3.Lerp(from, to, t) + (arrow ? Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.8f : Vector3.zero);
            yield return null;
        }
        Burst(to, arrow ? new Color(1f, 0.85f, 0.5f) : color, arrow ? 8 : 20, arrow ? 3f : 5f, arrow ? 0.18f : 0.35f, 0.5f);
        Destroy(go);
    }
}
