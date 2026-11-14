using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Controla los 5 niveles. Vive junto al NetworkManager (no se destruye al cambiar de escena).
// Solo el servidor decide: cuando muere el jefe carga la siguiente escena para TODOS.
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    // Nombres exactos de las escenas (están en File > Build Profiles).
    public string[] levelScenes =
    {
        "Nivel1_Chullachaqui",
        "Nivel2_Yacumama",
        "Nivel3_BufeoColorado",
        "Nivel4_Tunche",
        "Nivel5_Pishtaco",
    };
    public string victoryScene = "Victoria";
    public const string LobbyScene = "Sala";   // menú "dentro" de la partida (todos siguen conectados)

    public int CurrentLevel { get; private set; } = -1;
    bool changing;

    static NetworkManager Net => NetworkManager.Singleton;

    void Awake() => Instance = this;

    void Start()
    {
        Net.OnServerStarted += () =>
            Net.SceneManager.OnLoadEventCompleted += (sceneName, mode, done, timedOut) =>
            {
                changing = false;
                ReviveAll();
                SpawnBotsIfSolo(sceneName);
                foreach (var c in Net.ConnectedClientsList)
                    if (c.PlayerObject != null) c.PlayerObject.GetComponent<PlayerAbilities>().ResetLevelStats();
            };
    }

    // Lo llama el menú cuando el anfitrión pulsa "Empezar".
    public void StartGame() => StartAt(0);

    public void StartAt(int level)
    {
        if (!Net.IsServer) return;
        CurrentLevel = Mathf.Clamp(level, 0, levelScenes.Length - 1);
        Load(levelScenes[CurrentLevel]);
    }

    // Al morir el jefe ya no se pasa solo: el HUD muestra "¡Nivel superado!" y el anfitrión elige
    // "Siguiente nivel" o "Volver al menú". El progreso se guarda en cada PC (HUD → Progress).
    public void OnBossDefeated() { }

    public void NextLevel()
    {
        if (!Net.IsServer || changing) return;
        CurrentLevel++;
        Load(CurrentLevel < levelScenes.Length ? levelScenes[CurrentLevel] : victoryScene);
    }

    // Todos vuelven al menú sin desconectarse (escena "Sala").
    public void ReturnToMenu()
    {
        if (!Net.IsServer || changing) return;
        Load(LobbyScene);
    }

    public bool InLevel => CurrentLevel >= 0 && CurrentLevel < levelScenes.Length &&
        SceneManager.GetActiveScene().name == levelScenes[CurrentLevel];

    void Load(string scene)
    {
        changing = true;
        Net.SceneManager.LoadScene(scene, LoadSceneMode.Single);
    }

    // Jugando solo: dos compañeros bot (solo en niveles).
    void SpawnBotsIfSolo(string sceneName)
    {
        if (Net.ConnectedClientsList.Count != 1 || Progress.LevelOfScene(sceneName) < 0) return;
        var me = Net.ConnectedClientsList[0].PlayerObject;
        var spawn = GameObject.Find("PlayerSpawn");
        if (me == null || spawn == null) return;
        BotAlly.SpawnForSolo(me.GetComponent<PlayerAbilities>().Role, spawn.transform.position);
    }

    float allDeadSince = -1f;

    void ReviveAll()
    {
        foreach (var client in Net.ConnectedClientsList)
            if (client.PlayerObject != null && client.PlayerObject.TryGetComponent(out Health h)) h.Revive();
    }

    // Si mueren todos, se repite el nivel actual.
    void Update()
    {
        if (Net == null || !Net.IsServer || changing || BossAI.Active == null) return;
        if (BossAI.Active.GetComponent<Health>().IsDead) return;   // nivel ya ganado
        if (Net.ConnectedClientsList.Count == 0) return;
        foreach (var client in Net.ConnectedClientsList)
            if (client.PlayerObject != null && !client.PlayerObject.GetComponent<Health>().IsDead) { allDeadSince = -1f; return; }

        // Si hay un Sanador bot, le damos unos segundos para que llegue a revivir antes de la derrota.
        if (BotAlly.CanRevive)
        {
            if (allDeadSince < 0f) allDeadSince = Time.time;
            if (Time.time - allDeadSince < 6f) return;
        }
        allDeadSince = -1f;

        // Derrota: se muestra el aviso unos segundos y se repite el nivel.
        changing = true;
        BossAI.Active.ShowDefeatRpc();
        Invoke(nameof(Retry), RetryDelay);
    }

    public const float RetryDelay = 4f;

    void Retry() => Load(levelScenes[CurrentLevel]);
}
