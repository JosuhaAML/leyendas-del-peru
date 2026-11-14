using System.Collections;
using UnityEngine;

// Cámara lenta breve (esquiva perfecta jugando solo). Usa tiempo real para volver a la normalidad.
public class SlowMo : MonoBehaviour
{
    static SlowMo runner;
    Coroutine current;

    public static void Play(float scale, float realSeconds)
    {
        if (runner == null)
        {
            var go = new GameObject("CamaraLenta");
            DontDestroyOnLoad(go);
            runner = go.AddComponent<SlowMo>();
        }
        if (runner.current != null) runner.StopCoroutine(runner.current);
        runner.current = runner.StartCoroutine(runner.Run(scale, realSeconds));
    }

    IEnumerator Run(float scale, float realSeconds)
    {
        Time.timeScale = scale;
        yield return new WaitForSecondsRealtime(realSeconds);
        // Vuelve suave a velocidad normal.
        for (float t = 0f; t < 0.2f; t += Time.unscaledDeltaTime)
        {
            if (!PauseMenu.Open) Time.timeScale = Mathf.Lerp(scale, 1f, t / 0.2f);
            yield return null;
        }
        if (!PauseMenu.Open) Time.timeScale = 1f;
        current = null;
    }
}
