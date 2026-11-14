using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Saca una captura de cada escena desde su cámara inicial (para revisar cómo se ven).
public static class Capturas
{
    public static void RunBatch()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../capturas"));
        Directory.CreateDirectory(outDir);
        foreach (var s in EditorBuildSettings.scenes)
        {
            EditorSceneManager.OpenScene(s.path);
            var cam = Camera.main;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, Path.GetFileNameWithoutExtension(s.path) + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = null;
        }
        EditorApplication.Exit(0);
    }

    // Primer plano de una casa y de un árbol real en cada nivel (para revisar los modelos nuevos).
    public static void Detalles()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Pruebas/detalles"));
        Directory.CreateDirectory(outDir);
        foreach (var s in EditorBuildSettings.scenes)
        {
            if (!s.path.Contains("Nivel")) continue;
            EditorSceneManager.OpenScene(s.path);
            var cam = Camera.main;
            string name = Path.GetFileNameWithoutExtension(s.path);
            int k = 0;
            foreach (var target in new[] { "Casa ribereña", "Casa de adobe", "Arbol real" })
            {
                var go = GameObject.Find(target);
                if (go == null) continue;
                var t = go.transform;
                bool tree = target == "Arbol real";
                var fwd = tree ? (Vector3.zero - t.position).normalized : t.forward;
                fwd.y = 0f;
                cam.transform.position = t.position + fwd.normalized * (tree ? 14f : 11f) + Vector3.up * (tree ? 4f : 3.2f);
                cam.transform.LookAt(t.position + Vector3.up * (tree ? 6f : 2.8f));
                var rt = new RenderTexture(1280, 720, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, $"{name}_{k++}.png"), tex.EncodeToPNG());
                cam.targetTexture = null;
                RenderTexture.active = null;
            }
        }
        EditorApplication.Exit(0);
    }
}
