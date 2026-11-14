using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Ataques de la Yacumama en el nivel de la isla (variantes 16 a 19). Los lanza desde el agua cada pocos
// segundos aunque nadie esté a su alcance: así es ella la que ataca, y en la isla hay que esquivar.
public partial class BossAI
{
    static readonly Color WaterBlue = new Color(0.35f, 0.75f, 0.95f);
    float nextIslandBig;
    int lastIslandBig;

    // Desde Update (servidor) en el nivel de la isla.
    void IslandBigAttacks()
    {
        if (Time.time < nextIslandBig) return;
        bool first = nextIslandBig <= 0f;
        nextIslandBig = Time.time + Random.Range(5.5f, 8.5f) * Haste;
        if (first) return;
        int v;
        do v = Random.Range(16, 20); while (v == lastIslandBig);
        lastIslandBig = v;
        StartCoroutine(IslandBig(v, PickTarget()));
    }

    IEnumerator IslandBig(int v, Health target)
    {
        busy = true;
        yield return BigAttack(v, target);
        busy = false;
    }

    // ---- 16 · Coletazo: golpe de cola sobre el borde de la isla de su lado.
    IEnumerator TailSwipe()
    {
        var edge = Flat(transform.position).normalized * (IslandRadius - 2f);
        const float radius = 4.6f;
        AnnounceRpc("¡Coletazo! Aléjate de la orilla", 1.5f);
        WaterRuneRpc(edge, radius, 1.1f);
        yield return new WaitForSeconds(0.5f);
        PlayAnimRpc("Attack");
        yield return new WaitForSeconds(0.6f);
        foreach (var h in AlivePlayers())
            if (Flat(h.transform.position - edge).magnitude <= radius)
                h.TakeDamage(data.areaDamage * 0.65f * DamageMult, NetworkObjectId);
        SplashRpc(edge, radius);
        yield return new WaitForSeconds(0.4f);
    }

    // ---- 17 · Ola de la cocha: tres franjas de agua cruzan la isla una tras otra.
    IEnumerator Wave()
    {
        AnnounceRpc("¡Ola de la cocha! Esquiva las franjas", 1.8f);
        PlayAnimRpc("Cast");
        var dir = Flat(-transform.position).normalized;            // desde su lado hacia el otro
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
        var perp = Vector3.Cross(Vector3.up, dir);
        const float width = 3.6f, warn = 1f;
        float[] offsets = { -5.5f, -1.5f, 2.5f, 6.5f };
        for (int k = 0; k < offsets.Length; k++)
        {
            var mid = dir * offsets[k];
            WaveWarnRpc(mid - perp * 11f, perp, 22f, width, warn);
            StartCoroutine(WaveHit(mid, dir, width, warn));
            yield return new WaitForSeconds(0.45f);
        }
        yield return new WaitForSeconds(warn);
    }

    IEnumerator WaveHit(Vector3 mid, Vector3 dir, float width, float delay)
    {
        yield return new WaitForSeconds(delay);
        foreach (var h in AlivePlayers())
            if (Mathf.Abs(Vector3.Dot(Flat(h.transform.position) - mid, dir)) <= width / 2f)
                h.TakeDamage(data.areaDamage * 0.4f * DamageMult, NetworkObjectId);
        WaveCrashRpc(mid, dir);
    }

    // ---- 18 · Géiseres: brota agua a presión en varios puntos de la isla (algunos bajo los jugadores).
    IEnumerator Geysers()
    {
        AnnounceRpc("¡Géiseres! El agua brota bajo tus pies", 1.6f);
        PlayAnimRpc("Cast");
        var spots = new List<Vector3>();
        foreach (var h in AlivePlayers()) if (spots.Count < 3) spots.Add(Flat(h.transform.position));
        while (spots.Count < 5)
            spots.Add(Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * Random.Range(0f, IslandRadius - 1f));
        const float radius = 2.2f, warn = 1.15f;
        foreach (var s in spots) WaterRuneRpc(s, radius, warn);
        yield return new WaitForSeconds(warn);
        foreach (var h in AlivePlayers())
            foreach (var s in spots)
                if (Flat(h.transform.position - s).magnitude <= radius) { h.TakeDamage(data.areaDamage * 0.55f * DamageMult, NetworkObjectId); break; }
        GeyserRpc(spots.ToArray());
        yield return new WaitForSeconds(0.4f);
    }

