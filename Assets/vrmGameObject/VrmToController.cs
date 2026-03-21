using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UniVRM10;
using UnityEngine.EventSystems;
using UniGLTF.SpringBoneJobs.Blittables;
using System.Reflection;

public enum BodyVariant
{
    Normal,
    Normal_Bikini_Blue,
    Normal_Bikini_Pink,
    Normal_Swim,
    Normal_Nude,
    Cooking,
    School,
    Sifuku,
    Track
}

[DisallowMultipleComponent]
public sealed class VrmToController : MonoBehaviour
{
    private byte[] _vrmData;
    [Header("Body Prefabs (Per Variant)")]
    public GameObject bodyPrefabNormal;
    public GameObject bodyPrefabCooking;
    public GameObject bodyPrefabSchool;
    public GameObject bodyPrefabSifuku;
    public GameObject bodyPrefabTrack;

    public GameObject face3Prefab;

    [Header("Cloth / Skin Textures")]
    public Texture2D skinTex;

    [Space(6)]
    public Texture2D clothTexNormal;
    public Texture2D clothTexNormalBikiniBlue;
    public Texture2D clothTexNormalBikiniPink;
    public Texture2D clothTexNormalSwim;

    [Space(6)]
    public Texture2D clothTexCookingAndSifuku;
    public Texture2D clothTexSchool;

    [Space(6)]
    public Texture2D transparentClothTex;

    [Space(6)]
    public Texture2D face3Tex;
    public Texture2D face3Tex2;

    [Header("Final Shade Override")]
    public bool overrideShadeColor = false;
    public Color overrideShadeColorValue = Color.white;

    [Range(0, 100)] public float face3Key = 0f;
    [Range(-3, 100)] public float bodyKey = 0f;
    [Range(0, 100)] public float lowKey = 0f;
    [Range(0, 100)] public float bustKey = 0f;
    [Range(-0.4f, 1.5f)] public float height = 0f;

    [Header("VRM Head Settings (Name-Based)")]
    public string vrmHeadBoneName = "J_Bip_C_Head";

    [Header("Variant Selection")]
    public BodyVariant bodyVariant = BodyVariant.Normal;

    [HideInInspector] public string currentAnimationKey = "idle";
    [HideInInspector] public VrmExpressionController.SavedExpression expressionPreset = VrmExpressionController.SavedExpression.Neutral;
    [HideInInspector] public bool eyeContact = false;

    private VrmRetargeting _retarget;
    private BodyMotion _motion;
    private HumanoidAnimationController _humanoidAnim;

    private GameObject _bodyRoot;
    private Vrm10Instance _vrmInstance;
    private SkinnedMeshRenderer _bodyPrimarySmr;
    private SkinnedMeshRenderer _face3Smr;
    private int _face3KeyIndex = -1;
    private int _bodyKeyIndex = -1;

    private readonly List<(SkinnedMeshRenderer src, SkinnedMeshRenderer dst)> _smrPairs = new();
    private readonly List<Renderer> _dupRenderers = new();
    private readonly List<Renderer> _bodyAllRenderers = new();
    private readonly HashSet<Renderer> _bodySuppressed = new();

    private Dictionary<string, Transform> _bodyBoneMap;

    private bool _ready;

    private Vector3 _faceScalePivotWS = Vector3.zero;
    private float _faceUniformScaleS = 1f;
    private float _preBakeDeltaY = 0f;

    private Renderer _bodyHeadDescOnlyOnBody;

    private Transform _eyeL, _eyeR;
    private Quaternion _eyeLInitLocal, _eyeRInitLocal;

    private Vector3 _eyeLForwardLocal = Vector3.up;
    private Vector3 _eyeLUpLocal = Vector3.forward;
    private Vector3 _eyeRForwardLocal = Vector3.up;
    private Vector3 _eyeRUpLocal = Vector3.forward;

    private Camera _mainCam;

    private readonly List<SkinnedMeshRenderer> _vrmFaceSmrs = new();
    public bool IsReady => _ready;
    public IReadOnlyList<SkinnedMeshRenderer> VrmFaceSmrs => _vrmFaceSmrs;

    private VrmToRuntimeController vrmToRuntimeController;

    private float _eyeContactBlend01 = 0f;
    private const float EyeContactTransitionSeconds = 0.5f;

    private bool _lookAtOverrideActive = false;
    private Vector3 _lookAtOverrideWorldPos = Vector3.zero;
    private float _lookAtOverrideBlend01 = 0f;

    private Transform _headBone;

    private readonly List<Material> _runtimeMaterials = new();
    private static readonly int ShadeColorId = Shader.PropertyToID("_ShadeColor");
    private Texture2D _runtimeBakedBodyTex;
    private Texture2D _runtimeBakedFaceTex;

    private int _initVersion = 0;

    [Header("Blush (Sprite)")]
    public float blushValue = 0.3f;
    public bool visibleBlush = false;

    private GameObject _blushRoot;
    private SpriteRenderer[] _blushSpriteRenderers = Array.Empty<SpriteRenderer>();

    private Mesh _meshPullBodyMeshInst;
    private Mesh _meshPullFace3MeshInst;

    // =========================
    // Mesh Pull (Params + Runtime State)
    // =========================

    [Header("Mesh Pull Enable")]
    public bool meshPullEnabled = true;

    [Header("Mesh Pull Raycast")]
    public LayerMask meshPullRaycastMask;

    [Tooltip("Face collider hit is preferred if (faceDist <= bodyDist + bias).")]
    private float facePickPriorityBias = 1f;

    [Header("Mesh Pull Input")]
    [SerializeField] private float longPressSeconds = 0.18f;
    [SerializeField] private float tapMaxMovePixels = 8f;

    private float tapHoldSecondsBody = 0.05f;
    private float tapHoldSecondsFace = 0.05f;

    float pullRadiusBody = 0.0105f;
    float pullMaxOffsetBody = 0.009f;
    float springStiffnessBody = 400f;
    float springDampingBody = 4f;
    float dragGainBody = 1f;

    private float pullRadiusFace = 0.002f;
    private float pullMaxOffsetFace = 0.00008f; // 0.00005
    private float springStiffnessFace = 800f;
    private float springDampingFace = 15f;
    private float dragGainFace = 1f;

    // sleep eps: fixed/private (as you requested)
    private float sleepPosEps = 0.00001f;
    private float sleepVelEps = 0.00001f;

    // ---- runtime state ----
    private enum PullTargetKind { None, Body, Face3 }
    private PullTargetKind _currentPullTarget = PullTargetKind.None;

    private bool _pressActive = false;
    private bool _pressGrabStarted = false;
    private float _pressStartTime = 0f;
    private Vector2 _pressStartPos = Vector2.zero;

    // “Step” is allowed to be called multiple times per frame; we guard here to avoid double-stepping regressions
    private int _meshPullLastStepFrame = -1;

    private struct DeformState
    {
        public bool valid;
        public SkinnedMeshRenderer smr;
        public Mesh mesh;

        public Vector3[] restV;
        public Vector3[] workV;
        public Vector3[] velV;

        // weights for body/bust (only for body)
        public float[] wBody01;
        public float[] wBust01;
        public float[] wLow01;

        // press anchor
        public Vector3 grabLocal;
        public float grabCamDepth;

        // affected verts and falloff
        public List<int> idx;
        public List<float> w;

        // current “drag” target offset (local)
        public Vector3 currentTargetOffsetLocal;

        // state flags
        public bool hasActive;   // simulation should continue
        public bool grabbing;    // currently applying offset (swipe hold OR tap hold)

        // tap-as-virtual-swipe
        public bool tapActive;
        public float tapRemain;
        public Vector3 tapHoldOffsetLocal;
    }

    private DeformState _defBody;
    private DeformState _defFace3;


    public void ReloadFromBytes(
        byte[] newVrmData,
        BodyVariant newVariant,
        float newFace3Key,
        float newBodyKey,
        float newBustKey,
        float newHeight)
    {
        _vrmData = newVrmData;
        bodyVariant = newVariant;
        face3Key = newFace3Key;
        bodyKey = newBodyKey;
        bustKey = newBustKey;
        height = newHeight;
        _ = ReloadFromBytesAsync();
    }

    public async Task ReloadFromBytesAsync()
    {
        int myVersion = ++_initVersion;

        _ready = false;



        await CleanupRuntimeAsync();

        await ForceReleaseAfterDestroyAsync();

        if (myVersion != _initVersion) return;

        await InitializeRuntimeAsync(myVersion);
    }

