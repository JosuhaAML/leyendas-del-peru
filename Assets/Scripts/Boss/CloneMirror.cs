using UnityEngine;

// Réplica del Chullachaqui: copia EXACTAMENTE lo que hace el verdadero (caminar, girar, atacar), pero
// rotada alrededor del jugador al que persigue, como un reflejo. Así las 4 te rodean, se acercan y atacan a la vez.
// Se calcula con la posición sincronizada del jefe → todos los jugadores ven las réplicas en el mismo sitio.
// Solo es visual: no hace ni recibe daño.
public class CloneMirror : MonoBehaviour
{
    public Transform realVisual;   // "Visual" del jefe verdadero
    public Health realHealth;
    public Vector3 center;         // punto de giro (se va moviendo hacia el jugador más cercano)
    public float angle;            // giro de esta réplica respecto al verdadero
    public float lifeUntil;

    Animator anim, realAnim;
    Vector3 lastPos;
    float nextStep;

    void Start()
    {
        anim = GetComponentInChildren<Animator>();
        realAnim = realVisual != null ? realVisual.GetComponentInChildren<Animator>() : null;
        // Sin el ajuste al suelo propio (ya viene corregido del verdadero).
        foreach (var g in GetComponentsInChildren<BossGrounder>()) Destroy(g);
        center = NearestPlayer(center);
        lastPos = transform.position;
    }

    bool shown;

    Vector3 NearestPlayer(Vector3 fallback)
    {
        Vector3 best = fallback; float bestD = float.MaxValue;
        foreach (var p in FindObjectsByType<PlayerController>())
        {
            var h = p.GetComponent<Health>();
            if (h == null || h.IsDead) continue;
            float d = (p.transform.position - realVisual.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = p.transform.position; }
        }
        best.y = fallback.y;
        return best;
    }

    void LateUpdate()
    {
        if (realVisual == null || realHealth == null || realHealth.IsDead || Time.time > lifeUntil)
        {
            Poof();
            Destroy(gameObject);
            return;
        }

        center = Vector3.Lerp(center, NearestPlayer(center), 3f * Time.deltaTime);
        var rot = Quaternion.Euler(0f, angle, 0f);
        var pos = center + rot * (realVisual.position - center);
        // Que no salgan de la arena.
        var flat = new Vector3(pos.x, 0f, pos.z);
        if (flat.magnitude > 42f) { flat = flat.normalized * 42f; pos = new Vector3(flat.x, pos.y, flat.z); }
        transform.SetPositionAndRotation(pos, rot * realVisual.rotation);
        if (!shown) { shown = true; lastPos = pos; Poof(); }   // nube de hojas donde aparece

        // Misma animación y en el mismo instante que el verdadero (ataques incluidos).
        if (anim != null && realAnim != null && realAnim.runtimeAnimatorController != null)
        {
            var s = realAnim.GetCurrentAnimatorStateInfo(0);
            var mine = anim.GetCurrentAnimatorStateInfo(0);
            if (mine.fullPathHash != s.fullPathHash) anim.Play(s.fullPathHash, 0, s.normalizedTime % 1f);
            foreach (var p in realAnim.parameters)
                if (p.type == AnimatorControllerParameterType.Float) anim.SetFloat(p.nameHash, realAnim.GetFloat(p.nameHash));
        }

        // Pisadas con polvo, igual que el verdadero.
        float speed = Time.deltaTime > 0f ? Vector3.Distance(pos, lastPos) / Time.deltaTime : 0f;
        if (speed > 0.6f && Time.time >= nextStep)
        {
            nextStep = Time.time + 0.55f;
            Effects.Burst(pos + Vector3.up * 0.1f, new Color(0.45f, 0.38f, 0.28f), 8, 2f, 0.4f, 0.5f, 0.3f, flat: true);
        }
        lastPos = pos;
    }

    // Nube de hojas al aparecer y al desaparecer.
    void Poof() => Effects.Burst(transform.position + Vector3.up * 1.5f, new Color(0.35f, 0.6f, 0.2f), 30, 5f, 0.4f, 1f, 0.5f);
}
