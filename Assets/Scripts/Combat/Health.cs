using System;
using Unity.Netcode;
using UnityEngine;

// Vida sincronizada por red. Solo el servidor cambia el valor; todos lo ven.
public class Health : NetworkBehaviour
{
    [SerializeField] float maxHealth = 100f;

    public NetworkVariable<float> Current = new NetworkVariable<float>();
    // Vida máxima sincronizada (cambia con la clase del jugador o con el número de jugadores para el jefe).
    public NetworkVariable<float> MaxNet = new NetworkVariable<float>();
    public float Max => MaxNet.Value > 0f ? MaxNet.Value : maxHealth;
    public bool IsDead => Current.Value <= 0f;

    // Mientras sea true no recibe daño (Yacumama sumergida, Pishtaco en las sombras). Solo lo usa el servidor.
    public bool Invulnerable { get; set; }

    // Se dispara en el servidor cuando muere (el jefe lo usa para pasar de nivel).
    public event Action OnDied;

    // Avisa en todas las PCs cada cambio de vida (para los números de daño flotantes).
    public static event Action<Health, float> Changed;
    float spawnTime;

    public override void OnNetworkSpawn()
    {
        if (IsServer) { MaxNet.Value = maxHealth; Current.Value = maxHealth; }
        spawnTime = Time.time;
        Current.OnValueChanged += (oldValue, newValue) =>
        {
            // Se ignoran el ajuste inicial y el revivir al cambiar de nivel.
            if (Time.time - spawnTime < 1.5f || oldValue <= 0f || Mathf.Approximately(oldValue, newValue)) return;
            Changed?.Invoke(this, newValue - oldValue);
        };
    }

    public void SetMax(float value)
    {
        maxHealth = value;
        if (IsServer && IsSpawned) { MaxNet.Value = value; Current.Value = value; }
    }

    // Llamar SOLO desde el servidor.
    public void TakeDamage(float amount, ulong attackerId)
    {
        if (!IsServer || IsDead || Invulnerable || Time.time < protectedUntil) return;
        // Esquiva perfecta: el golpe llegó mientras esquivaba → no hace daño y premia al jugador (una vez por esquiva).
        if (Time.time < dodgeUntil) { if (!dodgeUsed) { dodgeUsed = true; OnPerfectDodge?.Invoke(); } return; }
        Current.Value = Mathf.Max(0f, Current.Value - amount);
        if (TryGetComponent(out BossAI boss)) boss.AddThreat(attackerId, amount);
        if (IsDead) OnDied?.Invoke();
    }

    public void Heal(float amount)
    {
        if (!IsServer || IsDead) return;
        Current.Value = Mathf.Min(Max, Current.Value + amount);
    }

    // fraction: 1 = vida completa (al cambiar de nivel); 0.5 = revivido por un compañero.
    public void Revive(float fraction = 1f)
    {
        if (!IsServer) return;
        Current.Value = Max * fraction;
        protectedUntil = Time.time + 2f;   // 2 s sin recibir daño para que no lo maten al instante
    }

    float protectedUntil;

    // Ventana de esquiva (servidor). Si le pegan dentro de ella, es una esquiva perfecta.
    float dodgeUntil; bool dodgeUsed;
    public event Action OnPerfectDodge;
    public void StartDodge(float window) { if (IsServer) { dodgeUntil = Time.time + window; dodgeUsed = false; } }

    // Sin recibir daño durante unos segundos (Muralla del Inti del Guardián). Solo servidor.
    public void Protect(float seconds)
    {
        if (IsServer) protectedUntil = Mathf.Max(protectedUntil, Time.time + seconds);
    }
}
