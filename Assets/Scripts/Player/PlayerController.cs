using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.SceneManagement;

// Movimiento en tercera persona. Cada jugador mueve solo a su personaje (IsOwner).
// La posición se comparte con los demás gracias a OwnerNetworkTransform.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [SerializeField] float moveSpeed = 6f;
    [SerializeField] float turnSpeed = 720f;
    [SerializeField] float gravity = -20f;
    [SerializeField] Animator animator;

    CharacterController cc;
    Health health;
    Transform visual;
    float verticalVel;
    Vector3 lastPosition;
    bool wasDead;
    HeroMotion heroMotion;
    float charmedUntil;
    Vector3 charmTarget;
    Light charmLight;

    // Esquivar (Espacio).
    public const float DodgeCooldown = 1.5f;
    const float DodgeDuration = 0.22f, DodgeSpeed = 20f;
    public float DodgeReadyAt { get; private set; }
    float dodgeUntil;
    Vector3 dodgeDir;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        health = GetComponent<Health>();
        visual = transform.Find("Visual");
    }

    // Muestra el modelo 3D de la clase ("Visual/Rol_<Clase>") o, si no tiene, la cápsula de su color.
    // Lo llama PlayerAbilities al aparecer y cada vez que el jugador cambia de clase.
    public void ApplyRoleVisual(PlayerRole role)
    {
        if (visual == null) return;
        var roleModel = visual.Find("Rol_" + role);
        foreach (Transform child in visual)
        {
            bool isRole = child.name.StartsWith("Rol_");
            child.gameObject.SetActive(roleModel != null ? child == roleModel : !isRole);
        }

        animator = null;
        heroMotion = null;
        if (roleModel != null)
        {
            animator = roleModel.GetComponentInChildren<Animator>();
            // Modelo sin esqueleto: se anima por código.
            if (animator == null)
                heroMotion = roleModel.GetComponent<HeroMotion>() ?? roleModel.gameObject.AddComponent<HeroMotion>();
        }
        else
        {
            var color = PlayerAbilities.RoleColor(role);
            foreach (var r in visual.GetComponentsInChildren<Renderer>()) r.material.color = color;
        }
    }

    public override void OnNetworkSpawn()
    {
        lastPosition = transform.position;

        if (!IsOwner) return;
        SceneManager.sceneLoaded += OnSceneLoaded;
        MoveToSpawn();
    }

    public override void OnNetworkDespawn()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => MoveToSpawn();

    // Cada nivel tiene un objeto "PlayerSpawn"; los 5 aparecen en círculo alrededor.
    void MoveToSpawn()
    {
        var spawn = GameObject.Find("PlayerSpawn");
        if (spawn == null) return;
        var offset = Quaternion.Euler(0f, OwnerClientId * 72f, 0f) * Vector3.forward * 2f;
        cc.enabled = false;
        var at = FreeSpot(spawn.transform.position + offset);   // si el sitio está ocupado, el libre más cercano
        GetComponent<NetworkTransform>().Teleport(at, spawn.transform.rotation, transform.localScale);
        cc.enabled = true;
    }

    // Encantamiento del Bufeo (llega a todas las PCs): luz rosada y, en la PC del dueño,
    // el personaje camina solo hacia el río durante 'duration' segundos.
    // Atrapado (raíces del Chullachaqui, burbuja del Bufeo): no puede moverse, pero sí usar habilidades.
    float rootedUntil;
    public bool Rooted => Time.time < rootedUntil;

    [Rpc(SendTo.Everyone)]
    public void RootRpc(float seconds) => rootedUntil = Mathf.Max(rootedUntil, Time.time + seconds);

    [Rpc(SendTo.Everyone)]
    public void FreeRpc() => rootedUntil = 0f;

    // Cadenas del ladrón (Pishtaco): el servidor avisa al dueño y este es arrastrado hasta el jefe.
    [Rpc(SendTo.Owner)]
    public void PullRpc(Vector3 bossPos, float bossRadius)
    {
        StartCoroutine(Pull(bossPos, bossRadius));
    }

    System.Collections.IEnumerator Pull(Vector3 bossPos, float bossRadius)
    {
        const float duration = 0.35f;
        leapUntil = Time.time + duration + 0.25f;   // sin control mientras te arrastra (y un instante después)
        var from = transform.position;
        var flat = Vector3.ProjectOnPlane(from - bossPos, Vector3.up);
        if (flat.sqrMagnitude < 0.01f) flat = -transform.forward;
        var to = bossPos + flat.normalized * (bossRadius + 1f);
        to.y = from.y;
        to = FreeSpot(to);   // nunca dentro de una roca
        cc.enabled = false;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(from, to, t / duration) + Vector3.up * Mathf.Sin(t / duration * Mathf.PI) * 0.6f;
            yield return null;
        }
        transform.position = to;
        cc.enabled = true;
        verticalVel = 0f;
        PlayAction("Hit");
    }

    public void Charm(Vector3 towards, float duration)
    {
        charmedUntil = Time.time + duration;
        charmTarget = towards;
        if (charmLight == null)
        {
            charmLight = new GameObject("Encanto").AddComponent<Light>();
            charmLight.transform.SetParent(transform, false);
            charmLight.transform.localPosition = new Vector3(0f, 2f, 0f);
            charmLight.type = LightType.Point;
            charmLight.color = new Color(1f, 0.35f, 0.7f);
            charmLight.range = 6f;
        }
    }

    public bool IsCharmed => Time.time < charmedUntil;

    // Los demás también ven la animación de esquiva.
    [Rpc(SendTo.NotOwner)]
    void DodgeRpc() => PlayAction("Dodge");

    // El servidor abre la ventana de esquiva perfecta (0,35 s): si el jefe pega ahí, no hace daño y hay contraataque.
    public const float PerfectWindow = 0.35f;
    [Rpc(SendTo.Server)]
    void DodgeServerRpc() => health.StartDodge(PerfectWindow);

    // Para pruebas automáticas: esquiva como si se pulsara Espacio.
    // Salto de cóndor (Guerrero): vuela en arco hasta quedar frente al jefe. Solo el dueño mueve (la posición se comparte sola).
    float leapUntil;
    public bool Leaping => Time.time < leapUntil;
    public void LeapTo(Vector3 bossPos, float bossRadius)
    {
        if (!IsOwner) return;
        StartCoroutine(Leap(bossPos, bossRadius));
    }

    System.Collections.IEnumerator Leap(Vector3 bossPos, float bossRadius)
    {
        const float duration = 0.45f;
        leapUntil = Time.time + duration;
        var from = transform.position;
        var flat = Vector3.ProjectOnPlane(from - bossPos, Vector3.up);
        if (flat.sqrMagnitude < 0.01f) flat = -transform.forward;
        var to = bossPos + flat.normalized * (bossRadius + 1.2f);
        to.y = from.y;
        to = FreeSpot(to);   // nunca dentro de una roca
        transform.rotation = Quaternion.LookRotation(-flat.normalized);
        cc.enabled = false;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            transform.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 3.5f;
            yield return null;
        }
        transform.position = to;
        cc.enabled = true;
        verticalVel = 0f;
    }

    // ---------------------------------------------------------------- Anti-atascos
    // ¿El personaje en 'p' quedaría metido dentro de algo sólido (roca, muro, casa, el jefe)?
    bool Blocked(Vector3 p)
    {
        float r = cc.radius * 0.95f;
        var c = p + cc.center;
        float half = Mathf.Max(0f, cc.height / 2f - cc.radius);
        var top = c + Vector3.up * half;
        var bottom = c - Vector3.up * half + Vector3.up * 0.1f;   // sin tocar el suelo
        foreach (var col in Physics.OverlapCapsule(top, bottom, r, ~0, QueryTriggerInteraction.Ignore))
        {
            if (col == cc || col.transform.IsChildOf(transform)) continue;
            if (col.GetComponentInParent<PlayerController>() != null || col.GetComponentInParent<BotAlly>() != null) continue;
            if (col.name == "Suelo" || col.name == "Isla") continue;
            return true;
        }
        // Rocas reales (malla hueca): si estás completamente dentro, la cápsula no toca nada.
        // Se lanza un rayo hacia arriba: si choca con la cara INTERIOR de algo, estás dentro.
        bool old = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        bool inside = false;
        foreach (var h in Physics.RaycastAll(p + Vector3.up * 0.3f, Vector3.up, 12f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider == cc || h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider is MeshCollider && Vector3.Dot(h.normal, Vector3.up) > 0.05f) { inside = true; break; }
        }
        Physics.queriesHitBackfaces = old;
        return inside;
    }

    // El sitio libre más cercano a 'desired' (busca en círculos cada vez más grandes).
    Vector3 FreeSpot(Vector3 desired)
    {
        if (!Blocked(desired)) return desired;
        for (float d = 0.6f; d <= 8f; d += 0.6f)
            for (int i = 0; i < 16; i++)
            {
                var p = desired + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * d;
                if (!Blocked(p)) return p;
            }
        return desired;
    }

    float stuckTime, nextStuckCheck;

    // Si quedó metido dentro de algo más de ~1 s (por un arrastre, un salto o el jefe encima), lo saca al sitio libre más cercano.
    void Unstick()
    {
        if (Time.time < nextStuckCheck || Leaping) return;
        nextStuckCheck = Time.time + 0.25f;
        stuckTime = Blocked(transform.position) ? stuckTime + 0.25f : 0f;
        if (stuckTime < 1f) return;
        stuckTime = 0f;
        // Registro: con qué estaba chocando (para encontrar la causa).
        var c0 = transform.position + cc.center;
        foreach (var col in Physics.OverlapCapsule(c0 + Vector3.up * 0.5f, c0 - Vector3.up * 0.4f, cc.radius, ~0, QueryTriggerInteraction.Ignore))
            if (col != cc && !col.transform.IsChildOf(transform)) Debug.Log($"ANTIATASCO choca con: {col.name} (padre {col.transform.root.name}, {col.GetType().Name})");
        var free = FreeSpot(transform.position);
        cc.enabled = false;
        transform.position = free;
        cc.enabled = true;
        verticalVel = 0f;
        Effects.Burst(free + Vector3.up * 0.3f, new Color(0.8f, 0.75f, 0.6f), 14, 3f, 0.4f, 0.5f, 0.2f, flat: true);
        Debug.Log("ANTIATASCO: jugador liberado en " + free);
    }

    public void TestDodge() { if (IsOwner) { dodgeDir = transform.forward; dodgeUntil = Time.time + DodgeDuration; PlayAction("Dodge"); DodgeRpc(); DodgeServerRpc(); } }

    // Lo llama PlayerAbilities en todas las PCs cuando el jugador ataca ("Attack") o cura ("Cast").
    public void PlayAction(string trigger)
    {
        if (health.IsDead) return;
        if (animator != null) animator.SetTrigger(trigger);
        if (heroMotion != null) heroMotion.Play(trigger);
    }

    // Corre en todas las PCs: animación según la velocidad real y la vida sincronizada.
    void UpdateVisuals()
    {
        bool dead = health.IsDead;
        if (heroMotion != null)
        {
            if (Time.deltaTime > 0f)
                heroMotion.Tick(Vector3.Distance(transform.position, lastPosition) / Time.deltaTime, dead);
        }
        else if (animator == null)
        {
            // Sin modelo: la cápsula se tumba al morir.
            if (visual != null)
                visual.localRotation = Quaternion.Slerp(visual.localRotation, dead ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity, 10f * Time.deltaTime);
        }
        else
        {
            if (dead && !wasDead) animator.SetTrigger("Die");
            if (!dead && wasDead) animator.Play("Idle", 0, 0f);   // revivió al cambiar de nivel
            if (Time.deltaTime > 0f)
                animator.SetFloat("Speed", Vector3.Distance(transform.position, lastPosition) / Time.deltaTime, 0.1f, Time.deltaTime);
        }
        wasDead = dead;
        lastPosition = transform.position;
    }

    void Update()
    {
        UpdateVisuals();
        if (charmLight != null) charmLight.intensity = IsCharmed ? 3f + Mathf.Sin(Time.time * 10f) : 0f;

        // Red de seguridad: si el jugador cae por debajo del suelo, vuelve al punto de inicio.
        if (IsOwner && transform.position.y < -4f)
        {
            verticalVel = 0f;
            MoveToSpawn();
            return;
        }

        if (IsOwner && !health.IsDead) Unstick();
        if (!IsOwner || health.IsDead || Camera.main == null || Leaping) return;

        var input = PauseMenu.Open ? Vector3.zero : new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        var cam = Camera.main.transform;
        var forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        var right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
        var move = (forward * input.z + right * input.x).normalized;
        // Piloto automático (para probar con varias ventanas sin gente): decide hacia dónde moverse.
        bool autoDodge = false;
        if (AutoPilot.Active) move = AutoPilot.Steer(this, out autoDodge);

        if (IsCharmed)
        {
            // Encantado: no responde a las teclas, camina lento hacia el Bufeo.
            var toTarget = Vector3.ProjectOnPlane(charmTarget - transform.position, Vector3.up);
            move = toTarget.magnitude > 1.5f ? toTarget.normalized * 0.6f : Vector3.zero;
        }
        else if (!PauseMenu.Open && (Input.GetKeyDown(KeyCode.Space) || autoDodge) && Time.time >= DodgeReadyAt)
        {
            // Esquivar: impulso corto hacia donde te mueves (o hacia adelante) para salir del círculo rojo.
            dodgeDir = move.sqrMagnitude > 0.01f ? move : transform.forward;
            dodgeUntil = Time.time + DodgeDuration;
            DodgeReadyAt = Time.time + DodgeCooldown / Amulets.Mult(GetComponent<PlayerAbilities>(), AmuletType.Sombra);
            Effects.Burst(transform.position + Vector3.up * 0.3f, new Color(0.8f, 0.75f, 0.6f), 14, 3f, 0.5f, 0.5f, 0.2f, flat: true);
            GetComponent<PlayerAbilities>().CountDodge();
            PlayAction("Dodge");
            DodgeRpc();
            DodgeServerRpc();
        }
        if (Time.time < dodgeUntil) move = dodgeDir * (DodgeSpeed / moveSpeed);
        if (Rooted) { move = Vector3.zero; dodgeUntil = 0f; }   // atrapado: ni caminar ni esquivar

        if (move.sqrMagnitude > 0.01f)
        {
            var targetRot = Quaternion.LookRotation(move);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
        }

        verticalVel = cc.isGrounded ? -1f : verticalVel + gravity * Time.deltaTime;
        cc.Move((move * moveSpeed * Amulets.Mult(GetComponent<PlayerAbilities>(), AmuletType.Viento) + Vector3.up * verticalVel) * Time.deltaTime);
    }
}
