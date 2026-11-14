using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// "Auto-rig" casero para los héroes (modelos estáticos de TRELLIS/TripoSR que Mixamo no pudo riggear):
// mide la forma del modelo, coloca un esqueleto humanoide simple (cadera, columna, pecho, cabeza,
// piernas con rodilla y brazos con codo) y reparte cada vértice entre los huesos según su altura y
// distancia al centro. Convierte el MeshRenderer en SkinnedMeshRenderer y agrega HeroRig.
public static class AutoRig
{
    const string MeshFolder = "Assets/Juego/Mallas";

    public static bool Build(GameObject model, Transform facing, HeroRig.Style style, string name)
    {
        var filters = model.GetComponentsInChildren<MeshFilter>();
        if (filters.Length == 0) return false;
        if (PrefabUtility.IsPartOfPrefabInstance(model))
            PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(model), PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        var root = model.transform;
        // Ejes del esqueleto (espacio local del modelo): arriba = arriba del mundo, adelante = frente del héroe.
        Vector3 up = root.InverseTransformDirection(Vector3.up).normalized;
        Vector3 fwd = root.InverseTransformDirection(Vector3.ProjectOnPlane(facing.forward, Vector3.up)).normalized;
        fwd = Vector3.ProjectOnPlane(fwd, up).normalized;
        Vector3 side = Vector3.Cross(up, fwd).normalized;   // derecha del héroe

        // Todos los vértices en espacio del modelo para medir.
        var all = new List<Vector3>();
        foreach (var f in filters)
            foreach (var v in f.sharedMesh.vertices)
                all.Add(root.InverseTransformPoint(f.transform.TransformPoint(v)));

        float minH = float.MaxValue, maxH = float.MinValue;
        Vector3 centroid = Vector3.zero;
        foreach (var p in all) { float h = Vector3.Dot(p, up); minH = Mathf.Min(minH, h); maxH = Mathf.Max(maxH, h); centroid += p; }
        centroid /= all.Count;
        float H = maxH - minH;
        Vector3 basePt = centroid - up * (Vector3.Dot(centroid, up) - minH);   // centro a nivel del piso

        // Ancho de hombros: percentil 92 de |lateral| a la altura del pecho (incluye los brazos colgando).
        float shoulder = Percentile(all, p => Mathf.Abs(Vector3.Dot(p - basePt, side)), p => Rel(p, basePt, up, H) is float h && h > 0.66f && h < 0.78f, 0.92f) * 0.78f;
        float hipHalf = Percentile(all, p => Mathf.Abs(Vector3.Dot(p - basePt, side)), p => Rel(p, basePt, up, H) is float h && h > 0.2f && h < 0.3f, 0.85f) * 0.5f;
        shoulder = Mathf.Clamp(shoulder, 0.12f * H, 0.24f * H);
        hipHalf = Mathf.Clamp(hipHalf, 0.04f * H, 0.1f * H);
        float torsoHalf = shoulder * 0.62f;
        // Profundidad de los brazos: mediana de la profundidad de lo que está fuera del torso a la altura del codo;
        // lo que está bastante más atrás que eso es capa.
        float armDepth = Percentile(all, p => Vector3.Dot(p - basePt, fwd),
            p => Rel(p, basePt, up, H) is float h && h > 0.42f && h < 0.5f && Mathf.Abs(Vector3.Dot(p - basePt, side)) > torsoHalf * 1.05f, 0.5f);

        Vector3 P(float lateral, float h) => basePt + side * lateral + up * (h * H);

        // Esqueleto (rotaciones identidad, todo en el espacio del modelo).
        Transform Bone(string n, Transform parent, Vector3 pos)
        {
            var t = new GameObject(n).transform;
            t.SetParent(parent, false);
            t.position = root.TransformPoint(pos);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            return t;
        }
        var rigRoot = new GameObject("Esqueleto").transform;
        rigRoot.SetParent(root, false);
        var hips = Bone("Cadera", rigRoot, P(0, 0.50f));
        var spine = Bone("Columna", hips, P(0, 0.58f));
        var chest = Bone("Pecho", spine, P(0, 0.70f));
        var head = Bone("Cabeza", chest, P(0, 0.84f));
        var legL = Bone("PiernaI", hips, P(-hipHalf, 0.49f));
        var kneeL = Bone("RodillaI", legL, P(-hipHalf, 0.26f));
        var legR = Bone("PiernaD", hips, P(hipHalf, 0.49f));
        var kneeR = Bone("RodillaD", legR, P(hipHalf, 0.26f));
        var armL = Bone("BrazoI", chest, P(-shoulder, 0.79f));
        var elbowL = Bone("CodoI", armL, P(-shoulder * 1.08f, 0.61f));
        var armR = Bone("BrazoD", chest, P(shoulder, 0.79f));
        var elbowR = Bone("CodoD", armR, P(shoulder * 1.08f, 0.61f));
        var bones = new[] { hips, spine, chest, head, legL, kneeL, legR, kneeR, armL, elbowL, armR, elbowR };
        const int HIPS = 0, SPINE = 1, CHEST = 2, HEAD = 3, LEGL = 4, KNEEL = 5, LEGR = 6, KNEER = 7, ARML = 8, ELBL = 9, ARMR = 10, ELBR = 11;

        System.IO.Directory.CreateDirectory(MeshFolder);
        foreach (var f in filters)
        {
            var src = f.sharedMesh;
            var mesh = Object.Instantiate(src);
            mesh.name = src.name + "_esqueleto";
            var verts = src.vertices;
            var weights = new BoneWeight[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var p = root.InverseTransformPoint(f.transform.TransformPoint(verts[i]));
                float h = Rel(p, basePt, up, H);
                float lat = Vector3.Dot(p - basePt, side);
                float alat = Mathf.Abs(lat);
                float depth = Vector3.Dot(p - basePt, fwd);   // + adelante, - atrás (capas)
                var w = new Dictionary<int, float>();
                void Add(int b, float v) { if (v <= 0f) return; w.TryGetValue(b, out float o); w[b] = o + v; }

                // Brazos: fuera del torso, entre la mano y el hombro.
                float torsoAt = torsoHalf * Mathf.Lerp(0.85f, 1f, Mathf.InverseLerp(0.4f, 0.75f, h));
                float arm = Mathf.InverseLerp(torsoAt - 0.015f * H, torsoAt + 0.035f * H, alat)
                          * Mathf.InverseLerp(0.30f, 0.38f, h) * (1f - Mathf.InverseLerp(0.80f, 0.86f, h))
                          // Las capas y mantos cuelgan detrás del cuerpo: no son brazo.
                          * (1f - Mathf.InverseLerp(0.05f * H, 0.08f * H, Mathf.Abs(depth - armDepth)));
                // Piernas: debajo de la cadera; se separan izquierda/derecha cerca del centro con suavidad.
                float leg = (1f - Mathf.InverseLerp(0.44f, 0.54f, h)) * (1f - arm);
                float rest = Mathf.Max(0f, 1f - arm - leg);

                if (arm > 0f)
                {
                    float fore = 1f - Mathf.InverseLerp(0.57f, 0.65f, h);
                    bool left = lat < 0f;
                    Add(left ? ELBL : ELBR, arm * fore);
                    Add(left ? ARML : ARMR, arm * (1f - fore));
                }
                if (leg > 0f)
                {
                    float rightK = Mathf.InverseLerp(-0.025f * H, 0.025f * H, lat);
                    float lower = 1f - Mathf.InverseLerp(0.22f, 0.30f, h);
                    Add(KNEEL, leg * (1f - rightK) * lower);
                    Add(LEGL, leg * (1f - rightK) * (1f - lower));
                    Add(KNEER, leg * rightK * lower);
                    Add(LEGR, leg * rightK * (1f - lower));
                }
                if (rest > 0f)
                {
                    if (h < 0.58f) { float k = Mathf.InverseLerp(0.52f, 0.62f, h); Add(HIPS, rest * (1 - k)); Add(SPINE, rest * k); }
                    else if (h < 0.80f) { float k = Mathf.InverseLerp(0.62f, 0.72f, h); Add(SPINE, rest * (1 - k)); Add(CHEST, rest * k); }
                    else { float k = Mathf.InverseLerp(0.80f, 0.86f, h); Add(CHEST, rest * (1 - k)); Add(HEAD, rest * k); }
                }
                weights[i] = ToBoneWeight(w);
            }
            mesh.boneWeights = weights;
            var bind = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++) bind[b] = bones[b].worldToLocalMatrix * f.transform.localToWorldMatrix;
            mesh.bindposes = bind;
            string path = $"{MeshFolder}/{name}_{f.gameObject.name}_esqueleto.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);

            var go = f.gameObject;
            var mats = go.GetComponent<MeshRenderer>().sharedMaterials;
            Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(f);
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.sharedMaterials = mats;
            skin.bones = bones;
            skin.rootBone = hips;
            skin.updateWhenOffscreen = true;
        }

