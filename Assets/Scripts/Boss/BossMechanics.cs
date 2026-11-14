using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public enum MechanicType { Ninguna, Clones, Sumergirse, Encantamiento, Silbido, Sombras }

// Mecánica propia de cada jefe, inspirada en su leyenda. La decide el servidor cada 'interval' segundos;
// los efectos visuales y los avisos en pantalla llegan a todas las PCs por RPC.
[RequireComponent(typeof(BossAI))]
public class BossMechanics : NetworkBehaviour
{
    public MechanicType type;
    public float interval = 20f;

    // Aviso grande en el centro de la pantalla (lo dibuja HUD).
    public static string Announcement;
    public static float AnnouncementUntil;

    BossAI ai;
    Health health;
    NetworkTransform netTransform;
    Vector3 home;
    float next;

    void Awake()
    {
        ai = GetComponent<BossAI>();
        health = GetComponent<Health>();
        netTransform = GetComponent<NetworkTransform>();
    }

    public override void OnNetworkSpawn()
    {
        home = transform.position;
        Announcement = null;
        if (IsServer) next = Time.time + ai.Data.introSeconds + LoadingScreen.Hold + BossEntrance.Duration + interval * 0.6f;
    }

    // Para pruebas automáticas: lanza la mecánica ya.
    public void TestTrigger() => next = Time.time;

    void Update()
    {
        if (!IsServer || type == MechanicType.Ninguna || health.IsDead || ai.Paused || Time.time < next) return;
        // La Yacumama pasa más tiempo en la superficie (se sumerge cada 30 s en vez de 20).
        next = Time.time + (type == MechanicType.Sumergirse ? 30f : type == MechanicType.Sombras ? interval * 0.7f : interval);
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        switch (type)
        {
            case MechanicType.Clones: yield return Clones(); break;
            case MechanicType.Sumergirse: yield return Submerge(); break;
            case MechanicType.Encantamiento: yield return Charm(); break;
            case MechanicType.Silbido: yield return Whistle(); break;
            case MechanicType.Sombras: yield return Shadows(); break;
        }
    }

    // ---------- Chullachaqui: se multiplica y solo el verdadero recibe daño ----------
    IEnumerator Clones()
    {
        const float duration = 12f;
        var spots = new List<Vector3>();
        float start = Random.Range(0f, 360f);
        for (int i = 0; i < 4; i++)
            spots.Add(home + Quaternion.Euler(0f, start + i * 90f, 0f) * Vector3.forward * 8f);
        int real = Random.Range(0, spots.Count);

        // Ángulo de cada réplica respecto al verdadero (giran alrededor de "home" como reflejos).
        // Alrededor del jugador: dos a los lados y una más abierta (a un lado al azar), para que se vean en cámara.
        float wide = Random.value < 0.5f ? 110f : -110f;
        var angles = new List<float> { -55f, 55f, wide };
        Teleport(spots[real]);
        AnnounceRpc("¡El Chullachaqui se multiplica! Solo el verdadero recibe daño", 4f);
        yield return new WaitForSeconds(0.15f);   // que la posición nueva llegue a todos antes de crear las réplicas
        ShowClonesRpc(home, angles.ToArray(), duration);
    }

    [Rpc(SendTo.Everyone)]
    void ShowClonesRpc(Vector3 center, float[] angles, float duration)
    {
        ai.Voice("_ramas", 1f);   // crujir de ramas al multiplicarse
        BossDialogue.Say(ai, "especial");
        ai.Voice("_voz", 0.8f);
        var visual = transform.Find("Visual");
        if (visual == null) return;
        foreach (float a in angles)
        {
            var rot = Quaternion.Euler(0f, a, 0f);
            var clone = Instantiate(visual.gameObject, center + rot * (visual.position - center), rot * visual.rotation);
            clone.name = "Clon Chullachaqui";
            var m = clone.AddComponent<CloneMirror>();
            m.realVisual = visual;
            m.realHealth = health;
            m.center = center;
            m.angle = a;
            m.lifeUntil = Time.time + duration;
        }
    }

    // Para pruebas automáticas.
    public void TestClones() { if (IsServer) StartCoroutine(Clones()); }