    // ---- 19 · Embestida por la isla: sale del agua y cruza la isla atravesándola, hasta el otro lado.
    IEnumerator IslandCharge(Health target)
    {
        if (target == null) yield break;
        AnnounceRpc("¡La Yacumama cruza la isla! Apártate", 1.6f);
        var start = Flat(transform.position);
        var dir = Flat(target.transform.position - start).normalized;
        if (dir.sqrMagnitude < 0.01f) dir = -start.normalized;
        const float length = 25f, width = 3.4f, warn = 1.1f;
        var end = start + dir * length;
        ChargeWarnRpc(start, dir, length, width, warn);
        transform.rotation = Quaternion.LookRotation(dir);
        yield return new WaitForSeconds(warn);
        PlayAnimRpc("Attack");
        var hit = new HashSet<Health>();
        for (float t = 0; t < 0.65f; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(start, end, t / 0.65f) + Vector3.up * transform.position.y;
            foreach (var h in AlivePlayers())
            {
                if (hit.Contains(h)) continue;
                var rel = Flat(h.transform.position - start);
                float along = Vector3.Dot(rel, dir);
                float side = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, dir)));
                if (side < width / 2f && along > -1f && along < Vector3.Distance(start, Flat(transform.position)) + 1f)
                {
                    hit.Add(h);
                    h.TakeDamage(data.meleeDamage * 1.4f * DamageMult, NetworkObjectId);
                    ImpactRpc(h.transform.position);
                }
            }
            yield return null;
        }
        SplashRpc(end, 4f);
        yield return new WaitForSeconds(0.5f);
    }

    const float IslandRadius = 8.5f;

    // ---------------------------------------------------------------- Efectos (todas las PCs)
    [Rpc(SendTo.Everyone)]
    // Aviso en rojo (el azul casi no se veía sobre el agua ni sobre el barro).
    void WaterRuneRpc(Vector3 at, float radius, float warn) => AbilityFX.RuneWarning(at, radius, warn, new Color(1f, 0.15f, 0.05f));

    [Rpc(SendTo.Everyone)]
    void SplashRpc(Vector3 at, float radius)
    {
        GameAudio.PlayAt("area", at, 0.9f);
        Effects.Burst(at + Vector3.up * 0.3f, new Color(0.75f, 0.92f, 1f), 90, radius * 2.2f, 0.6f, 1f, 1.5f);
        AbilityFX.RingAt(at + Vector3.up * 0.05f, WaterBlue, radius * 1.4f, 0.7f);
        GroundCracks.ShakeAt(at, 0.6f, 0.4f, 30f);
    }

    [Rpc(SendTo.Everyone)]
    void WaveWarnRpc(Vector3 start, Vector3 dir, float length, float width, float duration)
    {
        AbilityFX.LineWarning(start, dir, length, width, duration);
        Effects.Burst(start + Vector3.up * 0.4f, new Color(0.75f, 0.92f, 1f), 20, 4f, 0.5f, 0.8f, 1.2f);
    }

    [Rpc(SendTo.Everyone)]
    void WaveCrashRpc(Vector3 mid, Vector3 dir)
    {
        var perp = Vector3.Cross(Vector3.up, dir);
        for (int i = -4; i <= 4; i++)
            Effects.Burst(mid + perp * i * 2.4f + Vector3.up * 0.4f, new Color(0.7f, 0.9f, 1f), 18, 5f, 0.55f, 0.8f, 1.6f);
        GameAudio.PlayAt("area", mid, 0.6f);
        GroundCracks.ShakeAt(mid, 0.35f, 0.3f, 25f);
    }

    [Rpc(SendTo.Everyone)]
    void GeyserRpc(Vector3[] spots)
    {
        GameAudio.PlayAt("area", spots[0], 0.9f);
        foreach (var s in spots) StartCoroutine(GeyserColumn(s));
    }

    IEnumerator GeyserColumn(Vector3 at)
    {
        // Columna de agua que sube y cae en lluvia.
        for (int i = 0; i < 6; i++)
        {
            Effects.Burst(at + Vector3.up * (0.3f + i * 0.9f), new Color(0.8f, 0.94f, 1f), 16, 3f, 0.6f, 0.7f, 1.4f);
            yield return new WaitForSeconds(0.04f);
        }
        Effects.Burst(at + Vector3.up * 5f, new Color(0.8f, 0.94f, 1f), 40, 6f, 0.4f, 1.2f, 2.2f);
        AbilityFX.RingAt(at + Vector3.up * 0.05f, WaterBlue, 3f, 0.5f);
    }
}
