using UnityEngine;

// Niebla baja que se arrastra por el suelo alrededor del jugador: nubes grandes y suaves que aparecen,
// se mueven despacio y desaparecen. Solo visual (cada PC tiene la suya).
public class GroundMist : MonoBehaviour
{
    public Color color = new Color(0.8f, 0.85f, 0.9f);
    public float density = 1f;
    ParticleSystem ps;

    void Start()
    {
        ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(7f, 13f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(color.r, color.g, color.b, 1f);
        main.maxParticles = 160;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 9f * density;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(70f, 0.5f, 70f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        float a = Mathf.Clamp01(0.11f * density);
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(a, 0.3f), new GradientAlphaKey(a, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        var r = GetComponent<ParticleSystemRenderer>();
        r.material = Effects.SoftMaterial();
        r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        r.sortingFudge = 10f;
        // Empieza ya llena (sin esperar a que se forme).
        ps.Simulate(14f, true, true);
        ps.Play();
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var p = cam.transform.position;
        transform.position = new Vector3(p.x, 0.5f, p.z);
    }
}