    // ---------- Yacumama: se sumerge y emerge en otro punto de la cocha ----------
    IEnumerator Submerge()
    {
        ai.Paused = true;
        health.Invulnerable = true;
        AnnounceRpc("La Yacumama se sumerge en la cocha...", 2.5f);
        SubmergeRpc(true);

        // Sale cerca de la orilla, frente a un jugador al azar (no al fondo de la cocha).
        var target = RandomAlivePlayer();
        float x = target != null ? Mathf.Clamp(target.transform.position.x + Random.Range(-4f, 4f), -14f, 14f) : Random.Range(-10f, 10f);
        var emerge = new Vector3(x, home.y, Random.Range(14f, 18f));
        // Nivel 2 con isla: sale en el agua junto a la isla, del lado del jugador elegido.
        if (GameObject.Find("Isla") != null)
        {
            var d = target != null ? new Vector3(target.transform.position.x, 0f, target.transform.position.z) : Random.insideUnitSphere;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
            emerge = d.normalized * 11.5f + Vector3.up * home.y;
        }
        yield return new WaitForSeconds(1.5f);
        BubblesRpc(emerge);   // burbujas: aviso de dónde va a salir
        yield return new WaitForSeconds(1.5f);

        Teleport(emerge);
        SubmergeRpc(false);
        health.Invulnerable = false;
        // Al salir queda aturdida unos segundos: ventana para atacarla.
        AnnounceRpc("¡Emerge aturdida! ¡Ataquen ahora!", 3f);
        ai.AddStun(health.Max);
        yield return new WaitForSeconds(0.5f);
        ai.Paused = false;
    }

    // Burbujas y un remolino claro donde va a emerger la Yacumama.
    [Rpc(SendTo.Everyone)]
    void BubblesRpc(Vector3 at)
    {
        AbilityFX.RingAt(at, new Color(0.6f, 0.9f, 1f), 4f, 1.5f);
        for (int i = 0; i < 6; i++)
            Effects.Burst(at + new Vector3(Random.Range(-1.5f, 1.5f), 0.1f, Random.Range(-1.5f, 1.5f)), new Color(0.75f, 0.95f, 1f), 10, 2.5f, 0.35f, 1f, -0.8f);
        GameAudio.Play("area");
    }

    [Rpc(SendTo.Everyone)]
    void SubmergeRpc(bool value)
    {
        var serpent = GetComponentInChildren<SerpentMotion>();
        if (serpent != null) serpent.SetSubmerged(value);
        // Sisea al hundirse; ruge al salir.
        ai.Voice(value ? "_siseo" : "_voz", 1f);
        if (value) BossDialogue.Say(ai, "especial");
    }

    // ---------- Bufeo Colorado: encanta a un jugador y lo arrastra hacia el río ----------
    IEnumerator Charm()
    {
        var victim = RandomAlivePlayer();
        if (victim == null) yield break;

        const float duration = 4f;
        var role = PlayerAbilities.RoleName(victim.GetComponent<PlayerAbilities>().Role);
        AnnounceRpc($"¡El Bufeo encanta al {role}! Aléjenlo del río", 3f);
        CharmRpc(victim.NetworkObjectId, transform.position, duration);
        yield return new WaitForSeconds(duration);

        // Si llegó hasta el Bufeo, se lo lleva al fondo del río.
        var h = victim.GetComponent<Health>();
        if (!h.IsDead && Vector3.Distance(victim.transform.position, transform.position) < 6f)
        {
            ai.TriggerAnim("Attack");
            h.TakeDamage(ai.Data.areaDamage * 1.5f * ai.DamageMultiplier, NetworkObjectId);
        }
    }

