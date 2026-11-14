using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Interfaz del combate con estilo andino (UIStyle): vida del jefe arriba, el grupo a la izquierda,
// tu vida y habilidades abajo, avisos de mecánicas y números de daño/curación flotantes.
public class HUD : MonoBehaviour
{
    struct Floater
    {
        public Vector3 world;
        public string text;
        public Color color;
        public float start;
        public bool big;
    }

    readonly List<Floater> floaters = new List<Floater>();
    Texture2D white;
    BossAI introBoss;
    float introStart;

    void Awake()
    {
        white = Texture2D.whiteTexture;
        Health.Changed += OnHealthChanged;
    }

    void OnDestroy() => Health.Changed -= OnHealthChanged;

    void OnHealthChanged(Health h, float delta)
    {
        bool isBoss = h.GetComponent<BossAI>() != null;
        floaters.Add(new Floater
        {
            world = h.transform.position + Vector3.up * (isBoss ? 5.5f : 2.6f) + Random.insideUnitSphere * 0.6f,
            text = delta < 0f ? Mathf.RoundToInt(-delta).ToString() : "+" + Mathf.RoundToInt(delta),
            color = delta > 0f ? new Color(0.45f, 1f, 0.45f) : isBoss ? new Color(1f, 0.9f, 0.45f) : new Color(1f, 0.35f, 0.3f),
            start = Time.time,
            big = isBoss && -delta >= 35f,
        });
    }

    // Llena el rectángulo con la imagen recortando por abajo, para que se vea la cara del monstruo.
    public static void DrawCover(Rect r, Texture tex)
    {
        float visible = Mathf.Min(1f, (tex.width / (float)tex.height) / (r.width / r.height));
        float top = Mathf.Min(0.05f, 1f - visible);
        GUI.DrawTextureWithTexCoords(r, tex, new Rect(0f, 1f - top - visible, 1f, visible));
    }

    // Presentación del jefe al empezar el nivel: su imagen, nombre y leyenda. Devuelve true mientras se muestra.
    bool DrawIntro(BossData data, float w)
    {
        float t = Time.time - introStart;
        if (data.retrato == null || t > data.introSeconds) return false;

        float alpha = Mathf.Clamp01(Mathf.Min(t * 2f, data.introSeconds - t));
        GUI.color = new Color(0, 0, 0, alpha);
        GUI.DrawTexture(new Rect(0, 0, w, 720), white);
        GUI.color = new Color(1, 1, 1, alpha);
        DrawCover(new Rect(0, 0, w, 720), data.retrato);

        GUI.color = new Color(1, 1, 1, 0.92f * alpha);
        UIStyle.Panel(new Rect(w / 2 - 460, 510, 920, 190));
        GUI.color = new Color(1, 1, 1, alpha);

        int level = LevelManager.Instance != null ? LevelManager.Instance.CurrentLevel + 1 : 0;
        var name = new GUIStyle(UIStyle.Title) { fontSize = 50, alignment = TextAnchor.UpperCenter };
        var legend = new GUIStyle(UIStyle.Text) { alignment = TextAnchor.UpperCenter, fontSize = 17 };
        GUI.Label(new Rect(0, 520, w, 110), (level > 0 ? $"<size=20>— NIVEL {level} —</size>\n" : "") + data.bossName.ToUpper(), name);
        GUI.Label(new Rect(w / 2 - 420, 630, 840, 70), data.leyenda, legend);
        GUI.color = Color.white;
        return true;
    }

    void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        string scene = SceneManager.GetActiveScene().name;
        if (scene != "Victoria") creditsAutoShown = false;   // en la próxima victoria vuelven a salir
        if (nm == null || !nm.IsClient || scene == "Menu" || scene == LevelManager.LobbyScene) return;
        if (LoadingScreen.Showing) return;

        float scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float w = Screen.width / scale;

        if (scene == "Victoria")
        {
            DrawVictory(w);
            return;
        }

        var boss = BossAI.Active;
        if (boss != null && boss != introBoss)
        {
            introBoss = boss;
            introStart = Time.time;
            floaters.Clear();
            localDied = false;
        }
        IntroShowing = boss != null && DrawIntro(boss.Data, w);
        if (IntroShowing) { entranceFor = null; return; }
        // Al terminar el retrato, el jefe hace su entrada (una vez por jefe). Mientras dura, sin HUD.
        if (boss != null && entranceFor != boss && Time.time - introStart < boss.Data.introSeconds + 1f)
        {
            entranceFor = boss;
            BossEntrance.Play(boss);
        }
        if (BossEntrance.Playing)
        {
            IntroShowing = true;
            BossEntrance.DrawBanner(w);
            return;
        }

