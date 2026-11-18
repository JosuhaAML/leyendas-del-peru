using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Menú de pausa (Esc) durante los niveles: volumen, calidad gráfica, controles y salir.
// El juego no se detiene (es en línea), pero tu personaje deja de recibir teclas mientras está abierto.
public class PauseMenu : MonoBehaviour
{
    public static bool Open { get; private set; }

    // Para pruebas automáticas.
    public static void SetOpen(bool value) => Open = value;

    void Update()
    {
        string scene = SceneManager.GetActiveScene().name;
        bool inLevel = scene != "Menu" && scene != LevelManager.LobbyScene && !LoadingScreen.Showing &&
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient;
        if (!inLevel) { Open = false; return; }
        if (Input.GetKeyDown(KeyCode.Escape)) Open = !Open;
    }

    void OnGUI()
    {
        if (!Open) return;
        float scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float w = Screen.width / scale;

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0, 0, w, 720), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var area = new Rect(w / 2 - 250, 60, 500, 620);
        UIStyle.Panel(area);
        GUILayout.BeginArea(new Rect(area.x + 30, area.y + 24, area.width - 60, area.height - 48));
        GUILayout.Label("PAUSA", new GUIStyle(UIStyle.Title) { fontSize = 32 });
        GUILayout.Space(10);

        var audio = GameAudio.Instance;
        if (audio != null)
        {
            GUILayout.Label($"Música: {Mathf.RoundToInt(audio.musicVolume * 100)}%", UIStyle.Text);
            audio.musicVolume = GUILayout.HorizontalSlider(audio.musicVolume, 0f, 1f);
            GUILayout.Space(8);
            GUILayout.Label($"Efectos: {Mathf.RoundToInt(audio.effectsVolume * 100)}%", UIStyle.Text);
            audio.effectsVolume = GUILayout.HorizontalSlider(audio.effectsVolume, 0f, 1f);
            GUILayout.Space(8);
            GUILayout.Label($"Narrador: {Mathf.RoundToInt(audio.narratorVolume * 100)}%", UIStyle.Text);
            audio.narratorVolume = GUILayout.HorizontalSlider(audio.narratorVolume, 0f, 1f);
        }

        // Calidad gráfica (Baja / Media / Alta) y contador de FPS.
        GUILayout.Space(10);
        GUILayout.Label($"Gráficos: {Graficos.Nombres[(int)Graficos.Nivel]}", UIStyle.Text);
        var gr = GUILayoutUtility.GetRect(area.width - 60, 72);
        Graficos.DibujarSelector(gr);

        GUILayout.Space(14);
        GUILayout.Label("WASD mover · Clic izquierdo ataque básico · Espacio esquivar · 1 2 3 4 habilidades\nR definitivo · E revivir · Z X C V chat rápido · B saludar · N bailar · M celebrar · Clic derecho girar cámara",
            new GUIStyle(UIStyle.Text) { alignment = TextAnchor.MiddleCenter });
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Continuar", UIStyle.ButtonActive, GUILayout.Height(40))) Open = false;
        GUILayout.Space(6);
        var net = NetworkManager.Singleton;
        if (GUILayout.Button(net.IsServer ? "Volver al menú (todos)" : "Salir de la partida", UIStyle.Button, GUILayout.Height(36)))
        {
            Open = false;
            if (net.IsServer) LevelManager.Instance.ReturnToMenu();
            else { net.Shutdown(); SceneManager.LoadScene(LevelManager.LobbyScene); }
        }
        GUILayout.Space(6);
        if (GUILayout.Button("Salir del juego", UIStyle.Button, GUILayout.Height(36))) Application.Quit();
        GUILayout.EndArea();
    }
}
