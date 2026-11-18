using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;

public enum CalidadGrafica { Baja, Media, Alta }

// Calidad gráfica (Baja / Media / Alta), elegible en Opciones del menú y en la pausa (Esc).
// La primera vez se elige sola según la tarjeta de video (gráficos integrados → Baja).
// Se guarda en esta PC y se aplica al cargar cada escena. También muestra el contador de FPS si se activa.
//
//  Baja : sin sombras, sin postprocesado, luces por vértice, menos partículas, sin niebla baja y resolución al 75 %.
//  Media: sombras duras cercanas, postprocesado sin oclusión ambiental, 1 luz por píxel, partículas a 2/3.
//  Alta : todo al máximo (como estaba antes).
public class Graficos : MonoBehaviour
{
    public static CalidadGrafica Nivel { get; private set; } = CalidadGrafica.Alta;
    public static bool MostrarFps { get; private set; }
    public static readonly string[] Nombres = { "Baja", "Media", "Alta" };

    // Multiplicador para la cantidad de partículas de clima y ambiente.
    public static float Particulas => Nivel == CalidadGrafica.Baja ? 0.35f : Nivel == CalidadGrafica.Media ? 0.65f : 1f;
    public static bool NieblaBaja => Nivel != CalidadGrafica.Baja;
    public static float EscalaResolucion => Nivel == CalidadGrafica.Baja ? 0.75f : 1f;

    static Graficos instance;
    float fpsTimer, fpsShown;
    int fpsFrames;
    float slowSince = -1f, tipUntil;
    float logTimer; int logFrames;
    static bool logFps, sinLimite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        var go = new GameObject("Graficos");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<Graficos>();

        var args = System.Environment.GetCommandLineArgs();
        int q = System.Array.IndexOf(args, "-calidad");   // pruebas: -calidad 0/1/2
        logFps = System.Array.IndexOf(args, "-fpslog") >= 0;
        sinLimite = System.Array.IndexOf(args, "-sinlimite") >= 0;
        if (q >= 0 && q + 1 < args.Length && int.TryParse(args[q + 1], out int forced))
            Nivel = (CalidadGrafica)Mathf.Clamp(forced, 0, 2);
        else if (PlayerPrefs.HasKey("Calidad"))
            Nivel = (CalidadGrafica)Mathf.Clamp(PlayerPrefs.GetInt("Calidad"), 0, 2);
        else
            Nivel = Detectar();
        MostrarFps = PlayerPrefs.GetInt("MostrarFps", 0) == 1;
        Debug.Log($"GRAFICOS: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB) → calidad {Nombres[(int)Nivel]}");

