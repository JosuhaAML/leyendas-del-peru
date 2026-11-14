using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Cerebro del jefe. Corre SOLO en el servidor; los clientes ven el resultado
// gracias a NetworkTransform (posición) y Health (vida).
[RequireComponent(typeof(Health))]
public partial class BossAI : NetworkBehaviour
{
    public static BossAI Active { get; private set; }

    [SerializeField] BossData data;
    [SerializeField] Animator animator;
    [SerializeField] GameObject areaWarningPrefab;  // círculo rojo en el suelo
    public float HitRadius = 2f;

    readonly Dictionary<ulong, float> threat = new Dictionary<ulong, float>();
    Health health;
    Transform visual;
    float nextMelee, nextArea, activeAt;
    bool enraged, busy, deathPlayed, deathSoundPlayed, stepLeft;
    float nextStep;
    Vector3 lastPosition;
    SerpentMotion serpent;   // solo la Yacumama: animación por código en vez de Animator

    public BossData Data => data;
    // La furia la decide el servidor y la ven todos (aura roja y aviso en la barra).
    public NetworkVariable<bool> EnragedNet = new NetworkVariable<bool>(false);
    public bool Enraged => EnragedNet.Value;
    Light rageLight;
    float nextRageSpark;

    // Aturdido: si el equipo pega mucho seguido se llena el medidor; lleno = mareado 4 s y recibe +50 %.
    public NetworkVariable<float> StunMeter = new NetworkVariable<float>(0f);   // 0..1
    public NetworkVariable<bool> StunnedNet = new NetworkVariable<bool>(false);
    public bool Stunned => StunnedNet.Value;
    float stunnedUntil, stunResistUntil;
    int stunsDone;

    // Jugadores humanos al empezar el nivel: la dificultad se ajusta a cuántos son.
    public int PlayerCount { get; private set; } = 1;
    // Vida "de verdad" del jefe para este grupo (en pruebas de equilibrio la vida real puede ser enorme).
    float NominalMax => data.maxHealth * HealthMult(PlayerCount);

    // Vida según el grupo, calculada con el daño por segundo MEDIDO de cada grupo (pruebas con piloto automático):
    // 1 jugador + 2 bots ≈ 112, 2 ≈ 108, 3 ≈ 147, 4 ≈ 187, 5 ≈ 248. Así la pelea dura lo mismo sea cual sea el grupo.
    // (La vida base de cada jefe está pensada para un grupo que hace 100 de daño por segundo.)
    static readonly float[] GroupDps = { 1.12f, 1.25f, 1.55f, 1.9f, 2.4f };   // ajustado con peleas reales: 1→75 s, 2→62 s (subido), 5→80 s
    public static float HealthMult(int n) => n <= 1 && !BotAlly.Enabled ? 0.55f : GroupDps[Mathf.Clamp(n, 1, 5) - 1];
    float GroupDamage => 1f + 0.08f * Mathf.Max(0, PlayerCount - 2);

    public void AddStun(float damage)
    {
        if (!IsServer || Stunned || health.IsDead) return;
        // Tras un aturdimiento hay unos segundos de resistencia, y cada aturdimiento cuesta más (más aún en grupos grandes).
        if (Time.time < stunResistUntil) return;
        float need = NominalMax * 0.07f * (1f + 0.35f * stunsDone) * (1f + 0.15f * Mathf.Max(0, PlayerCount - 2));
        StunMeter.Value = Mathf.Min(1f, StunMeter.Value + damage / need);
        if (StunMeter.Value >= 1f)
        {
            stunsDone++;
            StunnedNet.Value = true;
            stunnedUntil = Time.time + 4f;
            StunRpc();
        }
    }

    void UpdateStun()
    {
        if (!IsServer) return;
        if (Stunned)
        {
            StunMeter.Value = Mathf.Clamp01((stunnedUntil - Time.time) / 4f);
            if (Time.time >= stunnedUntil) { StunnedNet.Value = false; StunMeter.Value = 0f; stunResistUntil = Time.time + 5f + 1.5f * Mathf.Max(0, PlayerCount - 2) + (IsFinalBoss ? 4f : 0f); }
        }
        else if (StunMeter.Value > 0f) StunMeter.Value = Mathf.Max(0f, StunMeter.Value - 0.08f * Time.deltaTime);
    }

    [Rpc(SendTo.Everyone)]
    void StunRpc()
    {
        BossMechanics.Announcement = $"¡{data.bossName} está ATURDIDO! ¡Ataquen con todo!";
        BossDialogue.Say(this, "aturdido");
        BossMechanics.AnnouncementUntil = Time.time + 3f;
        GameAudio.Play("area");
        Achievements.Unlock("mareado");
        StartCoroutine(StunStars());
    }

