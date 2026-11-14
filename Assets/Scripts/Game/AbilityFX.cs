using System.Collections;
using UnityEngine;

// Efectos visuales propios de cada poder (15 en total), hechos por código: ondas, columnas de luz,
// rayos, cortes, cúpulas, lluvia de flechas... Solo visuales: corren en cada PC (ShowBeamRpc).
public class AbilityFX : MonoBehaviour
{
    static AbilityFX runner;
    static Texture2D ringTex, beamTex;

    static AbilityFX Runner
    {
        get
        {
            if (runner == null)
            {
                var go = new GameObject("EfectosPoderes");
                DontDestroyOnLoad(go);
                runner = go.AddComponent<AbilityFX>();
            }
            return runner;
        }
    }

    public static void Play(PlayerAbilities caster, PlayerRole role, int index, bool heal, Vector3 target) =>
        PlayAt(caster.transform, role, index, heal, target);

    // Igual que Play, pero desde cualquier personaje (también los compañeros bot).
    public static void PlayAt(Transform t, PlayerRole role, int index, bool heal, Vector3 target)
    {
        var hand = t.position + Vector3.up * 1.4f + t.forward * 0.5f;
        var hit = target + Vector3.up * 2.5f;
        var gold = new Color(1f, 0.82f, 0.35f);
        var r = Runner;
        if (index == 3) { Signature(r, t, role, target, hand, hit); return; }

        switch (role)
        {
            case PlayerRole.Guerrero:
                if (index == 0)
                {
                    Effects.Burst(Vector3.Lerp(hand, hit, 0.6f), gold, 18, 6f, 0.3f, 0.35f, 1.5f);
                    r.Ring(target, new Color(1f, 0.75f, 0.4f), 3f, 0.35f);
                }
                else if (index == 1)
                {
                    r.StartCoroutine(r.Slash(t, new Color(1f, 0.3f, 0.2f)));
                    Effects.Burst(hit, new Color(1f, 0.35f, 0.25f), 30, 8f, 0.35f, 0.5f, 1f);
                }
                else
                {
                    // Furia del Inti: columna de sol sobre el jefe, onda y sacudida.
                    r.Pillar(target, new Color(1f, 0.75f, 0.25f), 14f, 2.2f, 1.1f);
                    r.Ring(target, new Color(1f, 0.7f, 0.25f), 9f, 0.7f);
                    Effects.Burst(target + Vector3.up * 2f, new Color(1f, 0.85f, 0.4f), 80, 10f, 0.5f, 1f, 0.4f);
                    CameraFollow.Shake(0.3f, 0.3f);
                }
                break;

            case PlayerRole.Guardian:
                if (index == 0)
                {
                    Effects.Burst(Vector3.Lerp(hand, hit, 0.6f), new Color(0.6f, 0.8f, 1f), 18, 6f, 0.3f, 0.35f, 1.5f);
                    r.Ring(target, new Color(0.5f, 0.7f, 1f), 2.5f, 0.3f);
                }
                else if (index == 1)
                {
                    // Provocar: onda roja que sale del Guardián y marca al jefe.
                    r.Ring(t.position, new Color(1f, 0.25f, 0.2f), 14f, 0.8f);
                    r.Ring(target, new Color(1f, 0.2f, 0.15f), 4f, 1f);
                    Effects.Burst(hit, new Color(1f, 0.2f, 0.15f), 25, 3f, 0.4f, 0.8f, -0.4f);
                }
                else
                {
                    // Muro de piedra: cúpula protectora y piedras que saltan del suelo.
                    r.StartCoroutine(r.Dome(t, new Color(0.55f, 0.75f, 1f), 2.4f, 1.6f));
                    Effects.Burst(t.position + Vector3.up * 0.2f, new Color(0.55f, 0.48f, 0.4f), 30, 6f, 0.45f, 0.8f, 2.5f, flat: true);
                }
                break;

            case PlayerRole.Arquero:
                if (index == 0) Effects.Projectile(hand, hit, PlayerAbilities.RoleColor(role), arrow: true);
                else if (index == 1)
                {
                    // Flecha certera: se marca el blanco con una diana antes del disparo.
                    r.Ring(target, new Color(0.5f, 1f, 0.45f), 2.5f, 0.6f);
                    Effects.Projectile(hand, hit, new Color(0.6f, 1f, 0.5f), arrow: true);
                    r.StartCoroutine(r.Delayed(0.25f, () =>
                    {
                        r.Ring(target, new Color(0.5f, 1f, 0.45f), 3.5f, 0.5f);
                        Effects.Burst(hit, new Color(0.7f, 1f, 0.5f), 30, 6f, 0.3f, 0.5f, 0.5f);
                    }));
                }
                else r.StartCoroutine(r.ArrowRain(target));
                break;

            case PlayerRole.Chaman:
                if (heal)
                {
                    // Poción curativa: vuela hasta el aliado y revienta curando a todos alrededor (5 m).
                    Effects.Projectile(hand, target + Vector3.up * 1.2f, new Color(0.45f, 1f, 0.55f), arrow: false);
                    r.StartCoroutine(r.Delayed(0.3f, () =>
                    {
                        r.Ring(target, new Color(0.45f, 1f, 0.55f), 5f, 0.7f);
                        r.Pillar(target, new Color(0.55f, 1f, 0.6f), 6f, 2.2f, 0.9f);
                        Effects.Burst(target + Vector3.up * 0.8f, new Color(0.5f, 1f, 0.55f), 60, 5f, 0.35f, 1.1f, -0.5f);
                        Effects.Burst(target + Vector3.up * 0.3f, new Color(0.8f, 0.5f, 1f), 30, 4f, 0.3f, 0.9f, 0.3f, flat: true);
                    }));
                }
                else if (index == 0) r.StartCoroutine(r.Lightning(hand, hit, new Color(0.8f, 0.6f, 1f)));
                else
                {
                    Effects.Projectile(hand, hit, new Color(0.7f, 0.3f, 1f), arrow: false);
                    r.StartCoroutine(r.Delayed(0.4f, () =>
                    {
                        Effects.Burst(hit, new Color(0.6f, 0.25f, 1f), 60, 7f, 0.6f, 0.9f, -0.5f);
                        Effects.Burst(hit, new Color(0.3f, 1f, 0.4f), 30, 4f, 0.4f, 1.2f, -0.8f);
                        r.Ring(target, new Color(0.6f, 0.3f, 1f), 5f, 0.6f);
                    }));
                }
                break;

            default: // Sanador
                if (!heal)
                {
                    Effects.Projectile(hand, hit, Color.Lerp(PlayerAbilities.RoleColor(role), Color.white, 0.3f), arrow: false);
                    break;
                }
                bool big = index == 2;
                r.Pillar(target, gold, big ? 10f : 6f, big ? 1.8f : 1f, big ? 1.4f : 0.9f);
                Effects.Burst(target + Vector3.up, new Color(1f, 0.9f, 0.5f), big ? 60 : 25, 2.5f, 0.3f, 1.4f, -0.7f);
                if (big) r.Ring(target, gold, 6f, 0.8f);
                Effects.Burst(t.position + Vector3.up * 1.2f, new Color(1f, 0.9f, 0.45f), 16, 2f, 0.3f, 0.8f, -0.3f);
                break;
        }
    }

