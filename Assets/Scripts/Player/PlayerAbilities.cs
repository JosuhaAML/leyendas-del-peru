using System.Collections;
using Unity.Netcode;
using UnityEngine;

public enum PlayerRole { Guerrero, Guardian, Arquero, Chaman, Sanador }

// Cada jugador recibe una clase según el orden en que entra (1º Guerrero, 2º Guardián...).
// Habilidades en teclas 1, 2, 3. El cliente pide, el servidor valida y aplica el daño
// (así nadie puede hacer trampa desde su PC).
public class PlayerAbilities : NetworkBehaviour
{
    [System.Serializable]
    public class Ability
    {
        public string name;
        public KeyCode key;
        public float damage;           // negativo = cura (al aliado con menos vida en rango)
        public float range;
        public float cooldown;
        public float threatBonus;      // el Guardián genera amenaza extra para que el jefe lo ataque
        public int unlockLevel;        // nivel (0 = el 1) desde el que se puede usar: la 4.ª habilidad sale en el nivel 3
        [HideInInspector] public float readyAt;

        public Ability(string name, KeyCode key, float damage, float range, float cooldown, float threatBonus = 0f)
        {
            this.name = name; this.key = key; this.damage = damage;
            this.range = range; this.cooldown = cooldown; this.threatBonus = threatBonus;
        }
    }

