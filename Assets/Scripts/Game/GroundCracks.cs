using System.Collections;
using UnityEngine;

// Grietas en el suelo para los golpes fuertes del jefe: se abren rápido, brillan un instante con el color
// del jefe, saltan piedras y la pantalla tiembla más cuanto más cerca estés. Solo visual (todas las PCs).
public class GroundCracks : MonoBehaviour
{
    static GroundCracks runner;
    static Material darkMat, glowMat;
    static Mesh quad;

    static GroundCracks Runner
    {
        get
        {
            if (runner == null) runner = new GameObject("Grietas").AddComponent<GroundCracks>();
            return runner;
        }
    }

    // Sacudida según la distancia: fuerte cerca, nada lejos.
    public static void ShakeAt(Vector3 at, float strength, float duration, float range)
    {
        var cam = Camera.main;
        if (cam == null) return;
        float k = 1f - Mathf.Clamp01(Vector3.Distance(cam.transform.position, at) / range);
        if (k > 0.05f) CameraFollow.Shake(strength * (0.35f + 0.65f * k), duration);
    }

    // water = en el agua no hay grietas: salpicadura y anillos de olas.
    public static void Open(Vector3 at, float radius, Color glow, bool water = false)
    {
        at.y = 0f;
        if (water)
        {
            Effects.Burst(at + Vector3.up * 0.2f, new Color(0.75f, 0.92f, 1f, 0.9f), 60, radius * 1.6f, 0.6f, 1f, 1.4f);
            AbilityFX.RingAt(at + Vector3.up * 0.05f, new Color(0.6f, 0.85f, 0.95f), radius * 1.2f, 0.9f);
            AbilityFX.RingAt(at + Vector3.up * 0.05f, new Color(0.6f, 0.85f, 0.95f), radius * 1.8f, 1.3f);
            return;
        }
        Runner.StartCoroutine(Runner.Crack(at, radius, glow));
        Runner.StartCoroutine(Runner.Debris(at, radius));
    }

    static void Setup()
    {
        if (darkMat != null) return;
        var tex = Resources.Load<Texture2D>("UI/grieta");
        darkMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        var add = Shader.Find("Legacy Shaders/Particles/Additive");
        glowMat = new Material(add != null ? add : Shader.Find("Sprites/Default")) { mainTexture = tex };
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad = q.GetComponent<MeshFilter>().sharedMesh;
        Destroy(q);
    }

    static GameObject Plane(string name, Material mat, Vector3 at, float yaw, float y)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(at + Vector3.up * y, Quaternion.Euler(90f, yaw, 0f));
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    IEnumerator Crack(Vector3 at, float radius, Color glow)
    {
        Setup();
        float yaw = Random.Range(0f, 360f);
        float size = radius * 2.9f;
        // Cada grieta con su propia copia del material para poder desvanecerla sola.
        var dm = new Material(darkMat);
        var gm = new Material(glowMat);
        var dark = Plane("Grieta", dm, at, yaw, 0.03f + Random.value * 0.01f);
        var lit = Plane("Grieta brillo", gm, at, yaw, 0.045f);
        lit.transform.localScale = Vector3.one * 0.01f;

        const float life = 4.5f;
        for (float t = 0f; t < life; t += Time.deltaTime)
        {
            // Se abre en 0,15 s (rápido, con un pequeño rebote).
            float open = Mathf.Clamp01(t / 0.15f);
            float s = size * (open < 1f ? Mathf.Lerp(0.25f, 1.05f, open) : Mathf.Lerp(1.05f, 1f, Mathf.Clamp01((t - 0.15f) / 0.1f)));
            dark.transform.localScale = new Vector3(s, s, 1f);
            lit.transform.localScale = new Vector3(s * 0.98f, s * 0.98f, 1f);

            float fade = Mathf.Clamp01((life - t) / 1.2f);
            dm.color = new Color(0.03f, 0.02f, 0.02f, fade);
            // Brillo: destello al abrirse y se apaga en ~1 s.
            float g = Mathf.Clamp01(1f - t / 1.6f);
            gm.SetColor("_TintColor", new Color(glow.r, glow.g, glow.b, 0.9f * g));
            yield return null;
        }
        Destroy(dark); Destroy(lit); Destroy(dm); Destroy(gm);
    }

    // Piedras que saltan del suelo y caen (sin física, arco simple).
    IEnumerator Debris(Vector3 at, float radius)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(radius * 2.5f), 6, 16);
        var rocks = new Transform[n];
        var vel = new Vector3[n];
        var spin = new Vector3[n];
        var mat = new Material(Shader.Find("Standard")) { color = new Color(0.36f, 0.3f, 0.24f) };
        for (int i = 0; i < n; i++)
        {
            var r = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(r.GetComponent<Collider>());
            r.GetComponent<Renderer>().sharedMaterial = mat;
            var dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            r.transform.position = at + dir * Random.Range(0.2f, radius * 0.6f) + Vector3.up * 0.1f;
            r.transform.rotation = Random.rotation;
            r.transform.localScale = Vector3.one * Random.Range(0.12f, 0.35f) * Mathf.Clamp(radius / 3f, 0.8f, 1.6f);
            rocks[i] = r.transform;
            vel[i] = dir * Random.Range(1.5f, 4.5f) + Vector3.up * Random.Range(4f, 8f);
            spin[i] = Random.insideUnitSphere * 540f;
        }
        for (float t = 0f; t < 1.6f; t += Time.deltaTime)
        {
            for (int i = 0; i < n; i++)
            {
                if (rocks[i] == null) continue;
                vel[i] += Vector3.down * 18f * Time.deltaTime;
                var p = rocks[i].position + vel[i] * Time.deltaTime;
                if (p.y < 0.05f) { p.y = 0.05f; vel[i] = new Vector3(vel[i].x * 0.4f, -vel[i].y * 0.25f, vel[i].z * 0.4f); spin[i] *= 0.5f; }
                rocks[i].position = p;
                rocks[i].Rotate(spin[i] * Time.deltaTime);
                if (t > 1.1f) rocks[i].localScale *= 1f - 3f * Time.deltaTime;   // se encogen al final
            }
            yield return null;
        }
        foreach (var r in rocks) if (r != null) Destroy(r.gameObject);
        Destroy(mat);
    }
}
