using UnityEngine;

// Amuletos andinos: al vencer a cada jefe, cada jugador elige uno de tres y se acumulan durante la aventura.
// Se guardan en PlayerAbilities.AmuletMask (3 bits por tipo, máximo 7 de cada uno) y los ve el servidor.
public enum AmuletType { Vida, Fuerza, Curacion, Inti, Viento, Sombra }

public static class Amulets
{
    public static readonly string[] Names = { "Amuleto de Vida", "Amuleto de Fuerza", "Amuleto de Curación", "Amuleto del Inti", "Amuleto del Viento", "Amuleto de Sombra" };
    public static readonly string[] Desc = { "+20 % de vida máxima", "+15 % de daño", "+20 % de curación", "Definitivo se carga 30 % más rápido", "+12 % de velocidad", "Esquivas 25 % más seguido" };
    public static readonly string[] Icon = { "corazon", "espadas", "cura", "sol", "flecha", "escudo" };
    public static readonly Color[] Colors =
    {
        new Color(0.95f, 0.35f, 0.35f), new Color(1f, 0.6f, 0.25f), new Color(0.45f, 0.95f, 0.5f),
        new Color(1f, 0.85f, 0.3f), new Color(0.55f, 0.85f, 1f), new Color(0.7f, 0.5f, 1f),
    };
    static readonly float[] Bonus = { 0.2f, 0.15f, 0.2f, 0.3f, 0.12f, 0.25f };

    public static int Count(PlayerAbilities p, AmuletType t) => p == null ? 0 : (p.AmuletMask.Value >> (3 * (int)t)) & 7;

    public static int Total(PlayerAbilities p)
    {
        int n = 0;
        for (int i = 0; i < 6; i++) n += Count(p, (AmuletType)i);
        return n;
    }

    // Multiplicador acumulado del amuleto (1 = sin amuletos de ese tipo).
    public static float Mult(PlayerAbilities p, AmuletType t) => 1f + Bonus[(int)t] * Count(p, t);

    // Tres amuletos distintos al azar para elegir (con semilla, para que no cambien mientras se dibuja el panel).
    public static AmuletType[] Offer(int seed)
    {
        var rnd = new System.Random(seed);
        var list = new System.Collections.Generic.List<int> { 0, 1, 2, 3, 4, 5 };
        var result = new AmuletType[3];
        for (int i = 0; i < 3; i++)
        {
            int k = rnd.Next(list.Count);
            result[i] = (AmuletType)list[k];
            list.RemoveAt(k);
        }
        return result;
    }
}