    // 変更：InitializeRuntimeAsync（face3Prefab周りを差し替え）
    private async Task InitializeRuntimeAsync(int myVersion)
    {
        try
        {
            if (_vrmData == null || _vrmData.Length == 0)
            {
                Debug.LogError("[VrmToController] VRM data not assigned.");
                return;
            }
            if (skinTex == null) { Debug.LogWarning("[VrmToController] skinTex is null. Recolor may fail."); }

            if (transparentClothTex == null)
            {
                transparentClothTex = CreateTransparentTexture(2, 2);
            }

            if (!TryResolveVariant(bodyVariant, out var selectedBodyPrefab, out var selectedClothTex))
            {
                Debug.LogError("[VrmToController] BodyVariant resolution failed. Check prefab assignments.");
                return;
            }
            if (selectedBodyPrefab == null)
            {
                Debug.LogError($"[VrmToController] Selected body prefab is null for variant={bodyVariant}.");
                return;
            }

            vrmToRuntimeController = GetComponent<VrmToRuntimeController>();

            _retarget = new VrmRetargeting();
            _motion = new BodyMotion();

            _bodyRoot = _retarget.InstantiateBodyRoot(selectedBodyPrefab, transform);
            _retarget.TransferOutlineToBodyRoot(gameObject, _bodyRoot);
            _retarget.CacheBodyRenderersAndHide(_bodyRoot.transform, _bodyAllRenderers);

            _bodyPrimarySmr = _retarget.FindPrimaryBodySmr(_bodyRoot.transform);
            if (_bodyPrimarySmr == null) { Debug.LogError("[VrmToController] body SMR not found."); return; }

            MakeRendererMaterialsInstance(_bodyPrimarySmr);

            var bodyMesh = _bodyPrimarySmr.sharedMesh;
            if (bodyMesh != null)
            {
                _bodyKeyIndex = bodyMesh.GetBlendShapeIndex("body");
                if (_bodyKeyIndex < 0) Debug.LogWarning("[VrmToController] body mesh has no blendshape 'body'.");
            }

            await Task.Yield();
            if (myVersion != _initVersion) return;

            _vrmInstance = await _retarget.LoadVrmAsync(_vrmData, transform);
            if (_vrmInstance == null || _vrmInstance.gameObject == null) { Debug.LogError("[VrmToController] VRM load failed."); return; }
            _vrmInstance.gameObject.name = "VRM_Source_Hidden";
            _retarget.Align(_vrmInstance.transform, _bodyRoot.transform);

            await Task.Yield();
            await Task.Yield();
            if (myVersion != _initVersion) return;

            _bodyBoneMap = _retarget.BuildBoneMap(_bodyRoot.transform);
            if (_bodyBoneMap.Count == 0) { Debug.LogError("[VrmToController] No bones under bodyPrefab."); return; }

            _eyeL = GetBoneExact("J_Adj_L_FaceEye");
            _eyeR = GetBoneExact("J_Adj_R_FaceEye");
            if (_eyeL == null || _eyeR == null)
            {
                if (_eyeL == null) Debug.LogError("[VrmToController] Left eye bone 'J_Adj_L_FaceEye' not found.");
                if (_eyeR == null) Debug.LogError("[VrmToController] Right eye bone 'J_Adj_R_FaceEye' not found.");
            }
            else
            {
                _eyeLInitLocal = _eyeL.localRotation;
                _eyeRInitLocal = _eyeR.localRotation;
            }

            // ★変更ここから：face3Prefabブロックを新関数に集約（srcChild(2)もblushChild(3)も頭ボーン追従）
            if (face3Prefab != null)
            {
                if (!TrySetupFace3FromPrefabAndAttachExtras()) return;
            }
            else
            {
                _face3Smr = _retarget.FindBodySmrByExactName(_bodyRoot.transform, "face3");
                if (_face3Smr != null) MakeRendererMaterialsInstance(_face3Smr);
            }
            // ★変更ここまで

            if (_face3Smr != null && _face3Smr.sharedMesh != null)
            {
                _face3KeyIndex = _face3Smr.sharedMesh.GetBlendShapeIndex("key");
                if (_face3KeyIndex < 0) Debug.LogWarning("[VrmToController] face3 has no 'key' blendshape.");
            }

            await Task.Yield();
            if (myVersion != _initVersion) return;

            var vrmFaceSmrs = _retarget.FindSmrsByNodeName(_vrmInstance.transform, "face");
            var vrmHairSmrs = _retarget.FindSmrsByNodeName(_vrmInstance.transform, "haier");
            if (vrmHairSmrs.Count == 0) vrmHairSmrs = _retarget.GuessHair(_vrmInstance.transform);
            if (vrmFaceSmrs.Count == 0) vrmFaceSmrs = _retarget.GuessFace(_vrmInstance.transform);
            if (vrmFaceSmrs.Count == 0 && vrmHairSmrs.Count == 0) { Debug.LogError("[VrmToController] VRM face/hair SMR not found."); return; }

            _vrmFaceSmrs.Clear();
            foreach (var s in vrmFaceSmrs) if (s != null && s.sharedMesh != null) _vrmFaceSmrs.Add(s);

            foreach (var r in _retarget.DisablePotentialFaceHairOnBodyCollect(_bodyRoot.transform)) _bodySuppressed.Add(r);

            _faceUniformScaleS = _retarget.MeasureFaceScaleAndApplyPreBakeOffset(
                _bodyRoot.transform, _vrmInstance.transform, vrmFaceSmrs, out _faceScalePivotWS, out _preBakeDeltaY);

            var faceHairSrc = vrmFaceSmrs.Concat(vrmHairSmrs).ToList();
            var (pairsFaceHair, createdFaceHairOnBody) = _retarget.BakeAndRebindAll(
                faceHairSrc, _bodyBoneMap, _bodyPrimarySmr, _faceScalePivotWS, _faceUniformScaleS);

            _smrPairs.AddRange(pairsFaceHair);

            foreach (var r in createdFaceHairOnBody)
            {
                if (r != null)
                {
                    r.enabled = false;
                    _dupRenderers.Add(r);

                }
            }

            await Task.Yield();
            await Task.Yield();
            if (myVersion != _initVersion) return;


            var vrmHead = FindTransformByExactName(_vrmInstance.transform, vrmHeadBoneName);
            if (vrmHead != null)
            {
                var headDescendants = new HashSet<Transform>(vrmHead.GetComponentsInChildren<Transform>(true));
                headDescendants.Remove(vrmHead);

                var allVrms = _vrmInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var exclude = new HashSet<SkinnedMeshRenderer>(vrmFaceSmrs);
                foreach (var h in vrmHairSmrs) exclude.Add(h);

                var bodyCandidates = new List<SkinnedMeshRenderer>();
                foreach (var s in allVrms)
                {
                    if (s == null || s.sharedMesh == null || s.bones == null || s.bones.Length == 0) continue;
                    if (exclude.Contains(s)) continue;
                    bodyCandidates.Add(s);
                }

                var vrmBodySmr = bodyCandidates
                    .OrderByDescending(s => s.sharedMesh != null ? s.sharedMesh.vertexCount : 0)
                    .FirstOrDefault();

                if (vrmBodySmr != null)
                {
                    var boneIndexSet = BuildBoneIndexSet_FromDescendants(vrmBodySmr, headDescendants);
                    int quick = CountVerticesInfluencedByBoneSet(vrmBodySmr, boneIndexSet, minWeight: 0.0001f);

                    if (boneIndexSet.Count > 0 && quick > 0)
                    {
                        await Task.Yield();
                        await Task.Yield();

                        var extractedBody = ExtractByBoneIndexSetWithName(
                            vrmBodySmr, boneIndexSet, minWeight: 0.0001f, requireAllVertsInTri: false, fixedNameForGo: "VRM_Body_HeadDescOnly");

                        if (extractedBody != null)
                        {
                            extractedBody.transform.SetParent(_vrmInstance.transform, false);
                            extractedBody.enabled = false;

                            var (pairsBodyDesc, createdBodyDescOnBody) = _retarget.BakeAndRebindAll(
                                new List<SkinnedMeshRenderer> { extractedBody },
                                _bodyBoneMap, _bodyPrimarySmr, _faceScalePivotWS, _faceUniformScaleS);

                            _smrPairs.AddRange(pairsBodyDesc);

                            if (createdBodyDescOnBody.Count > 0 && createdBodyDescOnBody[0] != null)
                            {
                                var rend = createdBodyDescOnBody[0];
                                rend.enabled = false;
                                _dupRenderers.Add(rend);
                                _bodyHeadDescOnlyOnBody = rend;
                            }
                        }
                    }
                }
            }

            _retarget.RevertPreBakeOffset(_vrmInstance.transform, _preBakeDeltaY);
            _retarget.HideAllRenderers(_vrmInstance.transform);

            if (_dupRenderers.Count == 0 && _smrPairs.Count == 0) { Debug.LogError("[VrmToController] No meshes created."); return; }

            await Task.Yield();
            if (myVersion != _initVersion) return;

            var animator = _bodyRoot.GetComponentInChildren<Animator>() ?? _bodyRoot.AddComponent<Animator>();

            if (_motion.TryCaptureReferences(animator, _bodyRoot.transform, out var bones))
            {
                _motion.Bones = bones;
            }

            _humanoidAnim = GetComponent<HumanoidAnimationController>();
            if (_humanoidAnim != null)
            {
                _humanoidAnim.Initialize(animator, _motion, _bodyRoot.transform);
            }
            else
            {
                Debug.LogError("[VrmToController] HumanoidAnimationController is not found on the same GameObject.");
            }

            _retarget.SetCharacterVisible(_bodyAllRenderers, _bodySuppressed, _dupRenderers, _face3Smr, true);
            _retarget.TryAttachOutlineTo(_bodyRoot != null ? _bodyRoot.transform : null);

            var faceSmr = _vrmInstance
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => s != null && s.sharedMesh != null && s.sharedMesh.vertexCount > 0 && vrmFaceSmrs.Contains(s))
                .OrderByDescending(s => s.sharedMesh.vertexCount)
                .FirstOrDefault();

            if (faceSmr != null)
            {
                if (_runtimeBakedFaceTex != null) Destroy(_runtimeBakedFaceTex);
                _runtimeBakedFaceTex = SkinRecolorBaker.BakeFinalFromFace(face3Tex, face3Tex2, faceSmr);

                if (_runtimeBakedFaceTex != null)
                {
                    try { _runtimeBakedFaceTex.Apply(false, true); } catch { }
                }

                var mat2 = (_face3Smr != null)
                    ? FindMaterialByBaseName(_face3Smr.sharedMaterials, "faceSkin")
                    : null;
                if (mat2 != null && _runtimeBakedFaceTex != null)
                {
                    SkinRecolorBaker.ApplyToMToon10Lighting(mat2, _runtimeBakedFaceTex, _runtimeBakedFaceTex);
                }

                Texture2D overlayTexForBake = selectedClothTex != null ? selectedClothTex : transparentClothTex;

                if (_runtimeBakedBodyTex != null) Destroy(_runtimeBakedBodyTex);
                try
                {
                    _runtimeBakedBodyTex = SkinRecolorBaker.BakeFinalFromFace(skinTex, overlayTexForBake, faceSmr);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VrmToController] BakeFinalFromFace failed, fallback to transparent overlay. {e.Message}");
                    _runtimeBakedBodyTex = SkinRecolorBaker.BakeFinalFromFace(skinTex, transparentClothTex, faceSmr);
                }

                if (_runtimeBakedBodyTex != null)
                {
                    try { _runtimeBakedBodyTex.Apply(false, true); } catch { }
                }

                var mat = (_bodyPrimarySmr != null)
                    ? FindMaterialByBaseName(_bodyPrimarySmr.sharedMaterials, "BodySkin")
                    : null;
                if (mat != null && _runtimeBakedBodyTex != null)
                {
                    SkinRecolorBaker.ApplyToMToon10Lighting(mat, _runtimeBakedBodyTex, _runtimeBakedBodyTex);
                }
                else
                {
                    Debug.LogWarning("[VrmToController] BodySkin material or baked texture missing; color correction skipped.");
                }
            }
            else
            {
                Debug.LogWarning("[vrmTo] vrmFaceSmrs is empty or invalid. Skip recolor bake.");
            }

            await Task.Yield();
            await Task.Yield();
            if (myVersion != _initVersion) return;

            _mainCam = Camera.main;
            if (_mainCam == null && eyeContact)
            {
                Debug.LogWarning("[VrmToController] Camera.main not found. Eye contact disabled.");
            }

            _eyeContactBlend01 = eyeContact ? 1f : 0f;

            if (myVersion != _initVersion) return;

            SetupMeshPullRuntime();
            ApplyFinalShadeOverride();

