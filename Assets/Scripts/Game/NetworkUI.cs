using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

// Menú principal (OnGUI) con dos pantallas:
// 1) Lobby: fondo ilustrado de los Andes, título, botón grande de jugar, conexión, "Elige el jefe"
//    (los niveles se abren uno por uno) y el botón "Seleccionar clase".
// 2) Selección de clase: lista de héroes a la izquierda, el héroe en grande y animado en el centro
//    (al pasar el mouse por otro se previsualiza), y a la derecha sus estadísticas y habilidades.
// En línea: Unity Relay (el anfitrión recibe un código). Red local: misma Wi-Fi o la misma PC.
// Se muestra en la escena "Menu" y en "Sala" (cuando el grupo vuelve al menú sin desconectarse).
public class NetworkUI : MonoBehaviour
{
    const int MaxPlayers = 5;
    public const ushort LanPort = 7777;

    [SerializeField] Texture2D[] fondos;   // retratos de los jefes (también los usan la carga y la victoria)
    public Texture2D[] Retratos => fondos;

    static readonly string[] BossNames = { "Chullachaqui", "Yacumama", "Bufeo Colorado", "Tunche", "Pishtaco" };

    string joinCode = "";
    string lanIp = "127.0.0.1";
    string status = "";
    string sessionCode;
    bool working;
    int selectedLevel = -1;

    // Pantalla de clases.
    public static bool ShowClasses;   // público para las pruebas automáticas
    int shownRole = -1;
    float roleChangedAt;

    static NetworkManager Net => NetworkManager.Singleton;

    void Start()
    {
        // Si se cae la conexión (o el anfitrión cierra) en medio de un nivel, vuelve al menú.
        Net.OnClientStopped += _ => ReturnToLobbyScene();
        Net.OnServerStopped += _ => ReturnToLobbyScene();
    }