        TrackAdventure(scene);
        if (boss != null && boss != fightFor) { fightFor = boss; fightStart = Time.time; }   // empieza al terminar la entrada
        if (boss != null) DrawFightTimer(boss, w);
        // Marco del juego: esquinas con grecas.
        GUI.color = new Color(1, 1, 1, 0.85f);
        UIStyle.ScreenCorners(w, 64);
        GUI.color = Color.white;
        DrawNameplates(scale);
        DrawFloaters(scale);
        if (boss != null) DrawBoss(boss, w);
        DrawParty();
        DrawChat(14 + 26 + (FindObjectsByType<PlayerAbilities>().Length + BotAlly.All.Count) * 50);
        DrawBubbles(scale);
        DrawPopups(scale);
        DrawToast(w);
        DrawFlash(w);
        var localHealth = nm.LocalClient?.PlayerObject != null ? nm.LocalClient.PlayerObject.GetComponent<Health>() : null;
        if (localHealth != null && localHealth.IsDead) localDied = true;
        DrawLocalPlayer(nm, w);
        DrawTutorial(nm, w);
        if (Time.time < DefeatUntil) DrawDefeat(w);
        if (boss != null) DrawLevelComplete(boss, scene, nm, w);
    }

    // Tabla del nivel: daño, curación y esquivas de cada jugador, con el MVP del nivel.
    void DrawLevelStats(PlayerAbilities[] players, float w, float y)
    {
        var head = new GUIStyle(UIStyle.Small) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UIStyle.Gold } };
        var cell = new GUIStyle(UIStyle.Text) { fontSize = 16, alignment = TextAnchor.MiddleLeft, richText = true };
        float x0 = w / 2 - 300;
        GUI.Label(new Rect(x0, y, 220, 24), "Héroe", head);
        GUI.Label(new Rect(x0 + 250, y, 110, 24), "Daño", head);
        GUI.Label(new Rect(x0 + 360, y, 110, 24), "Curación", head);
        GUI.Label(new Rect(x0 + 480, y, 110, 24), "Esquivas", head);
        PlayerAbilities mvp = null;
        foreach (var p in players)
            if (mvp == null || p.LevelDamage.Value + p.LevelHealing.Value > mvp.LevelDamage.Value + mvp.LevelHealing.Value) mvp = p;
        float yy = y + 26;
        foreach (var p in players)
        {
            string who = $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(PlayerAbilities.RoleColor(p.Role), Color.white, 0.25f))}><b>{p.DisplayName}</b></color>"
                + (p == mvp && players.Length > 1 ? "  <color=#f0d080>★ MVP</color>" : "");
            GUI.Label(new Rect(x0, yy, 250, 24), who, cell);
            GUI.Label(new Rect(x0 + 250, yy, 110, 24), Mathf.RoundToInt(p.LevelDamage.Value).ToString("N0"), cell);
            GUI.Label(new Rect(x0 + 360, yy, 110, 24), Mathf.RoundToInt(p.LevelHealing.Value).ToString("N0"), cell);
            GUI.Label(new Rect(x0 + 480, yy, 110, 24), p.LevelDodges.Value.ToString(), cell);
            yy += 24;
        }
    }

    // Mientras se muestra la presentación del jefe (los bots esperan).
    public static bool IntroShowing;
    static bool creditsAutoShown, creditsStarted;
    static string unlockToastScene;
    BossAI entranceFor;

    // "¡Nivel superado!": se guarda el progreso, cada jugador elige un amuleto y el anfitrión decide seguir o volver.
    BossAI beatenBoss;
    float beatenAt;
    static bool localDied;
    BossAI amuletChosenFor;
    int amuletChosen = -1;

    void DrawLevelComplete(BossAI boss, string scene, NetworkManager nm, float w)
    {
        if (!boss.GetComponent<Health>().IsDead) return;
        if (beatenBoss != boss)
        {
            beatenBoss = boss;
            beatenAt = Time.time;
            Progress.MarkBeaten(Progress.LevelOfScene(scene));
            Achievements.OnBossBeaten(Progress.LevelOfScene(scene), localDied);
            // Todos celebran con los brazos en alto.
            foreach (var pc in FindObjectsByType<PlayerController>()) pc.PlayAction("Victory");
            foreach (var bot in BotAlly.All) if (bot != null) bot.Cheer();
        }
        float t = Time.time - beatenAt - (BossDeathFX.Duration - 0.4f);   // primero se ve morir al jefe (muerte épica)
        if (t < 0f || PauseMenu.Open) return;

        int level = Progress.LevelOfScene(scene);
        bool last = level == Progress.Levels - 1;
        float alpha = Mathf.Clamp01(t * 2.5f);
        float pop = 0.9f + 0.1f * (1f - Mathf.Pow(1f - Mathf.Clamp01(t * 3f), 3f));
        var levelPlayers = FindObjectsByType<PlayerAbilities>();
        float dy = 34 + levelPlayers.Length * 24;   // alto de la tabla de estadísticas del nivel
        var r = new Rect(w / 2 - 360 * pop, Mathf.Max(70f, 160f - dy * 0.6f), 720 * pop, 440 + dy);
        // Brillo dorado detrás.
        GUI.color = new Color(1f, 0.7f, 0.3f, 0.35f * alpha);
        GUI.DrawTexture(new Rect(r.x - 80, r.y - 100, r.width + 160, r.height + 200), UIStyle.Tex("brillo"));
        GUI.color = new Color(1, 1, 1, alpha);
        UIStyle.Panel(r);
        // Emblema del Inti girando lento sobre el panel.
        var m = GUI.matrix;
        var c = new Vector2(w / 2, r.y - 8);
        GUIUtility.RotateAroundPivot(Mathf.Sin(Time.time) * 6f, c * (Screen.height / 720f));
        GUI.DrawTexture(new Rect(c.x - 62, c.y - 62, 124, 124), UIStyle.Tex("emblema_sol"));
        GUI.matrix = m;

        GUI.Label(new Rect(r.x, r.y + 56, r.width, 64), "<size=52>¡NIVEL SUPERADO!</size>", UIStyle.Title);
        GUI.Label(new Rect(r.x, r.y + 118, r.width, 30), $"{boss.Data.bossName} ha sido vencido en {FormatTime(FightTime(boss))}  ·  {MedalText(boss)}",
            new GUIStyle(UIStyle.Text) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Italic, richText = true });
        UIStyle.Separator(new Rect(w / 2 - 170, r.y + 152, 340, 18));
        string next = last ? "Las cinco leyendas han caído." : $"Desbloqueado: Nivel {level + 2} — {BossNames[level + 1]}";
        var nextStyle = new GUIStyle(UIStyle.Text) { alignment = TextAnchor.MiddleCenter, fontSize = 19, normal = { textColor = UIStyle.Gold } };
        float nw = nextStyle.CalcSize(new GUIContent(next)).x;
        if (!last) GUI.DrawTexture(new Rect(w / 2 - nw / 2 - 30, r.y + 174, 24, 24), UIStyle.Tex("candado"));
        GUI.Label(new Rect(r.x, r.y + 170, r.width, 32), next, nextStyle);

        DrawLevelStats(levelPlayers, w, r.y + 206);

        // Elegir un amuleto (cada jugador en su PC; se acumulan para el resto de la aventura).
        var me = nm.LocalClient?.PlayerObject != null ? nm.LocalClient.PlayerObject.GetComponent<PlayerAbilities>() : null;
        if (amuletChosenFor != boss) { amuletChosenFor = boss; amuletChosen = -1; }
        var offer = Amulets.Offer(level * 31 + (int)(me != null ? me.OwnerClientId : 0) * 7 + 3);
        GUI.Label(new Rect(r.x, r.y + 210 + dy, r.width, 26), amuletChosen < 0 ? "Elige un amuleto para el resto de la aventura" : "Amuleto elegido",
            new GUIStyle(UIStyle.Title) { fontSize = 19 });
        for (int i = 0; i < 3; i++)
        {
            int type = (int)offer[i];
            var card = new Rect(w / 2 - 330 + i * 225, r.y + 242 + dy, 210, 96);
            bool chosen = amuletChosen == type;
            GUI.color = new Color(1, 1, 1, alpha * (amuletChosen < 0 || chosen ? 1f : 0.35f));
            GUI.enabled = amuletChosen < 0 && me != null;
            if (GUI.Button(card, GUIContent.none, chosen ? UIStyle.ButtonActive : UIStyle.Button))
            {
                amuletChosen = type;
                me.AddAmulet((AmuletType)type);
                GameAudio.Play("cura");
            }
            GUI.enabled = true;
            GUI.color = new Color(Amulets.Colors[type].r, Amulets.Colors[type].g, Amulets.Colors[type].b, GUI.color.a);
            GUI.DrawTexture(new Rect(card.x + 30, card.y + 28, 40, 40), UIStyle.Icon(Amulets.Icon[type]));
            GUI.color = new Color(1, 1, 1, alpha * (amuletChosen < 0 || chosen ? 1f : 0.35f));
            var ink = chosen ? UIStyle.DarkText : UIStyle.Cream;
            GUI.Label(new Rect(card.x + 78, card.y + 14, card.width - 98, 26), Amulets.Names[type].Replace("Amuleto ", ""),
                new GUIStyle(UIStyle.Text) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = chosen ? UIStyle.DarkText : UIStyle.Gold } });
            GUI.Label(new Rect(card.x + 78, card.y + 40, card.width - 98, 46), Amulets.Desc[type],
                new GUIStyle(UIStyle.Small) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperLeft, normal = { textColor = ink } });
        }
        GUI.color = new Color(1, 1, 1, alpha);

        if (nm.IsServer)
        {
            if (GUI.Button(new Rect(w / 2 - 300, r.y + 364 + dy, 285, 52), last ? "Ver la victoria" : "Siguiente nivel", new GUIStyle(UIStyle.ButtonActive) { fontSize = 22 }))
                LevelManager.Instance.NextLevel();
            if (UIStyle.IconButton(new Rect(w / 2 + 15, r.y + 364 + dy, 285, 52), "casa", "Volver al menú", UIStyle.ButtonDanger, 22))
                LevelManager.Instance.ReturnToMenu();
        }
        else
            GUI.Label(new Rect(r.x, r.y + 374 + dy, r.width, 30), "Esperando a que el anfitrión elija...",
                new GUIStyle(UIStyle.Text) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic });
        GUI.color = Color.white;
    }

    // Cronómetro de la aventura: empieza al entrar al primer nivel y se detiene en la victoria.
    static float adventureStart = -1f, adventureEnd = -1f;
    static int defeats;
    static float lastDefeatSeen;
    static bool fullRun;
    static float bestTime = -1f;
    static bool newRecord;

    void TrackAdventure(string scene)
    {
        if (adventureStart < 0f && scene.StartsWith("Nivel"))
        {
            adventureStart = Time.time;
            fullRun = scene.StartsWith("Nivel1");   // la mejor marca solo cuenta si empezaste desde el nivel 1
        }
        if (DefeatUntil > lastDefeatSeen) { lastDefeatSeen = DefeatUntil; defeats++; }
    }

    // ---------------- Meta del nivel: cronómetro de la pelea y medalla
    BossAI fightFor;
    float fightStart;

    float FightTime(BossAI boss) => (beatenBoss == boss ? beatenAt : Time.time) - fightStart;

    string MedalText(BossAI boss)
    {
        float t = FightTime(boss), gold = boss.GoldSeconds;
        if (t <= gold) return "<color=#ffd34d><b>¡MEDALLA DE ORO!</b></color>";
        if (t <= gold * 1.5f) return "<color=#d8dde6><b>Medalla de plata</b></color>";
        return "<color=#d08a4a><b>Medalla de bronce</b></color>";
    }

    // Cronómetro y meta de oro, a la derecha de la barra del jefe.
    void DrawFightTimer(BossAI boss, float w)
    {
        if (boss.GetComponent<Health>().IsDead) return;
        float t = FightTime(boss), gold = boss.GoldSeconds;
        bool onGold = t <= gold;
        var s = new GUIStyle(UIStyle.Small) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, richText = true };
        GUI.Label(new Rect(w / 2 + 340, 76, 260, 18),
            $"{FormatTime(t)}  <color={(onGold ? "#ffd34d" : "#a0a0a0")}>· oro {FormatTime(gold)}</color>", s);
    }
    static string FormatTime(float seconds) => $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";

    // Textos que flotan en el mundo ("¡CRÍTICO!", "¡COMBO x2!", nombre del definitivo...).
    static readonly List<(Vector3 at, string text, Color color, float t)> popups = new List<(Vector3, string, Color, float)>();
    public static void AddPopup(Vector3 at, string text, Color color) => popups.Add((at, text, color, Time.time));

    void DrawPopups(float scale)
    {
        var cam = Camera.main;
        popups.RemoveAll(p => Time.time - p.t > 1.3f);
        if (cam == null) return;
        foreach (var p in popups)
        {
            float age = Time.time - p.t;
            var sp = cam.WorldToScreenPoint(p.at);
            if (sp.z <= 0f) continue;
            float x = sp.x / scale, y = (Screen.height - sp.y) / scale - age * 50f;
            float pop = 1f + 0.4f * Mathf.Clamp01(1f - age * 5f);
            var st = new GUIStyle(UIStyle.Title) { fontSize = Mathf.RoundToInt(30 * pop) };
            GUI.color = new Color(0, 0, 0, Mathf.Clamp01(1.3f - age) * 0.8f);
            GUI.Label(new Rect(x - 200 + 2, y - 20 + 3, 400, 40), p.text, st);
            GUI.color = new Color(p.color.r, p.color.g, p.color.b, Mathf.Clamp01(1.3f - age));
            GUI.Label(new Rect(x - 200, y - 20, 400, 40), $"<color=#{ColorUtility.ToHtmlStringRGB(p.color)}>{p.text}</color>", st);
        }
        GUI.color = Color.white;
    }

    // Cartel de logro desbloqueado (arriba al centro, unos segundos).
    static string toastText;
    static float toastAt = -10f;
    public static void Toast(string text) { toastText = text; toastAt = Time.time; }

    // Destello de pantalla completa (esquiva perfecta). Usa tiempo real (funciona en cámara lenta).
    static Color flashColor; static float flashAt = -10f, flashDur = 0.5f;
    public static void Flash(Color c, float duration) { flashColor = c; flashAt = Time.unscaledTime; flashDur = duration; }

    static void DrawFlash(float w)
    {
        float t = (Time.unscaledTime - flashAt) / flashDur;
        if (t < 0f || t > 1f) return;
        GUI.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashColor.a * (1f - t));
        GUI.DrawTexture(new Rect(0, 0, w, 720), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // Cadena de golpes (●●○) y aviso de contraataque listo, encima de la barra de habilidades.
    static void DrawCombo(PlayerAbilities me, float w)
    {
        bool counterReady = Time.time < me.CounterReadyUntilLocal;
        int hits = me.ComboCount.Value;
        if (hits <= 0 && !counterReady) return;
        float cx = w / 2;   // a la derecha de la barra de habilidades (no lo tapa el tutorial)
        if (hits > 0)
        {
            var big = new GUIStyle(UIStyle.Text) { fontSize = 20, richText = true };
            GUI.Label(new Rect(cx + 250, 592, 120, 30), "<b>Cadena</b>", big);
            for (int i = 0; i < 3; i++)
            {
                // Rombos dibujados (la fuente no tiene el símbolo): marco oscuro y relleno dorado si ya golpeaste.
                var r = new Rect(cx + 340 + i * 30, 598, 20, 20);
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f, r.center);
                GUI.color = new Color(0.25f, 0.2f, 0.12f, 0.9f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = i < hits ? new Color(1f, 0.77f, 0.28f) : new Color(0.45f, 0.4f, 0.3f, 0.6f);
                GUI.DrawTexture(new Rect(r.x + 3, r.y + 3, 14, 14), Texture2D.whiteTexture);
                GUI.matrix = m;
                GUI.color = Color.white;
            }
            if (hits == 2) GUI.Label(new Rect(cx + 440, 592, 160, 30), "<color=#FFC548><b>¡remata!</b></color>", big);
        }
        if (counterReady)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 10f);
            GUI.color = new Color(1f, 1f, 1f, pulse);
            var cs = new GUIStyle(UIStyle.Text) { fontSize = 21, richText = true };
            GUI.color = new Color(0f, 0f, 0f, 0.55f * pulse);
            GUI.Label(new Rect(cx + 252, 632, 420, 30), "<b>¡CONTRAATAQUE LISTO!</b> ×2,5", cs);   // sombra
            GUI.color = new Color(1f, 1f, 1f, pulse);
            GUI.Label(new Rect(cx + 250, 630, 420, 30), "<color=#8BE3FF><b>¡CONTRAATAQUE LISTO!</b> ×2,5</color>", cs);
            GUI.color = Color.white;
        }
    }

    static void DrawToast(float w)
    {
        float age = Time.time - toastAt;
        if (age > 4f || toastText == null) return;
        float a = Mathf.Clamp01(Mathf.Min(age * 4f, (4f - age) * 2f));
        GUI.color = new Color(1, 1, 1, a);
        var r = new Rect(w / 2 - 260, 130 - (1f - Mathf.Clamp01(age * 4f)) * 30f, 520, 56);
        GUI.Box(r, GUIContent.none, UIStyle.ButtonActive);
        GUI.DrawTexture(new Rect(r.x + 36, r.y + 12, 32, 32), UIStyle.Tex("emblema_sol"));
        GUI.Label(new Rect(r.x + 70, r.y, r.width - 90, r.height), toastText,
            new GUIStyle(UIStyle.Title) { fontSize = 20, normal = { textColor = UIStyle.DarkText } });
        GUI.color = Color.white;
    }

    // Chat rápido: últimas líneas (se borran solas) y burbuja sobre el héroe que habló.
    static readonly List<(string who, Color color, string text, float at)> chat = new List<(string, Color, string, float)>();

    public static void AddChat(PlayerAbilities p, string text)
    {
        chat.Add((p.DisplayName, PlayerAbilities.RoleColor(p.Role), text, Time.time));
        if (chat.Count > 5) chat.RemoveAt(0);
    }

    void DrawChat(float partyBottom)
    {
        float y = partyBottom + 8;
        foreach (var c in chat)
        {
            float age = Time.time - c.at;
            if (age > 6f) continue;
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01((6f - age) / 1f));
            GUI.Label(new Rect(16, y, 400, 24),
                $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(c.color, Color.white, 0.3f))}><b>{c.who}:</b></color> {c.text}",
                new GUIStyle(UIStyle.Text) { fontSize = 16, richText = true });
            y += 22;
        }
        GUI.color = Color.white;
    }

    void DrawBubbles(float scale)
    {
        var cam = Camera.main;
        if (cam == null) return;
        foreach (var p in FindObjectsByType<PlayerAbilities>())
        {
            float age = Time.time - p.LastMessageAt;
            if (age > 3f || p.LastMessage == null) continue;
            var sp = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 3.1f);
            if (sp.z <= 0f) continue;
            float x = sp.x / scale, y = (Screen.height - sp.y) / scale - Mathf.Min(age, 0.2f) * 40f;
            var style = new GUIStyle(UIStyle.Text) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            float tw = style.CalcSize(new GUIContent(p.LastMessage)).x + 30;
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01((3f - age) / 0.5f));
            var r = new Rect(x - tw / 2, y - 18, tw, 34);
            GUI.Box(r, GUIContent.none, UIStyle.ButtonActive);
            GUI.Label(r, p.LastMessage, new GUIStyle(style) { normal = { textColor = UIStyle.DarkText } });
            GUI.color = Color.white;
        }
    }

    // Sobre cada compañero: su clase y su vida. Si está caído y estás cerca: "Mantén E para revivir".
    void DrawNameplates(float scale)
    {
        var cam = Camera.main;
        if (cam == null) return;
        var nameStyle = new GUIStyle(UIStyle.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };
        foreach (var p in FindObjectsByType<PlayerAbilities>())
        {
            if (p.IsOwner) continue;
            var h = p.GetComponent<Health>();
            var sp = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 2.4f);
            if (sp.z <= 0f || sp.z > 60f) continue;
            float x = sp.x / scale, y = (Screen.height - sp.y) / scale;
            var color = PlayerAbilities.RoleColor(p.Role);
            string label = p.DisplayName + (h.IsDead ? "  ✝" : "");
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(x - 79, y - 21, 160, 20), label, nameStyle);
            GUI.color = h.IsDead ? new Color(0.7f, 0.7f, 0.7f) : Color.Lerp(color, Color.white, 0.35f);
            GUI.Label(new Rect(x - 80, y - 22, 160, 20), label, nameStyle);
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(x - 36, y, 72, 7), white);
            GUI.color = Color.Lerp(new Color(0.85f, 0.15f, 0.1f), new Color(0.2f, 0.85f, 0.25f), h.Current.Value / h.Max);
            GUI.DrawTexture(new Rect(x - 35, y + 1, 70 * Mathf.Clamp01(h.Current.Value / h.Max), 5), white);
            GUI.color = Color.white;
        }

        // Aviso de revivir para el jugador local.
        var target = PlayerAbilities.ReviveTarget;
        if (target != null)
        {
            var sp = cam.WorldToScreenPoint(target.transform.position + Vector3.up * 1.2f);
            if (sp.z > 0f)
            {
                float x = sp.x / scale, y = (Screen.height - sp.y) / scale;
                var r = new Rect(x - 120, y, 240, 54);
                UIStyle.Panel(r);
                GUI.Label(new Rect(r.x, r.y + 6, r.width, 22), "Mantén <color=#f0d080><b>E</b></color> para revivir",
                    new GUIStyle(UIStyle.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 15, richText = true });
                UIStyle.Bar(new Rect(r.x + 18, r.y + 32, r.width - 36, 12), PlayerAbilities.ReviveProgress, new Color(1f, 0.85f, 0.35f));
            }
        }
    }

    // Tutorial rápido: se muestra una sola vez, al terminar la presentación del primer jefe que ves.
    static float tutorialStart = -1f;
    const float TutorialSeconds = 11f;

    static readonly string[] ClassTips =
    {
        "<b>Guerrero</b>: pégate al jefe. Golpe de maza (1) siempre; guarda Furia del Inti (3) para cuando esté débil.",
        "<b>Guardián</b>: usa Provocar (2) para que el jefe te ataque a ti y no a tus amigos. Muro de piedra (3) te cura.",
        "<b>Arquero</b>: dispara desde lejos (hasta 25 m). Mantén distancia y muévete entre disparos.",
        "<b>Chamán</b>: Rayo (1) y Fuego de ayahuasca (2) hacen daño; Poción curativa (3) cura a todos los aliados cercanos al más herido.",
        "<b>Sanador</b>: Curar (2) y Gran curación (3) van solas al aliado con menos vida. ¡Tu equipo depende de ti!",
    };

    void DrawTutorial(NetworkManager nm, float w)
    {
        var me = nm.LocalClient?.PlayerObject != null ? nm.LocalClient.PlayerObject.GetComponent<PlayerAbilities>() : null;
        if (me == null) return;
        if (tutorialStart < 0f) tutorialStart = Time.time;
        float t = Time.time - tutorialStart;
        if (t > TutorialSeconds || Time.time < DefeatUntil) return;
        // Durante la muerte del jefe no se tapa la escena.
        if (BossAI.Active != null && BossAI.Active.GetComponent<Health>().IsDead) return;

        float alpha = Mathf.Clamp01(Mathf.Min(t * 3f, TutorialSeconds - t));
        GUI.color = new Color(1, 1, 1, alpha);
        // Abajo, sobre la barra de habilidades, para no tapar al jefe.
        var r = new Rect(w / 2 - 360, 412, 720, 152);
        UIStyle.Panel(r);
        var title = new GUIStyle(UIStyle.Title) { fontSize = 22 };
        var text = new GUIStyle(UIStyle.Text) { fontSize = 15, wordWrap = true, richText = true };
        GUI.Label(new Rect(r.x, r.y + 8, r.width, 30), "CÓMO JUGAR", title);
        GUI.Label(new Rect(r.x + 26, r.y + 40, r.width - 52, 110),
            "<color=#f0d080>WASD</color> mover · <color=#f0d080>Clic</color> ataque básico · <color=#f0d080>Espacio</color> esquivar · <color=#f0d080>1 2 3 4</color> habilidades · <color=#f0d080>R</color> poder definitivo · <color=#f0d080>Z X C V</color> chat · <color=#f0d080>B N M</color> gestos · <color=#f0d080>Esc</color> pausa\n" +
            "Sal del <color=#ff7060>círculo rojo</color> y lee los <color=#f0d080>avisos dorados</color>. Si cae un aliado, mantén <color=#f0d080>E</color> a su lado para revivirlo.\n" +
            "Esquiva <color=#73d9ff>justo antes del golpe</color> = contraataque ×2,5 · <color=#f0d080>3 golpes seguidos</color> = remate.\n" +
            ClassTips[(int)me.Role], text);
        GUI.color = Color.white;
    }

    static readonly string[] BossNames = { "Chullachaqui", "Yacumama", "Bufeo Colorado", "Tunche", "Pishtaco" };

    // Victoria: los 5 jefes vencidos, estadísticas de cada jugador y créditos.
    void DrawVictory(float w)
    {
        // Al terminar la aventura los créditos salen solos (con su canción); se pueden saltar. Después, la victoria.
        // Primero el epílogo (dos frases del narrador, con los jefes y luego los héroes), después los créditos.
        if (!creditsAutoShown) { creditsAutoShown = true; creditsStarted = false; Epilogo.Show(); }
        if (Epilogo.Open) { Epilogo.Draw(w, GetComponent<NetworkUI>()?.Retratos); return; }
        if (!creditsStarted) { creditsStarted = true; Creditos.Show(); }
        if (Creditos.Open) { Creditos.Draw(w); return; }
        var center = new GUIStyle(UIStyle.Text) { alignment = TextAnchor.MiddleCenter };
        // Fondo: atardecer andino con zoom lento, chispas doradas y esquinas.
        var bg = UIStyle.Tex("fondo_clases");
        float z = 1.03f + Mathf.Sin(Time.time * 0.1f) * 0.03f;
        if (bg != null) DrawCover(new Rect(-(z - 1) * w / 2, -(z - 1) * 360, w * z, 720 * z), bg);
        GUI.DrawTexture(new Rect(0, 0, w, 720), UIStyle.Tex("vineta"));
        UIStyle.ScreenCorners(w, 90);
        UIStyle.Panel(new Rect(w / 2 - 470, 20, 940, 680));
        GUI.Label(new Rect(0, 34, w, 70), "<size=58>¡VICTORIA!</size>", UIStyle.Title);
        GUI.Label(new Rect(0, 100, w, 30), "Las cinco leyendas del Perú han sido vencidas.", new GUIStyle(center) { fontSize = 20 });

        // Retratos de los jefes, con una cruz dorada de "vencido".
        var portraits = GetComponent<NetworkUI>()?.Retratos;
        float pw = 150f, ph = 170f, gap = 16f;
        float px = w / 2 - (5 * pw + 4 * gap) / 2;
        for (int i = 0; i < 5; i++)
        {
            var r = new Rect(px + i * (pw + gap), 140, pw, ph);
            GUI.Box(r, GUIContent.none, new GUIStyle { normal = { background = Resources.Load<Texture2D>("UI/ranura") }, border = new RectOffset(8, 8, 8, 8) });
            if (portraits != null && i < portraits.Length && portraits[i] != null)
                DrawCover(new Rect(r.x + 5, r.y + 5, r.width - 10, r.height - 10), portraits[i]);
            GUI.DrawTexture(new Rect(r.xMax - 34, r.yMax - 34, 28, 28), Resources.Load<Texture2D>("UI/chakana"));
            GUI.Label(new Rect(r.x, r.yMax + 2, pw, 24), BossNames[i], new GUIStyle(UIStyle.Small) { fontSize = 15, normal = { textColor = UIStyle.Gold } });
        }

        // Estadísticas del equipo.
        float y = 350;
        var head = new GUIStyle(UIStyle.Text) { fontStyle = FontStyle.Bold, normal = { textColor = UIStyle.Gold } };
        GUI.Label(new Rect(w / 2 - 340, y, 220, 26), "Héroe", head);
        GUI.Label(new Rect(w / 2 - 80, y, 160, 26), "Daño", head);
        GUI.Label(new Rect(w / 2 + 80, y, 160, 26), "Curación", head);
        GUI.Label(new Rect(w / 2 + 240, y, 160, 26), "Revividos", head);
        y += 30;
        PlayerAbilities mvp = null;
        foreach (var p in FindObjectsByType<PlayerAbilities>())
            if (mvp == null || p.DamageDealt.Value + p.HealingDone.Value > mvp.DamageDealt.Value + mvp.HealingDone.Value) mvp = p;
        foreach (var p in FindObjectsByType<PlayerAbilities>())
        {
            string who = $"<color=#{ColorUtility.ToHtmlStringRGB(PlayerAbilities.RoleColor(p.Role))}>{p.DisplayName}</color>"
                + (p.IsOwner ? " (tú)" : "") + (p == mvp ? "  <color=#f0d080>★ MVP</color>" : "");
            GUI.Label(new Rect(w / 2 - 340, y, 260, 26), who, UIStyle.Text);
            GUI.Label(new Rect(w / 2 - 80, y, 160, 26), Mathf.RoundToInt(p.DamageDealt.Value).ToString("N0"), UIStyle.Text);
            GUI.Label(new Rect(w / 2 + 80, y, 160, 26), Mathf.RoundToInt(p.HealingDone.Value).ToString("N0"), UIStyle.Text);
            GUI.Label(new Rect(w / 2 + 240, y, 160, 26), p.Revives.Value.ToString(), UIStyle.Text);
            y += 28;
        }

        // Tiempo total, derrotas y mejor marca (guardada en esta PC).
        if (adventureStart >= 0f && adventureEnd < 0f)
        {
            adventureEnd = Time.time;
            if (bestTime < 0f) bestTime = PlayerPrefs.GetFloat("MejorTiempo", -1f);
            float total = adventureEnd - adventureStart;
            if (fullRun && (bestTime < 0f || total < bestTime))
            {
                newRecord = true;
                bestTime = total;
                PlayerPrefs.SetFloat("MejorTiempo", total);
                PlayerPrefs.Save();
            }
        }
        if (adventureEnd >= 0f)
        {
            string summary = $"Tiempo de la aventura: <color=#f0d080>{FormatTime(adventureEnd - adventureStart)}</color>     Derrotas: <color=#f0d080>{defeats}</color>";
            if (bestTime >= 0f) summary += $"     Mejor marca: <color=#f0d080>{FormatTime(bestTime)}</color>" + (newRecord ? "  ¡NUEVO RÉCORD!" : "");
            else if (!fullRun) summary += "     <i>(modo demo: la marca cuenta desde el nivel 1)</i>";
            GUI.Label(new Rect(0, 526, w, 28), summary, new GUIStyle(center) { fontSize = 18, richText = true });
        }

        var nm = NetworkManager.Singleton;
        if (nm.IsServer)
        {
            if (GUI.Button(new Rect(w / 2 - 130, 560, 260, 40), "Volver al menú", UIStyle.ButtonActive))
                LevelManager.Instance.ReturnToMenu();
        }
        else
            GUI.Label(new Rect(0, 565, w, 30), "El anfitrión puede volver al menú.", new GUIStyle(center) { fontStyle = FontStyle.Italic });

        // Créditos.
        var credits = new GUIStyle(UIStyle.Small) { fontSize = 13, wordWrap = true };
        GUI.Label(new Rect(w / 2 - 420, 612, 840, 80),
            "<b>LEYENDAS DEL PERÚ</b> — Proyecto final\n" +
            "Motor: Unity 6 + Netcode for GameObjects · Modelos 3D: TRELLIS (Microsoft, código abierto) y Mixamo (Adobe)\n" +
            "Música, sonidos, interfaz, efectos, clima y la serpiente Yacumama: generados por código\n" +
            "Basado en los mitos del Chullachaqui, la Yacumama, el Bufeo Colorado, el Tunche y el Pishtaco",
            credits);
    }

    // Lo pone BossAI.ShowDefeatRpc cuando caen todos los jugadores.
    public static float DefeatUntil;

    void DrawDefeat(float w)
    {
        float left = DefeatUntil - Time.time;
        float alpha = Mathf.Clamp01((LevelManager.RetryDelay - left) * 2f);
        GUI.color = new Color(0.15f, 0f, 0f, 0.65f * alpha);
        GUI.DrawTexture(new Rect(0, 0, w, 720), white);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        UIStyle.Panel(new Rect(w / 2 - 340, 230, 680, 200));
        GUI.Label(new Rect(0, 250, w, 70), "<color=#ff6b5a>DERROTA</color>", new GUIStyle(UIStyle.Title) { fontSize = 52 });
        GUI.Label(new Rect(0, 325, w, 40), "Las leyendas los han vencido...", new GUIStyle(UIStyle.Text) { alignment = TextAnchor.MiddleCenter, fontSize = 22 });
        GUI.Label(new Rect(0, 365, w, 40), $"Reintentando en {Mathf.CeilToInt(left)}...", new GUIStyle(UIStyle.Text) { alignment = TextAnchor.MiddleCenter });
        GUI.color = Color.white;
    }

    void DrawBoss(BossAI boss, float w)
    {
        var bh = boss.GetComponent<Health>();
        string name = boss.Data.bossName.ToUpper() + (boss.Enraged ? "   <color=#ff5a4a>FURIA</color>" : boss.Phase2 ? "   <color=#ffb347>FASE 2</color>" : "");
        var titleStyle = new GUIStyle(UIStyle.Title) { fontSize = 26 };
        float tw = titleStyle.CalcSize(new GUIContent(name)).x;
        GUI.Label(new Rect(w / 2 - 320, 4, 640, 36), name, titleStyle);
        UIStyle.Separator(new Rect(w / 2 - tw / 2 - 90, 13, 80, 18));
        UIStyle.Separator(new Rect(w / 2 + tw / 2 + 10, 13, 80, 18));
        UIStyle.Bar(new Rect(w / 2 - 330, 40, 660, 30), bh.Current.Value / bh.Max, new Color(0.85f, 0.12f, 0.08f),
            $"{Mathf.CeilToInt(bh.Current.Value)} / {Mathf.CeilToInt(bh.Max)}");
        // Medidor de aturdimiento (fino, debajo de la vida).
        if (boss.StunMeter.Value > 0.01f || boss.Stunned)
        {
            var sr = new Rect(w / 2 - 200, 71, 400, 9);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(sr, Texture2D.whiteTexture);
            GUI.color = boss.Stunned ? new Color(1f, 0.9f, 0.3f, 0.7f + 0.3f * Mathf.Sin(Time.time * 10f)) : new Color(1f, 0.85f, 0.3f);
            GUI.DrawTexture(new Rect(sr.x + 1, sr.y + 1, (sr.width - 2) * boss.StunMeter.Value, sr.height - 2), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (boss.Stunned) GUI.Label(new Rect(sr.xMax + 6, sr.y - 7, 120, 22), "¡ATURDIDO!", new GUIStyle(UIStyle.Small) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UIStyle.Gold } });
        }

        // Furia: se llena con el tiempo; llena = FURIOSO (más daño y más rápido).
        if (!bh.IsDead)
        {
            float fury = boss.FuryNet.Value;
            var fr = new Rect(w / 2 + 340, 46, 120, 10);
            var small = new GUIStyle(UIStyle.Small) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            GUI.color = new Color(0, 0, 0, 0.65f);
            GUI.DrawTexture(fr, Texture2D.whiteTexture);
            bool full = boss.Furious;
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 8f);
            GUI.color = full ? new Color(1f, 0.15f, 0.05f, pulse) : Color.Lerp(new Color(1f, 0.6f, 0.15f), new Color(0.95f, 0.15f, 0.05f), fury);
            GUI.DrawTexture(new Rect(fr.x + 1, fr.y + 1, (fr.width - 2) * fury, fr.height - 2), Texture2D.whiteTexture);
            GUI.color = Color.white;
            small.normal.textColor = full ? new Color(1f, 0.35f, 0.25f) : new Color(1f, 0.75f, 0.45f);
            GUI.Label(new Rect(fr.x, fr.y + 9, 160, 18), full ? "¡FURIOSO!" : "Furia", small);
        }

        if (bh.IsDead)
        {
            // Unos segundos después lo reemplaza el panel "¡Nivel superado!".
            if (beatenBoss == boss && Time.time - beatenAt > BossDeathFX.Duration - 0.4f) return;
            // Primero se ve la muerte sin tapar el centro; el letrero aparece con el sello dorado.
            if (beatenBoss == boss && Time.time - beatenAt < 2.2f) return;
            UIStyle.Panel(new Rect(w / 2 - 260, 138, 520, 90));
            GUI.Label(new Rect(0, 153, w, 60), "<size=38>¡Jefe derrotado!</size>", UIStyle.Title);
        }
        else if (!string.IsNullOrEmpty(BossMechanics.Announcement) && Time.time < BossMechanics.AnnouncementUntil)
        {
            // Aviso de la mecánica del jefe, con un leve pulso para llamar la atención.
            var rect = new Rect(w / 2 - 400, 78, 800, 52);
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 8f);
            GUI.color = new Color(1f, 0.3f, 0.2f, 0.45f * pulse);
            GUI.DrawTexture(new Rect(rect.x - 30, rect.y - 30, rect.width + 60, rect.height + 60), UIStyle.Tex("brillo"));
            GUI.color = Color.white;
            UIStyle.Banner(rect);
            var style = new GUIStyle(UIStyle.Title) { fontSize = 22, normal = { textColor = new Color(1f, 0.93f, 0.8f) } };
            float aw = style.CalcSize(new GUIContent(BossMechanics.Announcement)).x;
            GUI.color = new Color(1f, 0.85f, 0.4f, pulse);
            GUI.DrawTexture(new Rect(w / 2 - aw / 2 - 42, rect.y + 11, 30, 30), UIStyle.Tex("icono_alerta"));
            GUI.color = new Color(1f, 1f, 1f, pulse);
            GUI.Label(new Rect(rect.x + 20, rect.y, rect.width, rect.height), BossMechanics.Announcement, style);
            GUI.color = Color.white;
        }
    }

    void DrawParty()
    {
        var players = FindObjectsByType<PlayerAbilities>();
        if (players.Length == 0) return;
        int bots = BotAlly.All.Count;
        UIStyle.Panel(new Rect(12, 14, 240, 26 + (players.Length + bots) * 50));
        // Compañeros bot (espíritus aliados): cara, nombre y "aliado".
        float by = 28 + players.Length * 50;
        foreach (var b in BotAlly.All)
        {
            if (b == null) continue;
            var col = PlayerAbilities.RoleColor(b.Role);
            GUI.color = col;
            GUI.DrawTexture(new Rect(20, by, 42, 42), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(22, by + 2, 38, 38), UIStyle.Tex("cara_" + b.Role), ScaleMode.ScaleAndCrop);
            GUI.Label(new Rect(70, by - 2, 170, 22), $"{b.BotName} <color=#c8b88a><i>(aliado)</i></color>", UIStyle.Text);
            GUI.Label(new Rect(70, by + 18, 170, 22), PlayerAbilities.RoleName(b.Role),
                new GUIStyle(UIStyle.Small) { fontSize = 13, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.Lerp(col, Color.white, 0.3f) } });
            by += 50;
        }
        float y = 28;
        foreach (var player in players)
        {
            var h = player.GetComponent<Health>();
            var color = PlayerAbilities.RoleColor(player.Role);
            // Cara del héroe con marco de su color (gris si cayó).
            GUI.color = h.IsDead ? Color.gray : color;
            GUI.DrawTexture(new Rect(20, y, 42, 42), Texture2D.whiteTexture);
            GUI.color = h.IsDead ? new Color(0.45f, 0.45f, 0.45f) : Color.white;
            GUI.DrawTexture(new Rect(22, y + 2, 38, 38), UIStyle.Tex("cara_" + player.Role), ScaleMode.ScaleAndCrop);
            GUI.color = Color.white;
            string who = player.DisplayName + (player.IsOwner ? " (tú)" : "");
            GUI.Label(new Rect(70, y - 2, 170, 22), h.IsDead ? $"<color=#999999>{who} ✝</color>" : who, UIStyle.Text);
            UIStyle.Bar(new Rect(68, y + 20, 168, 20), h.Current.Value / h.Max, color);
            y += 50;
        }
    }

    void DrawLocalPlayer(NetworkManager nm, float w)
    {
        var local = nm.LocalClient?.PlayerObject;
        if (local == null) return;
        var me = local.GetComponent<PlayerAbilities>();
        var myHealth = local.GetComponent<Health>();
        if (me.Abilities == null) return;

        var color = PlayerAbilities.RoleColor(me.Role);
        const float slotSize = 74f, gap = 10f;
        float totalW = me.Abilities.Length * (slotSize + gap) - gap;
        float x = w / 2 - Mathf.Max(totalW, 420f) / 2;
        float barW = Mathf.Max(totalW, 420f);

        DrawCombo(me, w);
        UIStyle.Panel(new Rect(x - 16, 572, barW + 32, 140));
        UIStyle.Bar(new Rect(x, 584, barW, 26), myHealth.Current.Value / myHealth.Max, new Color(0.25f, 0.78f, 0.3f),
            $"{PlayerAbilities.RoleName(me.Role)}   {Mathf.CeilToInt(myHealth.Current.Value)} / {Mathf.CeilToInt(myHealth.Max)}");

        float sx = w / 2 - totalW / 2;
        for (int i = 0; i < me.Abilities.Length; i++)
        {
            var a = me.Abilities[i];
            float left = Mathf.Max(0f, a.readyAt - Time.time);
            float frac = a.cooldown > 0f ? left / a.cooldown : 0f;
            var r = new Rect(sx + i * (slotSize + gap), 618, slotSize, slotSize);
            bool locked = !PlayerAbilities.IsUnlocked(a);
            if (locked)
            {
                // Bloqueada hasta el nivel 3: gris, con candado.
                GUI.color = new Color(0.55f, 0.55f, 0.55f, 0.8f);
                UIStyle.AbilitySlot(r, UIStyle.IconFor(a.name), new Color(0.4f, 0.4f, 0.4f), (i + 1).ToString(), 1f, $"Nivel {a.unlockLevel + 1}");
                GUI.color = Color.white;
            }
            else if (a.unlockLevel > 0 && PlayerAbilities.CurrentLevelIndex == a.unlockLevel && unlockToastScene != SceneManager.GetActiveScene().name)
            {
                // Primera vez en el nivel donde se desbloquea: aviso.
                unlockToastScene = SceneManager.GetActiveScene().name;
                Toast($"¡Nueva habilidad desbloqueada: {a.name}! (tecla {i + 1})");
            }
            else UIStyle.AbilitySlot(r, UIStyle.IconFor(a.name), a.damage < 0f ? new Color(0.5f, 1f, 0.5f) : color, (i + 1).ToString(), frac,
                left > 0f ? $"{left:0.0}s" : "");
            if (r.Contains(Event.current.mousePosition))
                GUI.Label(new Rect(r.x - 40, r.y - 26, slotSize + 80, 24), a.name + (locked ? " (nivel 3)" : ""), UIStyle.Small);
        }

        // Esquivar (Espacio), a la derecha de las habilidades.
        var pc = local.GetComponent<PlayerController>();
        float dodgeLeft = Mathf.Max(0f, pc.DodgeReadyAt - Time.time);
        var dodgeRect = new Rect(sx + me.Abilities.Length * (slotSize + gap) + 14, 630, 58, 58);
        UIStyle.AbilitySlot(dodgeRect, "condor", UIStyle.Cream, "Esp", dodgeLeft / PlayerController.DodgeCooldown,
            dodgeLeft > 0f ? $"{dodgeLeft:0.0}s" : "Esquivar");

        // Poder definitivo (R): ranura a la izquierda que se llena de oro y brilla cuando está lista.
        float ult = me.UltCharge.Value / PlayerAbilities.UltMax;
        var ultRect = new Rect(sx - 14 - 66, 624, 66, 66);

        // Ataque básico (clic izquierdo), a la izquierda del definitivo.
        var basic = me.Basic;
        float bLeft = Mathf.Max(0f, me.BasicReadyAt - Time.time);
        var basicRect = new Rect(ultRect.x - 12 - 54, 630, 54, 54);
        UIStyle.AbilitySlot(basicRect, UIStyle.IconFor(basic.name), Color.Lerp(color, Color.white, 0.3f), "Clic", bLeft / basic.cooldown, "");
        if (basicRect.Contains(Event.current.mousePosition))
            GUI.Label(new Rect(basicRect.x - 50, basicRect.y - 26, basicRect.width + 100, 24), basic.name + " (clic izquierdo)", UIStyle.Small);
        if (me.UltReady)
        {
            float glow = 0.6f + 0.4f * Mathf.Sin(Time.time * 5f);
            GUI.color = new Color(1f, 0.8f, 0.3f, glow);
            GUI.DrawTexture(new Rect(ultRect.x - 26, ultRect.y - 26, ultRect.width + 52, ultRect.height + 52), UIStyle.Tex("brillo"));
            GUI.color = Color.white;
        }
        UIStyle.AbilitySlot(ultRect, "sol", me.UltReady ? new Color(1f, 0.85f, 0.3f) : new Color(0.6f, 0.5f, 0.3f), "R", 1f - ult,
            me.UltReady ? "¡LISTO!" : $"{Mathf.FloorToInt(ult * 100)}%");
        if (ultRect.Contains(Event.current.mousePosition))
            GUI.Label(new Rect(ultRect.x - 60, ultRect.y - 26, 190, 24), PlayerAbilities.UltName(me.Role), UIStyle.Small);

        // Amuletos que lleva (puntitos de color sobre la barra).
        int ax = 0;
        for (int t = 0; t < 6; t++)
            for (int n = 0; n < Amulets.Count(me, (AmuletType)t); n++)
            {
                GUI.color = Amulets.Colors[t];
                GUI.DrawTexture(new Rect(x + ax * 18, 560, 14, 14), UIStyle.Icon(Amulets.Icon[t]));
                ax++;
            }
        GUI.color = Color.white;

        if (myHealth.IsDead)
        {
            UIStyle.Panel(new Rect(w / 2 - 300, 300, 600, 70));
            GUI.Label(new Rect(0, 310, w, 50), "Has caído... tus compañeros siguen luchando.", new GUIStyle(UIStyle.Title) { fontSize = 22 });
        }
    }

    // Números que suben y se desvanecen sobre quien recibió daño o curación.
    void DrawFloaters(float scale)
    {
        var cam = Camera.main;
        if (cam == null) return;
        const float life = 1.2f;
        var style = new GUIStyle(UIStyle.Title) { fontSize = 22 };
        var shadow = new GUIStyle(style) { normal = { textColor = Color.black } };
        for (int i = floaters.Count - 1; i >= 0; i--)
        {
            var f = floaters[i];
            float t = (Time.time - f.start) / life;
            if (t >= 1f) { floaters.RemoveAt(i); continue; }
            var sp = cam.WorldToScreenPoint(f.world);
            if (sp.z <= 0f) continue;
            float x = sp.x / scale, y = (Screen.height - sp.y) / scale - t * 45f;
            int size = f.big ? 30 : 22;
            style.fontSize = shadow.fontSize = Mathf.RoundToInt(size * (1f + 0.4f * Mathf.Max(0f, 0.2f - t) * 5f));
            var rect = new Rect(x - 60, y - 20, 120, 40);
            float a = 1f - t * t;
            GUI.color = new Color(1f, 1f, 1f, a * 0.8f);
            GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), f.text, shadow);
            GUI.color = new Color(f.color.r, f.color.g, f.color.b, a);
            style.normal.textColor = Color.white;
            GUI.Label(rect, f.text, style);
        }
        GUI.color = Color.white;
    }
}
