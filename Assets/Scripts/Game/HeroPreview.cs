using Unity.Netcode;
using UnityEngine;

// Vista 3D del héroe para la pantalla de selección de clase: copia el modelo de la clase
// (Visual/Rol_<Clase> del prefab del jugador) en un escenario escondido, lo ilumina y lo filma
// con su propia cámara a una textura con fondo transparente que NetworkUI dibuja en el menú.
// Se gira 360° arrastrando con el mouse; cuando lo sueltas gira solo y cada tanto ataca (HeroMotion).
public class HeroPreview : MonoBehaviour
{
    const int Layer = 30;   // capa propia: solo esta cámara la ve
    static HeroPreview instance;

    Camera cam;
    RenderTexture rt;
    Transform pivot;
    GameObject model;
    HeroMotion motion;
    int role = -1;
    float lastShown;

    public float Yaw;            // giro actual (grados)
    public static float TestRun;  // pruebas: hace que el héroe "corra" en el sitio
    public static bool TestNoAuto; // pruebas: sin ataques automáticos
    public void TestAction(string a) { if (motion != null) motion.Play(a); }
    public float LastDragAt = -10f;

    public static HeroPreview Get()
    {
        if (instance == null)
        {
            var go = new GameObject("VistaHeroe");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<HeroPreview>();
            instance.Build();
        }
        return instance;
    }

    void Build()
    {
        transform.position = new Vector3(0f, -1000f, 0f);
        rt = new RenderTexture(768, 1024, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "VistaHeroe" };

        cam = new GameObject("Camara").AddComponent<Camera>();
        cam.transform.SetParent(transform, false);
        cam.transform.localPosition = new Vector3(0f, 1.15f, 6.2f);
        cam.transform.LookAt(transform.position + new Vector3(0f, 0.95f, 0f));
        cam.fieldOfView = 22f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.cullingMask = 1 << Layer;
        cam.targetTexture = rt;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 30f;

        // Luz principal cálida (atardecer), luz de contorno azulada y relleno suave.
        AddLight(new Vector3(35f, 210f, 0f), new Color(1f, 0.82f, 0.6f), 0.85f);
        AddLight(new Vector3(20f, 20f, 0f), new Color(0.5f, 0.6f, 1f), 0.7f);
        AddLight(new Vector3(-10f, 150f, 0f), new Color(1f, 0.65f, 0.4f), 0.15f);

        pivot = new GameObject("Giro").transform;
        pivot.SetParent(transform, false);

        // Luz ambiente más tenue solo mientras esta cámara dibuja (el menú es muy claro).
        Camera.onPreRender += c =>
        {
            if (c != cam) return;
            savedAmbient = RenderSettings.ambientLight;
            savedMode = RenderSettings.ambientMode;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.27f, 0.24f);
        };
        Camera.onPostRender += c =>
        {
            if (c != cam) return;
            RenderSettings.ambientMode = savedMode;
            RenderSettings.ambientLight = savedAmbient;
        };
    }

    Color savedAmbient;
    UnityEngine.Rendering.AmbientMode savedMode;

    void AddLight(Vector3 euler, Color color, float intensity)
    {
        var l = new GameObject("Luz").AddComponent<Light>();
        l.transform.SetParent(transform, false);
        l.transform.localRotation = Quaternion.Euler(euler);
        l.type = LightType.Directional;
        l.color = color;
        l.intensity = intensity;
        l.cullingMask = 1 << Layer;
        l.shadows = LightShadows.None;
    }

    // Prepara el modelo de la clase. Devuelve false si esa clase no tiene modelo 3D (se usa la imagen).
    public bool Show(int newRole)
    {
        lastShown = Time.unscaledTime;
        if (newRole != role)
        {
            role = newRole;
            if (model != null) Destroy(model);
            model = null;
            motion = null;
            var prefab = NetworkManager.Singleton != null ? NetworkManager.Singleton.NetworkConfig.PlayerPrefab : null;
            var src = prefab != null ? prefab.transform.Find("Visual/Rol_" + (PlayerRole)role) : null;
            if (src != null)
            {
                model = Instantiate(src.gameObject, pivot);
                model.name = "Heroe";
                model.SetActive(true);
                model.transform.localPosition = src.localPosition;
                model.transform.localRotation = src.localRotation;
                model.transform.localScale = src.localScale;
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                foreach (var a in model.GetComponentsInChildren<Animator>(true)) a.applyRootMotion = false;
                motion = model.GetComponent<HeroMotion>();
                if (motion == null && model.GetComponentInChildren<Animator>() == null) motion = model.AddComponent<HeroMotion>();
                // La luz de HeroMotion queda en la capa propia.
                foreach (var t in pivot.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                foreach (var l in pivot.GetComponentsInChildren<Light>(true)) l.cullingMask = 1 << Layer;
                FitCamera();
                Yaw = -25f;   // de frente, un poco de tres cuartos
            }
        }
        gameObject.SetActive(true);
        return model != null;
    }

    // Encuadra al héroe completo (cada modelo tiene otra altura: penachos, tocados...).
    void FitCamera()
    {
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        float top = b.max.y - transform.position.y, bottom = Mathf.Min(0f, b.min.y - transform.position.y);
        float h = (top - bottom) * 1.12f;   // un poco de margen (y espacio para el ataque)
        float centerY = (top + bottom) / 2f;
        float dist = h / 2f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        // Los pies quedan siempre a la misma altura en la pantalla: bajamos el centro según el margen.
        cam.transform.localPosition = new Vector3(0f, centerY + h * 0.02f, dist);
        cam.transform.LookAt(transform.position + new Vector3(0f, centerY + h * 0.02f, 0f));
    }

    public Texture Texture => rt;

    void Update()
    {
        // Si el menú dejó de pedir la vista (salió de la pantalla de clases), se apaga.
        if (Time.unscaledTime - lastShown > 0.3f) { cam.enabled = false; return; }
        cam.enabled = true;
        if (model == null) return;

        // Quieto en su pose: solo gira cuando el jugador lo arrastra con el mouse.
        pivot.localRotation = Quaternion.Euler(0f, Yaw, 0f);

        // Respiración en bucle (HeroMotion quieto); sin ataques automáticos.
        if (motion != null) motion.Tick(TestRun, false);
    }
}