            _ready = true;
        }
        catch (Exception ex)
        {
            Debug.LogError("[VrmToController] Exception: " + ex.Message + "\n" + ex.StackTrace);
        }
    }

    public async Task ShutdownForSceneLeaveAsync()
    {
        _ready = false;
        _lookAtOverrideActive = false;
        _lookAtOverrideBlend01 = 0f;

        MeshPull_CancelAll();
        enabled = false;

        if (_humanoidAnim != null)
        {
            _humanoidAnim.enabled = false;
        }

        if (_bodyRoot != null)
        {
            foreach (var a in _bodyRoot.GetComponentsInChildren<Animator>(true))
            {
                if (a != null) a.enabled = false;
            }
        }

        TryStopSpringBoneRuntime();

        if (_vrmInstance != null && _vrmInstance.gameObject != null)
        {
            _vrmInstance.gameObject.SetActive(false);
        }

        if (_bodyRoot != null)
        {
            _bodyRoot.SetActive(false);
        }

        // job / animator 側が1フレームで抜けきらないことがあるので少し待つ
        await Task.Yield();
        await Task.Yield();

        await CleanupRuntimeAsync();
    }

    private void TryStopSpringBoneRuntime()
    {
        if (_vrmInstance == null) return;

        try
        {
            var springBone = _vrmInstance.Runtime?.SpringBone;
            if (springBone == null) return;

            // 新しめの版にだけ IsSpringBoneEnabled がある場合に備える
            var prop = springBone.GetType().GetProperty(
                "IsSpringBoneEnabled",
                BindingFlags.Instance | BindingFlags.Public
            );

            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(springBone, false);
                return;
            }

            // あなたの版向けのフォールバック
            springBone.SetModelLevel(
                _vrmInstance.transform,
                new BlittableModelLevel
                {
                    StopSpringBoneWriteback = true,
                    SupportsScalingAtRuntime = true,
                    ExternalForce = Vector3.zero
                }
            );
        }
        catch (Exception e)
        {
            Debug.LogWarning("[VrmToController] SpringBone stop failed: " + e.Message);
        }
    }


    private async Task CleanupRuntimeAsync()
    {
        _ready = false;

        _lookAtOverrideActive = false;
        _lookAtOverrideBlend01 = 0f;

        _eyeL = null;
        _eyeR = null;
        _headBone = null;

        if (_blushRoot != null) { Destroy(_blushRoot); _blushRoot = null; }
        _blushSpriteRenderers = Array.Empty<SpriteRenderer>();

        if (_runtimeBakedBodyTex != null) { Destroy(_runtimeBakedBodyTex); _runtimeBakedBodyTex = null; }
        if (_runtimeBakedFaceTex != null) { Destroy(_runtimeBakedFaceTex); _runtimeBakedFaceTex = null; }

        for (int i = 0; i < _runtimeMaterials.Count; i++)
        {
            var m = _runtimeMaterials[i];
            if (m != null) Destroy(m);
        }
        _runtimeMaterials.Clear();

        _smrPairs.Clear();
        _dupRenderers.Clear();
        _bodyAllRenderers.Clear();
        _bodySuppressed.Clear();
        _vrmFaceSmrs.Clear();
        _bodyBoneMap = null;

        _face3Smr = null;
        _bodyPrimarySmr = null;
        _face3KeyIndex = -1;
        _bodyKeyIndex = -1;

        try { _motion?.Dispose(); } catch { }
        _motion = null;

        try { _retarget?.Dispose(); } catch { }
        _retarget = null;

        if (_vrmInstance != null && _vrmInstance.gameObject != null)
        {
            Destroy(_vrmInstance.gameObject);
        }
        _vrmInstance = null;

        if (_bodyRoot != null)
        {
            Destroy(_bodyRoot);
        }
        _bodyRoot = null;

        ResetMeshPullRuntime();

        if (_meshPullBodyMeshInst != null) { Destroy(_meshPullBodyMeshInst); _meshPullBodyMeshInst = null; }
        if (_meshPullFace3MeshInst != null) { Destroy(_meshPullFace3MeshInst); _meshPullFace3MeshInst = null; }

        await Task.Yield();
        await Task.Yield();
    }

    private async Task ForceReleaseAfterDestroyAsync()
    {
        await Task.Yield();
        await Task.Yield();

        try
        {
            await Resources.UnloadUnusedAssets();
        }
        catch { }

        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        catch { }

        await Task.Yield();
        await Task.Yield();
    }


    // 変更：Update（bodyKeyに追従して透明度を更新。20→0、60→100、60以上は100固定）
    private void Update()
    {
        float bodyKey01 = bodyKey / 100f;

        if (_bodyPrimarySmr != null && _bodyKeyIndex >= 0)
        {
            _bodyPrimarySmr.SetBlendShapeWeight(_bodyKeyIndex, 100 * bodyKey01 * 0.95f);
            _bodyPrimarySmr.SetBlendShapeWeight(_bodyKeyIndex + 1, bustKey * 0.8f + bustKey * bodyKey01 * 0.4f);
            if (_bodyKeyIndex + 2 < _bodyPrimarySmr.sharedMesh.blendShapeCount)
            {
                _bodyPrimarySmr.SetBlendShapeWeight(_bodyKeyIndex + 2, lowKey);
            }
        }


        if (!_ready) return;

        if (_humanoidAnim != null)
        {
            _humanoidAnim.SetCurrentAnimationKey(currentAnimationKey);
        }

        if (_face3Smr != null && _face3KeyIndex >= 0)
        {
            _face3Smr.SetBlendShapeWeight(_face3KeyIndex, face3Key * Mathf.Pow(bodyKey / 100, 3f / 4f) * 0.9f);
        }


        if (_motion != null)
        {
            _motion.SetJobParams(bodyKey01, face3Key / 100f);
            _humanoidAnim?.UpdateBodyMotionJob(_motion);
        }

        if (_blushRoot != null)
        {

            if (_blushSpriteRenderers != null && _blushSpriteRenderers.Length > 0)
            {
                float lowA01 = Mathf.InverseLerp(10f, 50f, bodyKey) * 0.3f;
                float a01 = Mathf.Max(lowA01, visibleBlush ? blushValue : 0);

                for (int i = 0; i < _blushSpriteRenderers.Length; i++)
                {
                    var sr = _blushSpriteRenderers[i];
                    if (sr == null) continue;
                    var c = sr.color;
                    c.a = a01;
                    sr.color = c;
                }
            }
        }

        if (meshPullEnabled)
        {
            MeshPull_UpdateInput();
        }
    }


    private void LateUpdate()
    {
        if (!_ready) return;

        _retarget?.SyncBlendShapes(_smrPairs);

        if (meshPullEnabled)
        {
            MeshPull_StepSimulation(Time.deltaTime);
        }

        float bodyKey01 = bodyKey / 100f;

        _motion?.ApplyScalesAndPositions(height, bodyKey01, _bodyRoot != null ? _bodyRoot.transform : null);

        if (_mainCam == null) _mainCam = Camera.main;

        bool hasEyes = (_eyeL != null && _eyeR != null);
        bool hasDefaultTarget = (_mainCam != null);

        Vector3 defaultTargetPos = hasDefaultTarget ? _mainCam.transform.position : Vector3.zero;

        float step = (EyeContactTransitionSeconds <= 1e-6f) ? 1f : (Time.deltaTime / EyeContactTransitionSeconds);

        float overrideTarget = _lookAtOverrideActive ? 1f : 0f;
        _lookAtOverrideBlend01 = Mathf.MoveTowards(_lookAtOverrideBlend01, overrideTarget, step);

        bool hasAnyTarget = (_lookAtOverrideBlend01 > 0f) || (eyeContact && hasDefaultTarget);
        bool canAimNow = hasEyes && hasAnyTarget;

        float targetBlend = canAimNow ? 1f : 0f;
        _eyeContactBlend01 = Mathf.MoveTowards(_eyeContactBlend01, targetBlend, step);

        if (_eyeL != null || _eyeR != null)
        {
            Vector3 targetPos = defaultTargetPos;

            if (_lookAtOverrideBlend01 > 0f)
            {
                targetPos = Vector3.Lerp(defaultTargetPos, _lookAtOverrideWorldPos, _lookAtOverrideBlend01);
            }

            void AimEye(Transform eye, Vector3 forwardLocal, Vector3 upLocal, Quaternion initLocal)
            {
                Quaternion targetLocal = initLocal;

                if (canAimNow)
                {
                    var desiredForwardWS = (targetPos - eye.position);
                    if (desiredForwardWS.sqrMagnitude >= 1e-8f)
                    {
                        desiredForwardWS.Normalize();

                        var parentRot = (eye.parent != null) ? eye.parent.rotation : Quaternion.identity;
                        var desiredForwardParent = Quaternion.Inverse(parentRot) * desiredForwardWS;
                        var desiredForwardLocal = Quaternion.Inverse(initLocal) * desiredForwardParent;
                        desiredForwardLocal.Normalize();

                        var fL = forwardLocal.normalized;
                        var uL = upLocal.normalized;
                        var rL = Vector3.Cross(uL, fL).normalized;

                        float vf = Vector3.Dot(desiredForwardLocal, fL);
                        float vu = Vector3.Dot(desiredForwardLocal, uL);
                        float vr = Vector3.Dot(desiredForwardLocal, rL);
                        float projRF = Mathf.Sqrt(Mathf.Max(vf * vf + vr * vr, 1e-12f));

                        float yawDeg = Mathf.Atan2(vr, vf) * Mathf.Rad2Deg;
                        float pitchDeg = Mathf.Atan2(vu, projRF) * Mathf.Rad2Deg;

                        yawDeg = Mathf.Clamp(yawDeg, -20, 20);
                        pitchDeg = Mathf.Clamp(pitchDeg, -1, 20);

                        var yawQ = Quaternion.AngleAxis(yawDeg, uL);
                        var pitchQ = Quaternion.AngleAxis(-pitchDeg, rL);
                        targetLocal = initLocal * yawQ * pitchQ;
                    }
                }

                var aimedLocal = Quaternion.Slerp(initLocal, targetLocal, 0.5f);
                var blendedLocal = Quaternion.Slerp(initLocal, aimedLocal, _eyeContactBlend01);
                eye.localRotation = blendedLocal;
            }

            if (_eyeL != null) AimEye(_eyeL, _eyeLForwardLocal, _eyeLUpLocal, _eyeLInitLocal);
            if (_eyeR != null) AimEye(_eyeR, _eyeRForwardLocal, _eyeRUpLocal, _eyeRInitLocal);

            if (_lookAtOverrideBlend01 > 0f)
            {
                ApplyNeckHeadLook(targetPos, _lookAtOverrideBlend01);
            }
        }
    }

    private void ApplyNeckHeadLook(Vector3 targetPos, float weight01)
    {
        if (weight01 <= 0f) return;

        if (_headBone == null && !string.IsNullOrEmpty(vrmHeadBoneName))
            _headBone = GetBoneExact(vrmHeadBoneName);

        if (_headBone == null) return;

        Vector3 dir = targetPos - _headBone.position;
        if (dir.sqrMagnitude < 1e-8f) return;

        Quaternion delta = Quaternion.FromToRotation(_headBone.forward, dir.normalized);

        float deltaDeg;
        Vector3 deltaAxis;
        delta.ToAngleAxis(out deltaDeg, out deltaAxis);
        if (deltaDeg > 180f) deltaDeg -= 360f;

        float gain = 1.0f;
        float maxTotalDeg = 24f;

        deltaDeg *= gain;

        float clampedTotal = Mathf.Clamp(deltaDeg, -maxTotalDeg, maxTotalDeg);
        Quaternion clampedDelta = Quaternion.AngleAxis(clampedTotal, deltaAxis);

        Quaternion baseRot = _headBone.rotation;
        Quaternion desiredRot = clampedDelta * baseRot;

        _headBone.rotation = Quaternion.Slerp(baseRot, desiredRot, Mathf.Clamp01(weight01));
    }

    private void OnDestroy()
    {
        try { _motion?.Dispose(); } catch { }
        try { _retarget?.Dispose(); } catch { }

        if (_blushRoot != null) { Destroy(_blushRoot); _blushRoot = null; }
        _blushSpriteRenderers = Array.Empty<SpriteRenderer>();

        if (_runtimeBakedBodyTex != null) { Destroy(_runtimeBakedBodyTex); _runtimeBakedBodyTex = null; }
        if (_runtimeBakedFaceTex != null) { Destroy(_runtimeBakedFaceTex); _runtimeBakedFaceTex = null; }

        for (int i = 0; i < _runtimeMaterials.Count; i++)
        {
            var m = _runtimeMaterials[i];
            if (m != null) Destroy(m);
        }
        _runtimeMaterials.Clear();

        ResetMeshPullRuntime();

        // ★追加：ランタイム複製meshを破棄
        if (_meshPullBodyMeshInst != null) { Destroy(_meshPullBodyMeshInst); _meshPullBodyMeshInst = null; }
        if (_meshPullFace3MeshInst != null) { Destroy(_meshPullFace3MeshInst); _meshPullFace3MeshInst = null; }
    }


    private bool TryResolveVariant(BodyVariant variant, out GameObject prefab, out Texture2D cloth)
    {
        prefab = null;
        cloth = null;

        switch (variant)
        {
            case BodyVariant.Normal:
                prefab = bodyPrefabNormal ?? prefab;
                cloth = clothTexNormal;
                break;
            case BodyVariant.Normal_Bikini_Blue:
                prefab = bodyPrefabNormal ?? prefab;
                cloth = clothTexNormalBikiniBlue;
                break;
            case BodyVariant.Normal_Bikini_Pink:
                prefab = bodyPrefabNormal ?? prefab;
                cloth = clothTexNormalBikiniPink;
                break;
            case BodyVariant.Normal_Swim:
                prefab = bodyPrefabNormal ?? prefab;
                cloth = clothTexNormalSwim;
                break;
            case BodyVariant.Normal_Nude:
                prefab = bodyPrefabNormal ?? prefab;
                cloth = transparentClothTex;
                break;
            case BodyVariant.Cooking:
                prefab = bodyPrefabCooking ?? prefab;
                cloth = clothTexCookingAndSifuku;
                break;
            case BodyVariant.Sifuku:
                prefab = bodyPrefabSifuku ?? prefab;
                cloth = clothTexCookingAndSifuku;
                break;
            case BodyVariant.School:
                prefab = bodyPrefabSchool ?? prefab;
                cloth = clothTexSchool;
                break;
            case BodyVariant.Track:
                prefab = bodyPrefabTrack ?? prefab;
                cloth = transparentClothTex;
                break;
            default:
                Debug.LogError($"[VrmToController] Unknown BodyVariant: {variant}");
                return false;
        }

        if (prefab == null)
        {
            Debug.LogError($"[VrmToController] Prefab not assigned for variant={variant}. Please assign the proper prefab.");
            return false;
        }
        return true;
    }

    private Texture2D CreateTransparentTexture(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        var pixels = new Color32[w * h];
        var zero = new Color32(0, 0, 0, 0);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = zero;
        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    private Transform FindTransformByExactName(Transform root, string exact)
    {
        if (root == null || string.IsNullOrEmpty(exact)) return null;
        if (root.name == exact) return root;
        for (int i = 0; i < root.childCount; ++i)
        {
            var t = FindTransformByExactName(root.GetChild(i), exact);
            if (t != null) return t;
        }
        return null;
    }

    private Transform GetBoneExact(string exactName)
    {
        if (string.IsNullOrEmpty(exactName)) return null;
        if (_bodyBoneMap != null && _bodyBoneMap.TryGetValue(exactName, out var t) && t != null) return t;
        return (_bodyRoot != null) ? FindTransformByExactName(_bodyRoot.transform, exactName) : null;
    }

    private HashSet<int> BuildBoneIndexSet_FromDescendants(SkinnedMeshRenderer smr, HashSet<Transform> descendants)
    {
        var set = new HashSet<int>();
        if (smr == null || smr.bones == null || descendants == null) return set;
        var bones = smr.bones;
        for (int i = 0; i < bones.Length; ++i)
        {
            var b = bones[i];
            if (b != null && descendants.Contains(b)) set.Add(i);
        }
        return set;
    }

    private int CountVerticesInfluencedByBoneSet(SkinnedMeshRenderer smr, HashSet<int> boneIndexSet, float minWeight)
    {
        var mesh = smr.sharedMesh;
        if (mesh == null || mesh.boneWeights == null || boneIndexSet == null || boneIndexSet.Count == 0) return 0;
        var bw = mesh.boneWeights;
        int kept = 0;
        for (int i = 0; i < mesh.vertexCount; ++i)
        {
            var w = bw[i];
            float sum =
                (boneIndexSet.Contains(w.boneIndex0) ? w.weight0 : 0f) +
                (boneIndexSet.Contains(w.boneIndex1) ? w.weight1 : 0f) +
                (boneIndexSet.Contains(w.boneIndex2) ? w.weight2 : 0f) +
                (boneIndexSet.Contains(w.boneIndex3) ? w.weight3 : 0f);
            if (sum >= minWeight) kept++;
        }
        return kept;
    }

    private static Mesh EnsureUniqueMeshInstance(SkinnedMeshRenderer smr, string suffix)
    {
        if (smr == null) return null;

        var src = smr.sharedMesh;
        if (src == null) return null;

        // すでに専用インスタンスっぽいなら再生成しない（簡易ガード）
        if (!string.IsNullOrEmpty(src.name) && src.name.EndsWith(suffix, StringComparison.Ordinal))
        {
            return src;
        }

        var inst = Instantiate(src);
        inst.name = (src.name ?? "Mesh") + suffix;

        smr.sharedMesh = inst;
        return inst;
    }



    // 追加：vrmHeadBoneName を追従先として使う（新規関数）
    private Transform GetFaceFollowBoneOrFallback()
    {
        // 追従先は「ボディ側のボーン」
        var t = GetBoneExact(vrmHeadBoneName);
        if (t != null) return t;

        return (_bodyRoot != null) ? _bodyRoot.transform : transform;
    }

    // 追加：face3Prefab から srcChild(2) と blushChild(3) を取り込み、頭ボーンに追従させる（新規関数）
    // 変更：TrySetupFace3FromPrefabAndAttachExtras（blush参照を保持し、子2つのSpriteRendererをキャッシュ）
    private bool TrySetupFace3FromPrefabAndAttachExtras()
    {
        if (face3Prefab == null) return true;

        var followBone = GetFaceFollowBoneOrFallback();
        if (followBone == null)
        {
            Debug.LogError("[VrmToController] followBone is null.");
            return false;
        }

        var srcFace3 = _retarget.ExtractFace3SmrFromTemp(face3Prefab, _bodyRoot.transform);
        if (srcFace3 == null) return false;

        Transform tempRoot = srcFace3.transform;
        while (tempRoot.parent != null && tempRoot.parent != _bodyRoot.transform)
        {
            tempRoot = tempRoot.parent;
        }

        if (tempRoot.childCount <= 2)
        {
            Debug.LogError($"[VrmToController] tempRoot='{tempRoot.name}' has only {tempRoot.childCount} children. Need child index=2.");
            return false;
        }

        var srcChild = tempRoot.GetChild(2);
        if (srcChild == null)
        {
            Debug.LogError($"[VrmToController] tempRoot='{tempRoot.name}' child index=2 is null.");
            return false;
        }

        var blushChild = (tempRoot.childCount > 3) ? tempRoot.GetChild(3) : null;

        var srcBox = srcChild.GetComponent<BoxCollider>();
        if (srcBox == null)
        {
            Debug.LogError($"[VrmToController] BoxCollider not found on tempRoot='{tempRoot.name}' child index=2 ('{srcChild.name}').");
            return false;
        }

        _face3Smr = _retarget.CreateNoBake(srcFace3, _bodyBoneMap, _bodyPrimarySmr);
        if (_face3Smr == null) return false;

        MakeRendererMaterialsInstance(_face3Smr);

        // srcChild(2) の「当たり判定用host」を頭ボーン直下へ（追従）
        var host = new GameObject(srcChild.name);
        host.tag = "Facemouth";
        host.transform.SetParent(followBone, true);
        host.transform.position = srcChild.position;
        host.transform.rotation = srcChild.rotation;

        var dstBox = host.AddComponent<BoxCollider>();
        dstBox.center = srcBox.center;
        dstBox.size = srcBox.size;
        dstBox.isTrigger = srcBox.isTrigger;
        dstBox.material = srcBox.material;
        dstBox.enabled = srcBox.enabled;

        // blushChild(3) を複製して頭ボーン直下へ追加（Spriteのみ想定）
        if (blushChild != null)
        {
            // 既存があれば破棄（Reload等の安全策）
            if (_blushRoot != null) Destroy(_blushRoot);

            _blushRoot = Instantiate(blushChild.gameObject);
            _blushRoot.name = blushChild.name;
            _blushRoot.transform.SetParent(followBone, true);
            _blushRoot.transform.position = blushChild.position;
            _blushRoot.transform.rotation = blushChild.rotation;

            // 子2つのSpriteRendererをキャッシュ（要件：2か所）
            var srs = _blushRoot.GetComponentsInChildren<SpriteRenderer>(true);
            if (srs != null && srs.Length > 0)
            {
                _blushSpriteRenderers = (srs.Length >= 2) ? new[] { srs[0], srs[1] } : srs;
            }
            else
            {
                _blushSpriteRenderers = Array.Empty<SpriteRenderer>();
            }

            _blushRoot.SetActive(true);
        }

        _face3Smr.enabled = false;
        _dupRenderers.Add(_face3Smr);
        _retarget.DestroyTempFace3();

        return true;
    }



    private SkinnedMeshRenderer ExtractByBoneIndexSetWithName(
        SkinnedMeshRenderer srcSmr,
        HashSet<int> boneIndexSet,
        float minWeight,
        bool requireAllVertsInTri,
        string fixedNameForGo)
    {
        if (srcSmr == null || srcSmr.sharedMesh == null || boneIndexSet == null || boneIndexSet.Count == 0) return null;

        var srcMesh = srcSmr.sharedMesh;
        int vCount = srcMesh.vertexCount;
        if (vCount <= 0) return null;

        var bw = srcMesh.boneWeights;
        if (bw == null || bw.Length != vCount) return null;

        var keepV = new bool[vCount];
        int keptVertsByWeight = 0;
        for (int i = 0; i < vCount; ++i)
        {
            var w = bw[i];
            float sum =
                (boneIndexSet.Contains(w.boneIndex0) ? w.weight0 : 0f) +
                (boneIndexSet.Contains(w.boneIndex1) ? w.weight1 : 0f) +
                (boneIndexSet.Contains(w.boneIndex2) ? w.weight2 : 0f) +
                (boneIndexSet.Contains(w.boneIndex3) ? w.weight3 : 0f);

            if (sum >= minWeight)
            {
                keepV[i] = true;
                keptVertsByWeight++;
            }
        }
        if (keptVertsByWeight == 0) return null;

        var remap = new int[vCount];
        for (int i = 0; i < vCount; i++) remap[i] = -1;

        int subMeshCount = srcMesh.subMeshCount;
        if (subMeshCount <= 0) return null;


        var keptIndexCounts = new int[subMeshCount];
        int nextRemap = 0;
        int keptTrisTotal = 0;

        var tris = new List<int>(4096);

        for (int si = 0; si < subMeshCount; ++si)
        {
            tris.Clear();
            srcMesh.GetTriangles(tris, si);
            int triCount = tris.Count / 3;
            if (triCount <= 0) { keptIndexCounts[si] = 0; continue; }

            int keptIdxThis = 0;

            for (int t = 0; t < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];

                bool ka = (a >= 0 && a < vCount) && keepV[a];
                bool kb = (b >= 0 && b < vCount) && keepV[b];
                bool kc = (c >= 0 && c < vCount) && keepV[c];

                bool keepTri = requireAllVertsInTri ? (ka && kb && kc) : (ka || kb || kc);
                if (!keepTri) continue;

                if (remap[a] < 0) remap[a] = nextRemap++;
                if (remap[b] < 0) remap[b] = nextRemap++;
                if (remap[c] < 0) remap[c] = nextRemap++;

                keptIdxThis += 3;
                keptTrisTotal++;
            }

            keptIndexCounts[si] = keptIdxThis;
        }

        if (keptTrisTotal == 0) return null;

        int newV = nextRemap;
        if (newV <= 0) return null;

        var srcVertices = srcMesh.vertices;
        var srcNormals = srcMesh.normals;
        var srcTangents = srcMesh.tangents;
        var srcColors = srcMesh.colors;

        Vector3[] nv = new Vector3[newV];
        Vector3[] nn = (srcNormals != null && srcNormals.Length == vCount) ? new Vector3[newV] : null;
        Vector4[] nt = (srcTangents != null && srcTangents.Length == vCount) ? new Vector4[newV] : null;
        Color[] nc = (srcColors != null && srcColors.Length == vCount) ? new Color[newV] : null;

        var srcUVs = new List<Vector2>[8];
        var newUVs = new List<Vector2>[8];
        var hasUV = new bool[8];

        for (int ch = 0; ch < 8; ++ch)
        {
            var tmp = new List<Vector2>(vCount);
            srcMesh.GetUVs(ch, tmp);
            if (tmp != null && tmp.Count == vCount)
            {
                srcUVs[ch] = tmp;
                hasUV[ch] = true;
                newUVs[ch] = new List<Vector2>(newV);
            }
        }

        BoneWeight[] nbw = new BoneWeight[newV];

        for (int i = 0; i < vCount; ++i)
        {
            int m = remap[i];
            if (m < 0) continue;

            nv[m] = srcVertices[i];
            if (nn != null) nn[m] = srcNormals[i];
            if (nt != null) nt[m] = srcTangents[i];
            if (nc != null) nc[m] = srcColors[i];

            for (int ch = 0; ch < 8; ++ch)
            {
                if (hasUV[ch]) newUVs[ch].Add(srcUVs[ch][i]);
            }

            nbw[m] = bw[i];
        }

        var newMesh = new Mesh
        {
            name = "VRM_Body_HeadDescOnly_Mesh",
            indexFormat = srcMesh.indexFormat
        };

        newMesh.vertices = nv;
        if (nn != null) newMesh.normals = nn;
        if (nt != null) newMesh.tangents = nt;
        if (nc != null) newMesh.colors = nc;

        for (int ch = 0; ch < 8; ++ch)
        {
            if (hasUV[ch]) newMesh.SetUVs(ch, newUVs[ch]);
        }

        newMesh.boneWeights = nbw;
        newMesh.bindposes = srcMesh.bindposes;

        newMesh.subMeshCount = subMeshCount;

        var dst = new List<int>(4096);

        for (int si = 0; si < subMeshCount; ++si)
        {
            int keptIdxCount = keptIndexCounts[si];
            if (keptIdxCount <= 0)
            {
                newMesh.SetTriangles(System.Array.Empty<int>(), si, true);
                continue;
            }

            dst.Clear();
            if (dst.Capacity < keptIdxCount) dst.Capacity = keptIdxCount;

            tris.Clear();
            srcMesh.GetTriangles(tris, si);

            for (int t = 0; t < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];

                bool ka = (a >= 0 && a < vCount) && keepV[a];
                bool kb = (b >= 0 && b < vCount) && keepV[b];
                bool kc = (c >= 0 && c < vCount) && keepV[c];

                bool keepTri = requireAllVertsInTri ? (ka && kb && kc) : (ka || kb || kc);
                if (!keepTri) continue;

                dst.Add(remap[a]);
                dst.Add(remap[b]);
                dst.Add(remap[c]);
            }

            newMesh.SetTriangles(dst, si, true);
        }

        newMesh.RecalculateBounds();
        if (nn == null) newMesh.RecalculateNormals();
