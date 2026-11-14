using UnityEngine;

// Logros guardados en esta PC (PlayerPrefs). Se ven en el lobby y avisan con un cartel al desbloquearse.
public static class Achievements
{
    public static readonly (string id, string name, string desc)[] All =
    {
        ("primera", "Primera leyenda", "Vence a tu primer jefe."),
        ("cinco", "Las cinco leyendas", "Termina la aventura completa."),
        ("intocable", "Intocable", "Vence a un jefe sin caer ni una vez."),
        ("angel", "Ángel de la guarda", "Revive a 3 aliados (en total)."),
        ("criticos", "Golpe certero", "Haz 10 golpes críticos."),
        ("combo", "Trabajo en equipo", "Logra un combo tras la provocación del Guardián."),
        ("definitivo", "Poder del Inti", "Usa tu poder definitivo (R)."),
        ("mareado", "¡A la carga!", "Deja aturdido a un jefe."),
        ("sanador", "Manos sanadoras", "Cura 1000 de vida en total."),
        ("amuletos", "Coleccionista", "Reúne 3 amuletos en una aventura."),
    };

    public static bool Has(string id) => PlayerPrefs.GetInt("logro_" + id, 0) == 1;

    public static int UnlockedCount
    {
        get { int n = 0; foreach (var a in All) if (Has(a.id)) n++; return n; }
    }

    public static void Unlock(string id)
    {
        if (Has(id)) return;
        PlayerPrefs.SetInt("logro_" + id, 1);
        PlayerPrefs.Save();
        foreach (var a in All)
            if (a.id == id) HUD.Toast("Logro desbloqueado: " + a.name);
        GameAudio.Play("victoria");
    }

    static int Add(string stat, int amount)
    {
        int v = PlayerPrefs.GetInt("stat_" + stat, 0) + amount;
        PlayerPrefs.SetInt("stat_" + stat, v);
        return v;
    }

    // ---- Eventos (los de combate los decide el servidor y llegan al dueño con PlayerAbilities.StatRpc)
    public static void OnStat(int stat, float amount)
    {
        switch (stat)
        {
            case 0: if (Add("criticos", 1) >= 10) Unlock("criticos"); break;
            case 1: Unlock("combo"); break;
            case 2: Unlock("definitivo"); break;
            case 3: if (Add("revividos", 1) >= 3) Unlock("angel"); break;
            case 4: if (Add("curado", Mathf.RoundToInt(amount)) >= 1000) Unlock("sanador"); break;
        }
    }

    // Servidor: avisa al dueño del jugador para que cuente en SU PC.
    public static void ReportHit(PlayerAbilities p, bool crit, bool combo)
    {
        if (crit) p.SendStat(0, 1f);
        if (combo) p.SendStat(1, 1f);
    }

    public static void ReportUlt(PlayerAbilities p) => p.SendStat(2, 1f);

    public static void OnBossBeaten(int level, bool diedThisLevel)
    {
        Unlock("primera");
        if (!diedThisLevel) Unlock("intocable");
        if (level >= Progress.Levels - 1) Unlock("cinco");
    }
}
