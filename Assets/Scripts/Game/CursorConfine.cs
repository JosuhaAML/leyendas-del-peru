using UnityEngine;

// Mantiene el ratón dentro de la ventana del juego (en pantalla completa o en ventana) mientras el juego
// está activo: así no se escapa a la otra pantalla ni se hace clic fuera sin querer. Al cambiar de programa
// (Alt+Tab) se libera.
public class CursorConfine : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("Raton en la ventana");
        DontDestroyOnLoad(go);
        go.AddComponent<CursorConfine>();
    }

    void Update()
    {
        var want = Application.isFocused ? CursorLockMode.Confined : CursorLockMode.None;
        if (Cursor.lockState != want) Cursor.lockState = want;
    }

    void OnApplicationFocus(bool focus) => Cursor.lockState = focus ? CursorLockMode.Confined : CursorLockMode.None;
}