        var rig = model.AddComponent<HeroRig>();
        rig.style = style;
        rig.side = side; rig.up = up; rig.forward = fwd;
        rig.hips = hips; rig.spine = spine; rig.chest = chest; rig.head = head;
        rig.legL = legL; rig.kneeL = kneeL; rig.legR = legR; rig.kneeR = kneeR;
        rig.armL = armL; rig.elbowL = elbowL; rig.armR = armR; rig.elbowR = elbowR;
        Debug.Log($"AUTORIG {name}: altura {H:F2}, hombros {shoulder / H:F2}H, cadera {hipHalf / H:F2}H, brazos a {armDepth / H:F2}H de profundidad");
        return true;
    }

    static float Rel(Vector3 p, Vector3 basePt, Vector3 up, float H) => Vector3.Dot(p - basePt, up) / H;

    static float Percentile(List<Vector3> pts, System.Func<Vector3, float> value, System.Func<Vector3, bool> filter, float q)
    {
        var vals = new List<float>();
        foreach (var p in pts) if (filter(p)) vals.Add(value(p));
        if (vals.Count == 0) return 0f;
        vals.Sort();
        return vals[Mathf.Clamp((int)(q * (vals.Count - 1)), 0, vals.Count - 1)];
    }

    static BoneWeight ToBoneWeight(Dictionary<int, float> w)
    {
        var list = new List<KeyValuePair<int, float>>(w);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        while (list.Count > 4) list.RemoveAt(list.Count - 1);
        float total = 0f;
        foreach (var kv in list) total += kv.Value;
        if (total <= 0f) return new BoneWeight { boneIndex0 = 2, weight0 = 1f };
        var bw = new BoneWeight();
        for (int i = 0; i < list.Count; i++)
        {
            float v = list[i].Value / total;
            switch (i)
            {
                case 0: bw.boneIndex0 = list[i].Key; bw.weight0 = v; break;
                case 1: bw.boneIndex1 = list[i].Key; bw.weight1 = v; break;
                case 2: bw.boneIndex2 = list[i].Key; bw.weight2 = v; break;
                default: bw.boneIndex3 = list[i].Key; bw.weight3 = v; break;
            }
        }
        return bw;
    }
}
