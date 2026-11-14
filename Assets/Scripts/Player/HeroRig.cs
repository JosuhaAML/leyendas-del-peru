using UnityEngine;

// Esqueleto simple de un héroe (lo arma AutoRig en el editor a partir de la forma del modelo).
// HeroMotion lo mueve por código: piernas al caminar, brazos al correr, golpe, disparo y hechizo.
public class HeroRig : MonoBehaviour
{
    public enum Style { Melee, Bow, Staff }

    public Style style;
    public Vector3 side = Vector3.right;   // eje lateral (en el espacio del esqueleto): girar en él = adelante/atrás
    public Vector3 up = Vector3.up;
    public Vector3 forward = Vector3.forward;
    public float outSign = 1f;   // ajusta hacia dónde es "afuera" para los brazos (depende del modelo)
    public int variant;           // tipo de ataque cuerpo a cuerpo: 0 por arriba, 1 barrido lateral, 2 estocada
    // Tiempo normalizado (0..1) de las capas extra; < 0 = inactivas. Las pone HeroMotion.
    public float dodgeT = -1f, hitT = -1f, cheerT = -1f, waveT = -1f, danceT = -1f;
    public Transform hips, spine, chest, head;
    public Transform legL, kneeL, legR, kneeR;
    public Transform armL, elbowL, armR, elbowR;

    // Ángulo positivo = hacia adelante.
    Quaternion Fwd(float deg) => Quaternion.AngleAxis(-deg, side);
    // sign: +1 brazo/pierna izquierda, -1 derecha. Ángulo positivo = alejarse del cuerpo.
    Quaternion Out(float deg, float sign) => Quaternion.AngleAxis(-deg * sign * outSign, forward);
    Quaternion Twist(float deg) => Quaternion.AngleAxis(deg, up);

