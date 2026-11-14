using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// Ataques exclusivos del jefe final (Pishtaco): cadenas, eclipse, remolino, clones de sombra,
// lluvia de cuchillos y la furia final al 15 % de vida con pilares de sombra.
public partial class BossAI
{
    static readonly Color Purple = new Color(0.55f, 0.05f, 0.75f);

    // ---------------------------------------------------------------- 11 · Cadenas del ladrón
    IEnumerator Chains(Health target)
    {
        if (target == null) yield break;
        AnnounceRpc("¡Cadenas del ladrón! Esquiva la cadena", 1.6f);
        var dir = Flat(target.transform.position - transform.position).normalized;
        if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
        transform.rotation = Quaternion.LookRotation(dir);
        float len = Mathf.Min(20f, Flat(target.transform.position - transform.position).magnitude + 3f);
        ChargeWarnRpc(transform.position, dir, len, 1.6f, T(0.7f));
        yield return new WaitForSeconds(T(0.7f));
        PlayAnimRpc("Attack");
        ChainRpc(transform.position + Vector3.up * 2f, dir, len);
        // ¿A quién atrapa? Al primero que esté sobre la línea de la cadena.
        Health caught = null; float best = float.MaxValue;
        foreach (var h in AlivePlayers())
        {
            var rel = Flat(h.transform.position - transform.position);
            float along = Vector3.Dot(rel, dir);
            float side = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, dir)));
            if (along > 0f && along < len && side < 1.1f && along < best) { best = along; caught = h; }
        }
        if (caught == null) { yield return new WaitForSeconds(T(0.4f)); yield break; }
        float before = caught.Current.Value;
        caught.TakeDamage(data.meleeDamage * 0.4f * DamageMult, NetworkObjectId);
        if (caught.Current.Value >= before) { yield return new WaitForSeconds(T(0.4f)); yield break; }   // la esquivó a tiempo
        caught.GetComponent<PlayerController>().PullRpc(transform.position, HitRadius);
        yield return new WaitForSeconds(0.4f);
        AttackMoveRpc(1);
        PlayAnimRpc("Attack");
        yield return new WaitForSeconds(T(0.25f));
        if (!caught.IsDead) { caught.TakeDamage(data.meleeDamage * 1.8f * DamageMult, NetworkObjectId); ImpactRpc(caught.transform.position); }
        yield return new WaitForSeconds(T(0.3f));
    }

    [Rpc(SendTo.Everyone)]
    void ChainRpc(Vector3 from, Vector3 dir, float len) => StartCoroutine(ChainVisual(from, dir, len));

    IEnumerator ChainVisual(Vector3 from, Vector3 dir, float len)
    {
        var lr = new GameObject("Cadena").AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = new Color(0.45f, 0.42f, 0.5f);
        lr.startWidth = 0.16f; lr.endWidth = 0.1f;
        lr.positionCount = 2;
        Voice("_cuchillo", 0.9f, 0f);
        for (float t = 0; t < 0.6f; t += Time.deltaTime)
        {
            float k = t < 0.18f ? t / 0.18f : t < 0.3f ? 1f : 1f - (t - 0.3f) / 0.3f;   // sale, se queda, vuelve
            var tip = from + dir * len * Mathf.Clamp01(k);
            lr.SetPosition(0, from);
            lr.SetPosition(1, tip);
            if (t < 0.2f) Effects.Burst(tip, new Color(0.7f, 0.65f, 0.8f), 2, 1f, 0.15f, 0.3f);
            yield return null;
        }
        Destroy(lr.material);
        Destroy(lr.gameObject);
    }

    // ---------------------------------------------------------------- 12 · Eclipse
    IEnumerator Eclipse()
    {
        const float duration = 6f;
        AnnounceRpc("¡ECLIPSE! El Pishtaco ataca en la oscuridad", 2.2f);
        EclipseRpc(duration);
        PlayAnimRpc("Cast");
        float start = Time.time;
        yield return new WaitForSeconds(T(0.8f));
        // En la oscuridad: dos ataques seguidos que solo se ven por sus runas.
        yield return KnifeFan(PickTarget());
        yield return CrossDash(PickTarget());
        float left = duration - (Time.time - start);
        if (left > 0f) yield return new WaitForSeconds(left);
    }

    [Rpc(SendTo.Everyone)]
    void EclipseRpc(float duration) => StartCoroutine(EclipseVisual(duration));

    IEnumerator EclipseVisual(float duration)
    {
        var lights = FindObjectsByType<Light>();
        var intensity = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++) intensity[i] = lights[i].intensity;
        var ambient = RenderSettings.ambientLight;
        var eyes = RedEyes(4f);
        GameAudio.PlayAt("rugido", transform.position, 0.8f);
        Voice("_risa", 1f, 0f);
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            // Se oscurece en 0,6 s y vuelve la luz en el último segundo.
            float dark = t < 0.6f ? t / 0.6f : t > duration - 1f ? (duration - t) : 1f;
            float k = Mathf.Lerp(1f, 0.06f, dark);
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null && !lights[i].transform.IsChildOf(eyes.transform)) lights[i].intensity = intensity[i] * k;
            RenderSettings.ambientLight = ambient * Mathf.Lerp(1f, 0.15f, dark);
            yield return null;
        }
        for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].intensity = intensity[i];
        RenderSettings.ambientLight = ambient;
        if (!finalFuryVisual) Destroy(eyes);
    }

    // Ojos rojos brillantes en la cabeza del Pishtaco (luz roja + dos puntos que brillan).
    GameObject RedEyes(float lightIntensity)
    {
        var eyes = new GameObject("Ojos rojos");
        eyes.transform.SetParent(transform, false);
        eyes.transform.localPosition = new Vector3(0f, Mathf.Max(2.6f, HitRadius * 2f), 0.55f);
        var mat = new Material(Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default")) { mainTexture = UIStyle.Tex("brillo") };
        mat.SetColor("_TintColor", new Color(1f, 0.05f, 0.02f, 1f));
        foreach (var x in new[] { -0.17f, 0.17f })
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(eyes.transform, false);
            q.transform.localPosition = new Vector3(x, 0f, 0f);
            q.transform.localScale = Vector3.one * 0.45f;
            q.GetComponent<Renderer>().material = mat;
            q.AddComponent<FaceCamera>();
        }
        var l = eyes.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.1f, 0.05f); l.range = 7f; l.intensity = lightIntensity;
        return eyes;
    }

    // ---------------------------------------------------------------- 13 · Remolino de muerte
    IEnumerator DeathWhirl(Health target)
    {
        if (target == null) yield break;
        AnnounceRpc("¡Remolino de muerte! ¡CORRE, esquivar no basta!", 2f);
        const float duration = 3f;
        WhirlRpc(duration);
        PlayAnimRpc("Attack");
        float nextTick = 0f;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            if (target == null || target.IsDead) target = PickTarget();
            if (target == null) break;
            var to = Flat(target.transform.position - transform.position);
            // Un poco más lento que un jugador corriendo (6): se le escapa corriendo, no quedándose quieto.
            if (to.magnitude > 0.5f) transform.position += to.normalized * 5.3f * (finalFury ? 1.08f : 1f) * Time.deltaTime;
            if (t >= nextTick)
            {
                nextTick = t + 0.35f;
                foreach (var h in AlivePlayers())
                    if (Flat(h.transform.position - transform.position).magnitude <= HitRadius + 1.6f)
                        h.TakeDamage(data.meleeDamage * 0.45f * DamageMult, NetworkObjectId);
            }
            yield return null;
        }
        yield return new WaitForSeconds(T(0.5f));
    }

    [Rpc(SendTo.Everyone)]
    void WhirlRpc(float duration) => StartCoroutine(WhirlVisual(duration));

    IEnumerator WhirlVisual(float duration)
    {
        float nextFx = 0f;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            if (visual != null) visual.localRotation = Quaternion.Euler(0f, t * 1080f, 0f);
            if (t >= nextFx)
            {
                nextFx = t + 0.15f;
                AbilityFX.BossSlash(transform, new Color(0.9f, 0.85f, 1f), HitRadius + 1.6f, Random.value < 0.5f ? 1f : -1f, 0.2f);
                Effects.Burst(transform.position + Vector3.up * 0.2f, new Color(0.5f, 0.45f, 0.4f), 10, 4f, 0.5f, 0.5f, 0.4f, flat: true);
                if (Random.value < 0.4f) GameAudio.PlayAt("golpe", transform.position, 0.35f, 0.2f);
            }
            yield return null;
        }
        if (visual != null) visual.localRotation = Quaternion.identity;
    }

    // ---------------------------------------------------------------- 14 · Clones de sombra
    IEnumerator ShadowClones(Health target)
    {
        if (target == null) yield break;
        AnnounceRpc("¡Clones de sombra! Solo uno es real", 1.8f);
        var center = Flat(target.transform.position);
        float baseAngle = Random.Range(0f, 360f);
        var spots = new Vector3[3];
        for (int i = 0; i < 3; i++)
        {
            var p = center + Quaternion.Euler(0, baseAngle + i * 120f, 0) * Vector3.forward * 11f;
            if (Flat(p).magnitude > 40f) p = Flat(p).normalized * 40f;
            spots[i] = p;
        }
        int real = Random.Range(0, 3);
        var dirReal = Flat(center - spots[real]).normalized;
        var nt = GetComponent<NetworkTransform>();
        if (nt != null) nt.Teleport(spots[real], Quaternion.LookRotation(dirReal), transform.localScale);
        else transform.SetPositionAndRotation(spots[real], Quaternion.LookRotation(dirReal));
        float warn = T(0.9f);
        var fakes = new List<Vector3>();
        for (int i = 0; i < 3; i++)
        {
            var d = Flat(center - spots[i]).normalized;
            ChargeWarnRpc(spots[i], d, 22f, 2.8f, warn);
            if (i != real) fakes.Add(spots[i]);
        }
        yield return new WaitForSeconds(0.05f);
        CloneChargeRpc(fakes.ToArray(), center, warn);
        yield return new WaitForSeconds(warn);
        // Embestida del verdadero (atraviesa el centro).
        var start = transform.position;
        var end = start + dirReal * 22f;
        if (Flat(end).magnitude > 40f) end = Flat(end).normalized * 40f;
        PlayAnimRpc("Attack");
        var hit = new HashSet<Health>();
        for (float t = 0; t < 0.4f; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(start, end, t / 0.4f);
            foreach (var h in AlivePlayers())
            {
                if (hit.Contains(h)) continue;
                if (Flat(h.transform.position - transform.position).magnitude < HitRadius + 1.2f)
                {
                    hit.Add(h);
                    h.TakeDamage(data.meleeDamage * 1.6f * DamageMult, NetworkObjectId);
                    ImpactRpc(h.transform.position);
                }
            }
            yield return null;
        }
        yield return new WaitForSeconds(T(0.4f));
    }

    [Rpc(SendTo.Everyone)]
    void CloneChargeRpc(Vector3[] spots, Vector3 center, float warn) => StartCoroutine(CloneChargeVisual(spots, center, warn));

    IEnumerator CloneChargeVisual(Vector3[] spots, Vector3 center, float warn)
    {
        if (visual == null) yield break;
        var clones = new List<GameObject>();
        foreach (var s in spots)
        {
            var d = Flat(center - s).normalized;
            var c = Instantiate(visual.gameObject, s, Quaternion.LookRotation(d));
            foreach (var g in c.GetComponentsInChildren<BossGrounder>()) Destroy(g);
            // Sombra: todo muy oscuro.
            foreach (var r in c.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                {
                    if (m.HasProperty("_Color")) m.color = new Color(0.08f, 0.02f, 0.12f);
                    if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", new Color(0.08f, 0.02f, 0.12f));
                }
            Effects.Burst(s + Vector3.up * 1.5f, Purple, 30, 4f, 0.6f, 0.8f, -0.2f);
            clones.Add(c);
        }
        yield return new WaitForSeconds(warn);
        var starts = clones.ConvertAll(c => c.transform.position);
        for (float t = 0; t < 0.4f; t += Time.deltaTime)
        {
            for (int i = 0; i < clones.Count; i++)
            {
                var d = clones[i].transform.forward;
                clones[i].transform.position = starts[i] + d * 22f * (t / 0.4f);
                if (Random.value < 0.3f) Effects.Burst(clones[i].transform.position + Vector3.up, new Color(0.1f, 0f, 0.15f), 4, 1f, 0.7f, 0.6f);
            }
            yield return null;
        }
        foreach (var c in clones)
        {
            Effects.Burst(c.transform.position + Vector3.up * 1.5f, new Color(0.15f, 0f, 0.2f), 40, 5f, 0.8f, 1f, -0.3f);   // se deshace
            Destroy(c);
        }
    }

    // ---------------------------------------------------------------- 15 · Lluvia de cuchillos del cielo
    IEnumerator KnifeRain()
    {
        AnnounceRpc("¡Lluvia de cuchillos! No te quedes quieto", 1.8f);
        PlayAnimRpc("Cast");
        Voice("_risa", 1f, 0f);
        const float duration = 4f;
        float warn = T(0.75f);
        for (float t = 0; t < duration; t += T(0.16f))
        {
            Vector3 p;
            var players = AlivePlayers();
            if (players.Count > 0 && Random.value < 0.55f)
                p = Flat(players[Random.Range(0, players.Count)].transform.position) + new Vector3(Random.Range(-2.5f, 2.5f), 0, Random.Range(-2.5f, 2.5f));
            else
                p = Flat(transform.position) + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * Random.Range(2f, 15f);
            KnifeDropRpc(p, warn);
            StartCoroutine(KnifeDropHit(p, warn));
            yield return new WaitForSeconds(T(0.16f));
        }
        yield return new WaitForSeconds(warn);
    }

    IEnumerator KnifeDropHit(Vector3 at, float delay)
    {
        yield return new WaitForSeconds(delay);
        foreach (var h in AlivePlayers())
            if (Flat(h.transform.position - at).magnitude <= 1.4f)
                h.TakeDamage(data.areaDamage * 0.3f * DamageMult, NetworkObjectId);
    }

    [Rpc(SendTo.Everyone)]
    void KnifeDropRpc(Vector3 at, float warn) => StartCoroutine(KnifeDropVisual(at, warn));

    IEnumerator KnifeDropVisual(Vector3 at, float warn)
    {
        AbilityFX.RuneWarning(at, 1.4f, warn, new Color(0.95f, 0.04f, 0.02f));
        // Cuchillo que cae desde lo alto.
        var knife = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(knife.GetComponent<Collider>());
        knife.transform.localScale = new Vector3(0.08f, 0.9f, 0.22f);
        knife.GetComponent<Renderer>().material = new Material(Shader.Find("Standard")) { color = new Color(0.75f, 0.75f, 0.8f) };
        knife.GetComponent<Renderer>().material.SetFloat("_Glossiness", 0.9f);
        for (float t = 0; t < warn; t += Time.deltaTime)
        {
            float k = t / warn;
            knife.transform.position = at + Vector3.up * Mathf.Lerp(14f, 0.4f, k * k);
            knife.transform.rotation = Quaternion.Euler(0, t * 720f, 180f);
            yield return null;
        }
        Effects.Burst(at + Vector3.up * 0.2f, new Color(0.8f, 0.8f, 0.85f), 12, 4f, 0.2f, 0.4f, 1f);
        if (Random.value < 0.35f) GameAudio.PlayAt("golpe", at, 0.3f, 0.2f);
        Destroy(knife, 1.2f);
    }

    // ---------------------------------------------------------------- Furia final (15 % de vida)
    bool finalFuryVisual;
    List<Vector3> pillars = new List<Vector3>();
    float nextPillarTick;

    // Servidor: cada cuadro (desde Update).
    void UpdateFinalFury()
    {
        if (!IsServer || !IsFinalBoss || health.IsDead) return;
        if (!finalFury && health.Current.Value / health.Max <= 0.15f)
        {
            finalFury = true;
            pillars.Clear();
            for (int i = 0; i < 6; i++)
            {
                var p = Quaternion.Euler(0, i * 60f + Random.Range(-15f, 15f), 0) * Vector3.forward * Random.Range(7f, 15f);
                pillars.Add(Flat(p));
            }
            FinalFuryRpc(pillars.ToArray());
        }
        if (finalFury && Time.time >= nextPillarTick)
        {
            nextPillarTick = Time.time + 0.5f;
            foreach (var h in AlivePlayers())
                foreach (var p in pillars)
                    if (Flat(h.transform.position - p).magnitude <= 1.7f) { h.TakeDamage(data.areaDamage * 0.18f * DamageMult, NetworkObjectId); break; }
        }
    }

    [Rpc(SendTo.Everyone)]
    void FinalFuryRpc(Vector3[] pillarSpots)
    {
        finalFury = true;
        finalFuryVisual = true;
        BossMechanics.Announcement = "¡FURIA FINAL! El Pishtaco muestra su verdadera forma";
        BossMechanics.AnnouncementUntil = Time.time + 3.5f;
        if (animator != null) { animator.speed = 1.75f; animator.SetTrigger("Enrage"); }
        RedEyes(6f);
        GameAudio.PlayAt("rugido", transform.position, 1f);
        Voice("_voz", 1f, 0f);
        HUD.Flash(new Color(0.7f, 0f, 0.05f, 0.4f), 1f);
        CameraFollow.Shake(0.8f, 1f);
        AbilityFX.RingAt(transform.position + Vector3.up * 0.1f, new Color(1f, 0.05f, 0.05f), 16f, 1.2f);
        Effects.Burst(transform.position + Vector3.up * 2f, new Color(0.8f, 0.05f, 0.05f), 140, 12f, 0.7f, 1.3f, -0.3f);
        foreach (var p in pillarSpots) StartCoroutine(Pillar(p));
    }

    IEnumerator Pillar(Vector3 at)
    {
        // Pilar de sombra: columna negra con borde morado brillante, que sale del suelo y queda hasta el final.
        var root = new GameObject("Pilar de sombra").transform;
        root.position = at;
        var col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(col.GetComponent<Collider>());
        col.transform.SetParent(root, false);
        col.GetComponent<Renderer>().material = new Material(Shader.Find("Standard")) { color = new Color(0.03f, 0f, 0.05f) };
        var glowMat = new Material(Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default")) { mainTexture = UIStyle.Tex("brillo") };
        glowMat.SetColor("_TintColor", new Color(0.6f, 0.1f, 1f, 0.6f));
        var glow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(glow.GetComponent<Collider>());
        glow.transform.SetParent(root, false);
        glow.GetComponent<Renderer>().material = glowMat;
        var l = root.gameObject.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(0.6f, 0.15f, 1f); l.range = 6f; l.intensity = 2.5f;
        AbilityFX.RingAt(at + Vector3.up * 0.05f, Purple, 2.5f, 0.8f);
        GroundCracks.Open(at, 1.6f, Purple);
        for (float t = 0; t < 0.6f; t += Time.deltaTime)
        {
            float h = Mathf.Lerp(0.1f, 7f, t / 0.6f);
            col.transform.localScale = new Vector3(1.6f, h / 2f, 1.6f);
            col.transform.localPosition = Vector3.up * h / 2f;
            glow.transform.localScale = new Vector3(2.4f, h / 2f, 2.4f);
            glow.transform.localPosition = Vector3.up * h / 2f;
            yield return null;
        }
        float next = 0f;
        while (this != null && !health.IsDead)
        {
            if (Time.time >= next) { next = Time.time + 0.25f; Effects.Burst(at + Vector3.up * Random.Range(0.5f, 6.5f), Purple, 3, 1f, 0.5f, 0.8f, -0.4f); }
            yield return null;
        }
        Destroy(root.gameObject);
    }
}

// Un plano que siempre mira a la cámara (los ojos rojos).
public class FaceCamera : MonoBehaviour
{
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
    }
}
