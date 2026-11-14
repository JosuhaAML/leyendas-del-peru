using UnityEngine;

// Pantalla completa al abrir el juego (a la resolución del monitor) y F11 para alternar con ventana.
// Las pruebas automáticas (-autotest) usan su propia ventana y no se tocan.
public class ScreenMode : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        var go = new GameObject("PantallaCompleta");
        DontDestroyOnLoad(go);
        go.AddComponent<ScreenMode>();
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autotest") >= 0) return;
        var res = Screen.currentResolution;
        Screen.SetResolution(res.width, res.height, FullScreenMode.FullScreenWindow);
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F11)) return;
        if (Screen.fullScreenMode == FullScreenMode.Windowed)
        {
            var res = Screen.currentResolution;
            Screen.SetResolution(res.width, res.height, FullScreenMode.FullScreenWindow);
        }
        else Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
    }
}
