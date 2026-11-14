using UnityEngine;

// Cuerpo de serpiente animado por código (la Yacumama no es humanoide, así que Mixamo no sirve).
// Genera cada cuadro una malla en forma de tubo que sigue una curva: la cola enroscada en el agua
// y el cuello alzándose. BossAI le avisa de los ataques con Play("Attack" / "Cast" / "Enrage").
// Es solo visual: corre igual en todas las PCs.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SerpentMotion : MonoBehaviour
{
    [Header("Forma")]
    public float bodyLength = 24f;
    public float neckHeight = 4.2f;   // la cabeza queda a la altura de la cámara del jugador
    public float radius = 1.8f;
    public Transform head;
    public Transform[] spikes;   // púas y aletas a lo largo del lomo (siguen el cuerpo)
    float nextSplash;

    const int Rings = 48;
    const int Sides = 14;

    Mesh mesh;
    Vector3[] vertices;
    Vector3[] spine = new Vector3[Rings];

    // Estado de animación
    float reach = 2f, height, sink, jaw;
    float actionTime = -10f;
    string action;
    bool dead, submerged;

    void Awake()
    {
        height = neckHeight;
        mesh = new Mesh { name = "Cuerpo serpiente" };
        mesh.MarkDynamic();
        vertices = new Vector3[Rings * (Sides + 1)];
        var uv = new Vector2[vertices.Length];
        var tris = new int[(Rings - 1) * Sides * 6];
        int t = 0;
        for (int r = 0; r < Rings; r++)
        {
            for (int s = 0; s <= Sides; s++)
            {
                uv[r * (Sides + 1) + s] = new Vector2(s / (float)Sides, r / (float)(Rings - 1) * 8f);
                if (r == Rings - 1 || s == Sides) continue;
                int a = r * (Sides + 1) + s, b = a + Sides + 1;
                tris[t++] = a; tris[t++] = b; tris[t++] = a + 1;
                tris[t++] = a + 1; tris[t++] = b; tris[t++] = b + 1;
            }
        }
        UpdateBody(0f);
        mesh.uv = uv;
        mesh.triangles = tris;
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    public void Play(string trigger)
    {
        if (dead) return;
        action = trigger;
        actionTime = Time.time;
    }

    // Mecánica de la Yacumama: se hunde en la laguna (true) o vuelve a emerger (false).
    public void SetSubmerged(bool value) => submerged = value;

    // Entrada del jefe: empieza ya hundida (sin animación), para luego emerger.
    public void SnapSubmerged() { submerged = true; sink = 9f; }

    public void Die()
    {
        if (dead) return;
        dead = true;
        actionTime = Time.time;
    }

    void Update()
    {
        float since = Time.time - actionTime;
        float targetReach = 2f, targetHeight = neckHeight, targetJaw = 0.15f;

        if (dead || submerged)
        {
            sink = Mathf.MoveTowards(sink, 9f, (dead ? 3f : 6f) * Time.deltaTime);
            targetHeight = 1f;
        }
        // Al emerger vuelve a subir (antes se quedaba hundida para siempre tras la primera vez).
        else sink = Mathf.MoveTowards(sink, 0f, 7f * Time.deltaTime);

        if (dead || submerged) { }
        else if (action == "Attack" && since < 0.9f)
        {
            // Mordida: se recoge y se lanza hacia adelante con la boca abierta.
            float k = since < 0.3f ? -0.5f * (since / 0.3f) : Mathf.Sin((since - 0.3f) / 0.6f * Mathf.PI);
            targetReach = 2f + 8f * Mathf.Max(0f, k);
            targetHeight = neckHeight - 3.5f * Mathf.Max(0f, k);
            targetJaw = 0.8f;
        }
        else if ((action == "Cast" || action == "Enrage") && since < 2.2f)
        {
            // Se alza muy alto y luego golpea el agua.
            targetHeight = since < 1.6f ? neckHeight + 4f : 0.5f;
            targetReach = since < 1.6f ? 0f : 6f;
            targetJaw = 1f;
        }

        float speed = action == "Attack" ? 18f : 6f;
        reach = Mathf.Lerp(reach, targetReach, speed * Time.deltaTime);
        height = Mathf.Lerp(height, targetHeight, speed * Time.deltaTime);
        jaw = Mathf.Lerp(jaw, targetJaw, 10f * Time.deltaTime);

        UpdateBody(Time.time);
    }

    void UpdateBody(float time)
    {
        // Columna: la primera mitad serpentea sobre el agua (detrás), la segunda sube hasta la cabeza.
        for (int i = 0; i < Rings; i++)
        {
            float s = i / (float)(Rings - 1);
            Vector3 p;
            if (s < 0.5f)
            {
                float u = s / 0.5f;
                p = new Vector3(Mathf.Sin(u * 9f + time * 2f) * 2.2f * (1f - u * 0.6f),
                                -0.4f + Mathf.Sin(u * 5f + time) * 0.3f,
                                -(1f - u) * bodyLength * 0.5f);
            }
            else
            {
                float u = (s - 0.5f) / 0.5f;
                float lift = Mathf.SmoothStep(0f, 1f, u);
                p = new Vector3(Mathf.Sin(time * 1.3f + u * 3f) * 0.8f * u,
                                Mathf.Lerp(-0.2f, height, lift) + Mathf.Sin(time * 2f) * 0.25f * u,
                                reach * u * u);
            }
            p.y -= sink;
            spine[i] = p;
        }

        for (int i = 0; i < Rings; i++)
        {
            float s = i / (float)(Rings - 1);
            Vector3 fwd = (spine[Mathf.Min(i + 1, Rings - 1)] - spine[Mathf.Max(i - 1, 0)]).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            if (side.sqrMagnitude < 0.001f) side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(fwd, side);
            // Más gruesa en el centro, delgada en la cola y un poco en el cuello.
            float r = radius * Mathf.Lerp(0.15f, 1f, Mathf.Clamp01(s * 6f)) * Mathf.Lerp(1f, 0.75f, s);
            for (int k = 0; k <= Sides; k++)
            {
                float a = k / (float)Sides * Mathf.PI * 2f;
                vertices[i * (Sides + 1) + k] = spine[i] + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * r;
            }
        }
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Púas del lomo: repartidas por el cuerpo, saliendo hacia arriba e inclinadas hacia atrás.
        if (spikes != null)
            for (int j = 0; j < spikes.Length; j++)
            {
                if (spikes[j] == null) continue;
                int i = Mathf.Clamp(6 + j * (Rings - 12) / Mathf.Max(1, spikes.Length - 1), 1, Rings - 2);
                float s = i / (float)(Rings - 1);
                Vector3 fwd = (spine[i + 1] - spine[i - 1]).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                if (side.sqrMagnitude < 0.001f) side = Vector3.right;
                Vector3 up = Vector3.Cross(fwd, side.normalized);
                float r = radius * Mathf.Lerp(0.15f, 1f, Mathf.Clamp01(s * 6f)) * Mathf.Lerp(1f, 0.75f, s);
                spikes[j].localPosition = spine[i] + up * r * 0.8f;
                spikes[j].localRotation = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(-35f, 0f, 0f);
                spikes[j].localScale = Vector3.one * Mathf.Lerp(0.6f, 1.3f, Mathf.Sin(s * Mathf.PI));
            }

        // Salpicaduras donde el cuerpo toca el agua.
        if (Application.isPlaying && !dead && Time.time > nextSplash && sink < 1f)
        {
            nextSplash = Time.time + 0.25f;
            int i = Random.Range(2, Rings / 2);
            var world = transform.TransformPoint(spine[i]);
            if (Mathf.Abs(world.y) < 1.5f)
                Effects.Burst(new Vector3(world.x, 0.1f, world.z), new Color(0.75f, 0.9f, 0.95f, 0.8f), 8, 3f, 0.35f, 0.6f, 1.2f);
        }

        if (head != null)
        {
            Vector3 tip = spine[Rings - 1];
            Vector3 dir = (tip - spine[Rings - 3]).normalized;
            head.localPosition = tip;
            head.localRotation = Quaternion.LookRotation(Vector3.Slerp(dir, Vector3.forward, 0.6f), Vector3.up);
            var lowerJaw = head.Find("Mandibula");
            if (lowerJaw != null) lowerJaw.localRotation = Quaternion.Euler(jaw * 35f, 0f, 0f);
        }
    }
}
