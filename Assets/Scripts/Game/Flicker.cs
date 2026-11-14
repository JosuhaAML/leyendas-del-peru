using UnityEngine;

// Llama que parpadea: varía el brillo de su luz y el tamaño de la llama (velas, antorchas, faroles).
public class Flicker : MonoBehaviour
{
    public float amount = 0.25f;
    Light lightSource;
    float baseIntensity;
    Vector3 baseScale;
    float seed;

    void Start()
    {
        lightSource = GetComponentInChildren<Light>();
        if (lightSource != null) baseIntensity = lightSource.intensity;
        baseScale = transform.localScale;
        seed = Random.value * 100f;
    }

    void Update()
    {
        float n = Mathf.PerlinNoise(seed, Time.time * 6f) - 0.5f;
        float n2 = Mathf.PerlinNoise(seed + 7f, Time.time * 11f) - 0.5f;
        if (lightSource != null) lightSource.intensity = baseIntensity * (1f + (n + n2 * 0.5f) * amount * 2f);
        transform.localScale = new Vector3(baseScale.x * (1f + n2 * amount * 0.6f), baseScale.y * (1f + n * amount), baseScale.z * (1f + n2 * amount * 0.6f));
    }
}
