using UnityEngine;

// Pantalla completa al abrir el juego (a la resolución del monitor) y F11 para alternar con ventana.
// En calidad gráfica Baja la pantalla completa se dibuja al 75 % de la resolución (mucho más liviano).
// Las pruebas automáticas (-autotest) usan su propia ventana y no se tocan.
public class ScreenMode : MonoBehaviour
{
    static bool autotest;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        var go = new GameObject("PantallaCompleta");
        DontDestroyOnLoad(go);
        go.AddComponent<ScreenMode>();
        autotest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autotest") >= 0;
        if (autotest) return;
        PantallaCompleta();
    }

    static void PantallaCompleta()
    {
        var res = Screen.currentResolution;
        float s = Graficos.EscalaResolucion;
        Screen.SetResolution(Mathf.RoundToInt(res.width * s), Mathf.RoundToInt(res.height * s), FullScreenMode.FullScreenWindow);
    }

    // Al cambiar la calidad gráfica (entra o sale de Baja).
    public static void AplicarResolucion()
    {
        if (autotest || Screen.fullScreenMode == FullScreenMode.Windowed) return;
        PantallaCompleta();
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F11)) return;
        if (Screen.fullScreenMode == FullScreenMode.Windowed) PantallaCompleta();
        else Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
    }
}