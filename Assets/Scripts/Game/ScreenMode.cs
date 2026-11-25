using UnityEngine;

// Pantalla completa al abrir el juego (a la resolución del monitor) y F11 para alternar con ventana.
// En calidad gráfica Baja, dentro de los niveles, la pantalla completa se dibuja al 75 % de la resolución del monitor.
// Las pruebas automáticas (-autotest) usan su propia ventana y no se tocan.
public class ScreenMode : MonoBehaviour
{
    static readonly bool autotest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autotest") >= 0
        && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pantallareal") < 0;   // -pantallareal: probar la pantalla completa

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        var go = new GameObject("PantallaCompleta");
        DontDestroyOnLoad(go);
        go.AddComponent<ScreenMode>();
        if (autotest) return;
        PantallaCompleta();
    }

    // Resolución REAL del monitor. Ojo: Screen.currentResolution en pantalla completa devuelve la resolución
    // actual del juego, así que si ya estaba al 75 % cada cambio la achicaba un poco más.
    static int NativoAncho => Display.main.systemWidth > 0 ? Display.main.systemWidth : Screen.currentResolution.width;
    static int NativoAlto => Display.main.systemHeight > 0 ? Display.main.systemHeight : Screen.currentResolution.height;

    static void PantallaCompleta()
    {
        float s = Graficos.EscalaResolucion;
        int w = Mathf.RoundToInt(NativoAncho * s), h = Mathf.RoundToInt(NativoAlto * s);
        // Solo si cambia: evita un parpadeo de pantalla innecesario al cargar cada escena.
        if (Screen.fullScreenMode == FullScreenMode.FullScreenWindow && Screen.width == w && Screen.height == h) return;
        Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
    }

    // Al cambiar la calidad gráfica o al entrar/salir de un nivel.
    public static void AplicarResolucion()
    {
        if (autotest || Screen.fullScreenMode == FullScreenMode.Windowed) return;
        PantallaCompleta();
    }

    // Para pruebas automáticas: resolución que pondría la pantalla completa ahora mismo.
    public static Vector2Int Objetivo() => new Vector2Int(Mathf.RoundToInt(NativoAncho * Graficos.EscalaResolucion), Mathf.RoundToInt(NativoAlto * Graficos.EscalaResolucion));

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F11)) return;
        if (Screen.fullScreenMode == FullScreenMode.Windowed) PantallaCompleta();
        else Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
    }
}