        SceneManager.sceneLoaded += (s, m) => Aplicar();
        Aplicar();
    }

    // Gráficos integrados (Intel, Radeon de procesador) o poca memoria de video → Baja; tarjeta modesta → Media.
    static CalidadGrafica Detectar()
    {
        string gpu = SystemInfo.graphicsDeviceName.ToLowerInvariant();
        int vram = SystemInfo.graphicsMemorySize;
        bool integrada = gpu.Contains("intel") || gpu.Contains("uhd") || gpu.Contains("iris") || gpu.Contains("vega")
                         || gpu.Contains("radeon(tm) graphics") || gpu.Contains("radeon graphics") || gpu.Contains("microsoft basic");
        if (integrada || vram < 2000) return CalidadGrafica.Baja;
        if (vram < 4000) return CalidadGrafica.Media;
        return CalidadGrafica.Alta;
    }

    public static void Cambiar(CalidadGrafica nivel)
    {
        if (nivel == Nivel) return;
        bool cambiaResolucion = (nivel == CalidadGrafica.Baja) != (Nivel == CalidadGrafica.Baja);
        Nivel = nivel;
        PlayerPrefs.SetInt("Calidad", (int)nivel);
        PlayerPrefs.Save();
        Aplicar();
        if (cambiaResolucion) ScreenMode.AplicarResolucion();
        // El clima y la niebla se crean al empezar el nivel: se rehacen con la nueva cantidad.
        foreach (var a in FindObjectsByType<LevelAmbience>()) a.Rehacer();
        foreach (var m in FindObjectsByType<GroundMist>(FindObjectsInactive.Include)) m.gameObject.SetActive(NieblaBaja);
    }

    public static void CambiarFps(bool v)
    {
        MostrarFps = v;
        PlayerPrefs.SetInt("MostrarFps", v ? 1 : 0);
        PlayerPrefs.Save();
    }

    static void Aplicar()
    {
        switch (Nivel)
        {
            case CalidadGrafica.Baja:
                QualitySettings.SetQualityLevel(1, true);
                QualitySettings.shadows = ShadowQuality.Disable;
                QualitySettings.pixelLightCount = 0;
                QualitySettings.antiAliasing = 0;
                QualitySettings.lodBias = 0.5f;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                QualitySettings.globalTextureMipmapLimit = 1;   // texturas a la mitad: menos memoria de video
                QualitySettings.softParticles = false;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
                break;
            case CalidadGrafica.Media:
                QualitySettings.SetQualityLevel(2, true);
                QualitySettings.shadows = ShadowQuality.HardOnly;
                QualitySettings.shadowResolution = ShadowResolution.Medium;
                QualitySettings.shadowDistance = 35f;
                QualitySettings.shadowCascades = 1;
                QualitySettings.pixelLightCount = 1;
                QualitySettings.antiAliasing = 0;
                QualitySettings.lodBias = 1f;
                QualitySettings.globalTextureMipmapLimit = 0;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
                break;
            default:
                QualitySettings.SetQualityLevel(5, true);
                QualitySettings.globalTextureMipmapLimit = 0;
                Application.targetFrameRate = -1;
                break;
        }

        if (sinLimite) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }   // pruebas de rendimiento

        foreach (var cam in FindObjectsByType<Camera>())
        {
            if (cam.targetTexture != null) continue;   // la vista previa del héroe tiene su propia calidad
            cam.allowMSAA = Nivel == CalidadGrafica.Alta;
            cam.allowHDR = Nivel != CalidadGrafica.Baja;
            var layer = cam.GetComponent<PostProcessLayer>();
            if (layer != null)
            {
                layer.enabled = Nivel != CalidadGrafica.Baja;
                layer.antialiasingMode = Nivel == CalidadGrafica.Alta ? PostProcessLayer.Antialiasing.FastApproximateAntialiasing
                                                                      : PostProcessLayer.Antialiasing.None;
            }
        }
        // La oclusión ambiental es lo más caro del postprocesado: solo en Alta.
        foreach (var vol in FindObjectsByType<PostProcessVolume>())
        {
            if (vol.sharedProfile == null) continue;
            if (vol.profile.TryGetSettings(out AmbientOcclusion ao)) ao.active = Nivel == CalidadGrafica.Alta;
            if (vol.profile.TryGetSettings(out Bloom bloom)) bloom.fastMode.Override(Nivel != CalidadGrafica.Alta);
        }
        // Luces que no son el sol: en Baja y Media no hacen sombra (las sombras de luces puntuales son carísimas).
        foreach (var l in FindObjectsByType<Light>())
            if (l.type != LightType.Directional && Nivel != CalidadGrafica.Alta) l.shadows = LightShadows.None;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        fpsFrames++; fpsTimer += dt;
        if (fpsTimer >= 0.5f) { fpsShown = fpsFrames / fpsTimer; fpsFrames = 0; fpsTimer = 0f; }

        // Si va muy lento durante un rato en un nivel, se sugiere bajar los gráficos (una vez por sesión).
        bool enNivel = SceneManager.GetActiveScene().name.StartsWith("Nivel");
        if (enNivel && Nivel != CalidadGrafica.Baja && fpsShown > 0f && fpsShown < 25f && tipUntil == 0f)
        {
            if (slowSince < 0f) slowSince = Time.unscaledTime;
            else if (Time.unscaledTime - slowSince > 8f) tipUntil = Time.unscaledTime + 9f;
        }
        else slowSince = -1f;

        if (logFps && enNivel)
        {
            logFrames++; logTimer += dt;
            if (logTimer >= 10f) { Debug.Log($"FPS promedio ({Nombres[(int)Nivel]}, {SceneManager.GetActiveScene().name}): {logFrames / logTimer:0.0}"); logFrames = 0; logTimer = 0f; }
        }
    }

    void OnGUI()
    {
        GUI.depth = -100;
        float scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float w = Screen.width / scale;
        if (MostrarFps)
        {
            var c = fpsShown >= 50f ? new Color(0.5f, 1f, 0.5f) : fpsShown >= 30f ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.45f, 0.4f);
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight, normal = { textColor = c } };
            GUI.Label(new Rect(w - 130, 2, 120, 22), $"{fpsShown:0} FPS", st);
        }
        if (Time.unscaledTime < tipUntil)
        {
            var r = new Rect(w / 2 - 300, 70, 600, 40);
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(r, "¿El juego va lento? Pulsa Esc y baja los Gráficos a «Baja».",
                new GUIStyle(UIStyle.Text) { fontSize = 17, alignment = TextAnchor.MiddleCenter });
        }
    }

    // Fila de 3 botones (Baja / Media / Alta) y la casilla de FPS; la usan el menú y la pausa.
    public static void DibujarSelector(Rect r)
    {
        float bw = (r.width - 16) / 3f;
        for (int i = 0; i < 3; i++)
            if (GUI.Button(new Rect(r.x + i * (bw + 8), r.y, bw, 34), Nombres[i], new GUIStyle(i == (int)Nivel ? UIStyle.ButtonActive : UIStyle.Button) { fontSize = 16, padding = new RectOffset(4, 4, 2, 2) }))
                Cambiar((CalidadGrafica)i);
        bool fps = GUI.Toggle(new Rect(r.x, r.y + 44, 24, 22), MostrarFps, GUIContent.none);
        if (fps != MostrarFps) CambiarFps(fps);
        GUI.Label(new Rect(r.x + 28, r.y + 40, r.width - 28, 28), "Mostrar FPS", new GUIStyle(UIStyle.Text) { fontSize = 16, alignment = TextAnchor.MiddleLeft });
    }
}
