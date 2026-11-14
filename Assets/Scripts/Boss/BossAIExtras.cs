using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Ataques extra de Chullachaqui (20, 21), Bufeo Colorado (22, 23) y Tunche (24, 25).
public partial class BossAI
{
    int[] ExtraBigs => Kind switch
    {
        MechanicType.Clones => new[] { 20, 21 },
        MechanicType.Encantamiento => new[] { 22, 23 },
        MechanicType.Silbido => new[] { 24, 25 },
        _ => new int[0],
    };

    IEnumerator ExtraAttack(int variant, Health target)
    {
        switch (variant)
        {
            case 20: yield return RootTrap(); break;
            case 21: yield return FallingTrees(); break;
            case 22: yield return RiverWave(); break;
            case 23: yield return BubblePrison(target); break;
            case 24: yield return ChasingSouls(); break;
            case 25: yield return CandlesOut(); break;
        }
    }

    // ================================================================ CHULLACHAQUI
    // 20 · Trampa de raíces: runas verdes bajo hasta 2 jugadores; quien siga ahí queda atrapado 2 s.
    IEnumerator RootTrap()
    {
        AnnounceRpc("¡Trampa de raíces! Muévete de la runa verde", 1.6f);
        PlayAnimRpc("Cast");
        var spots = new List<Vector3>();
        foreach (var h in AlivePlayers()) if (spots.Count < 2) spots.Add(Flat(h.transform.position));
        const float radius = 1.9f, warn = 1.2f;
        foreach (var s in spots) ColoredRuneRpc(s, radius, warn, new Color(0.35f, 0.85f, 0.2f));
        yield return new WaitForSeconds(warn);
        foreach (var h in AlivePlayers())
            foreach (var s in spots)
                if (Flat(h.transform.position - s).magnitude <= radius)
                {
                    float before = h.Current.Value;
                    h.TakeDamage(data.areaDamage * 0.35f * DamageMult, NetworkObjectId);
                    if (h.Current.Value < before)   // si esquivó justo a tiempo no lo atrapa
                    {
                        h.GetComponent<PlayerController>().RootRpc(2f);
                        RootsVisualRpc(Flat(h.transform.position), 2f);
                    }
                    break;
                }
        foreach (var s in spots) RootsBurstRpc(s);
        yield return new WaitForSeconds(0.4f);
    }

    [Rpc(SendTo.Everyone)]
    void ColoredRuneRpc(Vector3 at, float radius, float warn, Color color) => AbilityFX.RuneWarning(at, radius, warn, color);

    [Rpc(SendTo.Everyone)]
    void RootsBurstRpc(Vector3 at)
    {
        Effects.Burst(at + Vector3.up * 0.2f, new Color(0.4f, 0.3f, 0.18f), 24, 4f, 0.5f, 0.6f, 1.2f, flat: true);
        GroundCracks.Open(at, 1.6f, new Color(0.45f, 0.85f, 0.25f));
    }

    [Rpc(SendTo.Everyone)]
    void RootsVisualRpc(Vector3 at, float seconds) => StartCoroutine(RootsVisual(at, seconds));

