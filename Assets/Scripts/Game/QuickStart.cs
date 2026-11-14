using System.Collections;
using Unity.Netcode;
using UnityEngine;

// Atajo para probar: abrir el juego directo en un nivel, jugando solo (con bots).
//   LeyendasDelPeru.exe -jugarnivel 5
public class QuickStart : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-jugarnivel");
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out int level)) return;
        var go = new GameObject("Inicio rapido");
        DontDestroyOnLoad(go);
        go.AddComponent<QuickStart>().StartCoroutine(Run(Mathf.Clamp(level, 1, Progress.Levels) - 1));
    }

    static IEnumerator Run(int level)
    {
        while (NetworkManager.Singleton == null || LevelManager.Instance == null) yield return null;
        yield return new WaitForSeconds(1f);
        if (PlayerAbilities.PreferredRole < 0) PlayerAbilities.PreferredRole = 0;   // sin clase elegida: Guerrero
        NetworkManager.Singleton.StartHost();
        yield return new WaitForSeconds(0.5f);
        LevelManager.Instance.StartAt(level);
    }
}
