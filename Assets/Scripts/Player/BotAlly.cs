using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Compañero controlado por la computadora cuando juegas SOLO: un "espíritu aliado" con el modelo de una clase.
// Solo existe en la PC del anfitrión (jugando solo no hay nadie más). El jefe no lo ataca (es un espíritu),
// pero él sí pelea: los de cuerpo a cuerpo se pegan al jefe, los de distancia disparan de lejos y el Sanador
// te cura y, si caes, corre a revivirte. En el silbido del Tunche también corren a una vela.
public class BotAlly : MonoBehaviour
{
    public static readonly List<BotAlly> All = new List<BotAlly>();
    public static bool Enabled
    {
        get => PlayerPrefs.GetInt("CompanerosIA", 1) == 1;
        set { PlayerPrefs.SetInt("CompanerosIA", value ? 1 : 0); PlayerPrefs.Save(); }
    }

    static readonly string[] Names = { "Wayra", "Amaru", "Kusi", "Yaku", "Killa" };   // por clase
    public PlayerRole Role { get; private set; }
    public string BotName => Names[(int)Role];

    CharacterController cc;
    HeroMotion motion;
    PlayerAbilities.Ability[] abilities;
    readonly float[] readyAt = new float[4];
    Vector3 lastPos;
    float verticalVel, reviveProgress, nextThink;
    Vector3 strafe;

    // Elige dos clases que el jugador no tiene (prioridad: Guardián y Sanador) y las crea junto al inicio.
    public static void SpawnForSolo(PlayerRole humanRole, Vector3 spawn)
    {
        foreach (var b in All.ToArray()) if (b != null) Destroy(b.gameObject);
        All.Clear();
        if (!Enabled) return;
        var order = new[] { PlayerRole.Guardian, PlayerRole.Sanador, PlayerRole.Arquero, PlayerRole.Guerrero, PlayerRole.Chaman };
        int made = 0;
        foreach (var r in order)
        {
            if (r == humanRole) continue;
            Create(r, spawn + (made == 0 ? Vector3.left : Vector3.right) * 3f);
            if (++made == 2) break;
        }
    }

    static void Create(PlayerRole role, Vector3 pos)
    {
        var prefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
        var src = prefab != null ? prefab.transform.Find("Visual/Rol_" + role) : null;
        if (src == null) return;
        var go = new GameObject("Aliado " + Names[(int)role]);
        go.transform.position = pos;
        var cc = go.AddComponent<CharacterController>();
        cc.center = new Vector3(0, 1, 0);
        cc.height = 2f;
        cc.radius = 0.4f;
        var model = Instantiate(src.gameObject, go.transform);
        model.SetActive(true);
        model.transform.localPosition = src.localPosition;
        model.transform.localRotation = src.localRotation;
        model.transform.localScale = src.localScale;
        var bot = go.AddComponent<BotAlly>();
        bot.Role = role;
        bot.cc = cc;
        bot.motion = model.GetComponent<HeroMotion>() ?? model.AddComponent<HeroMotion>();
        bot.abilities = PlayerAbilities.AbilitiesFor(role);
        bot.lastPos = pos;
        All.Add(bot);
        // Aparición con un destello del color de su clase.
        Effects.Burst(pos + Vector3.up, PlayerAbilities.RoleColor(role), 40, 4f, 0.4f, 1f, -0.5f);
    }

    void OnDestroy() => All.Remove(this);

    public void Cheer() => motion.Play("Victory");

    // El jefe le lanzó un golpe: lo esquiva hacia un lado (los bots no reciben daño, pero tienen que moverse).
    public void DodgeFrom(Vector3 attacker)
    {
        motion.Play("Dodge");
        var away = transform.position - attacker; away.y = 0f;
        var side = Vector3.Cross(Vector3.up, away.normalized) * (Random.value < 0.5f ? 1f : -1f);
        transform.position += (away.normalized * 1.5f + side * 2f);
        // En el nivel de la isla no puede saltar al agua.
        if (GameObject.Find("Isla") != null)
        {
            var flat = new Vector3(transform.position.x, 0f, transform.position.z);
            if (flat.magnitude > 8f) transform.position = new Vector3(0f, transform.position.y, 0f) + flat.normalized * 8f;
        }
        Effects.Burst(transform.position + Vector3.up * 0.3f, new Color(0.8f, 0.75f, 0.6f), 12, 3f, 0.4f, 0.5f, 0.2f, flat: true);
    }

    bool Melee => Role == PlayerRole.Guerrero || Role == PlayerRole.Guardian;

