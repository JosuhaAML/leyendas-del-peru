using UnityEngine;

public enum AmbienceType { Ninguno, Lluvia, Luciernagas, Nieve }

// Clima del nivel hecho con partículas: sigue a la cámara para que siempre llueva/nieve alrededor del jugador.
// Es solo visual (cada PC tiene el suyo). GameSetup lo pone en cada escena.
public class LevelAmbience : MonoBehaviour
{
    public AmbienceType type;
    ParticleSystem ps;

    void Start()
    {
        if (type == AmbienceType.Ninguno) return;
        ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        var emission = ps.emission;
        var shape = ps.shape;
        var renderer = GetComponent<ParticleSystemRenderer>();
        renderer.material = Effects.SoftMaterial();
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.loop = true;
        shape.shapeType = ParticleSystemShapeType.Box;

        switch (type)
        {
            case AmbienceType.Lluvia:
                main.startLifetime = 1.2f;
                main.startSpeed = 0f;
                main.startSize = 0.16f;
                main.startColor = new Color(0.8f, 0.88f, 1f, 0.65f);
                main.gravityModifier = 2.2f;
                main.maxParticles = 3000;
                emission.rateOverTime = 1400f;
                shape.scale = new Vector3(45f, 1f, 45f);
                transform.localRotation = Quaternion.identity;
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.06f;
                renderer.lengthScale = 1f;
                break;

            case AmbienceType.Nieve:
                main.startLifetime = 9f;
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
                main.startColor = new Color(1f, 1f, 1f, 0.85f);
                main.gravityModifier = 0.06f;
                main.maxParticles = 3000;
                emission.rateOverTime = 260f;
                shape.scale = new Vector3(55f, 1f, 55f);
                var drift = ps.noise;
                drift.enabled = true;
                drift.strength = 0.6f;
                drift.frequency = 0.3f;
                break;

            case AmbienceType.Luciernagas:
                main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
                main.startSpeed = 0.2f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
                main.startColor = new Color(0.85f, 1f, 0.4f, 1f);
                main.maxParticles = 500;
                emission.rateOverTime = 80f;
                shape.scale = new Vector3(40f, 5f, 40f);
                var wander = ps.noise;
                wander.enabled = true;
                wander.strength = 0.8f;
                wander.frequency = 0.5f;
                // Parpadeo: se encienden y apagan durante su vida.
                var blink = new Gradient();
                blink.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.2f, 0.45f),
                            new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
                var colorOverLife = ps.colorOverLifetime;
                colorOverLife.enabled = true;
                colorOverLife.color = blink;
                break;
        }
        ps.Play();
        if (type == AmbienceType.Lluvia) CreateSplashes();
    }

    // Salpicaduras de lluvia: anillitos que se abren en el suelo alrededor del jugador.
    ParticleSystem splashes;

    void CreateSplashes()
    {
        var go = new GameObject("Salpicaduras");
        go.transform.SetParent(transform, false);
        splashes = go.AddComponent<ParticleSystem>();
        splashes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = splashes.main;
        main.startLifetime = 0.35f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.startColor = new Color(0.85f, 0.9f, 1f, 0.5f);
        main.maxParticles = 600;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = splashes.emission;
        emission.rateOverTime = 420f;
        var shape = splashes.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(30f, 0.01f, 30f);
        var size = splashes.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.3f, 1f, 1.6f));
        var col = splashes.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = Effects.SoftMaterial();
        r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        splashes.Play();
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (ps == null || cam == null) return;
        // Lluvia y nieve caen desde arriba; las luciérnagas flotan a la altura del jugador.
        float height = type == AmbienceType.Luciernagas ? 1.5f : 14f;
        var p = cam.transform.position;
        transform.position = new Vector3(p.x, type == AmbienceType.Luciernagas ? height : p.y + height, p.z);
        if (splashes != null) splashes.transform.position = new Vector3(p.x, 0.06f, p.z + 8f);
    }
}