    static void ReturnToLobbyScene()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (scene != "Menu" && scene != LevelManager.LobbyScene) SceneManager.LoadScene(LevelManager.LobbyScene);
    }

    public static bool InMenu
    {
        get { string s = SceneManager.GetActiveScene().name; return s == "Menu" || s == LevelManager.LobbyScene; }
    }

    void Update()
    {
        if (!InMenu) return;
        // Atajos para la presentación: F9 abre todos los niveles; Shift+F9 borra el progreso.
        if (Input.GetKeyDown(KeyCode.F9))
        {
            if (Input.GetKey(KeyCode.LeftShift)) Progress.Reset(); else Progress.UnlockAll();
            selectedLevel = -1;
        }
        if (ShowClasses && Input.GetKeyDown(KeyCode.Escape)) ShowClasses = false;
    }

    void OnGUI()
    {
        if (!InMenu || LoadingScreen.Showing) return;
        if (selectedLevel < 0 || !Progress.IsUnlocked(selectedLevel)) selectedLevel = Progress.NextLevel;

        GUI.matrix = Matrix4x4.Scale(Vector3.one * Screen.height / 720f);
        float w = Screen.width * 720f / Screen.height;
        if (ShowClasses) DrawClassScreen(w);
        else DrawLobby(w);
    }

    // ================================================================ LOBBY
    void DrawLobby(float w)
    {
        Background(w, "fondo_lobby", 0.12f);
        Embers(w, 0f, 0.5f, new Color(1f, 0.7f, 0.3f));
        // Pantallas a pantalla completa encima del menú (bloquean los botones de abajo).
        if (Creditos.Open) { Creditos.Draw(w); return; }
        if (Bestiario.Open) { Bestiario.Draw(w, Retratos); return; }
        float cx = w / 2;

        // Emblema, título y lema.
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 1.5f);
        GUI.color = new Color(1, 1, 1, pulse);
        GUI.DrawTexture(new Rect(cx - 34, 0, 68, 68), UIStyle.Tex("emblema_sol"));
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(cx - 300, 30, 600, 197), UIStyle.Tex("titulo2"));
        GUI.Label(new Rect(0, 214, w, 32), "Cinco mitos. Un solo equipo.",
            new GUIStyle(UIStyle.Text) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic, fontSize = 21 });
        UIStyle.Separator(new Rect(cx - 220, 244, 440, 24));

        DrawLobbyCenter(cx);
        DrawBossPicker(new Rect(40, 524, 470, 156));
        DrawClassButton(new Rect(w - 500, 560, 460, 140));
        DrawNameField(new Rect(w - 500, 494, 460, 52));

        // Compañeros bot al jugar solo (se guarda en la PC).
        var botRect = new Rect(40, 476, 470, 36);
        PillBox(botRect);
        bool bots = GUI.Toggle(new Rect(botRect.x + 16, botRect.y + 8, 24, 22), BotAlly.Enabled, GUIContent.none);
        if (bots != BotAlly.Enabled) BotAlly.Enabled = bots;
        GUI.Label(new Rect(botRect.x + 44, botRect.y, botRect.width - 54, botRect.height), "Compañeros bot al jugar solo (Amaru, Killa...)",
            new GUIStyle(UIStyle.Text) { fontSize = 16, alignment = TextAnchor.MiddleLeft });

        // Logros (arriba a la derecha).
        if (UIStyle.IconButton(new Rect(w - 250, 18, 220, 46), "sol", $"Logros {Achievements.UnlockedCount}/{Achievements.All.Length}", UIStyle.Button, 18))
            { showAchievements = !showAchievements; showSound = false; }
        // Bestiario y créditos (arriba a la izquierda).
        if (UIStyle.IconButton(new Rect(30, 18, 200, 46), "chakana", "Bestiario", UIStyle.Button, 18)) { Bestiario.Open = true; showAchievements = false; }
        if (UIStyle.IconButton(new Rect(30, 72, 200, 40), "sol", "Créditos", UIStyle.Button, 16)) { Creditos.Show(); showAchievements = false; }
        if (UIStyle.IconButton(new Rect(30, 120, 200, 40), "grito", "Opciones", UIStyle.Button, 16)) { showSound = !showSound; showAchievements = false; }
        if (showAchievements) DrawAchievements(w);
        if (showSound) DrawSound(w);

        // Pie con los controles.
        UIStyle.Separator(new Rect(cx - 470, 692, 120, 20));
        UIStyle.Separator(new Rect(cx + 350, 692, 120, 20));
        GUI.Label(new Rect(0, 690, w, 24), "WASD mover  ·  Clic ataque básico  ·  Espacio esquivar  ·  1 2 3 4 habilidades  ·  R definitivo  ·  E revivir  ·  Z X C V chat  ·  B N M gestos  ·  Esc pausa",
            new GUIStyle(UIStyle.Small) { fontSize = 13, normal = { textColor = UIStyle.Cream } });
        if (!string.IsNullOrEmpty(status))
            GUI.Label(new Rect(cx - 300, 508, 600, 22), status, new GUIStyle(UIStyle.Small) { fontSize = 14, wordWrap = true });
        // Aviso si el juego te cambió de clase porque la tuya ya estaba tomada.
        if (PlayerAbilities.RoleNotice != null && Time.time - PlayerAbilities.RoleNoticeAt < 6f)
        {
            var nr = new Rect(cx - 340, 244, 680, 36);   // arriba, entre el título y la partida (no tapa nada)
            GUI.Box(nr, GUIContent.none, UIStyle.ButtonActive);
            GUI.Label(nr, PlayerAbilities.RoleNotice, new GUIStyle(UIStyle.Text) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UIStyle.DarkText } });
        }
    }

    void DrawLobbyCenter(float cx)
    {
        var field = new GUIStyle(GUI.skin.textField) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
        string levelLabel = $"Nivel {selectedLevel + 1}: {BossNames[selectedLevel]}";

        if (!Net.IsClient && !Net.IsServer)
        {
            GUI.enabled = !working;
            if (UIStyle.IconButton(new Rect(cx - 320, 282, 640, 62), "montana", "Crear partida en línea", UIStyle.ButtonActive, 26)) HostOnline();
            var row = new Rect(cx - 280, 356, 560, 96);
            PillBox(row);
            joinCode = GUI.TextField(new Rect(row.x + 20, row.y + 12, 130, 32), joinCode.ToUpper(), 8, field);
            if (GUI.Button(new Rect(row.x + 158, row.y + 10, row.width - 178, 36), "Unirse con código", UIStyle.Button)) JoinOnline(joinCode.Trim());
            lanIp = GUI.TextField(new Rect(row.x + 20, row.y + 54, 130, 32), lanIp, field);
            if (GUI.Button(new Rect(row.x + 158, row.y + 52, 170, 36), "Unirse por IP", UIStyle.Button)) JoinLan(lanIp.Trim());
            if (GUI.Button(new Rect(row.x + 336, row.y + 52, row.width - 356, 36), "Crear red local", UIStyle.Button)) HostLan();
            if (UIStyle.IconButton(new Rect(cx - 160, 462, 320, 42), "salir", "Salir del juego", UIStyle.ButtonDanger, 17)) Application.Quit();
            GUI.enabled = true;
            return;
        }

        if (Net.IsServer)
        {
            // No se puede empezar si alguien aún no eligió su clase.
            var missing = new System.Collections.Generic.List<string>();
            foreach (var p in FindObjectsByType<PlayerAbilities>())
                if (p.IsSpawned && !p.HasChosen.Value) missing.Add(p.IsOwner ? "tú" : p.DisplayName);
            GUI.enabled = missing.Count == 0;
            if (UIStyle.IconButton(new Rect(cx - 320, 282, 640, 62), "montana", "Jugar  ·  " + levelLabel, UIStyle.ButtonActive, 26))
            {
                ShowClasses = false;
                LevelManager.Instance.StartAt(selectedLevel);
            }
            GUI.enabled = true;
            if (missing.Count > 0)
            {
                var mr = new Rect(cx - 340, 244, 680, 36);
                GUI.Box(mr, GUIContent.none, UIStyle.ButtonDanger);
                GUI.Label(mr, "Falta elegir clase: " + string.Join(", ", missing) + "  (botón «Clase» abajo a la derecha)",
                    new GUIStyle(UIStyle.Text) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
            }
        }
        else
        {
            PillBox(new Rect(cx - 320, 284, 640, 58));
            GUI.Label(new Rect(cx - 320, 284, 640, 58), "Conectado · esperando a que el anfitrión empiece...",
                new GUIStyle(UIStyle.Text) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Italic });
        }

        // Datos de la partida.
        var info = new Rect(cx - 280, 356, 560, 46);
        PillBox(info);
        var it = new GUIStyle(UIStyle.Text) { fontSize = 17, alignment = TextAnchor.MiddleLeft, richText = true };
        GUI.DrawTexture(new Rect(info.x + 30, info.y + 11, 28, 24), UIStyle.Tex("icono_mando"));
        GUI.Label(new Rect(info.x + 66, info.y, 200, 46),
            string.IsNullOrEmpty(sessionCode) ? "Partida local" : $"Código: <color=#ffffff><b>{sessionCode}</b></color>", it);
        GUI.color = new Color(0.67f, 0.47f, 0.2f);
        GUI.DrawTexture(new Rect(info.center.x + 10, info.y + 10, 1, 26), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(info.center.x + 40, info.y + 10, 30, 26), UIStyle.Tex("icono_jugadores"));
        GUI.Label(new Rect(info.center.x + 80, info.y, 200, 46), $"Jugadores {(Net.IsServer ? Net.ConnectedClientsList.Count.ToString() : "?")}/{MaxPlayers}", it);

        GUI.DrawTexture(new Rect(cx - 150, 414, 24, 24), UIStyle.Tex("icono_escudo"));
        GUI.Label(new Rect(cx - 120, 408, 440, 36), TeamLine(), new GUIStyle(UIStyle.Text) { fontSize = 17, richText = true, alignment = TextAnchor.MiddleLeft });

        if (UIStyle.IconButton(new Rect(cx - 220, 456, 440, 46), "salir", "Salir de la partida", UIStyle.ButtonDanger, 19)) Leave();
    }

    bool showAchievements, showSound;
    public void TestSound(bool v) => showSound = v;

    // Panel de opciones: música, efectos, voz del narrador y calidad gráfica (se guarda en esta PC).
    void DrawSound(float w)
    {
        var audio = GameAudio.Instance;
        if (audio == null) return;
        var r = new Rect(w - 312, 76, 288, 412);   // a la derecha, donde no hay botones debajo (el de Jugar termina antes)
        UIStyle.Panel(r);
        GUI.Label(new Rect(r.x, r.y + 14, r.width, 34), "OPCIONES", new GUIStyle(UIStyle.Title) { fontSize = 24 });
        var label = new GUIStyle(UIStyle.Text) { fontSize = 17, alignment = TextAnchor.MiddleLeft };
        float y = r.y + 62;
        audio.musicVolume = VolumeRow(new Rect(r.x + 24, y, r.width - 48, 50), "Música", audio.musicVolume, label); y += 58;
        audio.effectsVolume = VolumeRow(new Rect(r.x + 24, y, r.width - 48, 50), "Efectos", audio.effectsVolume, label); y += 58;
        audio.narratorVolume = VolumeRow(new Rect(r.x + 24, y, r.width - 48, 50), "Narrador", audio.narratorVolume, label); y += 58;
        GUI.Label(new Rect(r.x + 24, y, r.width - 48, 24), "Gráficos", label);
        GUI.Label(new Rect(r.x + 24, y + 104, r.width - 48, 22), "Si el juego va lento, usa Baja", new GUIStyle(UIStyle.Small) { fontSize = 13, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleLeft });
        Graficos.DibujarSelector(new Rect(r.x + 24, y + 30, r.width - 48, 70));
        if (GUI.Button(new Rect(r.center.x - 80, r.yMax - 46, 160, 34), "Cerrar", UIStyle.Button)) showSound = false;
    }

    static float VolumeRow(Rect r, string name, float value, GUIStyle label)
    {
        GUI.Label(new Rect(r.x, r.y, 160, 24), name, label);
        GUI.Label(new Rect(r.xMax - 60, r.y, 60, 24), Mathf.RoundToInt(value * 100) + "%", new GUIStyle(label) { alignment = TextAnchor.MiddleRight });
        return GUI.HorizontalSlider(new Rect(r.x, r.y + 30, r.width, 18), value, 0f, 1f);
    }

    void DrawAchievements(float w)
    {
        var r = new Rect(w - 560, 76, 530, 470);
        UIStyle.Panel(r);
        GUI.Label(new Rect(r.x, r.y + 14, r.width, 34), "LOGROS", new GUIStyle(UIStyle.Title) { fontSize = 26 });
        UIStyle.Separator(new Rect(r.x + 90, r.y + 48, r.width - 180, 16));
        float y = r.y + 70;
        foreach (var a in Achievements.All)
        {
            bool has = Achievements.Has(a.id);
            GUI.color = has ? Color.white : new Color(1, 1, 1, 0.35f);
            GUI.DrawTexture(new Rect(r.x + 28, y + 4, 30, 30), UIStyle.Tex(has ? "emblema_sol" : "candado"));
            GUI.Label(new Rect(r.x + 70, y, 420, 22), a.name,
                new GUIStyle(UIStyle.Text) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = has ? UIStyle.Gold : UIStyle.Cream } });
            GUI.Label(new Rect(r.x + 70, y + 19, 430, 20), a.desc, new GUIStyle(UIStyle.Small) { fontSize = 13, alignment = TextAnchor.UpperLeft });
            y += 39;
        }
        GUI.color = Color.white;
    }

    // "Tu nombre": se guarda en la PC y, si ya estás conectado, lo ven todos al instante.
    string nameDraft;

    void DrawNameField(Rect r)
    {
        nameDraft ??= PlayerAbilities.PreferredName;
        PillBox(r);
        GUI.Label(new Rect(r.x + 18, r.y, 130, r.height), "Tu nombre",
            new GUIStyle(UIStyle.Text) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UIStyle.Gold } });
        var field = new GUIStyle(GUI.skin.textField) { fontSize = 18, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 4, 4) };
        string edited = GUI.TextField(new Rect(r.x + 140, r.y + 9, r.width - 158, r.height - 18), nameDraft, 16, field);
        if (edited != nameDraft)
        {
            nameDraft = edited;
            PlayerAbilities.PreferredName = edited.Trim();
            LocalAbilities()?.SetName(edited);
        }
    }

    static void PillBox(Rect r)
    {
        GUI.color = new Color(0.05f, 0.03f, 0.02f, 0.72f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(0.67f, 0.47f, 0.2f, 0.8f);
        var t = Texture2D.whiteTexture;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), t);
        GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), t);
        GUI.DrawTexture(new Rect(r.x, r.y, 1, r.height), t);
        GUI.DrawTexture(new Rect(r.xMax - 1, r.y, 1, r.height), t);
        GUI.color = Color.white;
    }

    // Panel "Elige el jefe" con los 5 medallones.
    void DrawBossPicker(Rect r)
    {
        UIStyle.Panel(r);
        var ribbon = new Rect(r.center.x - 120, r.y - 18, 240, 38);
        GUI.Box(ribbon, GUIContent.none, UIStyle.Button);
        GUI.Label(ribbon, "ELIGE EL JEFE", new GUIStyle(UIStyle.Title) { fontSize = 19 });

        for (int i = 0; i < 5; i++)
        {
            bool locked = !Progress.IsUnlocked(i), sel = i == selectedLevel;
            float size = sel ? 78f : 66f;
            float cx = r.x + 56 + i * 90, cy = r.y + 72;
            if (sel)
            {
                float g = size * 1.9f * (1f + Mathf.Sin(Time.time * 2.5f) * 0.06f);
                GUI.color = new Color(1, 1, 1, 0.75f);
                GUI.DrawTexture(new Rect(cx - g / 2, cy - g / 2, g, g), UIStyle.Tex("brillo"));
                GUI.color = Color.white;
            }
            float m = size * 1.2f;
            GUI.DrawTexture(new Rect(cx - m / 2, cy - m / 2, m, m), UIStyle.Tex(locked ? $"medallon_{i}_gris" : $"medallon_{i}"));
            if (locked) GUI.DrawTexture(new Rect(cx - 17, cy - 19, 34, 34), UIStyle.Tex("candado"));
            else if (Progress.IsBeaten(i)) GUI.DrawTexture(new Rect(cx + size / 2 - 18, cy + size / 2 - 22, 24, 24), UIStyle.Tex("chakana"));
            if (!locked && GUI.Button(new Rect(cx - size / 2, cy - size / 2, size, size), GUIContent.none, GUIStyle.none))
            {
                selectedLevel = i;
                GameAudio.Play("magia");
            }
        }
        string state = Progress.IsBeaten(selectedLevel) ? "vencido · jugar de nuevo" : "¡siguiente batalla!";
        GUI.Label(new Rect(r.x, r.yMax - 46, r.width, 28), $"Nivel {selectedLevel + 1} de 5 · <b>{BossNames[selectedLevel]}</b> — <i>{state}</i>",
            new GUIStyle(UIStyle.Text) { fontSize = 17, richText = true, alignment = TextAnchor.MiddleCenter });
    }

    void DrawClassButton(Rect r)
    {
        int role = CurrentRole();
        var name = role >= 0 ? PlayerAbilities.RoleName((PlayerRole)role) : "Seleccionar clase";
        var btn = new Rect(r.x, r.y, r.width, 70);
        if (GUI.Button(btn, GUIContent.none, UIStyle.ButtonActive)) { ShowClasses = true; GameAudio.Play("golpe"); }
        if (role >= 0)
        {
            GUI.DrawTexture(new Rect(btn.x + 34, btn.y + 9, 52, 52), UIStyle.Tex("cara_" + (PlayerRole)role), ScaleMode.ScaleAndCrop);
        }
        else GUI.DrawTexture(new Rect(btn.x + 42, btn.y + 15, 40, 40), UIStyle.Tex("icono_escudo"));
        GUI.Label(new Rect(btn.x + 96, btn.y, btn.width - 150, 70), role >= 0 ? $"Clase: {name}" : name,
            new GUIStyle(UIStyle.Title) { fontSize = 26, alignment = TextAnchor.MiddleLeft, normal = { textColor = UIStyle.DarkText } });
        GUI.color = UIStyle.DarkText;
        GUI.DrawTexture(new Rect(btn.xMax - 62, btn.y + 21, 28, 28), UIStyle.Tex("icono_siguiente"));
        GUI.color = Color.white;
        GUI.Label(new Rect(r.x, r.y + 80, r.width, 50), "Elige tu clase y mira a tu héroe\nen una pantalla dedicada.",
            new GUIStyle(UIStyle.Text) { fontSize = 16, fontStyle = FontStyle.Italic, alignment = TextAnchor.UpperCenter });
    }

    // ================================================================ SELECCIÓN DE CLASE
    static readonly string[] RoleIcon = { "maza", "escudo", "flecha", "rayo", "cura" };

    static readonly string[] RoleDesc =
    {
        "Golpea con su maza de bronce en el cuerpo a cuerpo. Mucho daño y buena vida.",
        "Defensor del equipo: provoca al jefe para que lo ataque a él y aguanta los golpes.",
        "Ataca desde lejos con su arco de chonta. Debe mantener la distancia y moverse.",
        "Domina la magia de la ayahuasca: rayos y fuego, y algo de curación.",
        "Cura al aliado más herido con la luz del Inti. ¡El equipo depende de él!",
    };

    // Salud, Daño, Defensa, Curación (de 0 a 6).
    static readonly int[,] RoleStats =
    {
        { 4, 6, 3, 0 },
        { 6, 2, 6, 2 },
        { 3, 5, 1, 0 },
        { 3, 4, 2, 3 },
        { 3, 1, 2, 6 },
    };
    static readonly string[] StatNames = { "Salud", "Daño", "Defensa", "Curación" };
    static readonly string[] StatIcons = { "corazon", "espadas", "escudo", "cura" };

    int CurrentRole()
    {
        var local = LocalAbilities();
        return local != null ? (int)local.Role : PlayerAbilities.PreferredRole;
    }

    static PlayerAbilities LocalAbilities() => Net.IsClient && Net.LocalClient?.PlayerObject != null
        ? Net.LocalClient.PlayerObject.GetComponent<PlayerAbilities>() : null;

    void ChooseRole(int i)
    {
        var local = LocalAbilities();
        if (local != null && !local.ChooseRole(i)) return;   // ya la tiene otro jugador
        PlayerAbilities.PreferredRole = i;
        GameAudio.Play("golpe");
    }

    void DrawClassScreen(float w)
    {
        Background(w, "fondo_clases", 0.08f);
        int current = CurrentRole();
        int hovered = -1;
        bool repaint = Event.current.type == EventType.Repaint;

        // Logo arriba a la izquierda.
        GUI.DrawTexture(new Rect(40, 6, 300, 136), UIStyle.Tex("logo"));

        // Lista de clases a la izquierda.
        for (int i = 0; i < 5; i++)
        {
            var role = (PlayerRole)i;
            var r = new Rect(30, 160 + i * 86, 360, 76);
            bool sel = i == current;
            bool hover = r.Contains(Event.current.mousePosition);
            if (hover) hovered = i;
            if (sel)
            {
                GUI.color = new Color(1, 1, 1, 0.55f);
                GUI.DrawTexture(new Rect(r.x - 30, r.y - 40, r.width + 60, r.height + 80), UIStyle.Tex("brillo"));
                GUI.color = Color.white;
            }
            // Clase tomada por otro jugador: gris y sin poder elegirla.
            var localP = LocalAbilities();
            var takenBy = localP != null ? PlayerAbilities.TakenBy(i, localP) : null;
            if (takenBy != null) GUI.color = new Color(1, 1, 1, 0.45f);
            if (GUI.Button(r, GUIContent.none, sel ? UIStyle.ButtonActive : UIStyle.Button) && takenBy == null) ChooseRole(i);
            GUI.DrawTexture(new Rect(r.x + 30, r.y + 8, 60, 60), UIStyle.Tex("cara_" + role), ScaleMode.ScaleAndCrop);
            var ink = sel ? UIStyle.DarkText : UIStyle.Gold;
            GUI.color = takenBy != null ? new Color(ink.r, ink.g, ink.b, 0.45f) : ink;
            GUI.DrawTexture(new Rect(r.x + 104, r.y + 22, 32, 32), UIStyle.Icon(RoleIcon[i]));
            GUI.color = takenBy != null ? new Color(1, 1, 1, 0.5f) : Color.white;
            GUI.Label(new Rect(r.x + 148, r.y - (takenBy != null ? 10 : 0), 200, r.height), PlayerAbilities.RoleName(role),
                new GUIStyle(UIStyle.Title) { fontSize = 24, alignment = TextAnchor.MiddleLeft, normal = { textColor = sel ? UIStyle.DarkText : UIStyle.Cream } });
            GUI.color = Color.white;
            if (takenBy != null)
                GUI.Label(new Rect(r.x + 148, r.y + 40, 210, 24), "Tomado por " + takenBy.DisplayName,
                    new GUIStyle(UIStyle.Small) { fontSize = 14, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1f, 0.6f, 0.5f) } });
        }

        // El mouse solo se lee en el repintado (en otros eventos su posición no es fiable).
        if (repaint)
        {
            int show = hovered >= 0 ? hovered : Mathf.Max(0, current);
            if (show != shownRole) { shownRole = show; roleChangedAt = Time.time; }
        }
        if (shownRole < 0) return;
        int shown = shownRole;
        DrawHero(w, shown);
        DrawClassInfo(w, shown);

        // Volver / Continuar.
        if (UIStyle.IconButton(new Rect(40, 640, 200, 48), "atras", "Volver", UIStyle.Button, 19)) ShowClasses = false;
        if (current < 0) GUI.enabled = false;
        if (GUI.Button(new Rect(w - 380, 620, 340, 70), "Continuar", new GUIStyle(UIStyle.ButtonActive) { fontSize = 32 }))
            ShowClasses = false;
        GUI.enabled = true;
        GUI.color = UIStyle.DarkText;
        GUI.DrawTexture(new Rect(w - 108, 640, 30, 30), UIStyle.Tex("icono_siguiente"));
        GUI.color = Color.white;
    }

    // El héroe en grande: entra con un salto, respira, se balancea y cada pocos segundos hace un amago de ataque.
    bool dragging;

    void DrawHero(float w, int role)
    {
        float t = Time.time - roleChangedAt;
        float enter = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.45f), 3f);   // ease-out
        float cx = w / 2 - 20;
        float baseY = 668f;

        // Aura del color de la clase y brillo en el suelo.
        var col = PlayerAbilities.RoleColor((PlayerRole)role);
        GUI.color = new Color(col.r, col.g, col.b, 0.45f * enter);
        GUI.DrawTexture(new Rect(cx - 330, baseY - 560, 660, 620), UIStyle.Tex("brillo"));
        GUI.color = new Color(1f, 0.8f, 0.45f, 0.5f * enter);
        GUI.DrawTexture(new Rect(cx - 220, baseY - 40, 440, 80), UIStyle.Tex("brillo"));
        GUI.color = Color.white;

        // Modelo 3D que se gira arrastrando con el mouse (360°).
        var preview = HeroPreview.Get();
        if (preview.Show(role))
        {
            var area = new Rect(cx - 240, 90, 480, 600);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && area.Contains(e.mousePosition)) { dragging = true; e.Use(); }
            else if (e.type == EventType.MouseUp) dragging = false;
            else if (e.type == EventType.MouseDrag && dragging)
            {
                preview.Yaw -= e.delta.x * 0.7f;
                preview.LastDragAt = Time.time;
                e.Use();
            }
            float h3 = 640f * (0.85f + 0.15f * enter);
            float w3 = h3 * 0.75f;
            GUI.color = new Color(1, 1, 1, enter);
            GUI.DrawTexture(new Rect(cx - w3 / 2, baseY + 30 - h3 + (1f - enter) * 60f, w3, h3), preview.Texture);
            GUI.color = Color.white;

            // Indicación para girar.
            var hint = new Rect(cx - 150, baseY + 18, 300, 30);
            GUI.color = new Color(1, 1, 1, 0.55f + 0.25f * Mathf.Sin(Time.time * 2f));
            GUI.DrawTexture(new Rect(hint.x + 20, hint.y + 6, 18, 18), UIStyle.Tex("icono_atras"));
            GUI.DrawTextureWithTexCoords(new Rect(hint.xMax - 38, hint.y + 6, 18, 18), UIStyle.Tex("icono_atras"), new Rect(1, 0, -1, 1));
            GUI.Label(hint, "Arrastra para girar 360°", new GUIStyle(UIStyle.Small) { fontSize = 15, fontStyle = FontStyle.Italic });
            GUI.color = Color.white;
            Embers(w, 0.3f, 0.7f, new Color(1f, 0.6f, 0.25f));
            return;
        }

        // Clase sin modelo 3D todavía: su ilustración animada.
        var tex = UIStyle.Tex("heroe_" + (PlayerRole)role);
        if (tex == null) return;
        float h = 560f;
        float hw = h * tex.width / tex.height;

        // Animación: respiración + balanceo + amago de ataque cada 3,5 s.
        float breathe = Mathf.Sin(Time.time * 2.2f) * 0.012f;
        float sway = Mathf.Sin(Time.time * 0.9f) * 1.6f;
        float cycle = Mathf.Repeat(Time.time - roleChangedAt, 3.5f);
        float strike = cycle > 2.8f ? Mathf.Sin((cycle - 2.8f) / 0.7f * Mathf.PI) : 0f;
        float scale = (0.85f + 0.15f * enter) * (1f + breathe + strike * 0.05f);
        float lean = sway + strike * -5f;
        float rise = (1f - enter) * 80f - strike * 10f;

        var size = new Vector2(hw * scale, h * scale);
        var rect = new Rect(cx - size.x / 2, baseY - size.y + rise, size.x, size.y);
        var m = GUI.matrix;
        float s = Screen.height / 720f;
        GUIUtility.RotateAroundPivot(lean, new Vector2(cx, baseY) * s);
        GUI.color = new Color(1, 1, 1, enter);
        GUI.DrawTexture(rect, tex);
        // Destello al entrar y al atacar.
        float flash = Mathf.Max(1f - t / 0.35f, strike * 0.6f);
        if (flash > 0f)
        {
            GUI.color = new Color(1f, 0.85f, 0.5f, flash * 0.5f);
            GUI.DrawTexture(new Rect(cx - 200, baseY - h * 0.75f, 400, 400), UIStyle.Tex("brillo"));
        }
        GUI.matrix = m;
        GUI.color = Color.white;
        Embers(w, 0.3f, 0.7f, new Color(1f, 0.6f, 0.25f));
    }

    void DrawClassInfo(float w, int role)
    {
        var pr = (PlayerRole)role;
        GUI.Label(new Rect(w - 480, 40, 440, 40), "SELECCIONA TU CLASE", new GUIStyle(UIStyle.Title) { fontSize = 30 });
        UIStyle.Separator(new Rect(w - 450, 82, 380, 22));

        var r = new Rect(w - 480, 120, 440, 470);
        UIStyle.Panel(r);
        GUI.DrawTexture(new Rect(r.x + 80, r.y + 26, 44, 44), UIStyle.Icon(RoleIcon[role]));
        GUI.Label(new Rect(r.x + 130, r.y + 22, 280, 52), PlayerAbilities.RoleName(pr),
            new GUIStyle(UIStyle.Title) { fontSize = 38, alignment = TextAnchor.MiddleLeft });
        UIStyle.Separator(new Rect(r.x + 40, r.y + 78, r.width - 80, 18));
        GUI.Label(new Rect(r.x + 34, r.y + 98, r.width - 68, 60), RoleDesc[role],
            new GUIStyle(UIStyle.Text) { fontSize = 17, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter });

        // Estadísticas en pastillas doradas.
        for (int s = 0; s < 4; s++)
        {
            float y = r.y + 172 + s * 36;
            GUI.color = UIStyle.Gold;
            GUI.DrawTexture(new Rect(r.x + 40, y + 4, 22, 22), UIStyle.Tex(s == 2 ? "icono_escudo" : s == 3 ? "icono_cura" : "icono_" + StatIcons[s]));
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 72, y, 120, 30), StatNames[s], new GUIStyle(UIStyle.Text) { fontSize = 17, alignment = TextAnchor.MiddleLeft });
            for (int k = 0; k < 6; k++)
            {
                bool on = k < RoleStats[role, s];
                float grow = Mathf.Clamp01((Time.time - roleChangedAt) * 4f - k * 0.35f);
                GUI.color = on ? new Color(0.98f, 0.76f, 0.3f, grow) : new Color(0.25f, 0.18f, 0.1f, 1f);
                GUI.DrawTexture(new Rect(r.x + 200 + k * 34, y + 9, 30, 12), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }
        GUI.Label(new Rect(r.x, r.y + 312, r.width, 20), $"{PlayerAbilities.MaxHealthFor(pr)} de vida",
            new GUIStyle(UIStyle.Small) { fontSize = 13, fontStyle = FontStyle.Italic });

        // Habilidades.
        GUI.Label(new Rect(r.x, r.y + 336, r.width, 30), "Habilidades", new GUIStyle(UIStyle.Title) { fontSize = 22 });
        UIStyle.Separator(new Rect(r.x + 50, r.y + 362, r.width - 100, 14));
        var abilities = PlayerAbilities.AbilitiesFor(pr);
        for (int i = 0; i < abilities.Length; i++)
        {
            var a = abilities[i];
            float step = abilities.Length > 3 ? (r.width - 80f) / abilities.Length : 128f;
            var slot = new Rect(r.x + 40 + i * step, r.y + 382, 104, 64);
            UIStyle.AbilitySlot(new Rect(slot.x + 20, slot.y - 6, 64, 64), UIStyle.IconFor(a.name), PlayerAbilities.RoleColor(pr),
                (i + 1).ToString(), 0f, "");
            GUI.Label(new Rect(slot.x - 10, slot.yMax - 2, 124, 20), a.name + (a.unlockLevel > 0 ? $"\n<color=#c9a86a>(desde el nivel {a.unlockLevel + 1})</color>" : ""),
                new GUIStyle(UIStyle.Small) { fontSize = 12, wordWrap = true, richText = true });
        }
    }

    // ================================================================ Fondo y chispas
    static void Background(float w, string tex, float zoomAmount)
    {
        var bg = UIStyle.Tex(tex);
        float z = 1.02f + (Mathf.Sin(Time.time * 0.12f) * 0.5f + 0.5f) * zoomAmount;
        var full = new Rect(0, 0, w, 720);
        GUI.color = Color.black;
        GUI.DrawTexture(full, Texture2D.whiteTexture);
        GUI.color = Color.white;
        if (bg != null) HUD.DrawCover(new Rect(-(z - 1) * w / 2, -(z - 1) * 360, w * z, 720 * z), bg);
        GUI.DrawTexture(full, UIStyle.Tex("vineta"));
        GUI.DrawTexture(new Rect(0, 470, w, 250), UIStyle.Tex("fundido_v"));
    }

    static void Embers(float w, float x0, float x1, Color color)
    {
        var dot = UIStyle.Tex("punto");
        for (int i = 0; i < 40; i++)
        {
            float seed = i * 12.9898f;
            float x = w * (x0 + (x1 - x0) * Mathf.Repeat(Mathf.Sin(seed) * 43758.5f, 1f));
            float speed = 14f + 22f * Mathf.Repeat(seed * 0.37f, 1f);
            float y = 720f - Mathf.Repeat(Time.time * speed + i * 37f, 760f);
            float s = 2f + 3f * Mathf.Repeat(seed * 0.71f, 1f);
            GUI.color = new Color(color.r, color.g, color.b, 0.35f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 1.3f + i)));
            GUI.DrawTexture(new Rect(x + Mathf.Sin(Time.time + i) * 8f, y, s, s), dot);
        }
        GUI.color = Color.white;
    }

    static string TeamLine()
    {
        var team = new System.Text.StringBuilder("Equipo:  ");
        foreach (var p in FindObjectsByType<PlayerAbilities>())
            team.Append($"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(PlayerAbilities.RoleColor(p.Role), Color.white, 0.25f))}><b>")
                .Append(p.DisplayName).Append(p.IsOwner ? " (tú)" : "").Append("</b></color>   ");
        return team.ToString();
    }

    // ================================================================ Red
    // Para pruebas automáticas del modo en línea.
    public void TestHostOnline() => HostOnline();
    public void TestJoinOnline(string code) => JoinOnline(code);
    public string SessionCode => sessionCode;
    public string Status => status;

    async void HostOnline()
    {
        working = true;
        status = "Creando partida...";
        try
        {
            await SignIn();
            var options = new SessionOptions { MaxPlayers = MaxPlayers }.WithRelayNetwork();
            var session = await MultiplayerService.Instance.CreateSessionAsync(options);
            sessionCode = session.Code;
            status = "Comparte el código con tus amigos.";
        }
        catch (Exception e)
        {
            status = "No se pudo crear (¿hay internet?): " + e.Message;
        }
        working = false;
    }

    async void JoinOnline(string code)
    {
        if (string.IsNullOrEmpty(code)) { status = "Escribe el código."; return; }
        working = true;
        status = "Uniéndose...";
        try
        {
            await SignIn();
            await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            sessionCode = code.ToUpper();   // el que se une también ve el código (para pasárselo a otro amigo)
            status = "";
        }
        catch (Exception e)
        {
            status = "No se pudo unir: " + e.Message;
        }
        working = false;
    }

    static async System.Threading.Tasks.Task SignIn()
    {
        // Perfil distinto en cada arranque: así dos copias del juego (por ejemplo, en la misma PC) no chocan
        // con la misma cuenta anónima.
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync(new InitializationOptions().SetProfile("j" + UnityEngine.Random.Range(0, 1000000)));
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    void HostLan()
    {
        Net.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", LanPort, "0.0.0.0");
        sessionCode = null;
        status = Net.StartHost() ? "Partida local creada. Los demás se unen con la IP de esta PC." : "No se pudo crear la partida.";
    }

    void JoinLan(string ip)
    {
        Net.GetComponent<UnityTransport>().SetConnectionData(ip, LanPort);
        status = Net.StartClient() ? "Conectando a " + ip + "..." : "No se pudo conectar.";
    }

    void Leave()
    {
        Net.Shutdown();
        sessionCode = null;
        status = "";
    }
}