    // Clase elegida por el jugador (la escribe el dueño, la ven todos). -1 = automática por orden de llegada.
    public NetworkVariable<int> RoleIndex = new NetworkVariable<int>(-1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Estadísticas de la partida (las cuenta el servidor; se muestran en la pantalla de victoria).
    public NetworkVariable<float> DamageDealt = new NetworkVariable<float>(0f);
    public NetworkVariable<float> HealingDone = new NetworkVariable<float>(0f);
    // Del nivel actual (se reinician al cargar cada nivel): para la tabla de "¡Nivel superado!".
    public NetworkVariable<float> LevelDamage = new NetworkVariable<float>(0f);
    public NetworkVariable<float> LevelHealing = new NetworkVariable<float>(0f);
    public NetworkVariable<int> LevelDodges = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public void ResetLevelStats() { if (IsServer) { LevelDamage.Value = 0f; LevelHealing.Value = 0f; } }
    public void CountDodge() { if (IsOwner) LevelDodges.Value++; }
    string lastScene;

    // Clase que eligió este jugador en el menú antes de conectarse (-1 = automática).
    // Se guarda en la PC: si ya elegiste clase antes, no hace falta volver a elegirla.
    public static int PreferredRole
    {
        get => SessionRole >= 0 ? SessionRole : PlayerPrefs.GetInt("ClaseElegida", -1);
        set { if (SessionRole >= 0) SessionRole = value; else { PlayerPrefs.SetInt("ClaseElegida", value); PlayerPrefs.Save(); } }
    }

    // Clase y nombre solo para esta ventana (piloto automático): no se guardan en la PC.
    public static int SessionRole = -1;
    // Pruebas de equilibrio: cuánto daño hizo cada clase y habilidad (solo servidor).
    public static readonly System.Collections.Generic.Dictionary<string, float> DebugDamage = new System.Collections.Generic.Dictionary<string, float>();
    static void AddDebugDamage(string key, float v) { DebugDamage.TryGetValue(key, out float o); DebugDamage[key] = o + v; }
    public static string SessionName;
    public void TryUlt() { if (IsOwner && UltReady) UltimateRpc(); }

    // ¿Este jugador ya eligió su clase? El anfitrión no puede empezar hasta que todos la elijan.
    public NetworkVariable<bool> HasChosen = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Nombre del jugador (lo escribe en el menú; se guarda en la PC y lo ven todos).
    public NetworkVariable<Unity.Collections.FixedString32Bytes> PlayerName = new NetworkVariable<Unity.Collections.FixedString32Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public static string PreferredName
    {
        get => SessionName ?? PlayerPrefs.GetString("NombreJugador", "");
        set { if (SessionName != null) SessionName = value; else { PlayerPrefs.SetString("NombreJugador", value); PlayerPrefs.Save(); } }
    }

    // Nombre para mostrar: el que escribió, o su clase si no puso nada.
    public string DisplayName
    {
        get { string n = PlayerName.Value.ToString(); return string.IsNullOrWhiteSpace(n) ? RoleName(Role) : n; }
    }

    public void SetName(string name)
    {
        if (!IsOwner) return;
        name = (name ?? "").Trim();
        if (name.Length > 16) name = name.Substring(0, 16);
        PlayerName.Value = name;
    }

    // Chat rápido: Z ¡Cúrame! · X ¡Cuidado! · C ¡Vamos! · V ¡Ayúdenme!
    public static readonly string[] QuickMessages = { "¡Cúrame!", "¡Cuidado!", "¡Vamos!", "¡Ayúdenme!" };
    static readonly KeyCode[] QuickKeys = { KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V };
    public string LastMessage { get; private set; }
    public float LastMessageAt { get; private set; } = -10f;
    float nextQuickChat;

    // Gestos: B saludar · N bailar · M celebrar (se ven en todas las PCs).
    public static readonly string[] EmoteNames = { "Saludar", "Bailar", "Celebrar" };
    static readonly string[] EmoteTriggers = { "Wave", "Dance", "Victory" };
    static readonly string[] EmoteBubbles = { "¡Hola!", "¡A bailar!", "¡Bien!" };
    static readonly KeyCode[] EmoteKeys = { KeyCode.B, KeyCode.N, KeyCode.M };
    float nextEmote;

    [Rpc(SendTo.Everyone)]
    void EmoteRpc(int index)
    {
        if (index < 0 || index >= EmoteTriggers.Length || health.IsDead) return;
        GetComponent<PlayerController>().PlayAction(EmoteTriggers[index]);
        LastMessage = EmoteBubbles[index];
        LastMessageAt = Time.time;
    }

    public void TestEmote(int i) { if (IsOwner) EmoteRpc(i); }
    [Rpc(SendTo.Everyone)]
    void QuickChatRpc(int index)
    {
        if (index < 0 || index >= QuickMessages.Length) return;
        LastMessage = QuickMessages[index];
        LastMessageAt = Time.time;
        HUD.AddChat(this, LastMessage);
        GameAudio.Play("magia");
    }

    public PlayerRole Role => RoleIndex.Value >= 0 ? (PlayerRole)RoleIndex.Value : RoleFor(OwnerClientId);
    public Ability[] Abilities { get; private set; }

    Health health;

    void Awake() => health = GetComponent<Health>();

    public override void OnNetworkSpawn()
    {
        RoleIndex.OnValueChanged += (_, _) => ApplyRole();
        AmuletMask.OnValueChanged += (_, _) => { if (IsServer) health.SetMax(MaxHealthFor(Role) * Amulets.Mult(this, AmuletType.Vida)); };
        ApplyRole();
        if (IsServer)
        {
            health.OnPerfectDodge += PerfectDodge;
            // Si te pegan se rompe el combo.
            health.Current.OnValueChanged += (oldV, newV) => { if (newV < oldV) ComboCount.Value = 0; };
        }
        if (IsOwner)
        {
            int wanted = PreferredRole >= 0 ? PreferredRole : (int)RoleFor(OwnerClientId);
            int got = FirstFreeRole(this, wanted);
            RoleIndex.Value = got;
            HasChosen.Value = PreferredRole >= 0;   // eligió en el menú (ahora o en otra partida)
            // Si la clase que elegiste ya la tenía otro al entrar, se avisa (en el menú) por qué te tocó otra.
            var holder = TakenBy(wanted, this);
            if (got != wanted && holder != null && PreferredRole >= 0)
            {
                RoleNotice = $"{RoleName((PlayerRole)wanted)} ya lo tiene {holder.DisplayName}: ahora eres {RoleName((PlayerRole)got)}";
                RoleNoticeAt = Time.time;
            }
        }
        if (IsOwner && !string.IsNullOrWhiteSpace(PreferredName)) SetName(PreferredName);
    }

    // El dueño cambia de clase (desde el menú, en la sala de espera). Cada clase la puede tomar UNA persona.
    public bool ChooseRole(int role)
    {
        if (!IsOwner) return false;
        if (TakenBy(role, this) != null) return false;
        RoleIndex.Value = role;
        HasChosen.Value = true;
        return true;
    }

    // Quién (otro jugador) tiene ya esa clase, o null.
    public static PlayerAbilities TakenBy(int role, PlayerAbilities except)
    {
        foreach (var p in FindObjectsByType<PlayerAbilities>())
            if (p != except && p.IsSpawned && (int)p.Role == role) return p;
        return null;
    }

    static int FirstFreeRole(PlayerAbilities except, int preferred)
    {
        if (preferred >= 0 && TakenBy(preferred, except) == null) return preferred;
        for (int r = 0; r < 5; r++) if (TakenBy(r, except) == null) return r;
        return Mathf.Max(0, preferred);
    }

    // Aviso para el menú cuando el juego te cambia de clase porque la tuya ya estaba tomada.
    public static string RoleNotice;
    public static float RoleNoticeAt = -10f;
    float nextRoleCheck;

    // Si dos eligieron la misma clase a la vez, se la queda quien entró primero y el otro pasa a una libre.
    void CheckRoleConflict()
    {
        if (!IsOwner || Time.time < nextRoleCheck) return;
        nextRoleCheck = Time.time + 0.5f;
        var other = TakenBy((int)Role, this);
        if (other == null || other.OwnerClientId > OwnerClientId) return;
        int free = FirstFreeRole(this, -1);
        if (free == (int)Role) return;
        RoleNotice = $"{RoleName(Role)} ya lo tiene {other.DisplayName}: ahora eres {RoleName((PlayerRole)free)}.";
        RoleNoticeAt = Time.time;
        RoleIndex.Value = free;
        PreferredRole = free;
    }

    // Habilidades, vida y aspecto según la clase actual (en todas las PCs).
    void ApplyRole()
    {
        Abilities = AbilitiesFor(Role);
        if (IsServer) health.SetMax(MaxHealthFor(Role) * Amulets.Mult(this, AmuletType.Vida));
        GetComponent<PlayerController>().ApplyRoleVisual(Role);
    }

    public static PlayerRole RoleFor(ulong clientId) => (PlayerRole)(clientId % 5);

    public static string RoleName(PlayerRole role) => role switch
    {
        PlayerRole.Guardian => "Guardián",
        PlayerRole.Chaman => "Chamán",
        _ => role.ToString(),
    };

    public static Color RoleColor(PlayerRole role) => role switch
    {
        PlayerRole.Guerrero => new Color(0.85f, 0.2f, 0.2f),
        PlayerRole.Guardian => new Color(0.4f, 0.55f, 0.8f),
        PlayerRole.Arquero => new Color(0.3f, 0.75f, 0.3f),
        PlayerRole.Chaman => new Color(0.6f, 0.3f, 0.85f),
        _ => new Color(0.95f, 0.85f, 0.3f),
    };

    public static float MaxHealthFor(PlayerRole role) => role switch
    {
        PlayerRole.Guardian => 220f,
        PlayerRole.Guerrero => 160f,
        _ => 110f,
    };

    public static Ability[] AbilitiesFor(PlayerRole role) => role switch
    {
        PlayerRole.Guerrero => new[]
        {
            new Ability("Golpe de maza",   KeyCode.Alpha1, 12f, 3f, 1f),
            new Ability("Tajo de cóndor",  KeyCode.Alpha2, 45f, 3f, 6f),
            new Ability("Furia del Inti",  KeyCode.Alpha3, 80f, 3f, 15f),
            new Ability("Salto de cóndor", KeyCode.Alpha4, 110f, 26f, 18f) { unlockLevel = 2 },
        },
        PlayerRole.Guardian => new[]
        {
            new Ability("Escudazo",        KeyCode.Alpha1, 8f,  3f,  1f, 30f),
            new Ability("Provocar",        KeyCode.Alpha2, 5f,  15f, 8f, 400f),
            new Ability("Muro de piedra",  KeyCode.Alpha3, -60f, 0f, 15f),
            new Ability("Temblor de la Pachamama", KeyCode.Alpha4, 30f, 7f, 20f, 200f) { unlockLevel = 2 },
        },
        PlayerRole.Arquero => new[]
        {
            new Ability("Flecha",          KeyCode.Alpha1, 10f, 25f, 1f),
            new Ability("Flecha certera",  KeyCode.Alpha2, 35f, 25f, 6f),
            new Ability("Lluvia de flechas", KeyCode.Alpha3, 70f, 25f, 15f),
            new Ability("Flecha de Illapa", KeyCode.Alpha4, 140f, 30f, 20f) { unlockLevel = 2 },
        },
        PlayerRole.Chaman => new[]
        {
            new Ability("Rayo",            KeyCode.Alpha1, 14f, 20f, 1.5f),
            new Ability("Fuego de ayahuasca", KeyCode.Alpha2, 40f, 20f, 8f),
            new Ability("Poción curativa", KeyCode.Alpha3, -32f, 20f, 7f),   // cura en área (todos a 5 m del aliado más herido)
            new Ability("Espíritu del jaguar", KeyCode.Alpha4, 115f, 20f, 18f) { unlockLevel = 2 },
        },
        _ => new[]
        {
            new Ability("Toque de luz",    KeyCode.Alpha1, 6f,  15f, 1f),
            new Ability("Curar",           KeyCode.Alpha2, -35f, 25f, 3f),
            new Ability("Gran curación",   KeyCode.Alpha3, -80f, 25f, 12f),
            new Ability("Bendición del Inti", KeyCode.Alpha4, -50f, 30f, 20f) { unlockLevel = 2 },
        },
    };

    // Revivir: mantener E junto a un aliado caído durante ReviveSeconds. Lo lee el HUD (solo el jugador local).
    public const float ReviveSeconds = 3f, ReviveRange = 3f;
    public static PlayerAbilities ReviveTarget { get; private set; }
    public static float ReviveProgress { get; private set; }
    public NetworkVariable<int> Revives = new NetworkVariable<int>(0);

    // El aliado caído más cercano en rango (o null).
    PlayerAbilities NearestFallen()
    {
        PlayerAbilities best = null;
        float bestDist = ReviveRange;
        foreach (var p in FindObjectsByType<PlayerAbilities>())
        {
            if (p == this || !p.health.IsDead) continue;
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d <= bestDist) { best = p; bestDist = d; }
        }
        return best;
    }

