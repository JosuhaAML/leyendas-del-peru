using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Compila el juego para Windows en "D:\Juego Final\Build".
public static class BuildGame
{
    [MenuItem("Leyendas/Compilar para Windows")]
    public static void Windows()
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = System.Array.ConvertAll(EditorBuildSettings.scenes, s => s.path),
            locationPathName = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../Build/LeyendasDelPeru.exe")),
            target = BuildTarget.StandaloneWindows64,
        });
        Debug.Log("BUILD " + report.summary.result + " errores=" + report.summary.totalErrors);
    }

    public static void RunBatch()
    {
        Windows();
        EditorApplication.Exit(0);
    }
}