    // Ataque básico (clic izquierdo): efecto pequeño y rápido, para que se note pero no tape las habilidades.
    public static void Basic(Transform t, PlayerRole role, Vector3 target)
    {
        var hand = t.position + Vector3.up * 1.4f + t.forward * 0.5f;
        var hit = target + Vector3.up * 2.2f;
        var color = PlayerAbilities.RoleColor(role);
        switch (role)
        {
            case PlayerRole.Guerrero:
            case PlayerRole.Guardian:
                Effects.Burst(Vector3.Lerp(hand, hit, 0.6f), Color.Lerp(color, Color.white, 0.4f), 8, 4f, 0.2f, 0.25f, 1.5f);
                GameAudio.PlayAt("golpe", hand, 0.45f, 0.12f);
                break;
            case PlayerRole.Arquero:
                Effects.Projectile(hand, hit, color, arrow: true);
                GameAudio.PlayAt("flecha", hand, 0.4f, 0.12f);
                break;
            default:
                Effects.Projectile(hand, hit, Color.Lerp(color, Color.white, 0.3f), arrow: false);
                GameAudio.PlayAt("magia", hand, 0.3f, 0.15f);
                break;
        }
    }

    // 4.ª habilidad de cada clase (se desbloquea en el nivel 3): efectos más grandes que las demás.
    static void Signature(AbilityFX r, Transform t, PlayerRole role, Vector3 target, Vector3 hand, Vector3 hit)
    {
        var gold = new Color(1f, 0.8f, 0.35f);
        switch (role)
        {
            case PlayerRole.Guerrero:
                // Salto de cóndor: estela al saltar y gran impacto al caer junto al jefe.
                Effects.Burst(t.position + Vector3.up * 0.3f, new Color(0.8f, 0.75f, 0.6f), 30, 5f, 0.5f, 0.6f, 0.5f, flat: true);
                r.StartCoroutine(r.Delayed(0.45f, () =>
                {
                    r.Ring(target, gold, 7f, 0.6f);
                    r.Pillar(target, new Color(1f, 0.6f, 0.25f), 9f, 1.4f, 0.8f);
                    Effects.Burst(target + Vector3.up * 1.5f, new Color(1f, 0.75f, 0.35f), 70, 10f, 0.5f, 0.8f, 0.8f);
                    GroundCracks.Open(t.position, 3f, gold);
                    GroundCracks.ShakeAt(t.position, 0.6f, 0.4f, 25f);
                }));
                break;
            case PlayerRole.Guardian:
                // Temblor de la Pachamama: la tierra se abre alrededor del Guardián y aturde al jefe.
                GroundCracks.Open(t.position, 4f, new Color(0.9f, 0.65f, 0.3f));
                r.Ring(t.position, new Color(0.75f, 0.55f, 0.3f), 9f, 0.7f);
                r.Ring(target, new Color(1f, 0.9f, 0.4f), 5f, 0.9f);
                Effects.Burst(t.position + Vector3.up * 0.3f, new Color(0.55f, 0.45f, 0.35f), 70, 9f, 0.6f, 0.9f, 1.5f, flat: true);
                GroundCracks.ShakeAt(t.position, 0.7f, 0.6f, 30f);
                break;
            case PlayerRole.Arquero:
                // Flecha de Illapa (dios del rayo): la flecha se carga y cae un relámpago sobre el jefe.
                Effects.Projectile(hand, hit, new Color(1f, 1f, 0.6f), arrow: true);
                r.StartCoroutine(r.Delayed(0.3f, () =>
                {
                    r.StartCoroutine(r.Lightning(hit + Vector3.up * 18f, hit, new Color(0.9f, 0.95f, 1f)));
                    r.Ring(target, new Color(0.85f, 0.9f, 1f), 6f, 0.5f);
                    Effects.Burst(hit, new Color(1f, 1f, 0.7f), 60, 9f, 0.35f, 0.6f, 0.4f);
                    GroundCracks.ShakeAt(target, 0.5f, 0.35f, 35f);
                }));
                break;
            case PlayerRole.Chaman:
                // Espíritu del jaguar: una bola verde y morada que salta y estalla en garras de luz.
                Effects.Projectile(hand, hit, new Color(0.4f, 1f, 0.45f), arrow: false);
                r.StartCoroutine(r.Delayed(0.4f, () =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var off = Quaternion.Euler(0, i * 120f, 0) * Vector3.forward * 1.2f;
                        Effects.Burst(hit + off, new Color(0.35f, 1f, 0.4f), 30, 6f, 0.4f, 0.7f, 0.2f);
                    }
                    Effects.Burst(hit, new Color(0.7f, 0.3f, 1f), 50, 8f, 0.6f, 1f, -0.4f);
                    r.Ring(target, new Color(0.4f, 1f, 0.45f), 6f, 0.7f);
                }));
                break;
            default:
                // Bendición del Inti: sol que cae sobre todo el grupo.
                r.Ring(t.position, gold, 12f, 1f);
                r.Pillar(t.position, gold, 14f, 2.2f, 1.4f);
                foreach (var p in FindObjectsByType<PlayerController>())
                    if (Vector3.Distance(p.transform.position, t.position) < 30f)
                    {
                        r.Pillar(p.transform.position, new Color(1f, 0.9f, 0.5f), 7f, 0.9f, 1.1f);
                        Effects.Burst(p.transform.position + Vector3.up, new Color(1f, 0.92f, 0.55f), 25, 2.5f, 0.3f, 1.3f, -0.7f);
                    }
                foreach (var b in BotAlly.All)
                    if (b != null) r.Pillar(b.transform.position, new Color(1f, 0.9f, 0.5f), 7f, 0.9f, 1.1f);
                break;
        }
    }

    // Para los jefes y otros scripts.
    public static void RingAt(Vector3 pos, Color color, float radius, float duration) => Runner.Ring(pos, color, radius, duration);

    public static void BossSlash(Transform who, Color color, float radius, float dir, float duration) =>
        Runner.StartCoroutine(Runner.Arc(who, color, radius, dir, duration, 1.6f, 0.9f));

    // Estela del arma de un héroe cuerpo a cuerpo (variante = dirección del tajo).
    public static void HeroSwing(Transform who, Color color, int variant)
    {
        float dir = variant == 1 ? -1f : 1f;
        float height = variant == 0 ? 1.8f : 1.2f;
        Runner.StartCoroutine(Runner.Arc(who, color, 1.6f, dir, 0.18f, height, variant == 0 ? 1.2f : 0.4f));
    }

    // Arco de luz que barre delante de 'who' (de un lado al otro según dir), a cierta altura y con inclinación.
    IEnumerator Arc(Transform who, Color color, float radius, float dir, float duration, float height, float slant)
    {
        var go = new GameObject("Estela");
        var line = go.AddComponent<LineRenderer>();
        line.material = Mat(Texture2D.whiteTexture, color);
        line.positionCount = 18;
        line.widthCurve = new AnimationCurve(new Keyframe(0, 0f), new Keyframe(0.6f, 0.45f), new Keyframe(1, 0.05f));
        for (float t = 0; t < duration && who != null; t += Time.deltaTime)
        {
            float k = t / duration;
            var center = who.position + Vector3.up * height;
            for (int i = 0; i < 18; i++)
            {
                float u = i / 17f * Mathf.Min(1f, k * 1.8f);
                float a = Mathf.Lerp(-80f, 80f, u) * dir;
                line.SetPosition(i, center + Quaternion.AngleAxis(a, Vector3.up) * who.forward * radius + Vector3.up * (u - 0.5f) * slant * -dir);
            }
            line.startColor = line.endColor = new Color(color.r, color.g, color.b, 1f - k * k);
            yield return null;
        }
        Destroy(go);
    }

    // ---------------------------------------------------------------- poderes definitivos (R)
    public static void Ultimate(PlayerAbilities caster, PlayerRole role, Vector3 bossPos) =>
        Runner.StartCoroutine(Runner.UltRoutine(caster, role, bossPos));

    IEnumerator UltRoutine(PlayerAbilities caster, PlayerRole role, Vector3 boss)
    {
        var gold = new Color(1f, 0.8f, 0.3f);
        CameraFollow.Shake(0.35f, 0.4f);
        switch (role)
        {
            case PlayerRole.Guerrero:
                // Lluvia de soles: columnas de sol que caen alrededor del jefe.
                Ring(boss, gold, 10f, 1f);
                for (int i = 0; i < 8; i++)
                {
                    var p = boss + Quaternion.Euler(0, i * 45, 0) * Vector3.forward * Random.Range(0.5f, 3.5f);
                    Pillar(p, new Color(1f, 0.7f, 0.2f), 16f, 1.4f, 0.8f);
                    Effects.Burst(p + Vector3.up, gold, 30, 7f, 0.4f, 0.6f, 0.5f);
                    yield return new WaitForSeconds(0.09f);
                }
                Effects.Burst(boss + Vector3.up * 2f, gold, 120, 12f, 0.6f, 1f, 0.3f);
                break;
            case PlayerRole.Guardian:
                // Muralla del Inti: cúpula dorada sobre cada compañero.
                foreach (var p in FindObjectsByType<PlayerAbilities>())
                    StartCoroutine(Dome(p.transform, new Color(1f, 0.85f, 0.4f), 2.2f, 4f));
                Ring(caster.transform.position, gold, 16f, 1f);
                break;
            case PlayerRole.Arquero:
                // Flecha del cóndor: una flecha enorme y una explosión verde.
                var from = caster.transform.position + Vector3.up * 2f;
                Effects.Projectile(from, boss + Vector3.up * 2.5f, new Color(0.5f, 1f, 0.45f), arrow: true);
                Effects.Projectile(from + Vector3.up * 0.4f, boss + Vector3.up * 2.8f, new Color(0.5f, 1f, 0.45f), arrow: true);
                yield return new WaitForSeconds(0.35f);
                Pillar(boss, new Color(0.45f, 1f, 0.45f), 18f, 3f, 1f);
                Ring(boss, new Color(0.5f, 1f, 0.45f), 9f, 0.8f);
                Effects.Burst(boss + Vector3.up * 2.5f, new Color(0.6f, 1f, 0.5f), 120, 12f, 0.6f, 1f, 0.3f);
                break;
            case PlayerRole.Chaman:
                // Tormenta de rayos: varios rayos caen del cielo sobre el jefe.
                for (int i = 0; i < 7; i++)
                {
                    var hit = boss + new Vector3(Random.Range(-2f, 2f), Random.Range(0.5f, 3f), Random.Range(-2f, 2f));
                    StartCoroutine(Lightning(hit + Vector3.up * 18f + Random.insideUnitSphere * 3f, hit, new Color(0.8f, 0.6f, 1f)));
                    yield return new WaitForSeconds(0.12f);
                }
                Ring(boss, new Color(0.7f, 0.45f, 1f), 8f, 0.8f);
                break;
            default:
                // Renacer del Inti: columna dorada sobre cada compañero.
                foreach (var p in FindObjectsByType<PlayerAbilities>())
                {
                    Pillar(p.transform.position, gold, 12f, 1.6f, 1.5f);
                    Ring(p.transform.position, gold, 5f, 1f);
                    Effects.Burst(p.transform.position + Vector3.up, new Color(1f, 0.95f, 0.6f), 60, 3f, 0.35f, 1.5f, -0.8f);
                }
                break;
        }
    }

    // ---------------------------------------------------------------- RUNAS (avisos de ataque)
    static Shader additive;

    static Material RuneMat(string tex, Color color)
    {
        if (additive == null) additive = Shader.Find("Legacy Shaders/Particles/Additive");
        Material m;
        if (additive != null)
        {
            m = new Material(additive) { mainTexture = UIStyle.Tex(tex) };
            m.SetColor("_TintColor", color);
        }
        else m = new Material(Shader.Find("Sprites/Default")) { mainTexture = UIStyle.Tex(tex), color = color };
        return m;
    }

    static void SetRuneColor(Material m, Color c)
    {
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c); else m.color = c;
    }

    static GameObject RuneQuad(string tex, Color color, out Material mat)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        mat = RuneMat(tex, color);
        q.GetComponent<Renderer>().material = mat;
        return q;
    }

    // Runa de aviso: aparece girando, se va llenando desde el centro hasta el momento del golpe y destella.
    public static void RuneWarning(Vector3 pos, float radius, float duration, Color color) =>
        Runner.StartCoroutine(Runner.RuneRoutine(pos, radius, duration, color));

    IEnumerator RuneRoutine(Vector3 pos, float radius, float duration, Color color)
    {
        var outer = RuneQuad("runa_circulo", color, out var mo);
        var inner = RuneQuad("runa_circulo", color, out var mi);
        foreach (var q in new[] { outer, inner }) q.transform.rotation = Quaternion.Euler(90, 0, 0);
        outer.transform.position = new Vector3(pos.x, pos.y + 0.08f, pos.z);
        inner.transform.position = new Vector3(pos.x, pos.y + 0.09f, pos.z);
        // Mancha oscura debajo: hace que la runa se vea también sobre suelo claro (pasto, agua, de día).
        var stain = MakeQuad(new Color(0f, 0f, 0f, 0f));
        stain.transform.SetPositionAndRotation(new Vector3(pos.x, pos.y + 0.07f, pos.z), Quaternion.Euler(90, 0, 0));
        stain.transform.localScale = Vector3.one * radius * 2.4f;
        var sm = stain.GetComponent<Renderer>().material;
        sm.mainTexture = Effects.SoftMaterial().mainTexture;
        var dark = Color.Lerp(Color.black, color, 0.3f);
        float nextSpark = 0f;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            sm.color = new Color(dark.r, dark.g, dark.b, 0.6f * Mathf.Clamp01(t / 0.2f));
            if (t >= nextSpark)
            {
                // Chispas que suben desde el borde (se ven aunque la runa quede tapada).
                nextSpark = t + (radius < 1.8f ? 0.25f : 0.1f);
                float a = Random.Range(0f, Mathf.PI * 2f);
                Effects.Burst(pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius + Vector3.up * 0.2f, Color.Lerp(color, Color.white, 0.25f), 3, 1.3f, 0.25f, 0.6f, -0.9f);
            }
            float k = t / duration;
            float appear = Mathf.Clamp01(t / 0.25f);
            outer.transform.localScale = Vector3.one * radius * 2f * Mathf.Lerp(0.6f, 1f, Mathf.SmoothStep(0, 1, appear));
            outer.transform.rotation = Quaternion.Euler(90, t * 40f, 0);
            inner.transform.localScale = Vector3.one * radius * 2f * k;
            inner.transform.rotation = Quaternion.Euler(90, -t * 90f, 0);
            float pulse = 0.75f + 0.25f * Mathf.Sin(t * (8f + k * 14f));
            SetRuneColor(mo, new Color(color.r, color.g, color.b, 1f * appear * pulse));
            SetRuneColor(mi, new Color(color.r, color.g, color.b, 0.5f + 0.5f * k));
            yield return null;
        }
        // Destello al golpear.
        Destroy(inner);
        Destroy(stain);
        for (float t = 0; t < 0.3f; t += Time.deltaTime)
        {
            float k = t / 0.3f;
            outer.transform.localScale = Vector3.one * radius * 2f * (1f + 0.15f * k);
            SetRuneColor(mo, new Color(color.r, color.g, color.b, (1f - k) * 1.2f));
            yield return null;
        }
        Destroy(outer);
    }

    // ---------------------------------------------------------------- ataques especiales de los jefes

    // Franja roja en el suelo que marca por dónde va a embestir el jefe (se llena hasta el momento del golpe).
    public static void LineWarning(Vector3 start, Vector3 dir, float length, float width, float duration) =>
        Runner.StartCoroutine(Runner.LineWarnRoutine(start, dir, length, width, duration));

    IEnumerator LineWarnRoutine(Vector3 start, Vector3 dir, float length, float width, float duration)
    {
        // Franja de runas rojas por donde va a pasar la embestida; se enciende de atrás hacia adelante.
        var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(90, 0, 0);
        var red = new Color(0.95f, 0.04f, 0.02f);
        var back = RuneQuad("runa_franja", red, out var mb);
        var fill = RuneQuad("runa_franja", red, out var mf);
        mb.mainTextureScale = new Vector2(1f, length / width / 4f);
        back.transform.rotation = fill.transform.rotation = rot;
        back.transform.position = start + dir * length / 2f + Vector3.up * 0.06f;
        back.transform.localScale = new Vector3(width, length, 1f);
        var stain = MakeQuad(new Color(0.12f, 0f, 0f, 0f));
        stain.transform.rotation = rot;
        stain.transform.position = start + dir * length / 2f + Vector3.up * 0.05f;
        stain.transform.localScale = new Vector3(width * 1.15f, length, 1f);
        var stm = stain.GetComponent<Renderer>().material;
        var side = Vector3.Cross(Vector3.up, dir);
        float nextSpark = 0f;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            stm.color = new Color(0.12f, 0f, 0f, 0.5f * Mathf.Clamp01(t / 0.2f));
            if (t >= nextSpark)
            {
                nextSpark = t + 0.08f;
                var along = start + dir * Random.Range(0f, length) + side * (Random.value < 0.5f ? -1f : 1f) * width / 2f;
                Effects.Burst(along + Vector3.up * 0.2f, new Color(1f, 0.3f, 0.15f), 3, 1.3f, 0.25f, 0.6f, -0.9f);
            }
            mb.mainTextureOffset = new Vector2(0f, -t * 0.8f);
            SetRuneColor(mb, new Color(red.r, red.g, red.b, 0.85f + 0.15f * Mathf.Sin(t * 12f)));
            fill.transform.position = start + dir * length * k / 2f + Vector3.up * 0.07f;
            fill.transform.localScale = new Vector3(width * 1.1f, length * k, 1f);
            mf.mainTextureScale = new Vector2(1f, Mathf.Max(0.05f, length * k / width / 4f));
            SetRuneColor(mf, new Color(1f, 0.12f, 0.05f, 1f));
            yield return null;
        }
        Destroy(stain);
        Destroy(back);
        Destroy(fill);
    }

    static GameObject MakeQuad(Color color)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.GetComponent<Renderer>().material = Mat(Texture2D.whiteTexture, color);
        return q;
    }

    // Proyectil del jefe (orbe brillante o cuchillo giratorio) que avanza en línea recta.
    static readonly System.Collections.Generic.Dictionary<int, GameObject> orbs = new System.Collections.Generic.Dictionary<int, GameObject>();

    public static void Orb(int id, Vector3 from, Vector3 dir, float speed, float life, Color color, bool knife) =>
        Runner.StartCoroutine(Runner.OrbRoutine(id, from, dir, speed, life, color, knife));

    public static void StopOrb(int id)
    {
        if (orbs.TryGetValue(id, out var go) && go != null) Destroy(go);
        orbs.Remove(id);
    }

    IEnumerator OrbRoutine(int id, Vector3 from, Vector3 dir, float speed, float life, Color color, bool knife)
    {
        var go = GameObject.CreatePrimitive(knife ? PrimitiveType.Cube : PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = knife ? new Vector3(0.15f, 0.05f, 0.9f) : Vector3.one * 0.7f;
        go.GetComponent<Renderer>().material = Mat(Texture2D.whiteTexture, knife ? new Color(0.85f, 0.85f, 0.9f) : color);
        var trail = go.AddComponent<TrailRenderer>();
        trail.material = Effects.SoftMaterial();
        trail.time = 0.35f;
        trail.startWidth = knife ? 0.3f : 0.7f;
        trail.endWidth = 0f;
        trail.startColor = color;
        trail.endColor = new Color(color.r, color.g, color.b, 0f);
        var light = go.AddComponent<Light>();
        light.color = color;
        light.range = 5f;
        light.intensity = 2.5f;
        orbs[id] = go;
        for (float t = 0; t < life && go != null; t += Time.deltaTime)
        {
            var p = from + dir * speed * t;
            p.y = 1.2f;
            go.transform.position = p;
            go.transform.rotation = knife ? Quaternion.LookRotation(dir) * Quaternion.Euler(0, t * 900f, 0) : Quaternion.identity;
            if (!knife) go.transform.localScale = Vector3.one * (0.7f + 0.1f * Mathf.Sin(t * 20f));
            yield return null;
        }
        if (go != null) Destroy(go);
        orbs.Remove(id);
    }

    // Zona peligrosa: primero un círculo que se llena (aviso) y luego la zona activa que daña.
    // roots = raíces que brotan del suelo (Chullachaqui); si no, un remolino/sombra que gira.
    public static void Zone(Vector3 center, float radius, float warn, float duration, Color color, bool roots) =>
        Runner.StartCoroutine(Runner.ZoneRoutine(center, radius, warn, duration, color, roots));

    IEnumerator ZoneRoutine(Vector3 center, float radius, float warn, float duration, Color color, bool roots)
    {
        // Aviso: runa roja que se llena. Después, la zona activa es una runa del color del jefe que gira.
        StartCoroutine(RuneRoutine(center, radius, warn, new Color(0.95f, 0.04f, 0.02f)));
        yield return new WaitForSeconds(warn);
        var ring = RuneQuad("runa_circulo", color, out var ringMat);
        ring.transform.position = center + Vector3.up * 0.07f;
        ring.transform.rotation = Quaternion.Euler(90, 0, 0);
        ring.transform.localScale = Vector3.one * radius * 2f;
        var disc = MakeQuad(new Color(color.r, color.g, color.b, 0f));
        disc.transform.SetPositionAndRotation(center + Vector3.up * 0.06f, Quaternion.Euler(90, 0, 0));
        var discMat = disc.GetComponent<Renderer>().material;
        discMat.mainTexture = Effects.SoftMaterial().mainTexture;
        disc.transform.localScale = Vector3.one * radius * 2.2f;
        var spikes = new System.Collections.Generic.List<GameObject>();
        if (roots)
        {
            var rootMat = Mat(Texture2D.whiteTexture, new Color(0.35f, 0.25f, 0.12f));
            for (int i = 0; i < 9; i++)
            {
                var r = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(r.GetComponent<Collider>());
                r.GetComponent<Renderer>().material = rootMat;
                var off = Random.insideUnitCircle * radius * 0.85f;
                r.transform.position = center + new Vector3(off.x, 0f, off.y);
                r.transform.rotation = Quaternion.Euler(Random.Range(-25f, 25f), 0, Random.Range(-25f, 25f));
                spikes.Add(r);
            }
            Effects.Burst(center + Vector3.up * 0.2f, new Color(0.45f, 0.35f, 0.2f), 40, 5f, 0.5f, 0.8f, 1.5f, flat: true);
        }
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            float fade = Mathf.Clamp01((duration - t) / 0.5f);
            discMat.color = new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f, (0.45f + 0.1f * Mathf.Sin(t * 6f)) * fade);
            SetRuneColor(ringMat, new Color(color.r, color.g, color.b, (0.9f + 0.1f * Mathf.Sin(t * 5f)) * fade));
            ring.transform.rotation = Quaternion.Euler(90, t * (roots ? 15f : 70f), 0);
            float grow = Mathf.Clamp01(t / 0.2f) * fade;
            for (int i = 0; i < spikes.Count; i++)
                spikes[i].transform.localScale = new Vector3(0.18f, 0.9f * grow * (0.7f + (i % 3) * 0.2f), 0.18f);
            if (!roots && Random.value < 0.15f)
                Effects.Burst(center + Random.insideUnitSphere * radius * 0.6f + Vector3.up * 0.2f, color, 3, 1f, 0.3f, 0.8f, -0.6f);
            yield return null;
        }
        Destroy(ring);
        Destroy(disc);
        foreach (var s in spikes) Destroy(s);
    }

    // ---------------------------------------------------------------- piezas

    static Material Mat(Texture2D tex, Color color)
    {
        var m = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, color = color };
        return m;
    }

    static Texture2D RingTex()
    {
        if (ringTex != null) return ringTex;
        const int n = 128;
        ringTex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) / 0.12f);
                ringTex.SetPixel(x, y, new Color(1, 1, 1, a * a));
            }
        ringTex.Apply();
        return ringTex;
    }

    static Texture2D BeamTex()
    {
        if (beamTex != null) return beamTex;
        const int w = 16, h = 64;
        beamTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                beamTex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(1f - y / (float)h, 1.5f) * 0.9f));
        beamTex.Apply();
        return beamTex;
    }

    // Onda que se expande a ras del suelo.
    public void Ring(Vector3 pos, Color color, float radius, float duration) => StartCoroutine(RingRoutine(pos, color, radius, duration));

    IEnumerator RingRoutine(Vector3 pos, Color color, float radius, float duration)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.transform.position = new Vector3(pos.x, pos.y + 0.08f, pos.z);
        q.transform.rotation = Quaternion.Euler(90, 0, 0);
        var m = Mat(RingTex(), color);
        q.GetComponent<Renderer>().material = m;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            if (q == null) yield break;   // se borró al cambiar de nivel
            float k = t / duration;
            q.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, radius * 2f, 1f - (1f - k) * (1f - k));
            m.color = new Color(color.r, color.g, color.b, 1f - k);
            yield return null;
        }
        Destroy(q);
    }

    // Columna de luz que baja/sube sobre un punto.
    public void Pillar(Vector3 pos, Color color, float height, float radius, float duration) => StartCoroutine(PillarRoutine(pos, color, height, radius, duration));

    IEnumerator PillarRoutine(Vector3 pos, Color color, float height, float radius, float duration)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(c.GetComponent<Collider>());
        var m = Mat(BeamTex(), color);
        c.GetComponent<Renderer>().material = m;
        var light = c.AddComponent<Light>();
        light.color = color;
        light.range = radius * 6f;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            float grow = Mathf.Clamp01(t / 0.15f);
            float w = radius * (1f - k * 0.6f);
            c.transform.position = pos + Vector3.up * height * 0.5f * grow;
            c.transform.localScale = new Vector3(w, height * 0.5f * grow, w);
            m.color = new Color(color.r, color.g, color.b, (1f - k) * 0.9f);
            light.intensity = 4f * (1f - k);
            yield return null;
        }
        Destroy(c);
    }

    // Rayo en zigzag que parpadea entre la mano del Chamán y el jefe.
    public IEnumerator Lightning(Vector3 from, Vector3 to, Color color)
    {
        var go = new GameObject("Rayo");
        var line = go.AddComponent<LineRenderer>();
        line.material = Mat(Texture2D.whiteTexture, color);
        line.positionCount = 12;
        line.widthMultiplier = 0.18f;
        var light = go.AddComponent<Light>();
        light.color = color;
        light.range = 12f;
        go.transform.position = (from + to) / 2f;
        Effects.Burst(to, color, 30, 6f, 0.3f, 0.4f, 0.5f);
        for (float t = 0; t < 0.35f; t += 0.04f)
        {
            for (int i = 0; i < 12; i++)
            {
                float k = i / 11f;
                var p = Vector3.Lerp(from, to, k);
                if (i > 0 && i < 11) p += Random.insideUnitSphere * 0.6f;
                line.SetPosition(i, p);
            }
            line.startColor = line.endColor = new Color(color.r, color.g, color.b, 1f - t / 0.35f);
            light.intensity = Random.Range(3f, 6f) * (1f - t / 0.35f);
            yield return new WaitForSeconds(0.04f);
        }
        Destroy(go);
    }

    // Tajo de cóndor: un arco rojo que barre delante del Guerrero.
    public IEnumerator Slash(Transform who, Color color)
    {
        var go = new GameObject("Tajo");
        var line = go.AddComponent<LineRenderer>();
        line.material = Mat(Texture2D.whiteTexture, color);
        line.positionCount = 16;
        line.widthCurve = new AnimationCurve(new Keyframe(0, 0f), new Keyframe(0.5f, 0.35f), new Keyframe(1, 0f));
        for (float t = 0; t < 0.25f; t += Time.deltaTime)
        {
            float k = t / 0.25f;
            var center = who.position + Vector3.up * 1.3f;
            for (int i = 0; i < 16; i++)
            {
                float a = Mathf.Lerp(-70f, 70f, i / 15f) * Mathf.Min(1f, k * 2f);
                line.SetPosition(i, center + Quaternion.AngleAxis(a, who.up) * who.forward * 1.8f + Vector3.up * (i / 15f - 0.5f) * 0.6f);
            }
            line.startColor = line.endColor = new Color(color.r, color.g, color.b, 1f - k);
            yield return null;
        }
        Destroy(go);
    }

    // Cúpula protectora que sigue al Guardián y se desvanece.
    public IEnumerator Dome(Transform who, Color color, float radius, float duration)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(s.GetComponent<Collider>());
        var m = Mat(Texture2D.whiteTexture, color);
        s.GetComponent<Renderer>().material = m;
        for (float t = 0; t < duration && who != null; t += Time.deltaTime)
        {
            float k = t / duration;
            float pop = Mathf.Min(1f, t / 0.15f);
            s.transform.position = who.position + Vector3.up * 1f;
            s.transform.localScale = Vector3.one * radius * 2f * pop * (1f + 0.04f * Mathf.Sin(t * 20f));
            m.color = new Color(color.r, color.g, color.b, 0.28f * (1f - k * k));
            yield return null;
        }
        Destroy(s);
    }

    // Lluvia de flechas: caen del cielo alrededor del jefe.
    public IEnumerator ArrowRain(Vector3 target)
    {
        // Runa verde en el suelo que marca la zona y luego caen las flechas brillando.
        Ring(target, new Color(0.45f, 1f, 0.4f), 4f, 1.2f);
        Pillar(target, new Color(0.5f, 1f, 0.45f), 12f, 2.5f, 0.6f);
        for (int i = 0; i < 22; i++)
        {
            var land = target + new Vector3(Random.Range(-3f, 3f), Random.Range(0.2f, 3f), Random.Range(-3f, 3f));
            var from = land + new Vector3(Random.Range(-2f, 2f), 14f, Random.Range(-4f, -2f));
            Effects.Projectile(from, land, new Color(0.55f, 1f, 0.45f), arrow: true);
            yield return new WaitForSeconds(0.035f);
        }
    }

    public IEnumerator Delayed(float seconds, System.Action action)
    {
        yield return new WaitForSeconds(seconds);
        action();
    }
}