    IEnumerator RootsVisual(Vector3 at, float seconds)
    {
        // Raíces que salen del suelo y se cierran alrededor del jugador.
        var mat = new Material(Shader.Find("Standard")) { color = new Color(0.3f, 0.22f, 0.12f) };
        var roots = new List<Transform>();
        for (int i = 0; i < 6; i++)
        {
            var r = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(r.GetComponent<Collider>());
            r.GetComponent<Renderer>().sharedMaterial = mat;
            roots.Add(r.transform);
        }
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            float grow = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((seconds - t) / 0.3f);
            for (int i = 0; i < roots.Count; i++)
            {
                var dir = Quaternion.Euler(0, i * 60f, 0) * Vector3.forward;
                var b = at + dir * 0.9f;
                roots[i].position = b + Vector3.up * 0.7f * grow - dir * 0.3f * grow;
                roots[i].rotation = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, -dir, 0.45f));
                roots[i].localScale = new Vector3(0.22f, 0.8f * grow + 0.01f, 0.22f);
            }
            yield return null;
        }
        foreach (var r in roots) Destroy(r.gameObject);
        Destroy(mat);
    }

    // 21 · Árboles que caen: tres troncos enormes caen desde el borde del bosque hacia los jugadores.
    IEnumerator FallingTrees()
    {
        AnnounceRpc("¡El monte se derrumba! Cuidado con los árboles", 1.8f);
        PlayAnimRpc("Cast");
        var players = AlivePlayers();
        for (int i = 0; i < 3; i++)
        {
            Vector3 aim = players.Count > 0 ? Flat(players[i % players.Count].transform.position) : Flat(transform.position);
            var from = aim + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * 13f;
            var dir = Flat(aim - from).normalized;
            const float len = 16f, width = 2.6f, warn = 1.3f;
            ChargeWarnRpc(from, dir, len, width, warn);
            TreeFallRpc(from, dir, len, warn);
            StartCoroutine(TreeHit(from, dir, len, width, warn));
            yield return new WaitForSeconds(0.55f);
        }
        yield return new WaitForSeconds(1.2f);
    }

    IEnumerator TreeHit(Vector3 from, Vector3 dir, float len, float width, float delay)
    {
        yield return new WaitForSeconds(delay);
        foreach (var h in AlivePlayers())
        {
            var rel = Flat(h.transform.position - from);
            float along = Vector3.Dot(rel, dir);
            float side = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, dir)));
            if (along > 0f && along < len && side < width / 2f) h.TakeDamage(data.areaDamage * 0.6f * DamageMult, NetworkObjectId);
        }
    }

    [Rpc(SendTo.Everyone)]
    void TreeFallRpc(Vector3 from, Vector3 dir, float len, float warn) => StartCoroutine(TreeFall(from, dir, len, warn));

    IEnumerator TreeFall(Vector3 from, Vector3 dir, float len, float warn)
    {
        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(trunk.GetComponent<Collider>());
        var mat = new Material(Shader.Find("Standard")) { color = new Color(0.35f, 0.26f, 0.18f) };
        trunk.GetComponent<Renderer>().sharedMaterial = mat;
        trunk.transform.localScale = new Vector3(1.1f, len / 2f, 1.1f);
        var side = Vector3.Cross(Vector3.up, dir);
        // De pie en el borde, se inclina despacio y cae de golpe al final.
        for (float t = 0; t < warn; t += Time.deltaTime)
        {
            float k = t / warn;
            float angle = Mathf.Lerp(0f, 90f, k * k * k);
            var rot = Quaternion.AngleAxis(angle, side);
            trunk.transform.rotation = rot;
            trunk.transform.position = from + rot * Vector3.up * (len / 2f);
            yield return null;
        }
        trunk.transform.rotation = Quaternion.AngleAxis(90f, side);
        trunk.transform.position = from + dir * (len / 2f) + Vector3.up * 0.55f;
        Effects.Burst(from + dir * len * 0.5f + Vector3.up * 0.3f, new Color(0.45f, 0.4f, 0.3f), 60, 8f, 0.7f, 0.9f, 0.6f, flat: true);
        Effects.Burst(from + dir * len * 0.9f + Vector3.up * 1f, new Color(0.3f, 0.6f, 0.2f), 30, 5f, 0.4f, 1f, 0.8f);
        GameAudio.PlayAt("golpe_jefe", from + dir * len * 0.5f, 1f);
        GroundCracks.ShakeAt(from + dir * len * 0.5f, 0.6f, 0.4f, 30f);
        yield return new WaitForSeconds(1.5f);
        for (float t = 0; t < 0.5f; t += Time.deltaTime) { trunk.transform.position += Vector3.down * Time.deltaTime * 2.5f; yield return null; }
        Destroy(trunk);
        Destroy(mat);
    }

    // ================================================================ BUFEO COLORADO
    // 22 · Ola del río: una ola cruza la arena de lado a lado. Se pasa esquivando (Espacio) justo al chocar.
    IEnumerator RiverWave()
    {
        AnnounceRpc("¡Ola del río! Atraviésala esquivando (Espacio)", 2.2f);
        PlayAnimRpc("Cast");
        var dir = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward;
        var perp = Vector3.Cross(Vector3.up, dir);
        var center = Flat(transform.position);
        float startOff = -22f, endOff = 22f;
        const float speed = 11f, width = 2.2f;
        RiverWaveRpc(center, dir, startOff, endOff, speed);
        yield return new WaitForSeconds(0.6f);
        var hit = new HashSet<Health>();
        float dur = (endOff - startOff) / speed;
        for (float t = 0; t < dur; t += Time.deltaTime)
        {
            float off = startOff + speed * t;
            foreach (var h in AlivePlayers())
            {
                if (hit.Contains(h)) continue;
                float d = Vector3.Dot(Flat(h.transform.position) - center, dir);
                if (Mathf.Abs(d - off) <= width / 2f)
                {
                    hit.Add(h);   // una sola vez por jugador (si esquivó, la ola pasa sin daño)
                    h.TakeDamage(data.areaDamage * 0.7f * DamageMult, NetworkObjectId);
                }
            }
            yield return null;
        }
    }

    [Rpc(SendTo.Everyone)]
    void RiverWaveRpc(Vector3 center, Vector3 dir, float startOff, float endOff, float speed) =>
        StartCoroutine(RiverWaveVisual(center, dir, startOff, endOff, speed));

    IEnumerator RiverWaveVisual(Vector3 center, Vector3 dir, float startOff, float endOff, float speed)
    {
        var perp = Vector3.Cross(Vector3.up, dir);
        // Aviso: franja del lado de donde viene.
        AbilityFX.LineWarning(center + dir * startOff - perp * 22f, perp, 44f, 3f, 0.6f);
        GameAudio.PlayAt("area", center + dir * startOff, 0.8f);
        yield return new WaitForSeconds(0.6f);
        float dur = (endOff - startOff) / speed, nextFx = 0f;
        for (float t = 0; t < dur; t += Time.deltaTime)
        {
            if (t >= nextFx)
            {
                nextFx = t + 0.06f;
                var line = center + dir * (startOff + speed * t);
                for (int i = -9; i <= 9; i++)
                    Effects.Burst(line + perp * i * 2.4f + Vector3.up * 0.6f, new Color(0.75f, 0.85f, 1f, 0.9f), 4, 3f, 0.6f, 0.5f, 1.4f);
            }
            yield return null;
        }
    }

    // 23 · Prisión de burbuja: encierra a un jugador 2,5 s. Un compañero a su lado la revienta antes.
    IEnumerator BubblePrison(Health target)
    {
        if (target == null) yield break;
        AnnounceRpc("¡Prisión de burbuja! Acércate para liberarlo", 1.8f);
        PlayAnimRpc("Cast");
        ColoredRuneRpc(Flat(target.transform.position), 1.6f, 0.9f, new Color(1f, 0.5f, 0.75f));
        yield return new WaitForSeconds(0.9f);
        var pc = target.GetComponent<PlayerController>();
        if (target.IsDead || pc == null) yield break;
        var at = Flat(target.transform.position);
        if (Flat(target.transform.position - at).magnitude > 1.6f) yield break;
        pc.RootRpc(2.5f);
        BubbleRpc(target.NetworkObjectId, 2.5f);
        float helped = 0f;
        for (float t = 0; t < 2.5f; t += Time.deltaTime)
        {
            bool ally = false;
            foreach (var h in AlivePlayers()) if (h != target && Flat(h.transform.position - target.transform.position).magnitude < 2.5f) ally = true;
            foreach (var b in BotAlly.All) if (b != null && Flat(b.transform.position - target.transform.position).magnitude < 2.5f) ally = true;
            helped = ally ? helped + Time.deltaTime : 0f;
            if (helped >= 0.7f) { pc.FreeRpc(); BubblePopRpc(target.NetworkObjectId, true); yield break; }
            yield return null;
        }
        target.TakeDamage(data.areaDamage * 0.45f * DamageMult, NetworkObjectId);   // revienta sola: duele
        BubblePopRpc(target.NetworkObjectId, false);
    }

    readonly Dictionary<ulong, GameObject> bubbles = new Dictionary<ulong, GameObject>();

    [Rpc(SendTo.Everyone)]
    void BubbleRpc(ulong playerId, float seconds)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerId, out var obj)) return;
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(s.GetComponent<Collider>());
        s.transform.SetParent(obj.transform, false);
        s.transform.localPosition = Vector3.up * 1.1f;
        s.transform.localScale = Vector3.one * 2.6f;
        var mat = new Material(Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default"));
        mat.SetColor("_TintColor", new Color(1f, 0.55f, 0.8f, 0.07f));   // transparente: que no encandile
        s.GetComponent<Renderer>().material = mat;
        bubbles[playerId] = s;
        Effects.Burst(obj.transform.position + Vector3.up, new Color(1f, 0.6f, 0.85f), 30, 3f, 0.3f, 0.8f, -0.3f);
        Destroy(s, seconds + 0.2f);
    }

    [Rpc(SendTo.Everyone)]
    void BubblePopRpc(ulong playerId, bool freed)
    {
        if (bubbles.TryGetValue(playerId, out var s) && s != null)
        {
            Effects.Burst(s.transform.position, new Color(1f, 0.7f, 0.9f), 50, 6f, 0.3f, 0.6f, 0.5f);
            Destroy(s);
        }
        bubbles.Remove(playerId);
        GameAudio.PlayAt("magia", transform.position, 0.6f);
        if (freed && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerId, out var obj))
            HUD.AddPopup(obj.transform.position + Vector3.up * 3f, "¡Liberado!", new Color(1f, 0.75f, 0.9f));
    }

    // ================================================================ TUNCHE
    // 24 · Almas que persiguen: tres almas lentas salen del Tunche y persiguen; explotan si te alcanzan.
    IEnumerator ChasingSouls()
    {
        AnnounceRpc("¡Almas en pena te persiguen! Aléjate", 1.8f);
        PlayAnimRpc("Cast");
        Voice("_voz", 0.8f);
        var players = AlivePlayers();
        int n = 3;
        var pos = new Vector3[n];
        var tgt = new Health[n];
        var alive = new bool[n];
        for (int i = 0; i < n; i++)
        {
            pos[i] = transform.position + Quaternion.Euler(0, i * 120f, 0) * Vector3.forward * 1.5f + Vector3.up * 1.4f;
            tgt[i] = players.Count > 0 ? players[i % players.Count] : null;
            alive[i] = true;
        }
        SoulsSpawnRpc(pos);
        float nextSync = 0f;
        for (float t = 0; t < 7f; t += Time.deltaTime)
        {
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (!alive[i]) continue;
                any = true;
                if (tgt[i] == null || tgt[i].IsDead) { var ps = AlivePlayers(); tgt[i] = ps.Count > 0 ? ps[Random.Range(0, ps.Count)] : null; }
                if (tgt[i] == null) continue;
                var goal = tgt[i].transform.position + Vector3.up * 1.2f;
                pos[i] = Vector3.MoveTowards(pos[i], goal, 3.6f * Time.deltaTime);   // más lentas que correr (6)
                if (Vector3.Distance(Flat(pos[i]), Flat(tgt[i].transform.position)) < 1.1f)
                {
                    alive[i] = false;
                    foreach (var h in AlivePlayers())
                        if (Flat(h.transform.position - pos[i]).magnitude < 2f) h.TakeDamage(data.areaDamage * 0.45f * DamageMult, NetworkObjectId);
                    SoulBoomRpc(i, pos[i]);
                }
            }
            if (!any) break;
            if (Time.time >= nextSync) { nextSync = Time.time + 0.1f; SoulsMoveRpc(pos, alive); }
            yield return null;
        }
        for (int i = 0; i < n; i++) if (alive[i]) SoulBoomRpc(i, pos[i]);   // se desvanecen
    }

    readonly List<Transform> souls = new List<Transform>();
    Vector3[] soulGoals;

    [Rpc(SendTo.Everyone)]
    void SoulsSpawnRpc(Vector3[] at)
    {
        foreach (var s in souls) if (s != null) Destroy(s.gameObject);
        souls.Clear();
        soulGoals = (Vector3[])at.Clone();
        var mat = new Material(Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default")) { mainTexture = UIStyle.Tex("brillo") };
        mat.SetColor("_TintColor", new Color(0.75f, 0.85f, 1f, 0.9f));
        foreach (var p in at)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.GetComponent<Renderer>().material = mat;
            q.transform.position = p;
            q.transform.localScale = Vector3.one * 1.4f;
            q.AddComponent<FaceCamera>();
            var l = q.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(0.6f, 0.7f, 1f); l.range = 5f; l.intensity = 2f;
            souls.Add(q.transform);
            Effects.Burst(p, new Color(0.7f, 0.8f, 1f), 20, 2f, 0.3f, 0.8f, -0.3f);
        }
        StartCoroutine(SoulsFollow());
    }

    IEnumerator SoulsFollow()
    {
        float nextTrail = 0f;
        while (souls.Count > 0)
        {
            bool any = false;
            for (int i = 0; i < souls.Count; i++)
            {
                if (souls[i] == null) continue;
                any = true;
                souls[i].position = Vector3.Lerp(souls[i].position, soulGoals[i], 10f * Time.deltaTime) + Vector3.up * Mathf.Sin(Time.time * 4f + i) * 0.005f;
                if (Time.time >= nextTrail) Effects.Burst(souls[i].position, new Color(0.55f, 0.6f, 0.9f, 0.7f), 3, 0.6f, 0.4f, 0.7f, -0.3f);
            }
            if (Time.time >= nextTrail) nextTrail = Time.time + 0.08f;
            if (!any) break;
            yield return null;
        }
    }

    [Rpc(SendTo.Everyone)]
    void SoulsMoveRpc(Vector3[] at, bool[] alive)
    {
        if (soulGoals == null) return;
        for (int i = 0; i < at.Length && i < soulGoals.Length; i++) soulGoals[i] = at[i];
    }

    [Rpc(SendTo.Everyone)]
    void SoulBoomRpc(int i, Vector3 at)
    {
        if (i < souls.Count && souls[i] != null) { Destroy(souls[i].gameObject); souls[i] = null; }
        Effects.Burst(at, new Color(0.7f, 0.8f, 1f), 50, 6f, 0.4f, 0.8f, 0.2f);
        AbilityFX.RingAt(new Vector3(at.x, 0.05f, at.z), new Color(0.6f, 0.7f, 1f), 2.2f, 0.5f);
        GameAudio.PlayAt("magia", at, 0.7f);
    }

    // 25 · Velas que se apagan: apaga la mitad de las velas 12 s y enseguida silba. Menos refugios.
    IEnumerator CandlesOut()
    {
        var altars = SortedAltars();
        if (altars.Count == 0) yield break;
        AnnounceRpc("¡El Tunche apaga las velas!", 1.8f);
        PlayAnimRpc("Cast");
        var pick = new List<int>();
        int start = Random.Range(0, 2);
        for (int i = start; i < altars.Count; i += 2) pick.Add(i);   // una sí, una no
        CandlesOutRpc(pick.ToArray(), 12f);
        yield return new WaitForSeconds(1.2f);
        var mech = GetComponent<BossMechanics>();
        if (mech != null) mech.TestTrigger();   // y silba enseguida: hay menos velas donde refugiarse
    }

    static List<Transform> SortedAltars()
    {
        var list = new List<Transform>();
        foreach (var t in FindObjectsByType<Transform>()) if (t.name == "Altar de velas") list.Add(t);
        list.Sort((a, b) => Mathf.Atan2(a.position.x, a.position.z).CompareTo(Mathf.Atan2(b.position.x, b.position.z)));
        return list;
    }

    [Rpc(SendTo.Everyone)]
    void CandlesOutRpc(int[] indices, float seconds) => StartCoroutine(CandlesOutVisual(indices, seconds));

    IEnumerator CandlesOutVisual(int[] indices, float seconds)
    {
        var altars = SortedAltars();
        var changed = new List<(Transform t, string name)>();
        var lights = new List<Light>();
        var flames = new List<Renderer>();
        foreach (int i in indices)
        {
            if (i >= altars.Count) continue;
            var a = altars[i];
            foreach (var t in a.GetComponentsInChildren<Transform>())
                if (t.name == "Vela") { changed.Add((t, t.name)); t.name = "Vela apagada"; }   // ya no cuenta como refugio
            foreach (var l in a.GetComponentsInChildren<Light>()) if (l.enabled) { l.enabled = false; lights.Add(l); }
            foreach (var r in a.GetComponentsInChildren<Renderer>())
                if (r.sharedMaterial != null && r.sharedMaterial.IsKeywordEnabled("_EMISSION") && r.enabled) { r.enabled = false; flames.Add(r); }
            Effects.Burst(a.position + Vector3.up * 1f, new Color(0.25f, 0.2f, 0.3f, 0.9f), 30, 1.5f, 0.8f, 1.6f, -0.3f);   // humo
        }
        GameAudio.PlayAt("magia", transform.position, 0.7f);
        yield return new WaitForSeconds(seconds);
        foreach (var (t, name) in changed) if (t != null) t.name = name;
        foreach (var l in lights) if (l != null) l.enabled = true;
        foreach (var r in flames) if (r != null) r.enabled = true;
    }
}
