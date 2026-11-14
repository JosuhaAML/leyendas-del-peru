using UnityEngine;

// Rayo de luz entre los árboles: siempre mira a la cámara (girando solo en vertical, con su inclinación)
// y su brillo late despacio, como la luz que se cuela entre las hojas.
public class LightShaft : MonoBehaviour
{
    float tilt, seed;
    Renderer rend;
    MaterialPropertyBlock block;
    Color baseTint;

    void Start()
    {
        tilt = transform.eulerAngles.z;
        seed = Random.value * 10f;
        rend = GetComponent<Renderer>();
        baseTint = rend.sharedMaterial.GetColor("_TintColor");
        block = new MaterialPropertyBlock();
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var to = cam.transform.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(-to) * Quaternion.Euler(0f, 0f, tilt);
        float k = 0.75f + 0.25f * Mathf.Sin(Time.time * 0.6f + seed) + 0.1f * Mathf.Sin(Time.time * 1.7f + seed * 2f);
        block.SetColor("_TintColor", new Color(baseTint.r, baseTint.g, baseTint.b, baseTint.a * k));
        rend.SetPropertyBlock(block);
    }
}
