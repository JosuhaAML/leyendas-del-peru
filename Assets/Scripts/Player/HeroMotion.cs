using UnityEngine;

// Animación por código para los héroes que todavía no tienen esqueleto (modelos estáticos de TRELLIS).
// Mueve el modelo entero: respira al estar quieto, se inclina y rebota al correr, se lanza al atacar,
// brilla al curar y cae al morir. Es solo visual: corre igual en todas las PCs.
public class HeroMotion : MonoBehaviour
{
    [SerializeField] Color castColor = new Color(1f, 0.85f, 0.35f);

    Vector3 basePos;
    Quaternion baseRot;
    Vector3 baseScale;
    float speed, phase, actionTime = -10f, deathBlend;
    string action;
    bool dead;
    Light glow;
    HeroRig rig;   // esqueleto del héroe (si tiene): mueve piernas y brazos
    int lastStep;

    const float AttackTime = 0.55f, CastTime = 0.9f;

    void Awake()
    {
        rig = GetComponentInChildren<HeroRig>();
        basePos = transform.localPosition;
        baseRot = transform.localRotation;
        baseScale = transform.localScale;

        glow = new GameObject("Brillo").AddComponent<Light>();
        glow.transform.SetParent(transform.parent, false);
        glow.transform.localPosition = new Vector3(0f, 1.2f, 0.3f);
        glow.type = LightType.Point;
        glow.range = 5f;
        glow.intensity = 0f;
        glow.color = castColor;
    }

    // Lo llama PlayerController cada cuadro con la velocidad real y si está muerto.
    public void Tick(float moveSpeed, bool isDead)
    {
        speed = Mathf.Lerp(speed, moveSpeed, 10f * Time.deltaTime);
        if (isDead && !dead) actionTime = Time.time;
        dead = isDead;
    }

    public void Play(string trigger)
    {
        if (dead) return;
        // Capas que no interrumpen el ataque o el hechizo en curso.
        if (trigger == "Dodge") { dodgeAt = Time.time; return; }
        if (trigger == "Hit") { hitAt = Time.time; return; }
        if (trigger == "Victory") { cheerAt = Time.time; return; }
        if (trigger == "Wave") { waveAt = Time.time; return; }
        if (trigger == "Dance") { danceAt = Time.time; return; }
        action = trigger;
        actionTime = Time.time;
        if (trigger == "Attack" && rig != null)
        {
            // Los ataques van alternando: por arriba, barrido lateral y estocada.
            attackVariant = (attackVariant + 1) % 3;
            rig.variant = attackVariant;
            if (rig.style == HeroRig.Style.Melee) StartCoroutine(SwingTrail(attackVariant));
        }
    }

    int attackVariant = -1;
    float dodgeAt = -10f, hitAt = -10f, cheerAt = -10f, waveAt = -10f, danceAt = -10f;
    const float WaveTime = 2.4f, DanceTime = 4.5f;
    const float DodgeTime = 0.45f, HitTime = 0.35f, CheerTime = 3f;

    // Estela del arma justo en el momento del golpe.
    System.Collections.IEnumerator SwingTrail(int variant)
    {
        yield return new WaitForSeconds(AttackTime * (variant == 2 ? 0.4f : 0.33f));
        AbilityFX.HeroSwing(transform, new Color(1f, 0.92f, 0.7f), variant);
    }