#if UNITY_2019_4_OR_NEWER
        if (nt == null) newMesh.RecalculateTangents();
#endif

        var go = new GameObject(string.IsNullOrEmpty(fixedNameForGo) ? (srcSmr.gameObject.name + "_HeadDescOnly") : fixedNameForGo);
        var dstSmr = go.AddComponent<SkinnedMeshRenderer>();
        dstSmr.sharedMesh = newMesh;

        var srcMats = srcSmr.sharedMaterials;
        var dstMats = new Material[newMesh.subMeshCount];
        for (int i = 0; i < dstMats.Length && i < srcMats.Length; ++i) dstMats[i] = srcMats[i];
        dstSmr.sharedMaterials = dstMats;

        dstSmr.bones = srcSmr.bones;
        dstSmr.rootBone = srcSmr.rootBone;
        dstSmr.updateWhenOffscreen = srcSmr.updateWhenOffscreen;
        dstSmr.allowOcclusionWhenDynamic = srcSmr.allowOcclusionWhenDynamic;
        dstSmr.quality = srcSmr.quality;

        return dstSmr;
    }


    public void SetLookAtOverrideWorld(Vector3 worldPos)
    {
        _lookAtOverrideWorldPos = worldPos;
        _lookAtOverrideActive = true;
        _lookAtOverrideBlend01 = 1f;
        _eyeContactBlend01 = 1f;
    }

    public void ClearLookAtOverrideWorld()
    {
        _lookAtOverrideActive = false;
    }

    private int CountAssigned(int[] map)
    {
        int cnt = 0;
        for (int i = 0; i < map.Length; ++i) if (map[i] >= 0) cnt++;
        return cnt;
    }

    public void BindToExistingRuntimeIfPresent()
    {
        vrmToRuntimeController = GetComponent<VrmToRuntimeController>();
        _humanoidAnim = GetComponent<HumanoidAnimationController>();

        Transform best = null;
        int bestDepth = int.MaxValue;

        var anims = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < anims.Length; i++)
        {
            var a = anims[i];
            if (a == null) continue;
            if (!a.transform.IsChildOf(transform)) continue;

            int d = GetHierarchyDepthFrom(a.transform, transform);
            if (d < bestDepth)
            {
                bestDepth = d;
                best = a.transform;
            }
        }

        if (best == null)
        {
            _ready = false;
            return;
        }

        _bodyRoot = best.gameObject;

        var smrs = _bodyRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        _bodyPrimarySmr = null;
        int bodyBestDepth = int.MaxValue;
        string bodyBestName = null;

        for (int i = 0; i < smrs.Length; i++)
        {
            var s = smrs[i];
            if (s == null || s.sharedMesh == null) continue;
            if (s.sharedMesh.GetBlendShapeIndex("body") < 0) continue;

            int d = GetHierarchyDepthFrom(s.transform, _bodyRoot.transform);
            string n = s.name ?? string.Empty;

            if (_bodyPrimarySmr == null ||
                d < bodyBestDepth ||
                (d == bodyBestDepth && string.CompareOrdinal(n, bodyBestName) < 0))
            {
                _bodyPrimarySmr = s;
                bodyBestDepth = d;
                bodyBestName = n;
            }
        }

        if (_bodyPrimarySmr == null)
        {
            _ready = false;
            return;
        }

        _face3Smr = null;
        int faceBestDepth = int.MaxValue;
        string faceBestName = null;

        for (int i = 0; i < smrs.Length; i++)
        {
            var s = smrs[i];
            if (s == null || s.sharedMesh == null) continue;
            if (s.sharedMesh.GetBlendShapeIndex("key") < 0) continue;

            int d = GetHierarchyDepthFrom(s.transform, _bodyRoot.transform);
            string n = s.name ?? string.Empty;

            if (_face3Smr == null ||
                d < faceBestDepth ||
                (d == faceBestDepth && string.CompareOrdinal(n, faceBestName) < 0))
            {
                _face3Smr = s;
                faceBestDepth = d;
                faceBestName = n;
            }
        }

        _bodyKeyIndex = _bodyPrimarySmr.sharedMesh.GetBlendShapeIndex("body");
        _face3KeyIndex = (_face3Smr != null) ? _face3Smr.sharedMesh.GetBlendShapeIndex("key") : -1;

        _retarget = _retarget ?? new VrmRetargeting();
        _motion = _motion ?? new BodyMotion();

        _bodyBoneMap = _retarget.BuildBoneMap(_bodyRoot.transform);

        var animator = _bodyRoot.GetComponentInChildren<Animator>(true) ?? _bodyRoot.AddComponent<Animator>();

        if (!_motion.TryCaptureReferences(animator, _bodyRoot.transform, out var bones))
        {
            _ready = false;
            return;
        }

        _motion.Bones = bones;

        if (_humanoidAnim != null)
            _humanoidAnim.Initialize(animator, _motion, _bodyRoot.transform);

        if (meshPullEnabled) SetupMeshPullRuntime();

        _ready = true;
    }
    public void ApplyEvent(string eventKey)
    {
        if (vrmToRuntimeController != null) vrmToRuntimeController.ApplyEvent(eventKey);
    }

    private void MakeRendererMaterialsInstance(SkinnedMeshRenderer smr)
    {
        if (smr == null) return;

        var shared = smr.sharedMaterials;
        if (shared == null || shared.Length == 0) return;

        var inst = new Material[shared.Length];
        for (int i = 0; i < shared.Length; i++)
        {
            var m = shared[i];
            if (m == null) { inst[i] = null; continue; }
            var c = new Material(m);
            inst[i] = c;
            _runtimeMaterials.Add(c);
        }
        smr.sharedMaterials = inst;
    }

    private static string BaseMatName(Material m)
    {
        if (m == null) return string.Empty;
        var n = m.name;
        const string suffix = " (Instance)";
        if (n != null && n.EndsWith(suffix, StringComparison.Ordinal))
        {
            return n.Substring(0, n.Length - suffix.Length);
        }
        return n ?? string.Empty;
    }

    private static Material FindMaterialByBaseName(Material[] materials, string baseName)
    {
        if (materials == null || string.IsNullOrEmpty(baseName)) return null;
        for (int i = 0; i < materials.Length; i++)
        {
            var m = materials[i];
            if (m == null) continue;
            if (string.Equals(BaseMatName(m), baseName, StringComparison.Ordinal)) return m;
        }
        return null;
    }

    private void ApplyFinalShadeOverride()
    {
        if (overrideShadeColor)
        {
            FindMaterialByBaseName(_bodyPrimarySmr.sharedMaterials, "BodySkin")
                .SetColor(ShadeColorId, overrideShadeColorValue);
            FindMaterialByBaseName(_face3Smr.sharedMaterials, "faceSkin")
                .SetColor(ShadeColorId, overrideShadeColorValue);
        }
    }

    // =========================
    // SetupMeshPullRuntime() and below: FULL BLOCK (no logs)
    // =========================

    private void SetupMeshPullRuntime()
    {
        ResetMeshPullRuntime();

        if (!meshPullEnabled)
        {
            // MeshPullを使わないモードでは、ランタイムmeshを生成しない
            return;
        }

        if (_mainCam == null) _mainCam = Camera.main;

        // ---- body ----
        if (_bodyPrimarySmr != null && _bodyPrimarySmr.sharedMesh != null
            && _bodyPrimarySmr.enabled && _bodyPrimarySmr.gameObject.activeInHierarchy)
        {
            var m = EnsureUniqueMeshInstance(_bodyPrimarySmr, "__MeshPullInst");
            _meshPullBodyMeshInst = m;

            var verts = m.vertices;

            _defBody = new DeformState
            {
                valid = true,
                smr = _bodyPrimarySmr,
                mesh = m,

                restV = verts,
                workV = (Vector3[])verts.Clone(),
                velV = new Vector3[m.vertexCount],

                wBody01 = null,
                wBust01 = null,
                wLow01 = null,

                grabLocal = Vector3.zero,
                grabCamDepth = 0f,

                idx = new List<int>(512),
                w = new List<float>(512),

                currentTargetOffsetLocal = Vector3.zero,

                hasActive = false,
                grabbing = false,

                tapActive = false,
                tapRemain = 0f,
                tapHoldOffsetLocal = Vector3.zero
            };

        }
        else
        {
            _defBody = default;
        }

        // ---- face3 ----
        if (_face3Smr != null && _face3Smr.sharedMesh != null
            && _face3Smr.enabled && _face3Smr.gameObject.activeInHierarchy)
        {
            var m = EnsureUniqueMeshInstance(_face3Smr, "__MeshPullInst");
            _meshPullFace3MeshInst = m;

            var verts = m.vertices;

            _defFace3 = new DeformState
            {
                valid = true,
                smr = _face3Smr,
                mesh = m,

                restV = verts,
                workV = (Vector3[])verts.Clone(),
                velV = new Vector3[m.vertexCount],

                wBody01 = null,
                wBust01 = null,

                grabLocal = Vector3.zero,
                grabCamDepth = 0f,

                idx = new List<int>(256),
                w = new List<float>(256),

                currentTargetOffsetLocal = Vector3.zero,

                hasActive = false,
                grabbing = false,

                tapActive = false,
                tapRemain = 0f,
                tapHoldOffsetLocal = Vector3.zero
            };

        }
        else
        {
            _defFace3 = default;
        }
    }

    private void ResetMeshPullRuntime()
    {
        _currentPullTarget = PullTargetKind.None;

        _defBody = default;
        _defFace3 = default;

        _pressActive = false;
        _pressGrabStarted = false;
        _pressStartTime = 0f;
        _pressStartPos = Vector2.zero;

        _meshPullLastStepFrame = -1;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) MeshPull_CancelAll();
    }

    private void OnDisable()
    {
        MeshPull_CancelAll();
        _motion?.RestoreReferencePose();
    }

    private static int GetHierarchyDepthFrom(Transform t, Transform rootExclusive)
    {
        int d = 0;
        while (t != null && t != rootExclusive)
        {
            t = t.parent;
            d++;
        }
        return d;
    }

    private void OnEnable()
    {
        // 既存の入力状態などが残っているとおかしくなるのでキャンセル
        MeshPull_CancelAll();

        // ★複製後に「既存の生成物（bodyRoot/Animator/SMRなど）」へ再バインド
        BindToExistingRuntimeIfPresent();
    }

    private void MeshPull_CancelAll()
    {
        _pressActive = false;
        _pressGrabStarted = false;

        if (_defBody.valid) MeshPull_EndGrab(ref _defBody);
        if (_defFace3.valid) MeshPull_EndGrab(ref _defFace3);

        _currentPullTarget = PullTargetKind.None;
    }

    private void MeshPull_GetParams(
        PullTargetKind kind,
        out float radius,
        out float maxOffset,
        out float k,
        out float d,
        out float dragGain)
    {
        if (kind == PullTargetKind.Face3)
        {
            radius = pullRadiusFace;
            maxOffset = pullMaxOffsetFace;
            k = springStiffnessFace;
            d = springDampingFace;
            dragGain = dragGainFace;
        }
        else
        {
            radius = pullRadiusBody;
            maxOffset = pullMaxOffsetBody;
            k = springStiffnessBody;
            d = springDampingBody;
            dragGain = dragGainBody;
        }
    }

    private float MeshPull_GetTapHoldSeconds(PullTargetKind kind)
    {
        return (kind == PullTargetKind.Face3) ? tapHoldSecondsFace : tapHoldSecondsBody;
    }

    private static readonly List<RaycastResult> _uiHits = new();
    private static PointerEventData _ped;

    private static bool MeshPull_IsPointerOverUI()
    {
        var es = EventSystem.current;
        if (es == null) return false;

        // 1) Touch 経由（通常スマホ）
        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (es.IsPointerOverGameObject(t.fingerId))
                    return true;
            }
            // touchCount>0でもモジュール不一致で false の場合があるので、下のレイキャストにも落とす
        }

        // 2) Mouse 経由（WebGLスマホでタップがMouse化するケース）
        if (es.IsPointerOverGameObject())
            return true;

        // 3) 最終手段：画面座標でUIレイキャスト（入力モジュール/ID不一致を回避）
        if (_ped == null) _ped = new PointerEventData(es);
        _ped.Reset();
        _ped.position = Input.mousePosition; // TouchがMouse化するケースでもここに座標が入る
        _uiHits.Clear();
        es.RaycastAll(_ped, _uiHits);
        return _uiHits.Count > 0;
    }


    private void MeshPull_UpdateInput()
    {
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null) return;

        bool down = Input.GetMouseButtonDown(0);
        bool held = Input.GetMouseButton(0);
        bool up = Input.GetMouseButtonUp(0);

        // ★Canvas/UI上での押下は開始しない
        if (down && MeshPull_IsPointerOverUI())
        {
            return;
        }

        // ---- Down（押下開始）----
        if (down)
        {
            var ray = _mainCam.ScreenPointToRay(Input.mousePosition);

            if (MeshPull_TryPick(ray, out var kind, out var hitWorld, out var hitDist))
            {
                _pressActive = true;
                _pressGrabStarted = false;
                _pressStartTime = Time.unscaledTime;
                _pressStartPos = Input.mousePosition;

                _currentPullTarget = kind;

                MeshPull_GetParams(kind, out float radius, out _, out _, out _, out _);

                if (kind == PullTargetKind.Body)
                    MeshPull_PreparePress(ref _defBody, hitWorld, hitDist, radius);
                else if (kind == PullTargetKind.Face3)
                    MeshPull_PreparePress(ref _defFace3, hitWorld, hitDist, radius);

                // down+up 同フレーム（超短タップ）もここで確定
                if (up)
                {
                    MeshPull_FireTapOrEndOnUp();
                }
            }
        }

        // ---- Up（タップ or 終了）----
        if (up && _pressActive)
        {
            MeshPull_FireTapOrEndOnUp();
        }

        // ---- Held（長押し/スワイプ）----
        if (held && _pressActive && _currentPullTarget != PullTargetKind.None)
        {
            float heldSec = Time.unscaledTime - _pressStartTime;
            float movedPx = ((Vector2)Input.mousePosition - _pressStartPos).magnitude;

            bool startGrab = (!_pressGrabStarted) && (heldSec >= longPressSeconds || movedPx >= tapMaxMovePixels);

            if (startGrab)
            {
                _pressGrabStarted = true;

                if (_currentPullTarget == PullTargetKind.Body && _defBody.valid)
                {
                    // swipe starts: cancel tap if any
                    _defBody.tapActive = false;
                    _defBody.tapRemain = 0f;
                    _defBody.tapHoldOffsetLocal = Vector3.zero;

                    _defBody.grabbing = true;
                    _defBody.hasActive = true;
                }
                else if (_currentPullTarget == PullTargetKind.Face3 && _defFace3.valid)
                {
                    _defFace3.tapActive = false;
                    _defFace3.tapRemain = 0f;
                    _defFace3.tapHoldOffsetLocal = Vector3.zero;

                    _defFace3.grabbing = true;
                    _defFace3.hasActive = true;
                }
            }

            if (_pressGrabStarted)
            {
                var inst = VrmChrSceneController.Instance;
                if (_currentPullTarget == PullTargetKind.Body && _defBody.valid && _defBody.grabbing)
                {
                    MeshPull_GetParams(PullTargetKind.Body, out _, out float maxOffset, out _, out _, out float dragGain);
                    MeshPull_UpdateDrag_Vector(ref _defBody, maxOffset, dragGain);
                    if (inst != null)
                    {
                        inst.SetBlushState(BlushState.blush_swaip);
                    }
                }
                else if (_currentPullTarget == PullTargetKind.Face3 && _defFace3.valid && _defFace3.grabbing)
                {
                    MeshPull_GetParams(PullTargetKind.Face3, out _, out float maxOffset, out _, out _, out float dragGain);
                    MeshPull_UpdateDrag_Vector(ref _defFace3, maxOffset, dragGain);
                    if (inst != null)
                    {
                        inst.SetBlushState(BlushState.blush_swaip_face);
                    }
                }
            }
        }

        // ---- 救済：入力が途切れているのに grabbing が残る等 ----
        if (!down && !held && !up)
        {
            if (_pressActive)
            {
                _pressActive = false;
                _pressGrabStarted = false;
            }

            if (_currentPullTarget == PullTargetKind.Body && _defBody.valid && _defBody.grabbing && !_defBody.tapActive)
                MeshPull_EndGrab(ref _defBody);
            if (_currentPullTarget == PullTargetKind.Face3 && _defFace3.valid && _defFace3.grabbing && !_defFace3.tapActive)
                MeshPull_EndGrab(ref _defFace3);

            _currentPullTarget = PullTargetKind.None;
        }
    }

    private void MeshPull_FireTapOrEndOnUp()
    {
        // Up on current target
        if (_currentPullTarget == PullTargetKind.Body && _defBody.valid)
        {
            if (_pressGrabStarted)
            {
                // if (bodyKey > 80)
                // {
                //     AudioManager.Instance.PlaySE("tap3");
                // }
                // else
                // {
                AudioManager.Instance.PlaySE("tap1");
                // }
                MeshPull_EndGrab(ref _defBody);
            }
            else
            {
                // if (bodyKey > 80)
                // {
                //     AudioManager.Instance.PlaySE("tap4");
                // }
                // else
                // {
                AudioManager.Instance.PlaySE("tap2");
                // }
                MeshPull_BeginTapVirtualSwipe(ref _defBody, PullTargetKind.Body);
            }
            var inst = VrmChrSceneController.Instance;
            if (inst != null)
            {
                inst.SetBlushState(BlushState.blush_tap);
            }
        }
        else if (_currentPullTarget == PullTargetKind.Face3 && _defFace3.valid)
        {
            if (_pressGrabStarted)
            {
                AudioManager.Instance.PlaySE("tap1");
                MeshPull_EndGrab(ref _defFace3);
                var inst = VrmChrSceneController.Instance;
                if (inst != null)
                {
                    inst.SetBlushState(BlushState.None);
                }
            }
            else
            {
                AudioManager.Instance.PlaySE("tap2");
                var inst = VrmChrSceneController.Instance;
                if (inst != null)
                {
                    inst.SetBlushState(BlushState.blush_tap_face);
                }
                MeshPull_BeginTapVirtualSwipe(ref _defFace3, PullTargetKind.Face3);
            }
        }

        _pressActive = false;
        _pressGrabStarted = false;
        _currentPullTarget = PullTargetKind.None;
    }

    private bool MeshPull_TryPick(Ray ray, out PullTargetKind kind, out Vector3 hitWorld, out float hitDist)
    {
        kind = PullTargetKind.None;
        hitWorld = default;
        hitDist = 0f;

        float bestFace = float.PositiveInfinity;
        Vector3 bestFacePt = default;
        bool hasFace = false;

        float bestBody = float.PositiveInfinity;
        Vector3 bestBodyPt = default;
        bool hasBody = false;

        var hits = Physics.RaycastAll(ray, 100f, meshPullRaycastMask, QueryTriggerInteraction.Collide);
        if (hits != null && hits.Length > 0)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                var col = h.collider;
                if (col == null) continue;

                // face collider host
                if (col.CompareTag("Facemouth"))
                {
                    if (_defFace3.valid && _defFace3.smr != null && _defFace3.smr.enabled && _defFace3.smr.gameObject.activeInHierarchy)
                    {
                        if (h.distance < bestFace)
                        {
                            bestFace = h.distance;
                            bestFacePt = h.point;
                            hasFace = true;
                        }
                    }
                    continue;
                }

                // body: accept children of body root only
                if (_defBody.valid && _defBody.smr != null && _defBody.smr.enabled && _defBody.smr.gameObject.activeInHierarchy)
                {
                    if (_bodyRoot != null && col.transform != null && col.transform.IsChildOf(_bodyRoot.transform))
                    {
                        if (h.distance < bestBody)
                        {
                            bestBody = h.distance;
                            bestBodyPt = h.point;
                            hasBody = true;
                        }
                    }
                }
            }

            if (hasFace && (!hasBody || bestFace <= bestBody + Mathf.Max(0f, facePickPriorityBias)))
            {
                kind = PullTargetKind.Face3;
                hitWorld = bestFacePt;
                hitDist = bestFace;
                return true;
            }
            if (hasBody)
            {
                kind = PullTargetKind.Body;
                hitWorld = bestBodyPt;
                hitDist = bestBody;
                return true;
            }
        }

        // collider無し保険：Bounds
        float faceD = float.PositiveInfinity;
        bool faceOk = false;
        Vector3 faceW = default;

        float bodyD = float.PositiveInfinity;
        bool bodyOk = false;
        Vector3 bodyW = default;

        if (_defFace3.valid && _defFace3.smr != null && _defFace3.smr.enabled && _defFace3.smr.gameObject.activeInHierarchy)
        {
            var b = _defFace3.smr.bounds;
            if (b.IntersectRay(ray, out float d))
            {
                faceOk = true;
                faceD = d;
                faceW = ray.GetPoint(d);
            }
        }

        if (_defBody.valid && _defBody.smr != null && _defBody.smr.enabled && _defBody.smr.gameObject.activeInHierarchy)
        {
            var b = _defBody.smr.bounds;
            if (b.IntersectRay(ray, out float d))
            {
                bodyOk = true;
                bodyD = d;
                bodyW = ray.GetPoint(d);
            }
        }

        if (faceOk && (!bodyOk || faceD <= bodyD + Mathf.Max(0f, facePickPriorityBias)))
        {
            kind = PullTargetKind.Face3;
            hitWorld = faceW;
            hitDist = faceD;
            return true;
        }
        if (bodyOk)
        {
            kind = PullTargetKind.Body;
            hitWorld = bodyW;
            hitDist = bodyD;
            return true;
        }

        return false;
    }

    // Press prepare: build affected verts + falloff around hit point
    private void MeshPull_PreparePress(ref DeformState ds, Vector3 hitWorld, float hitDist, float radius)
    {
        if (!ds.valid || ds.smr == null || ds.mesh == null) return;

        // Body のウェイトマップは初回のプル開始時にだけ作る（init時に作らない）
        if (ds.smr == _bodyPrimarySmr)
        {
            if (ds.wBody01 == null || ds.wBust01 == null || ds.wLow01 == null)
            {
                BuildKeyWeightMapsForBody(ds.mesh, out ds.wBody01, out ds.wBust01, out ds.wLow01);
            }
        }


        ds.hasActive = true;

        ds.grabCamDepth = hitDist;
        ds.grabLocal = ds.smr.transform.InverseTransformPoint(hitWorld);

        ds.idx.Clear();
        ds.w.Clear();

        float r = Mathf.Max(1e-6f, radius);
        float r2 = r * r;

        var v = ds.restV;
        int n = v.Length;

        for (int i = 0; i < n; i++)
        {
            float d2 = (v[i] - ds.grabLocal).sqrMagnitude;
            if (d2 > r2) continue;

            float d = Mathf.Sqrt(d2);
            float t = Mathf.Clamp01(1f - (d / r));
            float w = t * t * (3f - 2f * t); // smoothstep

            ds.idx.Add(i);
            ds.w.Add(w);
        }

        // do not start grabbing yet
        ds.grabbing = false;
        ds.currentTargetOffsetLocal = Vector3.zero;

        // cancel tap if any
        ds.tapActive = false;
        ds.tapRemain = 0f;
        ds.tapHoldOffsetLocal = Vector3.zero;
    }

    // Swipe path: update currentTargetOffsetLocal from pointer delta (same as before)
    private void MeshPull_UpdateDrag_Vector(ref DeformState ds, float maxOffset, float dragGain)
    {
        if (!ds.valid || !ds.grabbing || ds.smr == null) return;
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null) return;

        var ray = _mainCam.ScreenPointToRay(Input.mousePosition);
        Vector3 newWorld = ray.GetPoint(ds.grabCamDepth);

        Vector3 grabWorld = ds.smr.transform.TransformPoint(ds.grabLocal);
        Vector3 deltaWorld = newWorld - grabWorld;
        Vector3 deltaLocal = ds.smr.transform.InverseTransformVector(deltaWorld);

        float g = Mathf.Max(0f, dragGain);
        if (g != 1f) deltaLocal *= g;

        float max = Mathf.Max(0f, maxOffset);
        if (max > 0f && deltaLocal.magnitude > max)
            deltaLocal = deltaLocal.normalized * max;

        ds.currentTargetOffsetLocal = deltaLocal;
        ds.hasActive = true;
    }

    // Tap = virtual swipe: hold a constant offset for tapHoldSeconds
    private void MeshPull_BeginTapVirtualSwipe(ref DeformState ds, PullTargetKind kind)
    {
        if (!ds.valid || ds.smr == null) return;

        MeshPull_GetParams(kind, out _, out float maxOffset, out _, out _, out _);

        // direction: upward in this renderer's local space
        Vector3 upLocal = ds.smr.transform.InverseTransformVector(Vector3.up);
        if (upLocal.sqrMagnitude < 1e-12f) upLocal = Vector3.up;
        upLocal.Normalize();

        ds.tapHoldOffsetLocal = upLocal * Mathf.Max(0f, maxOffset);
        ds.tapRemain = Mathf.Max(0f, MeshPull_GetTapHoldSeconds(kind));
        ds.tapActive = ds.tapRemain > 1e-6f;

        // during tap hold, we behave exactly like a “swipe hold”
        ds.grabbing = ds.tapActive;
        ds.currentTargetOffsetLocal = ds.tapHoldOffsetLocal;

        ds.hasActive = true;
    }

    private void MeshPull_EndGrab(ref DeformState ds)
    {
        if (!ds.valid) return;

        // end both swipe hold and tap hold
        ds.grabbing = false;
        ds.currentTargetOffsetLocal = Vector3.zero;

        ds.tapActive = false;
        ds.tapRemain = 0f;
        ds.tapHoldOffsetLocal = Vector3.zero;

        ds.hasActive = true; // let spring settle
    }

    private void BuildKeyWeightMapsForBody(Mesh mesh, out float[] wBody01, out float[] wBust01, out float[] wLow01)
    {
        wBody01 = null;
        wBust01 = null;
        wLow01 = null;

        if (mesh == null) return;
        int n = mesh.vertexCount;
        if (n <= 0) return;

        int bodyIdx = mesh.GetBlendShapeIndex("body");
        int bustIdx = (bodyIdx >= 0) ? (bodyIdx + 1) : -1;
        int lowIdx = (bodyIdx >= 0) ? (bodyIdx + 2) : -1; // ★追加（あなたの Update の前提と同じ）

        var dv = new Vector3[n];
        var dn = new Vector3[n];
        var dt = new Vector3[n];

        if (bodyIdx >= 0 && mesh.GetBlendShapeFrameCount(bodyIdx) > 0)
        {
            mesh.GetBlendShapeFrameVertices(bodyIdx, 0, dv, dn, dt);
            wBody01 = BuildNormalizedMagnitudeMap_MaxOnly(dv);
        }
        else
        {
            wBody01 = new float[n];
        }

        if (bustIdx >= 0 && bustIdx < mesh.blendShapeCount && mesh.GetBlendShapeFrameCount(bustIdx) > 0)
        {
            Array.Clear(dv, 0, dv.Length);
            mesh.GetBlendShapeFrameVertices(bustIdx, 0, dv, dn, dt);
            wBust01 = BuildNormalizedMagnitudeMap_MaxOnly(dv);
        }
        else
        {
            wBust01 = new float[n];
        }

        if (lowIdx >= 0 && lowIdx < mesh.blendShapeCount && mesh.GetBlendShapeFrameCount(lowIdx) > 0)
        {
            Array.Clear(dv, 0, dv.Length);
            mesh.GetBlendShapeFrameVertices(lowIdx, 0, dv, dn, dt);
            wLow01 = BuildNormalizedMagnitudeMap_MaxOnly(dv);
        }
        else
        {
            wLow01 = new float[n];
        }
    }

    private float[] BuildNormalizedMagnitudeMap_MaxOnly(Vector3[] delta)
    {
        int n = delta.Length;
        var w = new float[n];

        float max = 0f;
        for (int i = 0; i < n; i++)
        {
            float m = delta[i].magnitude;
            w[i] = m;
            if (m > max) max = m;
        }

        if (max <= 1e-12f)
        {
            Array.Clear(w, 0, w.Length);
            return w;
        }

        float inv = 1f / max;
        for (int i = 0; i < n; i++)
        {
            w[i] = Mathf.Clamp01(w[i] * inv);
        }
        return w;
    }

    // This can be called from Update/LateUpdate as you already do.
    // Guard prevents double-stepping if you accidentally call twice per frame.
    private void MeshPull_StepSimulation(float dt)
    {
        if (dt <= 0f) return;

        int f = Time.frameCount;
        if (_meshPullLastStepFrame == f) return;
        _meshPullLastStepFrame = f;

        if (_defBody.valid)
        {
            MeshPull_GetParams(PullTargetKind.Body, out _, out _, out float k, out float d, out _);
            MeshPull_StepOne_Body(ref _defBody, dt, k, d);
        }

        if (_defFace3.valid)
        {
            MeshPull_GetParams(PullTargetKind.Face3, out _, out _, out float k, out float d, out _);
            MeshPull_StepOne_Face(ref _defFace3, dt, k, d);
        }
    }

    private void MeshPull_StepOne_Body(ref DeformState ds, float dt, float stiffness, float damping)
    {
        if (!ds.valid || ds.mesh == null || ds.restV == null || ds.workV == null || ds.velV == null) return;
        if (ds.wBody01 == null || ds.wBust01 == null) return;
        if (!ds.hasActive) return;

        int m = (ds.idx != null) ? ds.idx.Count : 0;
        if (m == 0) { ds.hasActive = false; return; }

        // tap hold countdown (same “grabbing” path)
        if (ds.tapActive)
        {
            ds.tapRemain -= dt;
            if (ds.tapRemain <= 0f)
            {
                ds.tapActive = false;
                ds.tapRemain = 0f;
                ds.tapHoldOffsetLocal = Vector3.zero;

                // release hold; spring continues
                ds.grabbing = false;
                ds.currentTargetOffsetLocal = Vector3.zero;
            }
            else
            {
                ds.grabbing = true;
                ds.currentTargetOffsetLocal = ds.tapHoldOffsetLocal;
            }
        }

        float k = Mathf.Max(0f, stiffness);
        float c = Mathf.Max(0f, damping);

        // bodyKey & bustKey are independent and equivalent contributors:
        // each scales its own per-vertex delta magnitude map.
        float body01 = Mathf.Clamp01(bodyKey / 100f);
        float bust01 = Mathf.Clamp01(bustKey / 100f);
        float low01 = Mathf.Clamp01(lowKey / 100f);

        bool anyMoving = false;

        float posEps2 = sleepPosEps * sleepPosEps;
        float velEps2 = sleepVelEps * sleepVelEps;

        for (int j = 0; j < m; j++)
        {
            int i = ds.idx[j];
            float falloff = ds.w[j];

            Vector3 offset = Vector3.zero;

            if (ds.grabbing)
            {
                float scale = (body01 * ds.wBody01[i]) + (bust01 * ds.wBust01[i]) + (low01 * ds.wLow01[i]); ;
                scale = Mathf.Clamp01(scale);

                offset = ds.currentTargetOffsetLocal * falloff * scale;
            }

            Vector3 target = ds.restV[i] + offset;

            Vector3 x = ds.workV[i];
            Vector3 v = ds.velV[i];

            Vector3 a = (k * (target - x)) - (c * v);
            v += a * dt;
            x += v * dt;

            ds.workV[i] = x;
            ds.velV[i] = v;

            if ((x - target).sqrMagnitude > posEps2 || v.sqrMagnitude > velEps2)
                anyMoving = true;
        }

        if (!ds.grabbing && !anyMoving)
        {
            for (int j = 0; j < m; j++)
            {
                int i = ds.idx[j];
                ds.workV[i] = ds.restV[i];
                ds.velV[i] = Vector3.zero;
            }

            ds.mesh.vertices = ds.workV;
            ds.mesh.RecalculateBounds();
            ds.mesh.RecalculateNormals();
#if UNITY_2019_4_OR_NEWER
            ds.mesh.RecalculateTangents();
#endif
            ds.hasActive = false;
            return;
        }

        ds.mesh.vertices = ds.workV;
        ds.mesh.RecalculateBounds();
        ds.mesh.RecalculateNormals();
#if UNITY_2019_4_OR_NEWER
        ds.mesh.RecalculateTangents();
#endif

        ds.hasActive = ds.grabbing || anyMoving;
    }

    private void MeshPull_StepOne_Face(ref DeformState ds, float dt, float stiffness, float damping)
    {
        if (!ds.valid || ds.mesh == null || ds.restV == null || ds.workV == null || ds.velV == null) return;
        if (!ds.hasActive) return;

        int m = (ds.idx != null) ? ds.idx.Count : 0;
        if (m == 0) { ds.hasActive = false; return; }

        // tap hold countdown (same “grabbing” path)
        if (ds.tapActive)
        {
            ds.tapRemain -= dt;
            if (ds.tapRemain <= 0f)
            {
                ds.tapActive = false;
                ds.tapRemain = 0f;
                ds.tapHoldOffsetLocal = Vector3.zero;

                ds.grabbing = false;
                ds.currentTargetOffsetLocal = Vector3.zero;
            }
            else
            {
                ds.grabbing = true;
                ds.currentTargetOffsetLocal = ds.tapHoldOffsetLocal;
            }
        }

        float k = Mathf.Max(0f, stiffness);
        float c = Mathf.Max(0f, damping);

        bool anyMoving = false;

        float posEps2 = sleepPosEps * sleepPosEps;
        float velEps2 = sleepVelEps * sleepVelEps;

        for (int j = 0; j < m; j++)
        {
            int i = ds.idx[j];
            float w = ds.w[j];

            Vector3 offset = ds.grabbing ? (ds.currentTargetOffsetLocal * w) : Vector3.zero;
            Vector3 target = ds.restV[i] + offset;

            Vector3 x = ds.workV[i];
            Vector3 v = ds.velV[i];

            Vector3 a = (k * (target - x)) - (c * v);
            v += a * dt;
            x += v * dt;

            ds.workV[i] = x;
            ds.velV[i] = v;

            if ((x - target).sqrMagnitude > posEps2 || v.sqrMagnitude > velEps2)
                anyMoving = true;
        }

        if (!ds.grabbing && !anyMoving)
        {
            for (int j = 0; j < m; j++)
            {
                int i = ds.idx[j];
                ds.workV[i] = ds.restV[i];
                ds.velV[i] = Vector3.zero;
            }

            ds.mesh.vertices = ds.workV;
            ds.mesh.RecalculateBounds();
            ds.mesh.RecalculateNormals();
#if UNITY_2019_4_OR_NEWER
            ds.mesh.RecalculateTangents();
#endif
            ds.hasActive = false;
            return;
        }

        ds.mesh.vertices = ds.workV;
        ds.mesh.RecalculateBounds();
        ds.mesh.RecalculateNormals();
#if UNITY_2019_4_OR_NEWER
        ds.mesh.RecalculateTangents();
#endif

        ds.hasActive = ds.grabbing || anyMoving;
    }


}
