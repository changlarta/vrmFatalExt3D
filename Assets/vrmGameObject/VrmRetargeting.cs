using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UniVRM10;
using UniGLTF;

public sealed class VrmRetargeting : IDisposable
{
    private GameObject _bodyRoot;
    private Vrm10Instance _vrmInstance;
    private SkinnedMeshRenderer _bodyPrimarySmr;

    private readonly List<Renderer> _dupRenderers = new();
    private readonly HashSet<Renderer> _bodySuppressed = new();
    private readonly List<(SkinnedMeshRenderer src, SkinnedMeshRenderer dst)> _smrPairs = new();

    private GameObject _tempFace3Instance;

    private readonly List<Mesh> _createdMeshes = new();

    public GameObject InstantiateBodyRoot(GameObject bodyPrefab, Transform parent)
    {
        _bodyRoot = UnityEngine.Object.Instantiate(bodyPrefab, parent);
        _bodyRoot.name = bodyPrefab.name + "_Instance";
        return _bodyRoot;
    }

    public async Task<Vrm10Instance> LoadVrmAsync(byte[] bytes, Transform parent)
    {
        if (bytes == null || bytes.Length == 0) return null;

#if UNITY_WEBGL && !UNITY_EDITOR
        IAwaitCaller awaitCaller = new RuntimeOnlyNoThreadAwaitCaller();
#else
        IAwaitCaller awaitCaller = new RuntimeOnlyAwaitCaller();
#endif

        // URP 向け MaterialGenerator を明示（自動判定に任せない）
        _vrmInstance = await Vrm10.LoadBytesAsync(
            bytes,
            canLoadVrm0X: true,
            controlRigGenerationOption: ControlRigGenerationOption.None,
            showMeshes: false,
            awaitCaller: awaitCaller,
            materialGenerator: new UrpVrm10MaterialDescriptorGenerator()
        );

        if (_vrmInstance == null) return null;

        _vrmInstance.transform.SetParent(parent, false);
        return _vrmInstance;
    }

    public void Align(Transform src, Transform dstLike)
    {
        src.position = dstLike.position;
        src.rotation = dstLike.rotation;

        var p = src.parent;
        if (p != null)
        {
            var parentScale = p.lossyScale;
            var want = dstLike.lossyScale;
            src.localScale = new Vector3(
                parentScale.x != 0 ? want.x / parentScale.x : 0,
                parentScale.y != 0 ? want.y / parentScale.y : 0,
                parentScale.z != 0 ? want.z / parentScale.z : 0
            );
        }
        else
        {
            src.localScale = dstLike.lossyScale;
        }
    }

    public SkinnedMeshRenderer FindPrimaryBodySmr(Transform root)
    {
        var candidates = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var c in candidates)
        {
            if (string.Equals(c.name, "body", StringComparison.OrdinalIgnoreCase)) return _bodyPrimarySmr = c;
            if (string.Equals(c.transform.name, "body", StringComparison.OrdinalIgnoreCase)) return _bodyPrimarySmr = c;
        }
        if (candidates.Length == 0) return _bodyPrimarySmr = null;

        _bodyPrimarySmr = candidates
            .OrderByDescending(s => s.sharedMesh != null ? s.sharedMesh.vertexCount : 0)
            .FirstOrDefault();

