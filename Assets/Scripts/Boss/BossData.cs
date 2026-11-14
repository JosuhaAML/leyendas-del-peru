using UnityEngine;

// Un archivo de datos por jefe: Create > Leyendas > Jefe.
// Así los 5 jefes usan el mismo código y solo cambian sus números y ataques.
[CreateAssetMenu(menuName = "Leyendas/Jefe")]
public class BossData : ScriptableObject
{
    public string bossName = "Chullachaqui";
    [TextArea] public string leyenda;
    public Texture2D retrato;            // imagen de la presentación al empezar el nivel
    public float introSeconds = 6f;      // el jefe espera quieto mientras se muestra
    public float maxHealth = 5000f;
    public float moveSpeed = 3.5f;

    [Header("Ataque básico al que tenga más amenaza")]
    public float meleeDamage = 15f;
    public float meleeRange = 4f;
    public float meleeCooldown = 2f;

    [Header("Ataque de área (todos deben alejarse)")]
    public float areaDamage = 40f;
    public float areaRadius = 8f;
    public float areaCooldown = 15f;
    public float areaWarningTime = 2f;   // tiempo para esquivar

    [Header("Fase 2 (furia) bajo este % de vida")]
    [Range(0f, 1f)] public float enrageAt = 0.3f;
    public float enrageDamageMultiplier = 1.5f;
}