    void Update()
    {
        float run = Mathf.Clamp01(speed / 6f);
        phase += Time.deltaTime * Mathf.Lerp(1.5f, 11f, run);

        // Quieto: respira. Corriendo: rebota a cada paso y se inclina hacia adelante, balanceándose.
        float bob = Mathf.Lerp(Mathf.Sin(phase) * 0.02f, Mathf.Abs(Mathf.Sin(phase)) * 0.12f, run);
        float lean = 12f * run;
        float sway = Mathf.Sin(phase) * 6f * run;
        float breathe = 1f + Mathf.Sin(phase) * 0.012f * (1f - run);
        Vector3 offset = new Vector3(0f, bob, 0f);
        float glowTarget = 0f;

        float since = Time.time - actionTime;
        float attackT = -1f, castT = -1f;
        if (rig != null)
        {
            // Con esqueleto: el cuerpo casi no se inclina, se mueven piernas y brazos.
            lean = 5f * run;
            sway = 0f;
            if (!dead && action == "Attack" && since < AttackTime)
            {
                attackT = since / AttackTime;
                offset.z += 0.25f * Mathf.Sin(attackT * Mathf.PI);   // pasito adelante al golpear
            }
            else if (!dead && action == "Cast" && since < CastTime)
            {
                castT = since / CastTime;
                float k = Mathf.Sin(castT * Mathf.PI);
                offset.y += 0.12f * k;
                glowTarget = 4f * k;
            }
        }
        else if (!dead && action == "Attack" && since < 0.45f)
        {
            // Ataque: se echa atrás y luego se lanza hacia adelante.
            float k = since < 0.15f ? -since / 0.15f : Mathf.Sin((since - 0.15f) / 0.3f * Mathf.PI);
            lean += 25f * k;
            offset.z += 0.45f * Mathf.Max(0f, k);
        }
        else if (!dead && action == "Cast" && since < 0.9f)
        {
            // Curación / hechizo: se eleva un poco y brilla.
            float k = Mathf.Sin(since / 0.9f * Mathf.PI);
            offset.y += 0.25f * k;
            lean -= 8f * k;
            glowTarget = 4f * k;
        }

        deathBlend = Mathf.MoveTowards(deathBlend, dead ? 1f : 0f, (dead ? 2.5f : 4f) * Time.deltaTime);
        var alive = Quaternion.Euler(lean, 0f, sway);
        var fallen = Quaternion.Euler(-85f, 0f, 0f);
        transform.localRotation = baseRot * Quaternion.Slerp(alive, fallen, deathBlend);
        transform.localPosition = basePos + Vector3.Lerp(offset, new Vector3(0f, 0.15f, -0.2f), deathBlend);
        transform.localScale = baseScale * Mathf.Lerp(breathe, 1f, deathBlend);
        glow.intensity = Mathf.Lerp(glow.intensity, glowTarget, 12f * Time.deltaTime);
        if (rig != null)
        {
            float dSince = Time.time - dodgeAt, hSince = Time.time - hitAt, cSince = Time.time - cheerAt;
            rig.dodgeT = dSince < DodgeTime ? dSince / DodgeTime : -1f;
            rig.hitT = hSince < HitTime ? hSince / HitTime : -1f;
            rig.cheerT = cSince < CheerTime ? cSince / CheerTime : -1f;
            float wSince = Time.time - waveAt, dnSince = Time.time - danceAt;
            rig.waveT = wSince < WaveTime ? wSince / WaveTime : -1f;
            rig.danceT = dnSince < DanceTime ? dnSince / DanceTime : -1f;
            if (rig.danceT >= 0f) transform.localPosition += Vector3.up * Mathf.Abs(Mathf.Sin(Time.time * 7f)) * 0.08f;   // rebote del baile
            // La esquiva también agacha y adelanta todo el cuerpo.
            if (rig.dodgeT >= 0f)
            {
                float i = Mathf.Sin(rig.dodgeT * Mathf.PI);
                transform.localPosition += new Vector3(0f, -0.3f * i, 0.2f * i);
                transform.localRotation *= Quaternion.Euler(20f * i, 0f, 0f);
            }
            rig.Pose(run, phase, attackT, castT, deathBlend, Mathf.Sin(phase * 0.5f));
        }

        // Polvo en cada pisada al correr (cada medio ciclo del paso).
        int step = Mathf.FloorToInt(phase / Mathf.PI);
        if (step != lastStep)
        {
            lastStep = step;
            if (run > 0.5f && !dead)
                Effects.Burst(transform.position + Vector3.up * 0.05f, new Color(0.6f, 0.52f, 0.4f, 0.7f), 5, 1.2f, 0.3f, 0.45f, 0.05f, flat: true);
        }
    }
}
