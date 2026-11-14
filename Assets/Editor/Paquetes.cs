using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

// Instala el importador de modelos .glb (glTFast) desde la línea de comandos.
public static class Paquetes
{
    static AddRequest request;

    public static void AgregarGltf()
    {
        request = Client.Add("com.unity.cloud.gltfast");
        EditorApplication.update += Esperar;
    }

    static void Esperar()
    {
        if (!request.IsCompleted) return;
        EditorApplication.update -= Esperar;
        if (request.Status == StatusCode.Success) Debug.Log("PKG " + request.Result.name + "@" + request.Result.version);
        else Debug.LogError("PKG ERROR " + request.Error.message);
        EditorApplication.Exit(request.Status == StatusCode.Success ? 0 : 1);
    }
}
