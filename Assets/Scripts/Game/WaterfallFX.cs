using UnityEngine;

// Cascada: el agua del plano baja (textura que se desplaza) y abajo sale una nube de vapor y salpicaduras.
public class WaterfallFX : MonoBehaviour
{
    public Renderer water;
    public float width = 8f;
    ParticleSystem mist;

    void Start()
    {
        var go = new GameObject("Vapor de la cascada");
        go.transform.SetParent(transform, false);
        mist = go.AddComponent<ParticleSystem>();
        mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = mist.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(2f, 5f);
        main.startColor = new Color(0.9f, 0.95f, 1f, 0.35f);
        main.maxParticles = 200;
        main.gravityModifier = -0.05f;
        var emission = mist.emission;
        emission.rateOverTime = 35f;
        var shape = mist.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(width, 0.5f, 2f);
        var col = mist.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        go.GetComponent<ParticleSystemRenderer>().material = Effects.SoftMaterial();
        mist.Play();
    }

    void Update()
    {
        if (water != null) water.material.mainTextureOffset = new Vector2(0f, Time.time * 1.2f);
    }
}