    [Rpc(SendTo.Everyone)]
    void CharmRpc(ulong playerObjectId, Vector3 towards, float duration)
    {
        ai.Voice("_canto", 1f);   // silbidos y chasquidos de delfín: el encantamiento
        BossDialogue.Say(ai, "especial");
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId, out var obj))
            obj.GetComponent<PlayerController>().Charm(towards, duration);
    }

    // ---------- Tunche: silbido que daña a quien no esté junto a una vela ----------
    IEnumerator Whistle()
    {
        const float warning = 3f;
        AnnounceRpc("Se oye un silbido... ¡corre junto a una vela!", warning);
        WhistleSoundRpc();
        yield return new WaitForSeconds(warning);

        var candles = new List<Vector3>();
        foreach (var t in FindObjectsByType<Transform>())
            if (t.name == "Vela") candles.Add(t.position);

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            var h = client.PlayerObject.GetComponent<Health>();
            if (h.IsDead) continue;
            bool safe = false;
            foreach (var c in candles)
                if (Vector3.Distance(Flat(c), Flat(h.transform.position)) < 3.5f) { safe = true; break; }
            if (!safe) h.TakeDamage(ai.Data.areaDamage * ai.DamageMultiplier, NetworkObjectId);
        }
    }

    [Rpc(SendTo.Everyone)]
    void WhistleSoundRpc()
    {
        // El silbido de la leyenda (sintetizado con eco de monte). Si faltara el audio, se usa el de respaldo.
        BossDialogue.Say(ai, "especial");
        if (GameAudio.Has("tunche_silbido")) { if (!GameAudio.Hushed) GameAudio.Play("tunche_silbido"); return; }
        var go = new GameObject("Silbido");
        var source = go.AddComponent<AudioSource>();
        source.clip = MakeWhistle();
        source.volume = 0.5f;
        source.Play();
        Destroy(go, 4f);
    }

    // Silbido agudo hecho por código: dos notas que suben y bajan con un temblor, como el del Tunche.
    static AudioClip MakeWhistle()
    {
        const int rate = 44100;
        const float seconds = 2.6f;
        var data = new float[(int)(rate * seconds)];
        float phase = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)rate;
            float freq = t < 1.2f ? Mathf.Lerp(1700f, 2500f, t / 1.2f) : Mathf.Lerp(2500f, 1500f, (t - 1.2f) / 1.4f);
            freq += Mathf.Sin(t * 40f) * 60f;
            phase += 2f * Mathf.PI * freq / rate;
            float envelope = Mathf.Clamp01(t * 8f) * Mathf.Clamp01((seconds - t) * 3f);
            data[i] = Mathf.Sin(phase) * envelope * 0.6f;
        }
        var clip = AudioClip.Create("Silbido", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ---------- Pishtaco: desaparece en las sombras y apuñala al más débil por la espalda ----------
    IEnumerator Shadows()
    {
        ai.Paused = true;
        health.Invulnerable = true;
        AnnounceRpc("El Pishtaco se desvanece en las sombras...", 3f);
        ShadowRpc(true);
        yield return new WaitForSeconds(2.2f);   // jefe final: antes eran 4 s escondido

        var victim = WeakestAlivePlayer();
        if (victim != null)
        {
            var behind = victim.transform.position - victim.transform.forward * 2.5f;
            Teleport(behind, Quaternion.LookRotation(Flat(victim.transform.position - behind)));
        }
        ShadowRpc(false);
        health.Invulnerable = false;
        if (victim != null)
        {
            // Aparece, levanta el cuchillo y apuñala tras un instante: si esquivas (Espacio) o te alejas, falla.
            AnnounceRpc("¡Aparece por la espalda! ¡Esquiva!", 2f);
            ai.TriggerAnim("Attack");
            yield return new WaitForSeconds(0.42f);   // apuñala rápido: hay que esquivar con reflejos (antes 0,7 s)
            var vh = victim.GetComponent<Health>();
            if (!vh.IsDead && Vector3.Distance(Flat(victim.transform.position), Flat(transform.position)) < 3.6f)
                vh.TakeDamage(ai.Data.meleeDamage * 2f * ai.DamageMultiplier, NetworkObjectId);
        }
        yield return new WaitForSeconds(0.35f);
        ai.Paused = false;
    }

    [Rpc(SendTo.Everyone)]
    void ShadowRpc(bool hidden)
    {
        var visual = transform.Find("Visual");
        if (visual != null) visual.gameObject.SetActive(!hidden);
        // Se ríe al desaparecer en las sombras; al volver se oye el filo del cuchillo.
        ai.Voice(hidden ? "_risa" : "_cuchillo", 1f);
        if (hidden) BossDialogue.Say(ai, "especial");
    }

    // ---------- Utilidades ----------

    [Rpc(SendTo.Everyone)]
    void AnnounceRpc(string text, float seconds)
    {
        Announcement = text;
        AnnouncementUntil = Time.time + seconds;
    }

    void Teleport(Vector3 position) => Teleport(position, transform.rotation);

    void Teleport(Vector3 position, Quaternion rotation)
    {
        if (netTransform != null) netTransform.Teleport(position, rotation, transform.localScale);
        else transform.SetPositionAndRotation(position, rotation);
    }

    NetworkObject RandomAlivePlayer()
    {
        var alive = new List<NetworkObject>();
        foreach (var c in NetworkManager.ConnectedClientsList)
            if (c.PlayerObject != null && !c.PlayerObject.GetComponent<Health>().IsDead) alive.Add(c.PlayerObject);
        return alive.Count > 0 ? alive[Random.Range(0, alive.Count)] : null;
    }

    NetworkObject WeakestAlivePlayer()
    {
        NetworkObject best = null;
        float bestFrac = float.MaxValue;
        foreach (var c in NetworkManager.ConnectedClientsList)
        {
            if (c.PlayerObject == null) continue;
            var h = c.PlayerObject.GetComponent<Health>();
            if (h.IsDead) continue;
            float frac = h.Current.Value / h.Max;
            if (frac < bestFrac) { bestFrac = frac; best = c.PlayerObject; }
        }
        return best;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