    // run: 0 quieto .. 1 corriendo; phase: ciclo del paso; attack/cast: 0..1 tiempo normalizado de la acción (o <0 si no hay).
    public void Pose(float run, float phase, float attack, float cast, float dead, float breathe)
    {
        float s = Mathf.Sin(phase);
        float c = Mathf.Cos(phase);

        // Piernas: zancada alterna y rodillas que se doblan al levantar el pie.
        float stride = 38f * run;
        legL.localRotation = Fwd(stride * s);
        legR.localRotation = Fwd(-stride * s);
        kneeL.localRotation = Fwd(-45f * run * Mathf.Max(0f, c));
        kneeR.localRotation = Fwd(-45f * run * Mathf.Max(0f, -c));

        // Brazos: se balancean al revés de las piernas; quieto respiran un poco.
        // Quieto: pose original del modelo (sin doblar codos) y solo un leve vaivén al respirar.
        float armSwing = 30f * run * s;
        float idleArm = (1f - run) * breathe * 1.2f;
        Quaternion aL = Fwd(-armSwing + idleArm) * Out(6f * run, 1f);
        Quaternion aR = Fwd(armSwing + idleArm) * Out(6f * run, -1f);
        Quaternion eL = Fwd(30f * run);
        Quaternion eR = Fwd(30f * run);
        Quaternion torso = Twist(8f * run * s);
        Quaternion chestRot = Quaternion.identity;
        Quaternion headRot = Twist(-6f * run * s);

        if (attack >= 0f)
        {
            switch (style)
            {
                case Style.Bow:
                {
                    // Tensa el arco: brazo izquierdo al frente, derecho tira de la cuerda y suelta.
                    float k = Mathf.Sin(Mathf.Clamp01(attack / 0.8f) * Mathf.PI * 0.5f);
                    float release = attack > 0.75f ? (attack - 0.75f) / 0.25f : 0f;
                    aL = Quaternion.Slerp(aL, Fwd(70f), k);
                    eL = Quaternion.Slerp(eL, Fwd(8f), k);
                    aR = Quaternion.Slerp(aR, Fwd(60f), k);
                    eR = Quaternion.Slerp(eR, Fwd(80f - 60f * release), k);
                    torso = Twist(-35f * k + 15f * release);
                    chestRot = Fwd(-6f * k);
                    break;
                }
                case Style.Staff when variant != 1:
                {
                    // Golpe con el báculo: lo levanta y lo baja hacia el frente.
                    float up1 = attack < 0.35f ? attack / 0.35f : 1f - (attack - 0.35f) / 0.65f;
                    aR = Fwd(Mathf.Lerp(15f, 75f, up1) - (attack > 0.35f ? 30f * (1f - up1) : 0f));
                    eR = Fwd(35f * up1);
                    chestRot = Fwd(-12f * up1 + 22f * (attack > 0.35f ? 1f - up1 : 0f));
                    torso = Twist(15f * up1 - 20f * (attack > 0.35f ? 1f - up1 : 0f));
                    break;
                }
                case Style.Melee when variant == 1:
                case Style.Staff when variant == 1:
                {
                    // Barrido lateral: carga girando el torso a un lado y barre hacia el otro.
                    float wind = attack < 0.3f ? Smooth(attack / 0.3f) : 0f;
                    float strike = attack >= 0.3f ? Smooth(Mathf.Clamp01((attack - 0.3f) / 0.45f)) : 0f;
                    torso = Twist(attack < 0.3f ? 40f * wind : Mathf.Lerp(40f, -55f, strike));
                    aR = Fwd(65f) * Out(attack < 0.3f ? 25f * wind : Mathf.Lerp(25f, 5f, strike), -1f);
                    eR = Fwd(Mathf.Lerp(45f, 10f, strike));
                    aL = Fwd(30f);
                    eL = Fwd(50f);
                    chestRot = Fwd(8f * strike);
                    hips.localRotation = Twist(-12f * strike);
                    break;
                }
                case Style.Melee when variant == 2:
                {
                    // Estocada: echa el brazo atrás y lo dispara al frente con un paso largo.
                    float wind = attack < 0.35f ? Smooth(attack / 0.35f) : 0f;
                    float strike = attack >= 0.35f ? Smooth(Mathf.Clamp01((attack - 0.35f) / 0.3f)) : 0f;
                    float back = 1f - strike;
                    aR = Fwd(attack < 0.35f ? Mathf.Lerp(10f, 15f, wind) : Mathf.Lerp(15f, 85f, strike));
                    eR = Fwd(attack < 0.35f ? 95f * wind : Mathf.Lerp(95f, 5f, strike));
                    aL = Fwd(20f) * Out(10f, 1f);
                    eL = Fwd(40f);
                    torso = Twist(attack < 0.35f ? 22f * wind : Mathf.Lerp(22f, -18f, strike));
                    chestRot = Fwd(attack < 0.35f ? -6f * wind : 14f * strike * (attack < 0.85f ? 1f : back));
                    legL.localRotation = Fwd(32f * (attack < 0.35f ? wind : 1f));
                    kneeL.localRotation = Fwd(-28f * (attack < 0.35f ? wind : 1f));
                    legR.localRotation = Fwd(-14f * strike);
                    break;
                }
                default:
                {
                    // Golpe de maza / escudo: carga arriba y descarga en arco hacia abajo, girando el torso.
                    float wind = attack < 0.33f ? attack / 0.33f : 0f;
                    float strike = attack >= 0.33f ? (attack - 0.33f) / 0.67f : 0f;
                    // Brazo moderado (las capas van pegadas al brazo); el golpe se nota con el giro del torso y el cuerpo.
                    float armAngle = attack < 0.33f ? Mathf.Lerp(10f, 80f, Smooth(wind)) : Mathf.Lerp(80f, 25f, Smooth(Mathf.Clamp01(strike * 1.6f)));
                    aR = Fwd(armAngle);
                    eR = Fwd(attack < 0.33f ? 70f * wind : 25f);
                    aL = Fwd(25f);   // escudo / brazo libre al frente
                    eL = Fwd(50f);
                    torso = Twist(attack < 0.33f ? 30f * wind : 30f - 65f * Smooth(Mathf.Clamp01(strike * 1.6f)));
                    chestRot = Fwd(attack < 0.33f ? -10f * wind : 22f * Mathf.Sin(strike * Mathf.PI));
                    legL.localRotation = Fwd(18f * (1f - strike) * (attack >= 0.33f ? 1f : wind));
                    kneeL.localRotation = Fwd(-15f * (attack >= 0.33f ? 1f - strike : wind));
                    break;
                }
            }
        }
        else if (cast >= 0f)
        {
            // Hechizo / curación: alza los brazos al cielo.
            float k = Mathf.Sin(cast * Mathf.PI);
            aL = Quaternion.Slerp(aL, Fwd(65f), k);
            aR = Quaternion.Slerp(aR, Fwd(65f), k);
            eL = Quaternion.Slerp(eL, Fwd(45f), k);
            eR = Quaternion.Slerp(eR, Fwd(45f), k);
            chestRot = Fwd(-16f * k);
            headRot = Fwd(-15f * k);
        }

        if (dead > 0f)
        {
            // Caído: brazos y piernas sueltos.
            aL = Quaternion.Slerp(aL, Fwd(170f) * Out(30f, 1f), dead);
            aR = Quaternion.Slerp(aR, Fwd(170f) * Out(30f, -1f), dead);
            eL = Quaternion.Slerp(eL, Quaternion.identity, dead);
            eR = Quaternion.Slerp(eR, Quaternion.identity, dead);
            legL.localRotation = Quaternion.Slerp(legL.localRotation, Out(12f, 1f), dead);
            legR.localRotation = Quaternion.Slerp(legR.localRotation, Out(12f, -1f), dead);
            kneeL.localRotation = Quaternion.Slerp(kneeL.localRotation, Quaternion.identity, dead);
            kneeR.localRotation = Quaternion.Slerp(kneeR.localRotation, Quaternion.identity, dead);
            headRot = Quaternion.Slerp(headRot, Twist(30f), dead);
        }

        // ---- Capas extra: esquiva, golpe recibido, celebración y mirar alrededor estando quieto.
        float idle = (1f - run) * (attack < 0f && cast < 0f ? 1f : 0f) * (1f - dead);
        if (idle > 0f)
        {
            headRot = headRot * Twist(28f * Mathf.Sin(Time.time * 0.45f) * idle) * Fwd(4f * Mathf.Sin(Time.time * 0.3f) * idle);
            torso = torso * Twist(6f * Mathf.Sin(Time.time * 0.45f - 0.6f) * idle);
        }
        if (dodgeT >= 0f && dead <= 0f)
        {
            // Esquiva: se agacha y se lanza hacia adelante con las piernas recogidas.
            float i = Mathf.Sin(Mathf.Clamp01(dodgeT) * Mathf.PI);
            torso = torso * Fwd(35f * i);
            chestRot = chestRot * Fwd(20f * i);
            aL = Quaternion.Slerp(aL, Fwd(-45f) * Out(20f, 1f), i);
            aR = Quaternion.Slerp(aR, Fwd(-45f) * Out(20f, -1f), i);
            eL = Quaternion.Slerp(eL, Fwd(60f), i);
            eR = Quaternion.Slerp(eR, Fwd(60f), i);
            legL.localRotation = Fwd(60f * i);
            kneeL.localRotation = Fwd(-95f * i);
            legR.localRotation = Fwd(-25f * i);
            kneeR.localRotation = Fwd(-70f * i);
        }
        if (hitT >= 0f && dead <= 0f)
        {
            // Golpe recibido: se echa hacia atrás, abre los brazos y sacude la cabeza.
            float i = Mathf.Sin(Mathf.Clamp01(hitT) * Mathf.PI);
            chestRot = chestRot * Fwd(-18f * i);
            headRot = headRot * Fwd(-16f * i) * Twist(12f * i);
            torso = torso * Twist(10f * i);
            aL = aL * Out(25f * i, 1f);
            aR = aR * Out(25f * i, -1f);
        }
        if (cheerT >= 0f && dead <= 0f)
        {
            // Celebración: brazos arriba (moderado para no estirar las capas) y cabeza en alto.
            float i = Mathf.Clamp01(cheerT * 4f) * Mathf.Clamp01((1f - cheerT) * 4f);
            float bounce = Mathf.Abs(Mathf.Sin(Time.time * 6f));
            aL = Quaternion.Slerp(aL, Fwd(95f + 15f * bounce) * Out(30f, 1f), i);
            aR = Quaternion.Slerp(aR, Fwd(95f + 15f * bounce) * Out(30f, -1f), i);
            eL = Quaternion.Slerp(eL, Fwd(25f), i);
            eR = Quaternion.Slerp(eR, Fwd(25f), i);
            headRot = headRot * Fwd(-15f * i);
            chestRot = chestRot * Fwd(-8f * i);
        }

        if (waveT >= 0f && dead <= 0f)
        {
            // Saludo: levanta el brazo derecho y lo mueve de lado a lado.
            float i = Mathf.Clamp01(waveT * 5f) * Mathf.Clamp01((1f - waveT) * 5f);
            float w = Mathf.Sin(Time.time * 11f);
            aR = Quaternion.Slerp(aR, Fwd(150f) * Out(15f + 20f * w, -1f), i);
            eR = Quaternion.Slerp(eR, Fwd(35f), i);
            headRot = headRot * Out(8f * i, 1f) * Fwd(-6f * i);
        }
        if (danceT >= 0f && dead <= 0f)
        {
            // Baile: cadera y torso giran al ritmo, brazos alternados y rodillas que rebotan.
            float i = Mathf.Clamp01(danceT * 6f) * Mathf.Clamp01((1f - danceT) * 6f);
            float b = Time.time * 7f;
            torso = torso * Twist(22f * Mathf.Sin(b) * i);
            chestRot = chestRot * Out(8f * Mathf.Sin(b * 0.5f) * i, 1f);
            headRot = headRot * Twist(-15f * Mathf.Sin(b) * i);
            aL = Quaternion.Slerp(aL, Fwd(70f + 50f * Mathf.Sin(b)) * Out(25f, 1f), i);
            aR = Quaternion.Slerp(aR, Fwd(70f - 50f * Mathf.Sin(b)) * Out(25f, -1f), i);
            eL = Quaternion.Slerp(eL, Fwd(70f), i);
            eR = Quaternion.Slerp(eR, Fwd(70f), i);
            float kL = Mathf.Max(0f, Mathf.Sin(b)), kR = Mathf.Max(0f, -Mathf.Sin(b));
            legL.localRotation = Quaternion.Slerp(legL.localRotation, Fwd(25f * kL), i);
            kneeL.localRotation = Quaternion.Slerp(kneeL.localRotation, Fwd(-45f * kL), i);
            legR.localRotation = Quaternion.Slerp(legR.localRotation, Fwd(25f * kR), i);
            kneeR.localRotation = Quaternion.Slerp(kneeR.localRotation, Fwd(-45f * kR), i);
        }
        armL.localRotation = aL;
        armR.localRotation = aR;
        elbowL.localRotation = eL;
        elbowR.localRotation = eR;
        spine.localRotation = torso;
        chest.localRotation = chestRot * Fwd(breathe * 1.5f);
        head.localRotation = headRot;
        hips.localRotation = Twist(-5f * run * s);
    }

    static float Smooth(float x) => x * x * (3f - 2f * x);
}
