using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Árboles y casas que tapan la cámara: si uno está entre la cámara y tu personaje (o la cámara quedó dentro
// de su copa), se oculta mientras estorba. Su sombra se queda, así que casi no se nota y la pelea se ve siempre.
public class CameraFoliage : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("Arboles que no tapan");
        DontDestroyOnLoad(go);
        go.AddComponent<CameraFoliage>();
    }

    // Lo que puede tapar la vista: árboles, casas del pueblo ribereño y andino, y ruinas.
    static readonly string[] Tapan = { "Arbol real", "Arbol", "Palmera", "Casa de adobe", "Casa ribereña", "Ruinas" };

    class Tree { public Bounds bounds; public Renderer[] renderers; public bool hidden; }
    readonly List<Tree> trees = new List<Tree>();
    float nextCheck;

    void OnEnable() => SceneManager.sceneLoaded += OnScene;
    void OnDisable() => SceneManager.sceneLoaded -= OnScene;

    void OnScene(Scene s, LoadSceneMode m)
    {
        trees.Clear();
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (System.Array.IndexOf(Tapan, t.name) < 0) continue;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            trees.Add(new Tree { bounds = b, renderers = rs });
        }
    }

    void LateUpdate()
    {
        if (Time.time < nextCheck || trees.Count == 0) return;
        nextCheck = Time.time + 0.1f;
        var cam = Camera.main;
        var nm = NetworkManager.Singleton;
        var player = nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null ? nm.LocalClient.PlayerObject.transform : null;
        if (cam == null || player == null) return;
        var from = cam.transform.position;
        var to = player.position + Vector3.up * 1.2f;
        var ray = new Ray(from, to - from);
        float dist = Vector3.Distance(from, to);
        foreach (var tr in trees)
        {
            // Un poco más pequeño que la copa entera: solo se oculta si de verdad estorba.
            var shrunk = new Bounds(tr.bounds.center, tr.bounds.size * 0.8f);
            bool block = shrunk.SqrDistance(from) < 1.5f * 1.5f
                      || (shrunk.IntersectRay(ray, out float d) && d < dist - 1f);
            if (block == tr.hidden) continue;
            tr.hidden = block;
            foreach (var r in tr.renderers)
                if (r != null) r.shadowCastingMode = block ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }
    }
}