    // ¿Hay un Sanador aliado que pueda revivir al jugador?
    public static bool CanRevive
    {
        get { foreach (var b in All) if (b != null && b.Role == PlayerRole.Sanador) return true; return false; }
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        var human = nm != null && nm.LocalClient?.PlayerObject != null ? nm.LocalClient.PlayerObject.GetComponent<Health>() : null;
        var boss = BossAI.Active;
        var bossHealth = boss != null ? boss.GetComponent<Health>() : null;
        bool fighting = boss != null && !bossHealth.IsDead && !LoadingScreen.Showing && !HUD.IntroShowing;

        Vector3 goal = transform.position;
        Vector3 look = transform.forward;

        if (Role == PlayerRole.Sanador && human != null && human.IsDead)
        {
            // Corre a revivir al jugador.
            goal = human.transform.position;
            if (Flat(goal - transform.position).magnitude < 2.2f)
            {
                goal = transform.position;
                reviveProgress += Time.deltaTime / 2.5f;
                if (Random.value < 0.3f) Effects.Burst(human.transform.position + Vector3.up * 0.5f, new Color(1f, 0.9f, 0.5f), 2, 1f, 0.2f, 0.6f, -0.5f);
                if (reviveProgress >= 1f)
                {
                    reviveProgress = 0f;
                    human.Revive(0.5f);
                    motion.Play("Cast");
                    GameAudio.Play("cura");
                    AbilityFX.PlayAt(transform, Role, 2, true, human.transform.position);
                    HUD.AddPopup(human.transform.position + Vector3.up * 3f, $"¡{BotName} te revivió!", new Color(1f, 0.9f, 0.5f));
                }
            }
        }
        else if (fighting && WhistleActive())
        {
            goal = NearestCandle(transform.position);   // silbido del Tunche: a la vela
        }
        else if (fighting)
        {
            reviveProgress = 0f;
            var fromBoss = Flat(transform.position - boss.transform.position);
            if (fromBoss.sqrMagnitude < 0.01f) fromBoss = Vector3.back;
            if (Time.time > nextThink)
            {
                nextThink = Time.time + Random.Range(1.5f, 3f);
                strafe = Quaternion.Euler(0, Random.Range(-35f, 35f), 0) * Vector3.forward;
            }
            float want = Melee ? boss.HitRadius + 2f : boss.HitRadius + (Role == PlayerRole.Sanador ? 9f : 11f);
            if (Role == PlayerRole.Sanador && human != null)
            {
                // El Sanador se queda cerca del jugador, a distancia del jefe.
                var nearHuman = human.transform.position + Flat(human.transform.position - boss.transform.position).normalized * 4f;
                goal = Vector3.Lerp(boss.transform.position + Quaternion.LookRotation(strafe) * fromBoss.normalized * want, nearHuman, 0.6f);
            }
            else goal = boss.transform.position + Quaternion.LookRotation(strafe) * fromBoss.normalized * want;
            look = Flat(boss.transform.position - transform.position);
            UseAbilities(boss, bossHealth, human);
        }
        else if (human != null)
        {
            // Fuera de combate: sigue al jugador.
            var behind = human.transform.position - human.transform.forward * 3f + human.transform.right * (Role == PlayerRole.Sanador ? 2f : -2f);
            if (Flat(behind - transform.position).magnitude > 2f) goal = behind;
        }

        Move(goal, look);
    }

    void Move(Vector3 goal, Vector3 look)
    {
        var to = Flat(goal - transform.position);
        var move = to.magnitude > 0.6f ? to.normalized * Mathf.Min(5.5f, to.magnitude * 3f) : Vector3.zero;
        verticalVel = cc.isGrounded ? -1f : verticalVel - 20f * Time.deltaTime;
        cc.Move((move + Vector3.up * verticalVel) * Time.deltaTime);
        var face = move.sqrMagnitude > 0.5f ? move : look;
        if (face.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), 540f * Time.deltaTime);
        float speed = Time.deltaTime > 0f ? Flat(transform.position - lastPos).magnitude / Time.deltaTime : 0f;
        lastPos = transform.position;
        motion.Tick(speed, false);
        if (transform.position.y < -4f) transform.position = new Vector3(0, 0.5f, -16f);   // red de seguridad
    }

    void UseAbilities(BossAI boss, Health bossHealth, Health human)
    {
        float dist = Flat(boss.transform.position - transform.position).magnitude - boss.HitRadius;
        for (int i = abilities.Length - 1; i >= 0; i--)
        {
            var a = abilities[i];
            if (Time.time < readyAt[i] || !PlayerAbilities.IsUnlocked(a)) continue;
            if (a.damage < 0f)
            {
                // Curación: solo si el jugador la necesita y está en rango.
                if (human == null || human.IsDead || human.Current.Value / human.Max > 0.8f) continue;
                if (Flat(human.transform.position - transform.position).magnitude > a.range) continue;
                human.Heal(-a.damage * 0.8f);
                readyAt[i] = Time.time + a.cooldown * 1.3f;
                motion.Play("Cast");
                GameAudio.Play("cura");
                AbilityFX.PlayAt(transform, Role, i, true, human.transform.position);
                return;
            }
            if (dist > a.range) continue;
            float before = bossHealth.Current.Value;
            float dmg = a.damage * 0.75f;
            // Los bots también cuentan para el ataque conjunto y el punto débil.
            bool joint = boss.RegisterHit(1000UL + (ulong)Mathf.Max(0, All.IndexOf(this)));
            bool weak = boss.HitsWeakPoint(transform.position);
            if (joint) dmg *= 1.8f;
            if (weak) dmg *= 3f;
            bossHealth.TakeDamage(dmg, 999);
            boss.AddStun(before - bossHealth.Current.Value);
            if (joint) boss.JointFxRpc(boss.transform.position);
            if (weak) boss.WeakHitFxRpc();
            readyAt[i] = Time.time + a.cooldown * 1.25f + Random.Range(0f, 0.4f);
            motion.Play("Attack");
            GameAudio.Play(Role == PlayerRole.Arquero ? "flecha" : Role == PlayerRole.Chaman || Role == PlayerRole.Sanador ? "magia" : "golpe");
            AbilityFX.PlayAt(transform, Role, i, false, boss.transform.position);
            return;
        }
    }

    static bool WhistleActive() =>
        BossMechanics.Announcement != null && BossMechanics.Announcement.Contains("silbido") && Time.time < BossMechanics.AnnouncementUntil;

    static Vector3 NearestCandle(Vector3 from)
    {
        Vector3 best = from;
        float bestD = float.MaxValue;
        foreach (var t in FindObjectsByType<Transform>())
            if (t.name == "Vela")
            {
                float d = (t.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t.position + (from - t.position).normalized * 1.5f; }
            }
        return best;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
