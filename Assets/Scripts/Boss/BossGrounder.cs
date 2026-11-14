using System.Collections;
using UnityEngine;

// Apoya al jefe en el suelo: tras unos cuadros de animación de reposo mide el vértice más bajo
// del modelo real (no la caja de límites, que suele ser más grande) y lo baja/sube para que
// los pies toquen el piso. Solo corrige una vez y como máximo 2 m.
public class BossGrounder : MonoBehaviour
{
    public Transform groundRef;   // el objeto raíz del jefe (su Y es el nivel del suelo)

    IEnumerator Start()
    {
        for (int i = 0; i < 6; i++) yield return null;
        Snap();
    }

    void Snap()
    {
        float ground = groundRef != null ? groundRef.position.y : 0f;
        float min = float.MaxValue;
        var baked = new Mesh();
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!smr.enabled) continue;
            smr.BakeMesh(baked, true);
            var m = smr.transform.localToWorldMatrix;
            foreach (var v in baked.vertices) min = Mathf.Min(min, m.MultiplyPoint3x4(v).y);
        }
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            var m = mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices) min = Mathf.Min(min, m.MultiplyPoint3x4(v).y);
        }
        Destroy(baked);
        if (min == float.MaxValue) return;
        float delta = ground - min;
        if (Mathf.Abs(delta) > 0.02f && Mathf.Abs(delta) < 2f)
        {
            transform.position += Vector3.up * delta;
            Debug.Log($"GROUNDER {transform.root.name}: corregido {delta:F2} m");
        }
    }
}
