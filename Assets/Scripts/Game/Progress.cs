using UnityEngine;

// Progreso de la aventura guardado en esta PC: hay que vencer el nivel 1 para abrir el 2, y así.
public static class Progress
{
    public const int Levels = 5;
    const string Key = "NivelesDesbloqueados";

    // Cuántos niveles están abiertos (1 = solo el primero).
    public static int Unlocked
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(Key, 1), 1, Levels);
        private set { PlayerPrefs.SetInt(Key, Mathf.Clamp(value, 1, Levels)); PlayerPrefs.Save(); }
    }

    // Niveles vencidos (0 a 5). Al vencer el último se marca la aventura como completada.
    public static int Beaten => Mathf.Clamp(PlayerPrefs.GetInt("NivelesVencidos", 0), 0, Levels);

    public static bool IsUnlocked(int level) => level < Unlocked;
    public static bool IsBeaten(int level) => level < Beaten;

    // Siguiente nivel por jugar (el primero sin vencer, o el último si ya ganaste todo).
    public static int NextLevel => Mathf.Min(Beaten, Levels - 1);

    public static void MarkBeaten(int level)
    {
        if (level < 0 || level >= Levels) return;
        if (level + 1 > Beaten) { PlayerPrefs.SetInt("NivelesVencidos", level + 1); PlayerPrefs.Save(); }
        if (level + 2 > Unlocked) Unlocked = level + 2;
    }

    // Para la presentación: abre todos los niveles (F9 en el menú).
    public static void UnlockAll() => Unlocked = Levels;

    public static void Reset()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.DeleteKey("NivelesVencidos");
        PlayerPrefs.Save();
    }

    // Número de nivel (0-4) a partir del nombre de la escena "NivelN_...", o -1.
    public static int LevelOfScene(string scene) =>
        scene.StartsWith("Nivel") && scene.Length > 5 && char.IsDigit(scene[5]) ? scene[5] - '1' : -1;
}