    // Estrellas que giran sobre la cabeza mientras está mareado.
    IEnumerator StunStars()
    {
        var stars = new List<GameObject>();
        float top = 3.5f + HitRadius;
        for (int i = 0; i < 5; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(s.GetComponent<Collider>());
            s.transform.localScale = Vector3.one * 0.35f;
            s.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.9f, 0.3f) };
            stars.Add(s);
        }
        while (Stunned && !health.IsDead)
        {
            for (int i = 0; i < stars.Count; i++)
            {
                float a = Time.time * 4f + i * Mathf.PI * 2f / stars.Count;
                stars[i].transform.position = transform.position + new Vector3(Mathf.Cos(a) * 1.4f, top + Mathf.Sin(Time.time * 6f + i) * 0.15f, Mathf.Sin(a) * 1.4f);
            }
            if (visual != null) visual.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 3f) * 6f, 0f, Mathf.Cos(Time.time * 2.5f) * 6f);
            yield return null;
        }
        if (visual != null) visual.localRotation = Quaternion.identity;
        foreach (var s in stars) Destroy(s);
    }

    // Segunda fase (al 50 % de vida): la decide el servidor y la ven todos.
    public NetworkVariable<bool> Phase2Net = new NetworkVariable<bool>(false);
    public bool Phase2 => Phase2Net.Value;
    float nextSpecial;
    int specialCount, projectileIds;

    // BossMechanics la pone en true mientras ejecuta su mecánica especial (el jefe no persigue ni golpea).
    public bool Paused { get; set; }
    public float DamageMultiplier => DamageMult;

    // Para las mecánicas: reproduce una animación en todas las PCs (solo servidor).
    public void TriggerAnim(string trigger) => PlayAnimRpc(trigger);

    void Awake()
    {
        health = GetComponent<Health>();
        visual = transform.Find("Visual");
        serpent = GetComponentInChildren<SerpentMotion>();
    }

    public override void OnNetworkSpawn()
    {
        Active = this;
        Health.Changed += OnAnyHealthChanged;
        islandLevel = GameObject.Find("Isla") != null;   // nivel 2 nuevo: la Yacumama rodea la isla
        if (animator != null && IsFinalBoss) animator.speed = 1.45f;   // jefe final: animaciones más rápidas
        if (!IsServer) return;
        // Más jugadores, más vida: +60 % por cada jugador extra (5 jugadores = 3,4 veces).
        int players = Mathf.Max(1, NetworkManager.ConnectedClientsList.Count);
        // Jugando solo: 60 % de vida y 25 % menos de daño (los compañeros bot ayudan con el resto).
        solo = players == 1;
        PlayerCount = players;
        health.SetMax(data.maxHealth * HealthMult(players));
        // Pruebas de equilibrio: vida enorme para medir cuánto daño por segundo hace el grupo.
        var argv = System.Environment.GetCommandLineArgs();
        int vp = System.Array.IndexOf(argv, "-vidaprueba");
        if (vp >= 0 && vp + 1 < argv.Length && float.TryParse(argv[vp + 1], out float testHp)) health.SetMax(testHp);
        health.OnDied += () => LevelManager.Instance.OnBossDefeated();
        activeAt = Time.time + data.introSeconds + LoadingScreen.Hold + BossEntrance.Duration;   // carga + presentación + entrada
        nextArea = activeAt + data.areaCooldown;
    }

    public override void OnNetworkDespawn()
    {
        if (Active == this) Active = null;
        Health.Changed -= OnAnyHealthChanged;
    }

    public void AddThreat(ulong clientId, float amount)
    {
        threat.TryGetValue(clientId, out float t);
        threat[clientId] = t + amount;
    }

    void Update()
    {
        UpdateVisuals();
        UpdateStun();
        UpdateFury();
        UpdateWeakOrb();
        UpdateIdleVoice();
        UpdateFinalFury();

        // El jefe nunca sale de la arena (ni por embestidas ni por empujones).
        if (IsServer)
        {
            var flat = new Vector3(transform.position.x, 0f, transform.position.z);
            if (flat.magnitude > 42f || transform.position.y < -2f)
                transform.position = Vector3.ClampMagnitude(flat, 42f);
        }

        if (!IsServer || health.IsDead || busy || Paused || Stunned || Time.time < activeAt) return;

        // Segunda fase al 50 % de vida: se transforma y empieza a usar ataques especiales.
        if (!Phase2Net.Value && health.Current.Value / health.Max <= 0.5f)
        {
            Phase2Net.Value = true;
            StartCoroutine(PhaseTransition());
            return;
        }
        if (Phase2Net.Value && Time.time >= nextSpecial)
        {
            nextSpecial = Time.time + 9f * Haste;
            StartCoroutine(SpecialAttack());
            return;
        }

        if (!enraged && health.Current.Value / health.Max <= data.enrageAt)
        {
            enraged = true;
            EnragedNet.Value = true;
            PlayAnimRpc("Enrage");
        }

        var target = PickTarget();
        if (target == null) return;

        if (Time.time >= nextArea)
        {
            StartCoroutine(AreaAttack());
            return;
        }

        // Nivel 2 (isla): la Yacumama nada en círculo alrededor de la isla y ataca desde el agua a quien esté a su alcance.
        if (serpent != null && islandLevel)
        {
            Orbit();
            IslandBigAttacks();
            if (busy) return;
            var toT = Flat(target.transform.position - transform.position);
            if (toT.magnitude <= data.meleeRange + HitRadius && Time.time >= nextMelee)
            {
                nextMelee = Time.time + data.meleeCooldown * Haste;
                transform.rotation = Quaternion.LookRotation(toT);   // se gira hacia la víctima para morder
                StartCoroutine(Melee(target));
            }
            return;
        }

        // A veces persigue a un compañero bot (jugando solo): así no te tiene siempre a ti en la mira.
        if (Time.time >= chaseDecideAt)
        {
            chaseDecideAt = Time.time + Random.Range(4f, 7f);
            botTarget = PickBotChase();
            if (botTarget != null) Debug.Log("OBJETIVO del jefe: bot " + botTarget.name);
        }
        if (botTarget != null && (Time.time < tauntUntil || !BotAlly.All.Contains(botTarget))) botTarget = null;
        if (botTarget != null)
        {
            ChaseBot(botTarget);
            return;
        }

        var toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toTarget), 5f * Time.deltaTime);

        if (toTarget.magnitude > data.meleeRange + HitRadius)
        {
            transform.position += toTarget.normalized * data.moveSpeed * (Furious ? 1.2f : 1f) * (IsFinalBoss ? 1.3f : 1f) * Time.deltaTime;
        }
        else
        {
            if (Time.time >= nextMelee)
            {
                nextMelee = Time.time + data.meleeCooldown * Haste;
                StartCoroutine(Melee(target));
            }
        }
    }

    // Corre en todas las PCs: la velocidad sale de cuánto se movió el jefe (la posición llega por red).
    void UpdateVisuals()
    {
        if (health.IsDead)
        {
            DeathSoundOnce();
            if (serpent != null)
            {
                serpent.Die();
            }
            else if (animator != null)
            {
                if (!deathPlayed) { animator.SetTrigger("Die"); deathPlayed = true; }
            }
            else if (visual != null)
            {
                visual.localRotation = Quaternion.Slerp(visual.localRotation, Quaternion.Euler(-80f, 0f, 0f), 2f * Time.deltaTime);
            }
            return;
        }

        float speed = Time.deltaTime > 0f ? Vector3.Distance(transform.position, lastPosition) / Time.deltaTime : 0f;
        if (animator != null) animator.SetFloat("Speed", speed, 0.1f, Time.deltaTime);
        lastPosition = transform.position;

        // Pisadas pesadas: polvo en los pies y el suelo tiembla si estás cerca.
        if (serpent == null && speed > 0.6f && Time.time >= nextStep && (visual == null || visual.gameObject.activeSelf))
        {
            nextStep = Time.time + 0.55f;
            var foot = transform.position + transform.right * (stepLeft ? -0.8f : 0.8f) * Mathf.Max(1f, HitRadius * 0.4f);
            stepLeft = !stepLeft;
            Effects.Burst(foot + Vector3.up * 0.05f, new Color(0.55f, 0.47f, 0.36f), 10, 2f, 0.55f, 0.7f, 0.1f, flat: true);
            var cam = Camera.main;
            if (cam != null && Vector3.Distance(cam.transform.position, transform.position) < 18f) CameraFollow.Shake(0.06f, 0.08f);
        }

        // Segunda fase: chispas del color del jefe que suben de su cuerpo.
        if (Phase2 && Time.time >= nextPhaseSpark && (visual == null || visual.gameObject.activeSelf))
        {
            nextPhaseSpark = Time.time + 0.25f;
            var c = phase2Color.a > 0f ? phase2Color : SpecialColor;
            Effects.Burst(transform.position + Vector3.up * 1.5f + Random.insideUnitSphere * HitRadius, c, 5, 1.2f, 0.3f, 1f, -0.5f);
        }

        // Furia: aura roja que late y chispas que suben del jefe.
        if (Enraged)
        {
            if (rageLight == null)
            {
                rageLight = new GameObject("Furia").AddComponent<Light>();
                rageLight.transform.SetParent(transform, false);
                rageLight.transform.localPosition = new Vector3(0f, 3f, 0f);
                rageLight.type = LightType.Point;
                rageLight.color = new Color(1f, 0.15f, 0.1f);
                rageLight.range = 12f;
            }
            bool visible = visual == null || visual.gameObject.activeSelf;   // el Pishtaco en las sombras no brilla
            rageLight.intensity = visible ? 3f + Mathf.Sin(Time.time * 6f) * 1.5f : 0f;
            if (visible && Time.time >= nextRageSpark)
            {
                nextRageSpark = Time.time + 0.25f;
                Effects.Burst(transform.position + Vector3.up * 1.5f + Random.insideUnitSphere * HitRadius,
                    new Color(1f, 0.25f, 0.1f), 6, 1.5f, 0.35f, 0.9f, -0.5f);
            }
        }
    }

    bool solo;
    float DamageMult => (enraged ? data.enrageDamageMultiplier : 1f) * (solo ? 0.85f : 1f) * (Furious ? 1.35f : 1f) * GroupDamage * (IsFinalBoss ? 1.2f : 1f);

    // ================================================================ FURIA (si la pelea dura mucho)
    // Se llena con el tiempo; llena = FURIOSO hasta el final: pega más fuerte y ataca más seguido.
    public NetworkVariable<float> FuryNet = new NetworkVariable<float>(0f);
    public bool Furious => FuryNet.Value >= 1f;
    float furyLocal;
    // Meta del nivel: vencerlo antes de que se enfurezca = medalla de oro. La vida base está hecha para que un grupo
    // que hace 100 de daño por segundo tarde vida/100 s; la furia llega al doble de ese tiempo.
    public float GoldSeconds => data.maxHealth / 50f;
    float FurySeconds => GoldSeconds;
    float Haste => (Furious ? 0.65f : 1f) * (PlayerCount >= 4 ? 0.85f : 1f) * (IsFinalBoss ? 0.75f : 1f);   // multiplica los tiempos de espera entre ataques

    void UpdateFury()
    {
        if (!IsServer || health.IsDead || Paused || Time.time < activeAt || furyLocal >= 1f) return;
        furyLocal = Mathf.Min(1f, furyLocal + Time.deltaTime / FurySeconds);
        if (furyLocal >= 1f || furyLocal - FuryNet.Value >= 0.01f) FuryNet.Value = furyLocal;
        if (furyLocal >= 1f) FuriousRpc();
    }

    // Para pruebas automáticas.
    public void TestFury() { if (IsServer) { furyLocal = 0.999f; FuryNet.Value = 0.999f; } }

    [Rpc(SendTo.Everyone)]
    void FuriousRpc()
    {
        BossMechanics.Announcement = $"¡{data.bossName} perdió la paciencia! Está FURIOSO";
        BossMechanics.AnnouncementUntil = Time.time + 3.5f;
        if (animator != null) animator.SetTrigger("Enrage");
        if (serpent != null) serpent.Play("Enrage");
        GameAudio.Play("rugido");
        HUD.Flash(new Color(0.8f, 0.05f, 0.02f, 0.35f), 0.8f);
        CameraFollow.Shake(0.6f, 0.8f);
        AbilityFX.RingAt(transform.position + Vector3.up * 0.1f, new Color(1f, 0.15f, 0.05f), 12f, 1f);
        Effects.Burst(transform.position + Vector3.up * 2f, new Color(0.9f, 0.1f, 0.05f), 90, 9f, 0.6f, 1.2f, -0.3f);
    }

    // ================================================================ ATAQUE CONJUNTO
    ulong lastHitter = ulong.MaxValue;
    float nextJointAt;
    float lastHitAt = -10f;

    // Servidor: registra quién golpeó; true si otro golpeó hace menos de 0,8 s y el jefe está aturdido.
    public bool RegisterHit(ulong who)
    {
        bool joint = Stunned && who != lastHitter && Time.time - lastHitAt <= 0.8f && Time.time >= nextJointAt;
        if (joint) nextJointAt = Time.time + 2.5f;   // como mucho uno cada 2,5 s (en grupos grandes salía siempre)
        lastHitter = who;
        lastHitAt = Time.time;
        return joint;
    }

    [Rpc(SendTo.Everyone)]
    public void JointFxRpc(Vector3 at)
    {
        var c = new Color(1f, 0.95f, 0.75f);
        HUD.AddPopup(at + Vector3.up * 5f, "¡ATAQUE CONJUNTO!", new Color(1f, 0.85f, 0.4f));
        AbilityFX.RingAt(at + Vector3.up * 0.1f, c, 9f, 0.8f);
        AbilityFX.RingAt(at + Vector3.up * 0.1f, new Color(1f, 0.7f, 0.3f), 6f, 0.6f);
        Effects.Burst(at + Vector3.up * 2.5f, c, 80, 11f, 0.5f, 0.8f, 0.3f);
        HUD.Flash(new Color(1f, 0.9f, 0.6f, 0.25f), 0.35f);
        CameraFollow.Shake(0.45f, 0.35f);
        GameAudio.Play("golpe_jefe");
    }

    // ================================================================ PUNTO DÉBIL
    // 0 = ninguno, 1 = de frente (cabeza/pecho), 2 = por la espalda (lomo/cola).
    public NetworkVariable<int> WeakSide = new NetworkVariable<int>(0);
    Transform weakOrb;
    Light weakLight;
    float nextWeakSpark;

    int WeakSideFor() => serpent != null || Kind == MechanicType.Silbido ? 1 : 2;

    // Servidor: tras un ataque grande se queda jadeando y deja ver su punto débil.
    IEnumerator Expose(float seconds)
    {
        if (health.IsDead) yield break;
        int side = WeakSideFor();
        WeakSide.Value = side;
        AnnounceRpc(side == 2 ? "¡Punto débil expuesto! Golpéalo por la ESPALDA (×3)" : "¡Punto débil expuesto! Golpéalo de FRENTE (×3)", 2.8f);
        for (float t = 0f; t < seconds && !health.IsDead; t += Time.deltaTime) yield return null;
        WeakSide.Value = 0;
    }

    public void TestExpose() { if (IsServer) StartCoroutine(Expose(6f)); }

    Vector3 Flat3(Vector3 v) { v.y = 0f; return v; }

    // Servidor: ¿el atacante está del lado del punto débil?
    public bool HitsWeakPoint(Vector3 attacker)
    {
        if (WeakSide.Value == 0) return false;
        var rel = Flat3(attacker - transform.position);
        if (rel.sqrMagnitude < 0.01f) return false;
        var face = Flat3(transform.forward) * (WeakSide.Value == 1 ? 1f : -1f);
        return Vector3.Angle(face, rel) < 70f;
    }

    [Rpc(SendTo.Everyone)]
    public void WeakHitFxRpc()
    {
        var at = weakOrb != null ? weakOrb.position : transform.position + Vector3.up * 2f;
        HUD.AddPopup(at + Vector3.up * 1.5f, "¡PUNTO DÉBIL! ×3", new Color(1f, 0.95f, 0.4f));
        Effects.Burst(at, new Color(1f, 0.9f, 0.4f), 60, 9f, 0.4f, 0.7f, 0.5f);
        AbilityFX.RingAt(transform.position + Vector3.up * 0.1f, new Color(1f, 0.85f, 0.3f), HitRadius + 3f, 0.5f);
        CameraFollow.Shake(0.35f, 0.25f);
        GameAudio.Play("golpe_jefe");
    }

    // Todas las PCs: orbe brillante sobre el punto débil mientras está expuesto.
    void UpdateWeakOrb()
    {
        int side = WeakSide.Value;
        if (side == 0 || health.IsDead)
        {
            if (weakOrb != null) { Destroy(weakOrb.gameObject); weakOrb = null; }
            return;
        }
        if (weakOrb == null)
        {
            weakOrb = new GameObject("Punto débil").transform;
            var mat = new Material(Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default"));
            mat.mainTexture = UIStyle.Tex("brillo");
            mat.SetColor("_TintColor", new Color(1f, 0.85f, 0.35f, 0.9f));
            mat.color = new Color(1f, 0.85f, 0.35f, 0.9f);
            for (int i = 0; i < 2; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.GetComponent<Renderer>().sharedMaterial = mat;
                q.transform.SetParent(weakOrb, false);
                q.transform.localScale = Vector3.one * (i == 0 ? 2.2f : 1f);
            }
            weakLight = weakOrb.gameObject.AddComponent<Light>();
            weakLight.type = LightType.Point;
            weakLight.color = new Color(1f, 0.85f, 0.4f);
            weakLight.range = 7f;
            Effects.Burst(transform.position + Vector3.up * 2f, new Color(1f, 0.85f, 0.4f), 30, 4f, 0.3f, 0.6f);
        }
        float height = serpent != null ? neckHeightGuess : Mathf.Max(1.6f, HitRadius * 1.3f + 0.6f);
        var dir = (side == 1 ? 1f : -1f) * Flat3(transform.forward).normalized;
        var src = serpent != null && side == 1 && visual != null ? transform.position + dir * 2.5f : transform.position + dir * HitRadius * 0.9f;
        weakOrb.position = src + Vector3.up * height;
        var cam = Camera.main;
        if (cam != null) weakOrb.rotation = Quaternion.LookRotation(weakOrb.position - cam.transform.position);
        float pulse = 0.8f + 0.35f * Mathf.Sin(Time.time * 9f);
        weakOrb.localScale = Vector3.one * pulse;
        weakLight.intensity = 3f + 2f * Mathf.Sin(Time.time * 9f);
        if (Time.time >= nextWeakSpark)
        {
            nextWeakSpark = Time.time + 0.15f;
            Effects.Burst(weakOrb.position, new Color(1f, 0.9f, 0.5f), 4, 1.5f, 0.15f, 0.5f, -0.3f);
        }
    }
    const float neckHeightGuess = 4.2f;

    // Ataca al jugador vivo que más amenaza ha generado (el Guardián debería "tanquear").
    // A quién ataca: mezcla la amenaza (quién le hace más daño) con la cercanía y un poco de azar, y cambia
    // de objetivo cada 4–7 s. Así no persigue siempre al mismo: también va por el más cercano y por los bots.
    // La Provocación del Guardián lo obliga a ir por él unos segundos.
    Health currentTarget, tauntTarget;
    float retargetAt, tauntUntil;

    public void Taunt(Health who, float seconds)
    {
        if (!IsServer || who == null) return;
        tauntTarget = who;
        tauntUntil = Time.time + seconds;
        currentTarget = who;
        retargetAt = Time.time + seconds;
    }

    // ---- Nivel 2: nado en círculo alrededor de la isla
    bool islandLevel;
    float orbitDir = 1f, nextOrbitFlip;
    const float OrbitRadius = 12.5f, OrbitSpeed = 6f;   // m/s: un poco más rápida que un héroe corriendo

    void Orbit()
    {
        if (Time.time >= nextOrbitFlip)
        {
            if (nextOrbitFlip > 0f && Random.value < 0.5f) orbitDir = -orbitDir;   // a veces cambia de sentido
            nextOrbitFlip = Time.time + Random.Range(7f, 13f);
        }
        var p = Flat(transform.position);
        float ang = Mathf.Atan2(p.x, p.z) + orbitDir * OrbitSpeed * (Furious ? 1.2f : 1f) / OrbitRadius * Time.deltaTime;
        var np = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * OrbitRadius;
        // Vuelve suave al círculo si un ataque o la mecánica la dejaron en otro sitio.
        var next = Vector3.MoveTowards(p, np, OrbitSpeed * 1.5f * Time.deltaTime + (np - p).magnitude * 2f * Time.deltaTime);
        transform.position = new Vector3(next.x, transform.position.y, next.z);
        var tangent = new Vector3(Mathf.Cos(ang), 0f, -Mathf.Sin(ang)) * orbitDir;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(tangent), 6f * Time.deltaTime);
    }

    // ---- Bots como objetivo (no tienen vida: esquivan el golpe, pero el jefe se distrae con ellos)
    BotAlly botTarget;
    float chaseDecideAt;

    BotAlly PickBotChase()
    {
        int bots = 0, players = 0;
        foreach (var b in BotAlly.All) if (b != null) bots++;
        foreach (var c in NetworkManager.ConnectedClientsList) if (c.PlayerObject != null) players++;
        if (bots == 0 || Random.value > 0.6f * bots / (float)(bots + players)) return null;
        // El bot más cercano tiene más probabilidad.
        BotAlly best = null;
        float bestScore = float.MinValue;
        foreach (var b in BotAlly.All)
        {
            if (b == null) continue;
            float score = -Vector3.Distance(Flat3(b.transform.position), Flat3(transform.position)) + Random.Range(0f, 8f);
            if (score > bestScore) { bestScore = score; best = b; }
        }
        return best;
    }

    void ChaseBot(BotAlly bot)
    {
        var to = Flat3(bot.transform.position - transform.position);
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 5f * Time.deltaTime);
        if (to.magnitude > data.meleeRange + HitRadius)
            transform.position += to.normalized * data.moveSpeed * (Furious ? 1.2f : 1f) * Time.deltaTime;
        else if (Time.time >= nextMelee)
        {
            nextMelee = Time.time + data.meleeCooldown * Haste;
            StartCoroutine(SwipeAtBot(bot));
        }
    }

    IEnumerator SwipeAtBot(BotAlly bot)
    {
        busy = true;
        AttackMoveRpc(Random.Range(0, 2));
        PlayAnimRpc("Attack");
        yield return new WaitForSeconds(T(MeleeImpact));
        if (bot != null)
        {
            bot.DodgeFrom(transform.position);
            ImpactRpc(bot.transform.position);
        }
        yield return new WaitForSeconds(T(MeleeRecover));
        busy = false;
    }

    Health PickTarget()
    {
        if (tauntTarget != null && !tauntTarget.IsDead && Time.time < tauntUntil) return tauntTarget;
        if (currentTarget != null && !currentTarget.IsDead && Time.time < retargetAt) return currentTarget;

        var candidates = new List<(Health h, float threat)>();
        float maxThreat = 0f;
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            var h = client.PlayerObject.GetComponent<Health>();
            if (h == null || h.IsDead) continue;
            threat.TryGetValue(client.ClientId, out float t);
            candidates.Add((h, t));
            maxThreat = Mathf.Max(maxThreat, t);
        }

        Health best = null;
        float bestScore = float.MinValue;
        foreach (var (h, t) in candidates)
        {
            float dist = Vector3.Distance(Flat3(h.transform.position), Flat3(transform.position));
            float score = (maxThreat > 0f ? t / maxThreat : 0f) * 0.5f      // amenaza
                        + (1f - Mathf.Clamp01(dist / 25f)) * 0.45f            // cercanía
                        + Random.Range(0f, 0.3f)                              // azar
                        - (h == currentTarget ? 0.15f : 0f);                  // prefiere cambiar de víctima
            if (score > bestScore) { bestScore = score; best = h; }
        }
        if (best != currentTarget && best != null) Debug.Log("OBJETIVO del jefe: " + best.name);
        currentTarget = best;
        retargetAt = Time.time + Random.Range(4f, 7f);
        return best;
    }

    // Ataques cuerpo a cuerpo (3 variantes al azar). El daño llega en el impacto: si el jugador
    // esquivó o se alejó a tiempo, falla.
    //  0 Golpe: se echa atrás y se lanza hacia adelante.
    //  1 Zarpazo doble: dos golpes rápidos (izquierda y derecha), cada uno con su daño.
    //  2 Salto aplastante: salta y cae con una onda que daña a todos alrededor.
    const float MeleeImpact = 0.55f, MeleeRecover = 0.35f;

    IEnumerator Melee(Health target)
    {
        busy = true;
        int variant = PickAttack();
        if (variant >= 3)
        {
            yield return BigAttack(variant, target);
            // Jefe final: a veces encadena otro ataque grande sin pausa (más seguido en la fase 2).
            if (IsFinalBoss && !health.IsDead && Random.value < (Phase2 ? 0.55f : 0.35f))
            {
                int[] next = { 3, 5, 7, 8, 9, 10, 11, 13, 14, 15 };
                int v2 = next[Random.Range(0, next.Length)];
                if (v2 == variant) v2 = variant == 7 ? 3 : 7;
                yield return BigAttack(v2, PickTarget());
            }
            busy = false;
            yield break;
        }
        AttackMoveRpc(variant);
        PlayAnimRpc("Attack");

        if (variant == 1)
        {
            yield return new WaitForSeconds(T(0.4f));
            HitIfInRange(target, 0.6f);
            PlayAnimRpc("Attack");
            yield return new WaitForSeconds(T(0.4f));
            HitIfInRange(target, 0.6f);
        }
        else if (variant == 2)
        {
            yield return new WaitForSeconds(T(0.75f));
            // Onda al caer: daña a todos los que estén cerca del jefe.
            float radius = HitRadius + 2.6f;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                var h = client.PlayerObject != null ? client.PlayerObject.GetComponent<Health>() : null;
                if (h == null || h.IsDead) continue;
                var d = h.transform.position - transform.position; d.y = 0f;
                if (d.magnitude <= radius) h.TakeDamage(data.meleeDamage * 1.2f * DamageMult, NetworkObjectId);
            }
            SlamRpc(transform.position, radius);
        }
        else
        {
            yield return new WaitForSeconds(T(MeleeImpact));
            HitIfInRange(target, 1f);
        }
        yield return new WaitForSeconds(T(MeleeRecover));
        busy = false;
    }

    // Para pruebas automáticas: fuerza un ataque grande (3 a 5) o el ataque en área (6).
    public void TestAttack(int variant)
    {
        if (!IsServer) return;
        if (variant == 6) StartCoroutine(AreaAttack());
        else StartCoroutine(BigAttack(variant, PickTarget()));
    }

    // Elige el ataque: los básicos son más frecuentes; los grandes (3 a 5) se alternan de vez en cuando.
    //  0 golpe · 1 zarpazo doble · 2 salto aplastante · 3 barrido giratorio · 4 rugido · 5 salto sobre el jugador
    int lastBig = -1;
    float nextBigAt;

    int PickAttack()
    {
        if (Time.time >= nextBigAt && Random.value < 0.45f)
        {
            int[] bigs = serpent != null ? new[] { 3, 4 } : IsFinalBoss ? new[] { 3, 4, 5, 7, 8, 9, 10, 11, 11, 12, 13, 13, 14, 15 } : System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(new[] { 3, 4, 5 }, ExtraBigs));   // 7 = caída de las sombras (solo el Pishtaco)
            int pick;
            do pick = bigs[Random.Range(0, bigs.Length)]; while (pick == lastBig && bigs.Length > 1);
            lastBig = pick;
            nextBigAt = Time.time + (IsFinalBoss ? 4.5f : 6f);
            return pick;
        }
        return serpent != null ? Random.Range(0, 2) : Random.Range(0, 3);
    }

    // ================================================================ JEFE FINAL (Pishtaco): todo más rápido
    // Sus ataques avisan menos y golpean antes: hay que esquivar con reflejos. Sus animaciones también van más rápido.
    public bool IsFinalBoss => data != null && data.bossName.Contains("Pishtaco");
    float Tempo => IsFinalBoss ? (finalFury ? 1.9f : 1.6f) : 1f;
    bool finalFury;
    float T(float seconds) => seconds / Tempo;

    // Caída de las sombras: salta muy alto, se vuelve una bola de oscuridad brillante y cae sobre un jugador.
    // En la fase 2 caen hasta 3 bolas seguidas (la suya y dos sombras más sobre otros jugadores o alrededor).
    IEnumerator DarkMeteor(Health target)
    {
        if (target == null) yield break;
        var land = Flat(target.transform.position);
        if (land.magnitude > 40f) land = land.normalized * 40f;
        const float radius = 4.6f, warn = 1.1f, gap = 0.35f;
        var lands = new List<Vector3> { land };
        if (Phase2)
        {
            foreach (var h in AlivePlayers())
                if (lands.Count < 3 && h != target) lands.Add(Flat(h.transform.position));
            while (lands.Count < 3)
                lands.Add(land + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * Random.Range(5f, 8f));
        }
        AnnounceRpc(lands.Count > 1 ? "¡Lluvia de sombras! ¡Muévete!" : "¡El Pishtaco cae desde las sombras!", 2f);
        PlayAnimRpc("Cast");
        for (int i = 0; i < lands.Count; i++)
        {
            float delay = warn + i * gap;
            DarkMeteorRpc(transform.position, lands[i], delay, i == 0);
            StartCoroutine(MeteorImpact(lands[i], radius, delay));
        }
        yield return new WaitForSeconds(warn);
        transform.position = land;
        yield return new WaitForSeconds((lands.Count - 1) * gap + 0.4f);
        if (Random.value < 0.25f) yield return Expose(2.5f);
    }

    IEnumerator MeteorImpact(Vector3 at, float radius, float delay)
    {
        yield return new WaitForSeconds(delay);
        foreach (var h in AlivePlayers())
            if (Flat(h.transform.position - at).magnitude <= radius)
                h.TakeDamage(data.areaDamage * 1.1f * DamageMult, NetworkObjectId);
        DarkImpactRpc(at, radius);
    }

    // ---- Más ataques del jefe final ----
    // 8 · Abanico de cuchillos: dos ráfagas de 7 cuchillos en abanico hacia un jugador (la segunda desfasada).
    IEnumerator KnifeFan(Health target)
    {
        if (target == null) yield break;
        AnnounceRpc("¡Abanico de cuchillos!", 1.5f);
        var aim = Flat(target.transform.position - transform.position).normalized;
        if (aim.sqrMagnitude < 0.01f) aim = transform.forward;
        transform.rotation = Quaternion.LookRotation(aim);
        BigWarnRpc(4, transform.position, aim, 12f, T(0.6f));
        yield return new WaitForSeconds(T(0.6f));
        for (int volley = 0; volley < 2; volley++)
        {
            PlayAnimRpc("Attack");
            Voice("_cuchillo", 0.8f);
            var origin = transform.position + Vector3.up * 2.2f + transform.forward * HitRadius;
            var shots = new List<(int id, Vector3 dir)>();
            for (int i = 0; i < 7; i++)
            {
                float ang = -36f + i * 12f + (volley == 1 ? 6f : 0f);
                var dir = Quaternion.Euler(0, ang, 0) * aim;
                int id = ++projectileIds;
                shots.Add((id, dir));
                ProjectileRpc(id, origin, dir, SpecialColor, true);
            }
            StartCoroutine(KnifeHits(origin, shots));
            yield return new WaitForSeconds(T(0.45f));
        }
        yield return new WaitForSeconds(T(0.3f));
    }

    IEnumerator KnifeHits(Vector3 origin, List<(int id, Vector3 dir)> shots)
    {
        const float speed = 11f, life = 3.5f;
        var alive = new HashSet<int>();
        foreach (var s in shots) alive.Add(s.id);
        for (float t = 0; t < life; t += Time.deltaTime)
        {
            foreach (var s in shots)
            {
                if (!alive.Contains(s.id)) continue;
                var p = origin + s.dir * speed * t; p.y = 1.2f;
                foreach (var h in AlivePlayers())
                    if (Vector3.Distance(Flat(h.transform.position), Flat(p)) < 1f)
                    {
                        h.TakeDamage(data.areaDamage * 0.4f * DamageMult, NetworkObjectId);
                        alive.Remove(s.id);
                        ProjectileHitRpc(s.id, p);
                        break;
                    }
            }
            yield return null;
        }
    }

    // 9 · Tajo cruzado: embiste atravesando al jugador y, sin pausa, se da vuelta y vuelve a embestir.
    IEnumerator CrossDash(Health target)
    {
        if (target == null || data.moveSpeed <= 0f) yield break;
        AnnounceRpc("¡Tajo cruzado! Apártate dos veces", 1.6f);
        for (int pass = 0; pass < 2; pass++)
        {
            if (target == null || target.IsDead) target = PickTarget();
            if (target == null) yield break;
            var start = transform.position;
            var dir = Flat(target.transform.position - start).normalized;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            float length = Mathf.Min(18f, Flat(target.transform.position - start).magnitude + 6f);
            var end = start + dir * length;
            if (Flat(end).magnitude > 40f) { end = start + dir * Mathf.Max(4f, 40f - Flat(start).magnitude); length = Vector3.Distance(start, end); }
            transform.rotation = Quaternion.LookRotation(dir);
            ChargeWarnRpc(start, dir, length, 3f, T(pass == 0 ? 0.75f : 0.55f));
            yield return new WaitForSeconds(T(pass == 0 ? 0.75f : 0.55f));
            PlayAnimRpc("Attack");
            AttackMoveRpc(0);
            var hit = new HashSet<Health>();
            for (float t = 0; t < 0.3f; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, end, t / 0.3f);
                foreach (var h in AlivePlayers())
                {
                    if (hit.Contains(h)) continue;
                    var rel = Flat(h.transform.position - start);
                    float along = Vector3.Dot(rel, dir);
                    float side = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, dir)));
                    if (side < 1.5f && along > -1f && along < Vector3.Distance(start, transform.position) + 1f)
                    {
                        hit.Add(h);
                        h.TakeDamage(data.meleeDamage * 1.4f * DamageMult, NetworkObjectId);
                        ImpactRpc(h.transform.position);
                    }
                }
                yield return null;
            }
            transform.position = end;
        }
        yield return new WaitForSeconds(T(0.4f));
    }

    // 10 · Anillos de sombra: dos anillos de runas que estallan uno tras otro (cerca del jefe y más lejos).
    //      Hay que quedarse entre los huecos o cambiar de anillo a tiempo.
    IEnumerator ShadowRings()
    {
        AnnounceRpc("¡Anillos de sombra! Busca los huecos", 1.8f);
        PlayAnimRpc("Cast");
        var center = transform.position;
        for (int ring = 0; ring < 2; ring++)
        {
            float r = ring == 0 ? 5f : 9.5f;
            int n = ring == 0 ? 7 : 11;
            float offset = Random.Range(0f, 360f);
            var spots = new List<Vector3>();
            for (int i = 0; i < n; i++)
                spots.Add(center + Quaternion.Euler(0, offset + i * 360f / n, 0) * Vector3.forward * r);
            float warn = T(1.1f);
            foreach (var s in spots) ShadowRuneRpc(s, 2.3f, warn);
            yield return new WaitForSeconds(warn);
            foreach (var h in AlivePlayers())
                foreach (var s in spots)
                    if (Flat(h.transform.position - s).magnitude <= 2.3f) { h.TakeDamage(data.areaDamage * 0.7f * DamageMult, NetworkObjectId); break; }
            ShadowBurstRpc(spots.ToArray());
            yield return new WaitForSeconds(T(0.25f));
        }
    }

    [Rpc(SendTo.Everyone)]
    void ShadowRuneRpc(Vector3 at, float radius, float warn) => AbilityFX.RuneWarning(at, radius, warn, new Color(0.55f, 0.05f, 0.75f));

    [Rpc(SendTo.Everyone)]
    void ShadowBurstRpc(Vector3[] spots)
    {
        GameAudio.PlayAt("area", spots[0], 0.9f);
        foreach (var s in spots)
        {
            Effects.Burst(s + Vector3.up * 0.4f, new Color(0.6f, 0.12f, 0.95f), 26, 6f, 0.5f, 0.7f, -0.2f);
            Effects.Burst(s + Vector3.up * 0.2f, new Color(0.05f, 0f, 0.08f), 14, 4f, 0.8f, 0.8f, -0.4f, flat: true);
        }
        GroundCracks.ShakeAt(spots[0], 0.5f, 0.4f, 30f);
    }

    [Rpc(SendTo.Everyone)]
    void DarkMeteorRpc(Vector3 from, Vector3 land, float warn, bool isBoss)
    {
        AbilityFX.RuneWarning(land, 4.6f, warn, new Color(0.55f, 0.05f, 0.75f));
        if (isBoss) { GameAudio.PlayAt("rugido", from, 0.8f); Voice("_risa", 1f, 0f); }
        StartCoroutine(DarkMeteorFlight(from, land, warn, isBoss));
    }
    IEnumerator DarkMeteorFlight(Vector3 from, Vector3 land, float warn, bool isBoss)
    {
        // La bola: núcleo negro, halo morado brillante y una luz que tiñe el suelo.
        var orb = new GameObject("Bola de oscuridad").transform;
        var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(core.GetComponent<Collider>());
        core.transform.SetParent(orb, false);
        core.transform.localScale = Vector3.one * 1.6f;
        core.GetComponent<Renderer>().material = new Material(Shader.Find("Standard")) { color = new Color(0.03f, 0f, 0.05f) };
        var glowMat = new Material(Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default")) { mainTexture = UIStyle.Tex("brillo") };
        glowMat.SetColor("_TintColor", new Color(0.7f, 0.15f, 1f, 0.9f));
        var halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(halo.GetComponent<Collider>());
        halo.transform.SetParent(orb, false);
        halo.transform.localScale = Vector3.one * 5f;
        halo.GetComponent<Renderer>().material = glowMat;
        var light = orb.gameObject.AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(0.7f, 0.2f, 1f); light.range = 16f; light.intensity = 5f;
        if (isBoss && visual != null) visual.gameObject.SetActive(false);   // el Pishtaco "se vuelve" la bola

        float rise = warn * 0.45f;
        var top = Vector3.Lerp(from, land, 0.35f) + Vector3.up * 8f;   // a la vista de la cámara
        var cam = Camera.main;
        float nextTrail = 0f;
        for (float t = 0; t < warn; t += Time.deltaTime)
        {
            Vector3 p;
            if (t < rise) p = Vector3.Lerp(from + Vector3.up * 2f, top, Mathf.SmoothStep(0, 1, t / rise));
            else { float k = (t - rise) / (warn - rise); p = Vector3.Lerp(top, land + Vector3.up * 1f, k * k); }   // cae acelerando
            orb.position = p;
            if (cam != null) halo.transform.rotation = Quaternion.LookRotation(p - cam.transform.position);
            float pulse = 1f + 0.15f * Mathf.Sin(Time.time * 20f);
            halo.transform.localScale = Vector3.one * 5f * pulse;
            if (Time.time >= nextTrail)
            {
                nextTrail = Time.time + 0.04f;
                Effects.Burst(p, new Color(0.45f, 0.05f, 0.7f, 0.9f), 6, 1.5f, 0.6f, 0.7f, 0f);
                Effects.Burst(p, new Color(0.05f, 0f, 0.08f, 0.9f), 4, 1f, 0.9f, 0.9f, -0.2f);
            }
            yield return null;
        }
        Destroy(orb.gameObject);
        if (isBoss && visual != null) visual.gameObject.SetActive(true);
    }

    [Rpc(SendTo.Everyone)]
    void DarkImpactRpc(Vector3 at, float radius)
    {
        var purple = new Color(0.65f, 0.15f, 1f);
        GameAudio.PlayAt("area", at, 1f);
        GameAudio.PlayAt("muerte_jefe", at, 0.5f);
        Effects.Burst(at + Vector3.up * 0.5f, purple, 120, 14f, 0.7f, 1f, 0.2f);
        Effects.Burst(at + Vector3.up * 0.3f, new Color(0.05f, 0f, 0.08f), 80, 10f, 1.2f, 1.2f, -0.3f, flat: true);
        AbilityFX.RingAt(at + Vector3.up * 0.1f, purple, radius * 2.2f, 0.8f);
        AbilityFX.RingAt(at + Vector3.up * 0.1f, new Color(1f, 0.3f, 0.6f), radius * 1.4f, 0.6f);
        GroundCracks.Open(at, radius, purple);
        GroundCracks.ShakeAt(at, 1f, 0.7f, 40f);
        HUD.Flash(new Color(0.4f, 0.05f, 0.6f, 0.3f), 0.5f);
    }

    // Ataques grandes, todos con runa de aviso.
    IEnumerator BigAttack(int variant, Health target)
    {
        if (variant == 7) { yield return DarkMeteor(target); yield break; }
        if (variant == 8) { yield return KnifeFan(target); yield break; }
        if (variant == 9) { yield return CrossDash(target); yield break; }
        if (variant == 10) { yield return ShadowRings(); yield break; }
        if (variant == 11) { yield return Chains(target); yield break; }
        if (variant == 12) { yield return Eclipse(); yield break; }
        if (variant == 13) { yield return DeathWhirl(target); yield break; }
        if (variant == 14) { yield return ShadowClones(target); yield break; }
        if (variant == 15) { yield return KnifeRain(); yield break; }
        if (variant >= 20 && variant <= 25) { yield return ExtraAttack(variant, target); yield break; }
        if (variant == 16) { yield return TailSwipe(); yield break; }
        if (variant == 17) { yield return Wave(); yield break; }
        if (variant == 18) { yield return Geysers(); yield break; }
        if (variant == 19) { yield return IslandCharge(target); yield break; }
        var red = new Color(1f, 0.12f, 0.06f);
        if (variant == 3)
        {
            // Barrido giratorio: runa alrededor del jefe, gira 360° y golpea a todos los que estén cerca.
            float radius = HitRadius + 3.2f;
            BigWarnRpc(3, transform.position, transform.forward, radius, T(0.9f));
            yield return new WaitForSeconds(T(0.9f));
            AttackMoveRpc(3);
            PlayAnimRpc("Attack");
            yield return new WaitForSeconds(T(0.35f));
            foreach (var h in AlivePlayers())
                if (Flat(h.transform.position - transform.position).magnitude <= radius)
                {
                    h.TakeDamage(data.meleeDamage * 1.3f * DamageMult, NetworkObjectId);
                    ImpactRpc(h.transform.position);
                }
            yield return new WaitForSeconds(T(0.6f));
            // Tras el barrido queda mareado: punto débil expuesto.
            if (Random.value < (IsFinalBoss ? 0.32f : 0.65f)) yield return Expose(3.5f);
        }
        else if (variant == 4)
        {
            // Rugido: franja de runas al frente; se alza, ruge y lanza una onda en cono.
            var dir = target != null ? Flat(target.transform.position - transform.position).normalized : transform.forward;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            transform.rotation = Quaternion.LookRotation(dir);
            float length = serpent != null ? 14f : 10f;
            BigWarnRpc(4, transform.position, dir, length, T(1f));
            yield return new WaitForSeconds(T(1f));
            AttackMoveRpc(4);
            PlayAnimRpc("Cast");
            yield return new WaitForSeconds(T(0.3f));
            RoarWaveRpc(transform.position, dir, length);
            foreach (var h in AlivePlayers())
            {
                var rel = Flat(h.transform.position - transform.position);
                if (rel.magnitude <= length + HitRadius && Vector3.Angle(dir, rel) < 32f)
                    h.TakeDamage(data.areaDamage * 0.8f * DamageMult, NetworkObjectId);
            }
            yield return new WaitForSeconds(T(0.8f));
        }
        else
        {
            // Salto sobre el jugador: runa bajo sus pies, el jefe salta en arco y cae ahí.
            if (target == null || data.moveSpeed <= 0f) yield break;
            var land = Flat(target.transform.position);
            if (land.magnitude > 40f) land = land.normalized * 40f;
            float radius = 3.6f;
            BigWarnRpc(5, land, transform.forward, radius, T(1.2f));
            yield return new WaitForSeconds(T(0.5f));
            PlayAnimRpc("Attack");
            var from = transform.position;
            transform.rotation = Quaternion.LookRotation(Flat(land - from).sqrMagnitude > 0.01f ? Flat(land - from) : transform.forward);
            LeapRpc(from, land, T(0.7f));
            yield return new WaitForSeconds(T(0.7f));
            transform.position = land;
            foreach (var h in AlivePlayers())
                if (Flat(h.transform.position - land).magnitude <= radius + 0.5f)
                    h.TakeDamage(data.meleeDamage * 1.6f * DamageMult, NetworkObjectId);
            SlamRpc(land, radius);
            yield return new WaitForSeconds(T(0.7f));
            // Al caer del salto queda expuesto.
            if (Random.value < (IsFinalBoss ? 0.32f : 0.65f)) yield return Expose(3.5f);
        }
    }

    [Rpc(SendTo.Everyone)]
    void BigWarnRpc(int variant, Vector3 at, Vector3 dir, float size, float duration)
    {
        var red = new Color(0.95f, 0.04f, 0.02f);
        if (variant == 4) AbilityFX.LineWarning(at, dir, size, 7f, duration);
        else AbilityFX.RuneWarning(at, size, duration, red);
        GameAudio.Play(variant == 4 ? "rugido" : "area");
    }

    // Salto en arco (solo visual en los clientes; el servidor coloca al jefe al aterrizar).
    [Rpc(SendTo.Everyone)]
    void LeapRpc(Vector3 from, Vector3 to, float duration)
    {
        if (visual != null) StartCoroutine(LeapMotion(from, to, duration));
    }

    IEnumerator LeapMotion(Vector3 from, Vector3 to, float duration)
    {
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            if (IsServer) transform.position = Vector3.Lerp(from, to, k);
            visual.localPosition = Vector3.up * Mathf.Sin(k * Mathf.PI) * 5f;
            visual.localRotation = Quaternion.Euler(Mathf.Lerp(-20f, 25f, k), 0f, 0f);
            yield return null;
        }
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
    }

    // Onda del rugido: polvo y anillos que avanzan en cono.
    [Rpc(SendTo.Everyone)]
    void RoarWaveRpc(Vector3 from, Vector3 dir, float length) => StartCoroutine(RoarWave(from, dir, length));

    IEnumerator RoarWave(Vector3 from, Vector3 dir, float length)
    {
        CameraFollow.Shake(0.35f, 0.5f);
        var c = serpent != null ? new Color(0.5f, 0.85f, 1f) : new Color(1f, 0.6f, 0.35f);
        for (int i = 1; i <= 6; i++)
        {
            var p = from + dir * (length * i / 6f) + Vector3.up * 0.3f;
            Effects.Burst(p, c, 18, 3f + i, 0.5f + i * 0.08f, 0.6f, 0.3f, flat: true);
            AbilityFX.RingAt(p, c, 1.2f + i * 0.5f, 0.4f);
            yield return new WaitForSeconds(0.05f);
        }
    }

    void HitIfInRange(Health target, float mult)
    {
        if (health.IsDead || target == null || target.IsDead) return;
        var d = target.transform.position - transform.position;
        d.y = 0f;
        if (d.magnitude > data.meleeRange + HitRadius + 0.6f) return;
        target.TakeDamage(data.meleeDamage * mult * DamageMult, NetworkObjectId);
        ImpactRpc(target.transform.position);
    }

    // Movimiento del cuerpo del jefe durante el ataque (en todas las PCs, se suma a la animación).
    [Rpc(SendTo.Everyone)]
    void AttackMoveRpc(int variant)
    {
        if (visual == null || serpent != null) return;
        StopCoroutine(nameof(AttackMotion));
        StartCoroutine(AttackMotion(variant));
    }

    IEnumerator AttackMotion(int variant)
    {
        var basePos = Vector3.zero;
        var baseRot = Quaternion.identity;
        float size = Mathf.Max(1f, HitRadius * 0.6f);
        var slashColor = Enraged ? new Color(1f, 0.25f, 0.15f) : new Color(1f, 0.85f, 0.6f);
        if (variant == 1)
        {
            // Zarpazo doble: gira a un lado y al otro con dos estelas.
            for (int s = 0; s < 2; s++)
            {
                float dir = s == 0 ? 1f : -1f;
                AbilityFX.BossSlash(transform, slashColor, HitRadius + 1.6f, dir, 0.22f);
                for (float t = 0; t < 0.4f; t += Time.deltaTime)
                {
                    float k = Mathf.Sin(t / 0.4f * Mathf.PI);
                    visual.localRotation = baseRot * Quaternion.Euler(10f * k, 28f * dir * k, -6f * dir * k);
                    visual.localPosition = basePos + Vector3.forward * 0.5f * size * k;
                    yield return null;
                }
            }
        }
        else if (variant == 3)
        {
            // Barrido giratorio: se agacha y gira una vuelta completa con estelas a los lados.
            for (float t = 0; t < 0.9f; t += Time.deltaTime)
            {
                float spin = t < 0.15f ? 0f : Mathf.SmoothStep(0f, 360f, (t - 0.15f) / 0.5f);
                visual.localRotation = baseRot * Quaternion.Euler(t < 0.15f ? 10f * t / 0.15f : 10f, spin, 0f);
                if (t >= 0.2f && t - Time.deltaTime < 0.2f)
                {
                    AbilityFX.BossSlash(transform, slashColor, HitRadius + 3f, 1f, 0.45f);
                    AbilityFX.RingAt(transform.position, slashColor, HitRadius + 3.2f, 0.5f);
                }
                yield return null;
            }
        }
        else if (variant == 4)
        {
            // Rugido: se echa atrás alzándose y luego se proyecta hacia adelante.
            for (float t = 0; t < 1.1f; t += Time.deltaTime)
            {
                float k = t < 0.35f ? t / 0.35f : 1f - Mathf.Clamp01((t - 0.35f) / 0.75f);
                visual.localRotation = baseRot * Quaternion.Euler(-22f * k + (t > 0.35f ? 12f * Mathf.Sin((t - 0.35f) * 20f) * (1f - k) : 0f), 0f, 0f);
                visual.localScale = Vector3.one * (1f + 0.08f * k);
                yield return null;
            }
            visual.localScale = Vector3.one;
        }
        else if (variant == 2)
        {
            // Salto aplastante: se agacha, salta y cae de golpe.
            for (float t = 0; t < 0.75f; t += Time.deltaTime)
            {
                float y, sq;
                if (t < 0.25f) { float k = t / 0.25f; y = 0f; sq = 1f - 0.12f * k; }
                else if (t < 0.6f) { float k = (t - 0.25f) / 0.35f; y = Mathf.Sin(k * Mathf.PI * 0.5f) * 1.8f * size; sq = 1.08f; }
                else { float k = (t - 0.6f) / 0.15f; y = Mathf.Lerp(1.8f * size, 0f, k * k); sq = 1.08f - 0.15f * k; }
                visual.localPosition = basePos + Vector3.up * y;
                visual.localScale = new Vector3(2f - sq, sq, 2f - sq);
                yield return null;
            }
            for (float t = 0; t < 0.25f; t += Time.deltaTime)
            {
                float k = t / 0.25f;
                float sq = Mathf.Lerp(0.93f, 1f, k);
                visual.localScale = new Vector3(2f - sq, sq, 2f - sq);
                yield return null;
            }
            visual.localScale = Vector3.one;
        }
        else
        {
            // Golpe: carga hacia atrás y se lanza.
            for (float t = 0; t < 0.9f; t += Time.deltaTime)
            {
                float lean, push;
                if (t < 0.4f) { float k = t / 0.4f; lean = -12f * k; push = -0.3f * k; }
                else if (t < 0.6f) { float k = (t - 0.4f) / 0.2f; lean = Mathf.Lerp(-12f, 18f, k); push = Mathf.Lerp(-0.3f, 1f, k); }
                else { float k = (t - 0.6f) / 0.3f; lean = Mathf.Lerp(18f, 0f, k); push = Mathf.Lerp(1f, 0f, k); }
                if (t >= 0.5f && t - Time.deltaTime < 0.5f) AbilityFX.BossSlash(transform, slashColor, HitRadius + 1.6f, 1f, 0.2f);
                visual.localRotation = baseRot * Quaternion.Euler(lean, 0f, 0f);
                visual.localPosition = basePos + Vector3.forward * push * size;
                yield return null;
            }
        }
        visual.localPosition = basePos;
        visual.localRotation = baseRot;
    }

    // ================================================================ SEGUNDA FASE
    MechanicType Kind => GetComponent<BossMechanics>() != null ? GetComponent<BossMechanics>().type : MechanicType.Ninguna;

    // Color y nombre de los ataques especiales de cada jefe.
    Color SpecialColor => Kind switch
    {
        MechanicType.Clones => new Color(0.55f, 0.75f, 0.2f),
        MechanicType.Sumergirse => new Color(0.3f, 0.7f, 1f),
        MechanicType.Encantamiento => new Color(1f, 0.45f, 0.7f),
        MechanicType.Silbido => new Color(0.65f, 0.3f, 1f),
        _ => new Color(0.85f, 0.85f, 0.95f),
    };

    IEnumerator PhaseTransition()
    {
        busy = true;
        PlayAnimRpc("Enrage");
        PhaseRpc();
        yield return new WaitForSeconds(2.2f);
        busy = false;
        nextSpecial = Time.time + 3f;
    }

    [Rpc(SendTo.Everyone)]
    void PhaseRpc()
    {
        GameAudio.Play("rugido");
        BossMechanics.Announcement = $"¡SEGUNDA FASE! {data.bossName} se transforma";
        BossDialogue.Say(this, "fase2", true);
        BossMechanics.AnnouncementUntil = Time.time + 3f;
        var c = SpecialColor;
        Effects.Burst(transform.position + Vector3.up * 2.5f, c, 120, 10f, 0.8f, 1.4f, -0.3f);
        AbilityFX.RingAt(transform.position, c, 14f, 1f);
        CameraFollow.Shake(0.5f, 0.6f);
        StartCoroutine(Grow());
        // Aura del color del jefe.
        var aura = new GameObject("Aura fase 2").AddComponent<Light>();
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.up * 2.5f;
        aura.color = c;
        aura.range = 12f;
        aura.intensity = 2.5f;
        phase2Color = c;
    }

    Color phase2Color;
    float nextPhaseSpark;

    // Crece un 15 % en un segundo (se escalan los modelos dentro de "Visual").
    IEnumerator Grow()
    {
        if (visual == null) yield break;
        var children = new List<Transform>();
        var baseScales = new List<Vector3>();
        foreach (Transform ch in visual) { children.Add(ch); baseScales.Add(ch.localScale); }
        for (float t = 0; t < 1f; t += Time.deltaTime)
        {
            float k = 1f + 0.15f * Mathf.SmoothStep(0f, 1f, t);
            for (int i = 0; i < children.Count; i++) if (children[i] != null) children[i].localScale = baseScales[i] * k;
            yield return null;
        }
    }

    IEnumerator SpecialAttack()
    {
        specialCount++;
        bool first = specialCount % 2 == 1;
        switch (Kind)
        {
            case MechanicType.Clones: yield return first ? Zones("¡Brotan raíces del suelo! Sal de los círculos", true) : Charge("¡El Chullachaqui embiste!"); break;
            case MechanicType.Sumergirse: yield return first ? Projectiles("¡La Yacumama escupe agua!", 5) : Zones("¡Remolinos en la orilla! Sal de los círculos", false); break;
            case MechanicType.Encantamiento: yield return first ? Charge("¡El Bufeo embiste! Apártate de la línea") : Projectiles("¡Burbujas encantadas!", 6); break;
            case MechanicType.Silbido: yield return first ? Projectiles("¡Almas en pena vuelan hacia ti!", 6) : Zones("¡Sombras bajo tus pies! Muévete", false); break;
            default: yield return first ? Charge("¡El Pishtaco embiste con su cuchillo!") : Projectiles("¡Lanza cuchillos!", 5); break;
        }
        // Agotado tras su ataque especial: a veces deja ver su punto débil.
        if (!health.IsDead && Random.value < 0.6f) { busy = true; yield return Expose(3.5f); busy = false; }
    }

    [Rpc(SendTo.Everyone)]
    void AnnounceRpc(string text, float seconds)
    {
        BossMechanics.Announcement = text;
        BossMechanics.AnnouncementUntil = Time.time + seconds;
    }

    List<Health> AlivePlayers()
    {
        var list = new List<Health>();
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            var h = client.PlayerObject != null ? client.PlayerObject.GetComponent<Health>() : null;
            if (h != null && !h.IsDead) list.Add(h);
        }
        return list;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // Embestida: marca una franja roja hacia un jugador y, tras un instante, corre por ella.
    IEnumerator Charge(string text)
    {
        var target = PickTarget();
        if (target == null || data.moveSpeed <= 0f) yield break;
        busy = true;
        AnnounceRpc(text, 2f);
        var start = transform.position;
        var dir = Flat(target.transform.position - start).normalized;
        if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
        float length = 16f, width = 3.4f;
        var end = start + dir * length;
        if (Flat(end).magnitude > 40f) end = start + dir * Mathf.Max(4f, 40f - Flat(start).magnitude);
        length = Vector3.Distance(start, end);
        transform.rotation = Quaternion.LookRotation(dir);
        ChargeWarnRpc(start, dir, length, width, T(1.1f));
        yield return new WaitForSeconds(T(1.1f));
        PlayAnimRpc("Attack");
        AttackMoveRpc(0);
        var hit = new HashSet<Health>();
        for (float t = 0; t < 0.45f; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(start, end, t / 0.45f);
            foreach (var h in AlivePlayers())
            {
                if (hit.Contains(h)) continue;
                var rel = Flat(h.transform.position - start);
                float along = Vector3.Dot(rel, dir);
                float side = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, dir)));
                if (side < width / 2f && along > -1f && along < Vector3.Distance(start, transform.position) + 1f)
                {
                    hit.Add(h);
                    h.TakeDamage(data.meleeDamage * 1.5f * DamageMult, NetworkObjectId);
                    ImpactRpc(h.transform.position);
                }
            }
            yield return null;
        }
        transform.position = end;
        SlamRpc(end, 3f);
        yield return new WaitForSeconds(T(0.5f));
        busy = false;
    }

    [Rpc(SendTo.Everyone)]
    void ChargeWarnRpc(Vector3 start, Vector3 dir, float length, float width, float duration)
    {
        GameAudio.Play("rugido");
        AbilityFX.LineWarning(start, dir, length, width, duration);
    }

    // Proyectiles lentos hacia cada jugador (y algunos sueltos): se esquivan moviéndose de lado.
    IEnumerator Projectiles(string text, int count)
    {
        busy = true;
        AnnounceRpc(text, 2f);
        PlayAnimRpc("Cast");
        yield return new WaitForSeconds(0.6f);
        var players = AlivePlayers();
        var origin = transform.position + Vector3.up * 2.2f + transform.forward * HitRadius;
        var shots = new List<(int id, Vector3 dir)>();
        for (int i = 0; i < count; i++)
        {
            Vector3 dir;
            if (i < players.Count) dir = Flat(players[i].transform.position - origin).normalized;
            else dir = Quaternion.Euler(0, Random.Range(-60f, 60f), 0) * Flat(transform.forward).normalized;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            int id = ++projectileIds;
            shots.Add((id, dir));
            ProjectileRpc(id, origin, dir, SpecialColor, Kind == MechanicType.Ninguna || Kind == MechanicType.Sombras);
        }
        busy = false;
        const float speed = 11f, life = 3.5f;
        var alive = new HashSet<int>();
        foreach (var s in shots) alive.Add(s.id);
        for (float t = 0; t < life; t += Time.deltaTime)
        {
            foreach (var s in shots)
            {
                if (!alive.Contains(s.id)) continue;
                var p = origin + s.dir * speed * t;
                p.y = 1.2f;
                foreach (var h in AlivePlayers())
                    if (Vector3.Distance(Flat(h.transform.position), Flat(p)) < 1.1f)
                    {
                        h.TakeDamage(data.areaDamage * 0.45f * DamageMult, NetworkObjectId);
                        alive.Remove(s.id);
                        ProjectileHitRpc(s.id, p);
                        break;
                    }
            }
            yield return null;
        }
    }

    [Rpc(SendTo.Everyone)]
    void ProjectileRpc(int id, Vector3 origin, Vector3 dir, Color color, bool knife) =>
        AbilityFX.Orb(id, origin, dir, 11f, 3.5f, color, knife);

    [Rpc(SendTo.Everyone)]
    void ProjectileHitRpc(int id, Vector3 at)
    {
        AbilityFX.StopOrb(id);
        Effects.Burst(at, SpecialColor, 25, 5f, 0.35f, 0.5f, 0.5f);
        GameAudio.Play("golpe_jefe");
    }

    // Zonas peligrosas bajo cada jugador: aparecen marcadas y luego dañan mientras estés dentro.
    IEnumerator Zones(string text, bool roots)
    {
        busy = true;
        AnnounceRpc(text, 2.5f);
        PlayAnimRpc("Cast");
        var centers = new List<Vector3>();
        foreach (var h in AlivePlayers()) centers.Add(Flat(h.transform.position));
        const float radius = 2.8f, warn = 1f, duration = 6f;
        foreach (var c in centers) ZoneRpc(c, radius, warn, duration, roots);
        yield return new WaitForSeconds(0.6f);
        busy = false;
        StartCoroutine(ZoneDamage(centers, radius, warn, duration));
    }

    IEnumerator ZoneDamage(List<Vector3> centers, float radius, float warn, float duration)
    {
        yield return new WaitForSeconds(warn);
        for (float t = 0; t < duration; t += 0.5f)
        {
            foreach (var h in AlivePlayers())
                foreach (var c in centers)
                    if (Vector3.Distance(Flat(h.transform.position), c) < radius)
                    {
                        h.TakeDamage(data.areaDamage * 0.12f * DamageMult, NetworkObjectId);
                        break;
                    }
            yield return new WaitForSeconds(0.5f);
        }
    }

    [Rpc(SendTo.Everyone)]
    void ZoneRpc(Vector3 center, float radius, float warn, float duration, bool roots) =>
        AbilityFX.Zone(center, radius, warn, duration, SpecialColor, roots);

    // Caída del salto: onda, polvo y temblor fuerte.
    [Rpc(SendTo.Everyone)]
    void SlamRpc(Vector3 at, float radius)
    {
        GameAudio.Play("area");
        AbilityFX.RingAt(at, new Color(0.9f, 0.7f, 0.45f), radius, 0.5f);
        Effects.Burst(at + Vector3.up * 0.1f, new Color(0.6f, 0.5f, 0.38f), 50, 7f, 0.8f, 0.8f, 0.6f, flat: true);
        GroundCracks.Open(at, radius, SpecialColor, serpent != null);
        GroundCracks.ShakeAt(at, 0.7f, 0.5f, 30f);
    }

    // Impacto del golpe: sonido, polvo y sacudida en todas las PCs.
    [Rpc(SendTo.Everyone)]
    void ImpactRpc(Vector3 at)
    {
        GameAudio.Play("golpe_jefe");
        Effects.Burst(at + Vector3.up * 0.1f, new Color(0.65f, 0.55f, 0.42f), 22, 4f, 0.6f, 0.6f, 0.4f, flat: true);
        if (serpent == null) GroundCracks.Open(at, 1.4f, SpecialColor);
        GroundCracks.ShakeAt(at, 0.3f, 0.25f, 20f);
    }

    // Explosión del ataque en área: grieta enorme, polvo y temblor fuerte.
    [Rpc(SendTo.Everyone)]
    void AreaBlastRpc(Vector3 at, float radius)
    {
        GameAudio.Play("area");
        Effects.Burst(at + Vector3.up * 0.2f, new Color(0.6f, 0.5f, 0.38f), 80, radius * 1.2f, 0.9f, 1f, 0.6f, flat: true);
        AbilityFX.RingAt(at + Vector3.up * 0.05f, SpecialColor, radius, 0.6f);
        GroundCracks.Open(at, Mathf.Min(radius * 0.8f, 7f), SpecialColor, serpent != null);
        GroundCracks.ShakeAt(at, 0.9f, 0.6f, radius + 22f);
    }

    IEnumerator AreaAttack()
    {
        busy = true;
        nextArea = Time.time + data.areaCooldown * Haste;
        PlayAnimRpc("Cast");
        ShowAreaWarningRpc(transform.position, data.areaRadius, T(data.areaWarningTime));

        yield return new WaitForSeconds(T(data.areaWarningTime));

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            var h = client.PlayerObject.GetComponent<Health>();
            if (h != null && Vector3.Distance(h.transform.position, transform.position) <= data.areaRadius)
                h.TakeDamage(data.areaDamage * DamageMult, NetworkObjectId);
        }
        AreaBlastRpc(transform.position, data.areaRadius);
        busy = false;
    }

    [Rpc(SendTo.Everyone)]
    void PlayAnimRpc(string trigger)
    {
        if (animator != null) animator.SetTrigger(trigger);
        if (serpent != null) serpent.Play(trigger);
        // Voz propia de cada jefe: gruñido al atacar; grito (y rugido grave debajo) al lanzar poderes o enfurecerse.
        if (trigger == "Attack") Voice("_ataque", 0.85f);
        else { Voice("_voz", 1f); GameAudio.PlayAt("rugido", transform.position, 0.45f); }
    }

    [Rpc(SendTo.Everyone)]
    void ShowAreaWarningRpc(Vector3 pos, float radius, float duration)
    {
        // Runa roja que gira y se llena hasta el golpe (en vez del círculo rojo plano).
        AbilityFX.RuneWarning(pos, radius, duration, new Color(0.95f, 0.04f, 0.02f));
        Invoke(nameof(AreaImpactSound), duration);
    }

    // Cayeron todos los jugadores: aviso de derrota en todas las PCs (LevelManager reintenta el nivel).
    [Rpc(SendTo.Everyone)]
    public void ShowDefeatRpc()
    {
        HUD.DefeatUntil = Time.time + LevelManager.RetryDelay;
        GameAudio.Play("rugido");
    }

    // Cae el ataque de área: sonido, sacudida y nube de polvo (o salpicón de agua en la laguna de la Yacumama).
    void AreaImpactSound()
    {
        GameAudio.Play("area");
        CameraFollow.Shake(0.35f, 0.45f);
        var color = serpent != null ? new Color(0.6f, 0.8f, 0.9f) : new Color(0.55f, 0.45f, 0.32f);
        Effects.Burst(transform.position + Vector3.up * 0.3f, color, 70, 10f, 1.1f, 1f, 0.6f, flat: true);
    }

    // ================================================================ SONIDOS PROPIOS DEL JEFE
    // Prefijo de sus audios (Herramientas/generar_audio3.py): chulla_, yacu_, bufeo_, tunche_, pishtaco_.
    public string SfxPrefix
    {
        get
        {
            string n = data != null ? data.bossName : "";
            return n.Contains("Chulla") ? "chulla" : n.Contains("Yacu") ? "yacu" : n.Contains("Bufeo") ? "bufeo" : n.Contains("Tunche") ? "tunche" : "pishtaco";
        }
    }

    // Voz en la posición del jefe (todas las PCs). pitchJitter: variación para que no suene siempre igual.
    public void Voice(string suffix, float volume, float pitchJitter = 0.08f)
    {
        string clip = SfxPrefix + suffix;
        var at = transform.position + Vector3.up * (serpent != null ? 4f : 2f);
        if (GameAudio.Has(clip)) GameAudio.PlayAt(clip, at, volume, pitchJitter);
    }

    float nextIdleVoice, nextPainVoice;

    // De vez en cuando gruñe, sisea o silba aunque no esté atacando (da vida al jefe).
    void UpdateIdleVoice()
    {
        if (health.IsDead || HUD.IntroShowing || GameAudio.Hushed || Time.time < nextIdleVoice) return;
        if (nextIdleVoice > 0f) Voice("_voz", 0.55f, 0.12f);
        nextIdleVoice = Time.time + Random.Range(8f, 14f);
        // Burla de vez en cuando (la decide el servidor para que todos la oigan a la vez).
        if (IsServer && Time.time >= nextTauntLine && Time.time > activeAt + 10f)
        {
            if (nextTauntLine > 0f) SayRpc("burla");
            nextTauntLine = Time.time + Random.Range(28f, 45f);
        }
    }

    // Quejido al recibir daño fuerte (como mucho uno por segundo).
    float nextTauntLine;

    [Rpc(SendTo.Everyone)]
    void SayRpc(string moment) => BossDialogue.Say(this, moment);
    bool saidLowHealth;

    void OnAnyHealthChanged(Health h, float delta)
    {
        // Diálogos: cuando cae un jugador y cuando al jefe le queda poca vida.
        if (h != health && delta < 0f && h.IsDead && h.GetComponent<PlayerController>() != null) BossDialogue.Say(this, "cae");
        if (h == health && !saidLowHealth && !health.IsDead && health.Current.Value / health.Max < 0.2f) { saidLowHealth = true; BossDialogue.Say(this, "pocavida"); }
        if (h != health || delta >= 0f || health.IsDead || Time.time < nextPainVoice) return;
        if (-delta < health.Max * 0.008f) return;   // los golpecitos no lo hacen quejarse
        nextPainVoice = Time.time + Random.Range(1.2f, 2.2f);
        Voice("_dolor", 0.7f, 0.12f);
    }

    void DeathSoundOnce()
    {
        if (deathSoundPlayed) return;
        deathSoundPlayed = true;
        GameAudio.Play("muerte_jefe");
        Voice("_dolor", 1f, 0f);
        Voice("_voz", 0.8f, 0f);
        BossDialogue.Say(this, "muerte", true);
        CameraFollow.Shake(0.5f, 0.8f);
        // Muerte épica: cámara lenta, la cámara se acerca y cada jefe muere a su manera.
        BossDeathFX.Play(this, visual, data.bossName, serpent != null);
    }
}