    void UpdateRevive()
    {
        var target = health.IsDead || PauseMenu.Open ? null : NearestFallen();
        if (target != ReviveTarget) ReviveProgress = 0f;
        ReviveTarget = target;
        if (target == null) return;

        if (Input.GetKey(KeyCode.E))
        {
            ReviveProgress += Time.deltaTime / ReviveSeconds;
            if (ReviveProgress >= 1f)
            {
                ReviveProgress = 0f;
                ReviveRpc(target.NetworkObjectId);
            }
        }
        else ReviveProgress = 0f;
    }

    [Rpc(SendTo.Server)]
    void ReviveRpc(ulong targetId)
    {
        if (health.IsDead || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var obj)) return;
        var target = obj.GetComponent<Health>();
        if (target == null || !target.IsDead) return;
        if (Vector3.Distance(transform.position, obj.transform.position) > ReviveRange + 1f) return;
        target.Revive(0.5f);
        Revives.Value++;
        SendStat(3, 1f);
        ShowReviveRpc(obj.transform.position);
    }

    [Rpc(SendTo.Everyone)]
    void ShowReviveRpc(Vector3 at)
    {
        GameAudio.Play("cura");
        Effects.Burst(at + Vector3.up * 0.5f, new Color(1f, 0.9f, 0.5f), 50, 3f, 0.4f, 1.4f, -0.6f);
        Effects.Burst(at + Vector3.up * 0.1f, new Color(1f, 0.85f, 0.4f), 30, 4f, 0.5f, 0.8f, 0f, flat: true);
    }

    // Para pruebas automáticas.
    public void TestRevive(PlayerAbilities target) => ReviveRpc(target.NetworkObjectId);

    void Update()
    {
        if (IsOwner) UpdateRevive();
        CheckRoleConflict();
        // Nuevo nivel: el contador de esquivas vuelve a cero.
        if (IsOwner)
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene != lastScene) { lastScene = scene; LevelDodges.Value = 0; }
        }
        if (IsOwner && !PauseMenu.Open && !NetworkUI.InMenu && Time.time >= nextQuickChat)
            for (int q = 0; q < QuickKeys.Length; q++)
                if (Input.GetKeyDown(QuickKeys[q])) { QuickChatRpc(q); nextQuickChat = Time.time + 1f; break; }
        if (IsOwner && !PauseMenu.Open && !NetworkUI.InMenu && Time.time >= nextEmote && !health.IsDead)
            for (int q = 0; q < EmoteKeys.Length; q++)
                if (Input.GetKeyDown(EmoteKeys[q])) { EmoteRpc(q); nextEmote = Time.time + 1.5f; break; }
        if (!IsOwner || health.IsDead || Abilities == null || PauseMenu.Open) return;
        if (Input.GetKeyDown(KeyCode.R) && UltReady) UltimateRpc();
        // Ataque básico con clic izquierdo (no si el clic fue sobre un botón de la pantalla).
        if (Input.GetMouseButtonDown(0) && Time.time >= BasicReadyAt && GUIUtility.hotControl == 0 && !PauseMenu.Open)
        {
            BasicReadyAt = Time.time + Basic.cooldown;
            BasicAttackRpc();
        }

        for (int i = 0; i < Abilities.Length; i++)
        {
            if (Input.GetKeyDown(Abilities[i].key) && Time.time >= Abilities[i].readyAt && IsUnlocked(Abilities[i]))
            {
                Abilities[i].readyAt = Time.time + Abilities[i].cooldown;
                UseAbilityRpc(i);
            }
        }
    }

    // Para pruebas automáticas: usa la habilidad como si se hubiera pulsado su tecla.
    public void TestUse(int index)
    {
        if (IsOwner && Abilities != null && index < Abilities.Length) UseAbilityRpc(index);
    }

    [Rpc(SendTo.Server)]
    void UseAbilityRpc(int index)
    {
        if (index < 0 || index >= Abilities.Length || health.IsDead) return;
        var ability = Abilities[index];
        if (!IsUnlocked(ability)) return;

        // Bendición del Inti (Sanador, 4.ª): cura a TODOS los aliados en rango, no solo al más herido.
        if (ability.damage < 0f && Role == PlayerRole.Sanador && index == 3)
        {
            float total = 0f;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                var h = client.PlayerObject != null ? client.PlayerObject.GetComponent<Health>() : null;
                if (h == null || h.IsDead || Vector3.Distance(transform.position, h.transform.position) > ability.range) continue;
                float b = h.Current.Value;
                h.Heal(-ability.damage * Amulets.Mult(this, AmuletType.Curacion));
                total += h.Current.Value - b;
            }
            foreach (var bot in BotAlly.All)
                if (bot != null && bot.TryGetComponent(out Health bh) && !bh.IsDead) bh.Heal(-ability.damage);
            HealingDone.Value += total;
            LevelHealing.Value += total;
            SendStat(4, total);
            AddUlt(total * 0.3f);
            ShowBeamRpc(transform.position, true, index);
            return;
        }

        // Poción curativa (Chamán): la lanza al aliado más herido y cura a TODOS los que estén a 5 m de donde cae.
        if (ability.damage < 0f && Role == PlayerRole.Chaman && index == 2)
        {
            Health worst = null;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                var h = client.PlayerObject != null ? client.PlayerObject.GetComponent<Health>() : null;
                if (h == null || h.IsDead || Vector3.Distance(transform.position, h.transform.position) > ability.range + 0.1f) continue;
                if (worst == null || h.Current.Value / h.Max < worst.Current.Value / worst.Max) worst = h;
            }
            var spot = worst != null ? worst.transform.position : transform.position;
            float total = 0f;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                var h = client.PlayerObject != null ? client.PlayerObject.GetComponent<Health>() : null;
                if (h == null || h.IsDead || Vector3.Distance(spot, h.transform.position) > 5f) continue;
                float b = h.Current.Value;
                h.Heal(-ability.damage * Amulets.Mult(this, AmuletType.Curacion));
                total += h.Current.Value - b;
            }
            HealingDone.Value += total;
            LevelHealing.Value += total;
            SendStat(4, total);
            AddUlt(total * 0.4f);
            ShowBeamRpc(spot, true, index);
            return;
        }
        if (ability.damage < 0f)
        {
            Health best = null;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                var h = client.PlayerObject != null ? client.PlayerObject.GetComponent<Health>() : null;
                if (h == null || h.IsDead) continue;
                if (Vector3.Distance(transform.position, h.transform.position) > ability.range + 0.1f) continue;
                if (best == null || h.Current.Value / h.Max < best.Current.Value / best.Max) best = h;
            }
            if (best != null)
            {
                float before = best.Current.Value;
                best.Heal(-ability.damage * Amulets.Mult(this, AmuletType.Curacion));
                float healed = best.Current.Value - before;
                HealingDone.Value += healed;
                LevelHealing.Value += healed;
                SendStat(4, healed);
                AddUlt(healed * 0.45f);
                ShowBeamRpc(best.transform.position, true, index);
            }
            return;
        }

        var boss = BossAI.Active;
        if (boss == null) return;
        var bossHealth = boss.GetComponent<Health>();
        if (bossHealth.IsDead) return;
        float dist = Vector3.Distance(transform.position, boss.transform.position) - boss.HitRadius;
        if (dist > ability.range) return;

        // Multiplicadores: crítico (15 %), combo (golpe justo después de la provocación de OTRO jugador),
        // jefe aturdido (+50 %) y amuleto de daño.
        float damage = ability.damage * Amulets.Mult(this, AmuletType.Fuerza);
        bool crit = Random.value < 0.15f;
        // Combo del Guardián: con 2 jugadores ×2 durante 4 s; en grupos grandes ×1,5 durante 2,5 s
        // (si no, con 4 compañeros aprovechándolo a la vez el jefe se derretía).
        bool bigGroup = boss.PlayerCount >= 3;
        bool combo = Role != PlayerRole.Guardian && Time.time - lastTauntAt < (bigGroup ? 2.5f : 4f);
        if (crit) damage *= 2f;
        if (combo) damage *= bigGroup ? 1.5f : 2f;
        if (boss.Stunned) damage *= 1.5f;

        // Contraataque: primer golpe tras una esquiva perfecta (×2,5).
        bool counter = Time.time < counterUntil;
        if (counter) { damage *= 2.5f; counterUntil = 0f; }
        // Cadena de golpes: 3 seguidos (cada uno a menos de ComboWindow del anterior) = REMATE (×1,8 y mucho aturdimiento).
        int hits = Time.time - lastChainHit <= ComboWindow ? ComboCount.Value + 1 : 1;
        lastChainHit = Time.time;
        bool finisher = hits >= 3;
        if (finisher) damage *= 1.8f;
        ComboCount.Value = finisher ? 0 : hits;
        if (Role == PlayerRole.Guardian && index == 1) { lastTauntAt = Time.time; boss.Taunt(health, 4f); }
        // Ataque conjunto: dos jugadores (o un jugador y un bot) golpean casi a la vez al jefe aturdido.
        bool joint = boss.RegisterHit(OwnerClientId);
        if (joint) damage *= 1.8f;
        // Punto débil expuesto: golpearlo desde el lado correcto hace el triple.
        bool weak = boss.HitsWeakPoint(transform.position);
        if (weak) damage *= 3f;

        float hpBefore = bossHealth.Current.Value;
        bossHealth.TakeDamage(damage, OwnerClientId);
        float dealt = hpBefore - bossHealth.Current.Value; AddDebugDamage($"{Role}_hab{index + 1}", dealt);
        DamageDealt.Value += dealt;
        LevelDamage.Value += dealt;
        AddUlt(dealt * 0.3f);
        boss.AddStun(dealt);
        Achievements.ReportHit(this, crit, combo);
        if (ability.threatBonus > 0f) boss.AddThreat(OwnerClientId, ability.threatBonus);
        ShowBeamRpc(boss.transform.position, false, index);
        if (crit || combo) PopupRpc(boss.transform.position + Vector3.up * 4.5f, combo && crit ? "¡COMBO CRÍTICO!" : combo ? "¡COMBO x2!" : "¡CRÍTICO!", combo);
        if (finisher) boss.AddStun(dealt * 1.5f);
        if (finisher || counter) StrikeFxRpc(boss.transform.position, finisher, counter);
        // Temblor de la Pachamama (Guardián, 4.ª): llena mucho el medidor de aturdimiento.
        if (Role == PlayerRole.Guardian && index == 3) boss.AddStun(bossHealth.Max * 0.035f);
        if (joint) boss.JointFxRpc(boss.transform.position);
        if (weak) boss.WeakHitFxRpc();
    }

    // ================================================================ ATAQUE BÁSICO (clic izquierdo)
    // Golpe rápido y débil (menos que la habilidad 1). Carga poco el poder definitivo y no cuenta
    // para la cadena de golpes ni el contraataque (así no se abusa de él).
    public static Ability BasicFor(PlayerRole role) => role switch
    {
        PlayerRole.Guerrero => new Ability("Golpe rápido", KeyCode.Mouse0, 7f, 3f, 0.55f),
        PlayerRole.Guardian => new Ability("Empujón de escudo", KeyCode.Mouse0, 5f, 3f, 0.6f, 15f),
        PlayerRole.Arquero => new Ability("Disparo rápido", KeyCode.Mouse0, 6f, 25f, 0.5f),
        PlayerRole.Chaman => new Ability("Chispa", KeyCode.Mouse0, 7f, 20f, 0.6f),
        _ => new Ability("Destello", KeyCode.Mouse0, 4f, 15f, 0.6f),
    };
    public Ability Basic => BasicFor(Role);
    public float BasicReadyAt { get; private set; }

    // Para pruebas automáticas.
    public void TestBasic() { if (IsOwner) BasicAttackRpc(); }

    [Rpc(SendTo.Server)]
    void BasicAttackRpc()
    {
        if (health.IsDead) return;
        var a = Basic;
        var boss = BossAI.Active;
        if (boss == null) return;
        var bossHealth = boss.GetComponent<Health>();
        if (bossHealth.IsDead) return;
        if (Vector3.Distance(transform.position, boss.transform.position) - boss.HitRadius > a.range) return;

        float damage = a.damage * Amulets.Mult(this, AmuletType.Fuerza);
        bool crit = Random.value < 0.1f;
        if (crit) damage *= 2f;
        if (boss.Stunned) damage *= 1.5f;
        bool joint = boss.RegisterHit(OwnerClientId);
        if (joint) damage *= 1.8f;
        bool weak = boss.HitsWeakPoint(transform.position);
        if (weak) damage *= 3f;

        float before = bossHealth.Current.Value;
        bossHealth.TakeDamage(damage, OwnerClientId);
        float dealt = before - bossHealth.Current.Value; AddDebugDamage($"{Role}_basico", dealt);
        DamageDealt.Value += dealt;
        LevelDamage.Value += dealt;
        AddUlt(dealt * 0.12f);   // las habilidades cargan 0,3: el básico mucho menos
        boss.AddStun(dealt * 0.5f);
        if (a.threatBonus > 0f) boss.AddThreat(OwnerClientId, a.threatBonus);
        BasicFxRpc(boss.transform.position);
        if (crit) PopupRpc(boss.transform.position + Vector3.up * 4.5f, "¡CRÍTICO!", false);
        if (joint) boss.JointFxRpc(boss.transform.position);
        if (weak) boss.WeakHitFxRpc();
    }

    [Rpc(SendTo.Everyone)]
    void BasicFxRpc(Vector3 target)
    {
        GetComponent<PlayerController>().PlayAction("Attack");
        AbilityFX.Basic(transform, Role, target);
    }

    // ¿Se puede usar ya? (la 4.ª habilidad se desbloquea al llegar al nivel 3)
    public static int CurrentLevelIndex => Progress.LevelOfScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    public static bool IsUnlocked(Ability a) => a.unlockLevel <= 0 || CurrentLevelIndex >= a.unlockLevel;

    // ================================================================ CONTRAATAQUE Y CADENA DE GOLPES
    public const float ComboWindow = 2.5f;
    // Golpes seguidos de la cadena actual (0–2). Lo escribe el servidor, lo ve el HUD.
    public NetworkVariable<int> ComboCount = new NetworkVariable<int>(0);
    float lastChainHit = -10f, counterUntil;
    // Solo en la PC del dueño: hasta cuándo se muestra "contraataque listo".
    public float CounterReadyUntilLocal { get; private set; }

    // Servidor: el jefe pegó mientras este jugador esquivaba.
    void PerfectDodge()
    {
        counterUntil = Time.time + 3f;
        PerfectDodgeRpc();
    }

    [Rpc(SendTo.Everyone)]
    void PerfectDodgeRpc()
    {
        var c = new Color(0.45f, 0.85f, 1f);
        AbilityFX.RingAt(transform.position + Vector3.up * 0.1f, c, 3.5f, 0.6f);
        Effects.Burst(transform.position + Vector3.up * 1f, c, 24, 6f, 0.35f, 0.6f, 0f);
        HUD.AddPopup(transform.position + Vector3.up * 3.2f, "¡ESQUIVA PERFECTA!", c);
        GameAudio.Play("magia");
        if (!IsOwner) return;
        CounterReadyUntilLocal = Time.time + 3f;
        // Solo (sin otros jugadores humanos): cámara lenta de verdad. En línea no se puede frenar el tiempo de todos,
        // así que se usa un destello azul en la pantalla.
        if (IsServer && NetworkManager.ConnectedClientsList.Count <= 1) SlowMo.Play(0.3f, 0.6f);
        HUD.Flash(new Color(0.45f, 0.85f, 1f, 0.35f), 0.5f);
    }

    [Rpc(SendTo.Everyone)]
    void StrikeFxRpc(Vector3 bossPos, bool finisher, bool counter)
    {
        if (IsOwner) CounterReadyUntilLocal = 0f;
        var color = finisher ? new Color(1f, 0.75f, 0.25f) : new Color(0.45f, 0.85f, 1f);
        AbilityFX.RingAt(bossPos + Vector3.up * 0.1f, color, finisher ? 6f : 4f, 0.7f);
        Effects.Burst(bossPos + Vector3.up * 2f, color, finisher ? 50 : 30, 9f, 0.45f, 0.7f, 0.3f);
        string text = finisher && counter ? "¡REMATE CONTRAATAQUE!" : finisher ? "¡REMATE!" : "¡CONTRAATAQUE!";
        HUD.AddPopup(bossPos + Vector3.up * 3.6f, text, color);
        GameAudio.Play("golpe");
        if (IsOwner) CameraFollow.Shake(finisher ? 0.5f : 0.3f, 0.35f);
        if (finisher) GetComponent<PlayerController>().PlayAction("Attack");
    }

    // ================================================================ AMULETOS Y LOGROS
    // Amuletos elegidos (3 bits por tipo). Los escribe el dueño; el servidor los usa para el daño, la curación, etc.
    public NetworkVariable<int> AmuletMask = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public void AddAmulet(AmuletType t)
    {
        if (!IsOwner || Amulets.Count(this, t) >= 7) return;
        AmuletMask.Value += 1 << (3 * (int)t);
        if (Amulets.Total(this) >= 3) Achievements.Unlock("amuletos");
    }

    // Servidor → dueño: cuenta una estadística de logros en la PC de ese jugador.
    public void SendStat(int stat, float amount) { if (IsServer) StatRpc(stat, amount); }

    [Rpc(SendTo.Owner)]
    void StatRpc(int stat, float amount) => Achievements.OnStat(stat, amount);

    // Para pruebas automáticas.
    public void TestChat(int i) { if (IsOwner) QuickChatRpc(i); }
    public void TestUlt() { if (IsServer) { UltCharge.Value = UltMax; UltimateRpc(); } }

    // Combo: momento de la última provocación del Guardián (servidor).
    static float lastTauntAt = -10f;

    [Rpc(SendTo.Everyone)]
    void PopupRpc(Vector3 at, string text, bool combo) =>
        HUD.AddPopup(at, text, combo ? new Color(0.55f, 0.85f, 1f) : new Color(1f, 0.82f, 0.3f));

    // ================================================================ PODER DEFINITIVO (R)
    // Barra que se llena peleando (servidor). Con la barra llena, R lanza el súper ataque de la clase.
    public NetworkVariable<float> UltCharge = new NetworkVariable<float>(0f);
    public const float UltMax = 100f;
    public bool UltReady => UltCharge.Value >= UltMax;

    void AddUlt(float amount)
    {
        if (!IsServer) return;
        UltCharge.Value = Mathf.Min(UltMax, UltCharge.Value + amount * Amulets.Mult(this, AmuletType.Inti));
    }

    public static string UltName(PlayerRole role) => role switch
    {
        PlayerRole.Guerrero => "Lluvia de soles",
        PlayerRole.Guardian => "Muralla del Inti",
        PlayerRole.Arquero => "Flecha del cóndor",
        PlayerRole.Chaman => "Tormenta de rayos",
        _ => "Renacer del Inti",
    };

    [Rpc(SendTo.Server)]
    void UltimateRpc()
    {
        if (health.IsDead || !UltReady) return;
        var boss = BossAI.Active;
        var bossHealth = boss != null ? boss.GetComponent<Health>() : null;
        UltCharge.Value = 0f;
        Achievements.ReportUlt(this);
        switch (Role)
        {
            case PlayerRole.Guardian:
                // Protege a todo el equipo 4 segundos.
                foreach (var c in NetworkManager.ConnectedClientsList)
                    if (c.PlayerObject != null && c.PlayerObject.TryGetComponent(out Health h) && !h.IsDead) h.Protect(4f);
                break;
            case PlayerRole.Sanador:
                // Revive a los caídos y cura a todos.
                foreach (var c in NetworkManager.ConnectedClientsList)
                    if (c.PlayerObject != null && c.PlayerObject.TryGetComponent(out Health h))
                    {
                        float before = h.IsDead ? 0f : h.Current.Value;
                        if (h.IsDead) { h.Revive(0.7f); Revives.Value++; } else h.Heal(h.Max);
                        HealingDone.Value += h.Current.Value - before;
                    }
                break;
            default:
                if (bossHealth != null && !bossHealth.IsDead)
                {
                    float dmg = Role switch { PlayerRole.Guerrero => 260f, PlayerRole.Arquero => 300f, _ => 240f } * Amulets.Mult(this, AmuletType.Fuerza);
                    if (boss.Stunned) dmg *= 1.5f;
                    float before = bossHealth.Current.Value;
                    bossHealth.TakeDamage(dmg, OwnerClientId);
                    DamageDealt.Value += before - bossHealth.Current.Value; AddDebugDamage($"{Role}_definitivo", before - bossHealth.Current.Value);
                    boss.AddStun(before - bossHealth.Current.Value);
                }
                break;
        }
        UltimateFxRpc(boss != null ? boss.transform.position : transform.position);
    }

    [Rpc(SendTo.Everyone)]
    void UltimateFxRpc(Vector3 bossPos)
    {
        GetComponent<PlayerController>().PlayAction(Role == PlayerRole.Sanador || Role == PlayerRole.Guardian ? "Cast" : "Attack");
        GameAudio.Play("victoria");
        HUD.AddPopup(transform.position + Vector3.up * 3.5f, "¡" + UltName(Role).ToUpper() + "!", RoleColor(Role));
        AbilityFX.Ultimate(this, Role, bossPos);
    }

    // Animación, sonido y efecto de la habilidad en todas las PCs: flechas, orbes mágicos o chispas de golpe.
    [Rpc(SendTo.Everyone)]
    void ShowBeamRpc(Vector3 target, bool heal, int index)
    {
        GetComponent<PlayerController>().PlayAction(heal ? "Cast" : "Attack");
        GameAudio.Play(heal ? "cura" : Role switch
        {
            PlayerRole.Arquero => "flecha",
            PlayerRole.Chaman or PlayerRole.Sanador => "magia",
            _ => "golpe",
        });
        if (!heal && index == 3 && Role == PlayerRole.Guerrero && IsOwner && BossAI.Active != null)
            GetComponent<PlayerController>().LeapTo(BossAI.Active.transform.position, BossAI.Active.HitRadius);
        AbilityFX.Play(this, Role, index, heal, target);
    }
}
