using UnityEngine;

// Altar de velas del Tunche: el círculo ritual (zona segura) late y gira despacio, y suben chispas
// de las llamas. Durante el silbido del Tunche brilla más fuerte para que se vea dónde refugiarse.
public class CandleGlow : MonoBehaviour
{
    Transform circle;
    Material mat;
    Color baseTint;
    ParticleSystem embers;
    float seed;

    void Start()
    {
        circle = transform.Find("Zona segura");
        if (circle != null)
        {
            mat = circle.GetComponent<Renderer>().material;
            baseTint = mat.GetColor("_TintColor");
        }
        seed = Random.value * 10f;

        // Chispas que suben desde las velas.
        var go = new GameObject("Chispas");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 1f;
        embers = go.AddComponent<ParticleSystem>();
        embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = embers.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
        main.startColor = new Color(1f, 0.7f, 0.3f, 1f);
        main.gravityModifier = -0.05f;
        main.maxParticles = 60;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = embers.emission;
        emission.rateOverTime = 8f;
        var shape = embers.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.4f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var noise = embers.noise;
        noise.enabled = true;
        noise.strength = 0.3f;
        var col = embers.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        go.GetComponent<ParticleSystemRenderer>().material = Effects.SoftMaterial();
        embers.Play();
    }

    void Update()
    {
        if (circle == null) return;
        circle.Rotate(0f, 0f, 6f * Time.deltaTime, Space.Self);
        bool whistle = BossMechanics.Announcement != null && BossMechanics.Announcement.Contains("silbido") && Time.time < BossMechanics.AnnouncementUntil;
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 2f + seed) + (Mathf.PerlinNoise(seed, Time.time * 3f) - 0.5f) * 0.2f;
        float boost = whistle ? 1.8f + 0.4f * Mathf.Sin(Time.time * 10f) : 1f;
        var c = baseTint * pulse * boost;
        c.a = Mathf.Clamp01(baseTint.a * pulse * boost);
        mat.SetColor("_TintColor", c);
    }
}