        return _bodyPrimarySmr;
    }

    public Dictionary<string, Transform> BuildBoneMap(Transform root)
    {
        var map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!map.ContainsKey(t.name)) map.Add(t.name, t);
        }
        return map;
    }

    public SkinnedMeshRenderer FindBodySmrByExactName(Transform root, string exactName)
    {
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (string.Equals(smr.name, exactName, StringComparison.OrdinalIgnoreCase)) return smr;
            if (string.Equals(smr.transform.name, exactName, StringComparison.OrdinalIgnoreCase)) return smr;
        }
        return null;
    }

    public List<SkinnedMeshRenderer> FindSmrsByNodeName(Transform root, string nodeName)
    {
        var list = new List<SkinnedMeshRenderer>();
        if (string.IsNullOrEmpty(nodeName)) return list;

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(t.name, nodeName, StringComparison.OrdinalIgnoreCase))
                list.AddRange(t.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }
        return list;
    }

    public List<SkinnedMeshRenderer> GuessHair(Transform root)
    {
        var list = new List<SkinnedMeshRenderer>();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var n = smr.name.ToLowerInvariant();
            if (n.Contains("hair") || n.Contains("haier") || n.Contains("haire")) list.Add(smr);
        }
        return list;
    }

    public List<SkinnedMeshRenderer> GuessFace(Transform root)
    {
        var list = new List<SkinnedMeshRenderer>();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var n = smr.name.ToLowerInvariant();
            if (n.Contains("face") || n.Contains("head")) list.Add(smr);
        }
        return list;
    }

    public IEnumerable<Renderer> DisablePotentialFaceHairOnBodyCollect(Transform bodyRoot)
    {
        var suppressed = new List<Renderer>();
        foreach (var smr in bodyRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var n = smr.name.ToLowerInvariant();
            if (n.Equals("face3")) continue;

            if (n.Contains("hair") || n.Contains("haier") || n.Contains("face") || n.Contains("head"))
            {
                smr.enabled = false;
                suppressed.Add(smr);
            }
        }
        foreach (var r in suppressed) _bodySuppressed.Add(r);
        return suppressed;
    }

    public void HideAllRenderers(Transform root)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
    }

    public void CacheBodyRenderersAndHide(Transform bodyRoot, List<Renderer> all)
    {
        all.Clear();
        all.AddRange(bodyRoot.GetComponentsInChildren<Renderer>(true));
        foreach (var r in all) r.enabled = false;
    }

    public void SetCharacterVisible(List<Renderer> bodyAll, HashSet<Renderer> suppressed, List<Renderer> dup, SkinnedMeshRenderer face3, bool on)
    {
        foreach (var r in bodyAll)
        {
            if (suppressed.Contains(r)) continue;
            r.enabled = on;
        }
        foreach (var r in dup) r.enabled = on;
        if (face3 != null) face3.enabled = on;
    }

    public void TransferOutlineToBodyRoot(GameObject fromGO, GameObject toRoot)
    {
        var outlines = fromGO.GetComponents<MonoBehaviour>()
            .Where(m => m != null && m.GetType().Name == "Outline")
            .ToArray();

        if (outlines.Length == 0) return;

        foreach (var src in outlines)
        {
            var t = src.GetType();
            try
            {
                var dst = toRoot.AddComponent(t);
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                foreach (var f in t.GetFields(flags))
                {
                    if (f.IsLiteral || f.IsInitOnly) continue;
                    if (Attribute.IsDefined(f, typeof(NonSerializedAttribute))) continue;
                    try { f.SetValue(dst, f.GetValue(src)); } catch { }
                }

                foreach (var p in t.GetProperties(flags))
                {
                    if (!p.CanRead || !p.CanWrite) continue;
                    if (p.GetIndexParameters().Length != 0) continue;
                    try { p.SetValue(dst, p.GetValue(src)); } catch { }
                }

                UnityEngine.Object.Destroy(src);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VrmRetargeting] Failed to move Outline: " + e.Message);
            }
        }
    }

    public void TryAttachOutlineTo(Transform host)
    {
        if (host == null) return;

        var outline = host.GetComponent<Outline>();
        if (outline == null) outline = host.gameObject.AddComponent<Outline>();

        outline.OutlineColor = Color.black;
        outline.OutlineWidth = 0.3f;

        outline.enabled = true;
    }

    private static Type FindTypeByName(string typeName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(typeName);
            if (t != null) return t;

            try
            {
                t = asm.GetTypes().FirstOrDefault(x => x.Name == typeName);
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    public Transform FindHumanoidHead(Transform root)
    {
        if (root == null) return null;

        var animator = root.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman && animator.avatar != null)
        {
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) return head;
        }

        string[] candidates = { "Head", "head", "J_Bip_C_Head", "HEAD" };
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(t.name, candidates[i], StringComparison.Ordinal)) return t;
            }
        }

        return null;
    }

    public SkinnedMeshRenderer ExtractFace3SmrFromTemp(GameObject face3Prefab, Transform parent)
    {
        _tempFace3Instance = UnityEngine.Object.Instantiate(face3Prefab, parent, false);
        _tempFace3Instance.name = face3Prefab.name + "_Instance";

        var srcFace3Smr = _tempFace3Instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (srcFace3Smr == null)
        {
            UnityEngine.Object.Destroy(_tempFace3Instance);
            _tempFace3Instance = null;
            return null;
        }

        return srcFace3Smr;
    }

    public void DestroyTempFace3()
    {
        if (_tempFace3Instance != null) UnityEngine.Object.Destroy(_tempFace3Instance);
        _tempFace3Instance = null;
    }

    public float MeasureFaceScaleAndApplyPreBakeOffset(
        Transform bodyRoot, Transform vrmRoot,
        IReadOnlyList<SkinnedMeshRenderer> vrmFaceSmrs,
        out Vector3 pivotWS, out float preBakeDeltaY)
    {
        preBakeDeltaY = 0f;
        pivotWS = Vector3.zero;

        var bodyHead = FindHumanoidHead(bodyRoot);
        var vrmHead = FindHumanoidHead(vrmRoot);

        if (bodyHead != null && vrmHead != null)
        {
            preBakeDeltaY = bodyHead.position.y - vrmHead.position.y;
            if (Mathf.Abs(preBakeDeltaY) > 1e-6f)
            {
                vrmRoot.position += new Vector3(0f, preBakeDeltaY, 0f);
            }
        }

        float S = 1f;
        pivotWS = bodyHead != null ? bodyHead.position : Vector3.zero;

        try
        {
            SkinnedMeshRenderer rep = null;
            int maxVerts = -1;

            foreach (var fsmr in vrmFaceSmrs)
            {
                var m = fsmr != null ? fsmr.sharedMesh : null;
                int vc = (m != null) ? m.vertexCount : -1;
                if (vc > maxVerts) { maxVerts = vc; rep = fsmr; }
            }

            if (rep != null && rep.sharedMesh != null)
            {
                var tmp = new Mesh();
                rep.BakeMesh(tmp);

                var l2w = rep.transform.localToWorldMatrix;
                var verts = tmp.vertices;

                if (verts != null && verts.Length > 0)
                {
                    float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        var v = l2w.MultiplyPoint3x4(verts[i]);
                        if (v.y < minY) minY = v.y;
                        if (v.y > maxY) maxY = v.y;
                    }

                    float measured = (maxY - minY);
                    float targetFaceLength = 0.233343f;
                    S = (targetFaceLength > 0f && measured > 0f) ? (targetFaceLength / measured) : 1f;
                }

                UnityEngine.Object.Destroy(tmp);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[VrmRetargeting] Face scale measure failed; S=1. " + ex.Message);
            S = 1f;
        }

        return S;
    }

    public void RevertPreBakeOffset(Transform vrmRoot, float preBakeDeltaY)
    {
        if (Mathf.Abs(preBakeDeltaY) > 1e-6f)
        {
            vrmRoot.position -= new Vector3(0f, preBakeDeltaY, 0f);
        }
    }

    public SkinnedMeshRenderer CreateNoBake(
        SkinnedMeshRenderer src,
        Dictionary<string, Transform> targetBoneMap,
        SkinnedMeshRenderer bodyPrimarySmr)
    {
        if (src == null || src.sharedMesh == null || bodyPrimarySmr == null) return null;

        var go = new GameObject("VRM_" + src.name + "_NoBake");
        go.transform.SetParent(bodyPrimarySmr.transform.parent, false);
        go.transform.position = src.transform.position;
        go.transform.rotation = src.transform.rotation;
        go.transform.localScale = src.transform.lossyScale;

        var dst = go.AddComponent<SkinnedMeshRenderer>();
        var srcMesh = src.sharedMesh;
        var mesh = UnityEngine.Object.Instantiate(srcMesh);
        _createdMeshes.Add(mesh);

        dst.sharedMaterials = src.sharedMaterials.ToArray();
        dst.updateWhenOffscreen = true;

        var mappedRoot = MapOrCreateBoneChain(src.rootBone, targetBoneMap, out _);
        if (mappedRoot == null) mappedRoot = bodyPrimarySmr.rootBone;
        if (mappedRoot == null) { UnityEngine.Object.Destroy(go); return null; }
        dst.rootBone = mappedRoot;

        var mapped = new Transform[src.bones.Length];
        for (int i = 0; i < src.bones.Length; i++)
        {
            var m = MapOrCreateBoneChain(src.bones[i], targetBoneMap, out _);
            if (m == null) { UnityEngine.Object.Destroy(go); return null; }
            mapped[i] = m;
        }
        dst.bones = mapped;

        var newBindposes = new Matrix4x4[mapped.Length];
        var smrL2W = dst.transform.localToWorldMatrix;
        for (int i = 0; i < mapped.Length; i++)
        {
            newBindposes[i] = mapped[i].worldToLocalMatrix * smrL2W;
        }
        mesh.bindposes = newBindposes;
        dst.sharedMesh = mesh;

        var bc = mesh.blendShapeCount;
        for (int i = 0; i < bc; i++)
        {
            var w = src.GetBlendShapeWeight(i);
            dst.SetBlendShapeWeight(i, w);
        }

        return dst;
    }

    public (List<(SkinnedMeshRenderer src, SkinnedMeshRenderer dst)> pairs, List<Renderer> created)
        BakeAndRebindAll(IEnumerable<SkinnedMeshRenderer> srcSmrs,
        Dictionary<string, Transform> targetBoneMap,
        SkinnedMeshRenderer bodyPrimarySmr,
        Vector3 facePivotWS, float faceUniformScaleS)
    {
        var pairs = new List<(SkinnedMeshRenderer, SkinnedMeshRenderer)>();
        var created = new List<Renderer>();

        foreach (var smr in srcSmrs)
        {
            var dup = CreateRendererOnBodyWithBakeAndRebind(smr, targetBoneMap, bodyPrimarySmr, facePivotWS, faceUniformScaleS);
            if (dup == null) { Debug.LogError("[VrmRetargeting] Bake&Rebind failed: " + smr.name); continue; }

            pairs.Add((smr, dup));
            created.Add(dup);
        }

        _smrPairs.AddRange(pairs);
        _dupRenderers.AddRange(created);
        return (pairs, created);
    }

    private SkinnedMeshRenderer CreateRendererOnBodyWithBakeAndRebind(
        SkinnedMeshRenderer src,
        Dictionary<string, Transform> targetBoneMap,
        SkinnedMeshRenderer bodyPrimarySmr,
        Vector3 pivotWS, float scaleS)
    {
        if (src == null || src.sharedMesh == null || bodyPrimarySmr == null) return null;

        var go = new GameObject("VRM_" + src.name);
        go.transform.SetParent(bodyPrimarySmr.transform.parent, false);
        go.transform.localPosition = bodyPrimarySmr.transform.localPosition;
        go.transform.localRotation = bodyPrimarySmr.transform.localRotation;
        go.transform.localScale = bodyPrimarySmr.transform.localScale;

        var dst = go.AddComponent<SkinnedMeshRenderer>();
        var srcMesh = src.sharedMesh;
        var mesh = UnityEngine.Object.Instantiate(srcMesh);
        _createdMeshes.Add(mesh);

        dst.sharedMaterials = src.sharedMaterials.ToArray();
        dst.updateWhenOffscreen = true;

        var mappedRoot = MapOrCreateBoneChain(src.rootBone, targetBoneMap, out _);
        if (mappedRoot == null) mappedRoot = bodyPrimarySmr.rootBone;
        if (mappedRoot == null) { UnityEngine.Object.Destroy(go); return null; }
        dst.rootBone = mappedRoot;

        var mapped = new Transform[src.bones.Length];
        for (int i = 0; i < src.bones.Length; i++)
        {
            var m = MapOrCreateBoneChain(src.bones[i], targetBoneMap, out _);
            if (m == null) { UnityEngine.Object.Destroy(go); return null; }
            mapped[i] = m;
        }
        dst.bones = mapped;

        var newBindposes = new Matrix4x4[mapped.Length];
        var smrL2W = dst.transform.localToWorldMatrix;
        for (int i = 0; i < mapped.Length; i++)
        {
            newBindposes[i] = mapped[i].worldToLocalMatrix * smrL2W;
        }
        mesh.bindposes = newBindposes;

        var baked = new Mesh();
        src.BakeMesh(baked);

        var dstW2L = dst.transform.worldToLocalMatrix;
        var srcL2W = src.transform.localToWorldMatrix;

        var Tpos = Matrix4x4.Translate(pivotWS);
        var Tneg = Matrix4x4.Translate(-pivotWS);
        var Suni = Matrix4x4.Scale(new Vector3(scaleS, scaleS, scaleS));
        var toDstLocal = dstW2L * Tpos * Suni * Tneg * srcL2W;

        var verts = baked.vertices;
        var norms = baked.normals;
        var tangs = baked.tangents;

        var vLen = verts.Length;
        var vDst = new Vector3[vLen];
        var nDst = norms != null && norms.Length == vLen ? new Vector3[vLen] : null;
        var tDst = tangs != null && tangs.Length == vLen ? new Vector4[vLen] : null;

        var invT = toDstLocal.inverse.transpose;

        for (int i = 0; i < vLen; i++) vDst[i] = toDstLocal.MultiplyPoint3x4(verts[i]);

        if (nDst != null)
        {
            for (int i = 0; i < vLen; i++) nDst[i] = invT.MultiplyVector(norms[i]).normalized;
        }

        if (tDst != null)
        {
            for (int i = 0; i < vLen; i++)
            {
                var t = tangs[i];
                var tv = invT.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                tDst[i] = new Vector4(tv.x, tv.y, tv.z, t.w);
            }
        }

        mesh.vertices = vDst;
        if (nDst != null) mesh.normals = nDst;
        if (tDst != null) mesh.tangents = tDst;
        mesh.bounds = baked.bounds;

        mesh.ClearBlendShapes();
        int shapeCount = srcMesh.blendShapeCount;
        for (int s = 0; s < shapeCount; s++)
        {
            string shapeName = srcMesh.GetBlendShapeName(s);
            int frameCount = srcMesh.GetBlendShapeFrameCount(s);
            for (int f = 0; f < frameCount; f++)
            {
                var dV = new Vector3[vLen];
                var dN = new Vector3[vLen];
                var dT = new Vector3[vLen];
                srcMesh.GetBlendShapeFrameVertices(s, f, dV, dN, dT);

                for (int i = 0; i < vLen; i++)
                {
                    dV[i] = toDstLocal.MultiplyVector(dV[i]);
                    dN[i] = invT.MultiplyVector(dN[i]);
                    dT[i] = invT.MultiplyVector(dT[i]);
                }

                float w = srcMesh.GetBlendShapeFrameWeight(s, f);
                mesh.AddBlendShapeFrame(shapeName, w, dV, dN, dT);
            }
        }

        dst.sharedMesh = mesh;

        var bc = mesh.blendShapeCount;
        for (int i = 0; i < bc; i++)
        {
            var w = src.GetBlendShapeWeight(i);
            dst.SetBlendShapeWeight(i, w);
        }

        UnityEngine.Object.Destroy(baked);
        return dst;
    }

    private Transform MapOrCreateBoneChain(Transform srcBone, Dictionary<string, Transform> targetBoneMap, out int createdCount)
    {
        createdCount = 0;
        if (srcBone == null) return null;

        if (targetBoneMap.TryGetValue(srcBone.name, out var found)) return found;

        var chain = new List<Transform>();
        var cur = srcBone;
        while (cur != null && !targetBoneMap.ContainsKey(cur.name))
        {
            chain.Add(cur);
            cur = cur.parent;
        }
        if (cur == null) return null;

        var parentTarget = targetBoneMap[cur.name];
        for (int k = chain.Count - 1; k >= 0; k--)
        {
            var s = chain[k];
            var created = new GameObject(s.name).transform;
            created.SetParent(parentTarget, true);
            created.SetPositionAndRotation(s.position, s.rotation);
            created.localScale = DivVector(s.lossyScale, parentTarget.lossyScale);

            targetBoneMap[s.name] = created;
            parentTarget = created;
            createdCount++;
        }
        return parentTarget;
    }

    private static Vector3 DivVector(Vector3 a, Vector3 b)
    {
        return new Vector3(
            b.x != 0 ? a.x / b.x : 0,
            b.y != 0 ? a.y / b.y : 0,
            b.z != 0 ? a.z / b.z : 0
        );
    }

    public void SyncBlendShapes(List<(SkinnedMeshRenderer src, SkinnedMeshRenderer dst)> pairs)
    {
        for (int p = 0; p < pairs.Count; p++)
        {
            var src = pairs[p].src;
            var dst = pairs[p].dst;
            if (src == null || dst == null) continue;

            var srcMesh = src.sharedMesh;
            var dstMesh = dst.sharedMesh;
            if (srcMesh == null || dstMesh == null) continue;

            var count = Mathf.Min(srcMesh.blendShapeCount, dstMesh.blendShapeCount);
            for (int i = 0; i < count; i++)
            {
                var w = src.GetBlendShapeWeight(i);
                dst.SetBlendShapeWeight(i, w);
            }
        }
    }

    public void TryAttachOutlineTo(Transform host, bool enable = true)
    {
        if (!enable) return;
        TryAttachOutlineTo(host);
    }

    public void Dispose()
    {
        DestroyTempFace3();

        for (int i = 0; i < _createdMeshes.Count; i++)
        {
            var m = _createdMeshes[i];
            if (m != null) UnityEngine.Object.Destroy(m);
        }
        _createdMeshes.Clear();
    }
}
