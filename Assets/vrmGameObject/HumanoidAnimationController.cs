using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DisallowMultipleComponent]
public sealed class HumanoidAnimationController : MonoBehaviour
{
    public enum ReferencePoseMode
    {
        FirstFrame,
        TPose,
        None
    }

    [Serializable]
    public class AnimationEntry
    {
        public string key;
        public AnimationClip clip;
        public ReferencePoseMode referencePoseMode = ReferencePoseMode.FirstFrame;
        public bool useIK = false;
        public float transitionDuration = 0.5f;
    }
    [Header("Common Reference Pose")]
    public AnimationClip tposeClip;

    [Header("Animations")]
    public AnimationEntry[] animations;


    [Header("Foot Plant")]
    [SerializeField] private float _contactHeight = 0.08f;
    [SerializeField] private float _legShrinkFactor = 0.8f;

    private Animator _animator;
    private BodyMotion _bodyMotion;
    private Transform _bodyRoot;

    private PlayableGraph _graph;
    private AnimationPlayableOutput _output;

    private AnimationMixerPlayable _stateMixer;
    private AnimationClipPlayable[] _clipPlayables;

    private AnimationScriptPlayable _bodyJobPlayable;
    private bool _hasBodyJob;

    private AnimationScriptPlayable _motionScalePlayable;
    private bool _hasMotionScaleJob;

    private NativeArray<TransformStreamHandle> _motionHandles;
    private NativeArray<Quaternion> _motionRefRotations;
    private NativeArray<float> _perBoneFactors;
    private Transform[] _motionBoneTransforms;
    private int _currentRefAnimationIndex = -1;

    private bool _initialized;
    private readonly Dictionary<string, int> _keyToIndex = new Dictionary<string, int>();

    private VrmToController vrmToController;
    private string _currentAnimationKey = "idle";

    private int _activeAnimationIndex = 0;
    private int _targetAnimationIndex = 0;
    private bool _isTransitioning = false;
    private float _transitionTime = 0f;

    private Transform _rootTransform;
    private Transform _hipsT;
    private Transform _lUpperT;
    private Transform _lLowerT;
    private Transform _lFootT;
    private Transform _rUpperT;
    private Transform _rLowerT;
    private Transform _rFootT;

    private TransformStreamHandle _rootH;
    private TransformStreamHandle _hipsH;
    private TransformStreamHandle _lUpperH;
    private TransformStreamHandle _lLowerH;
    private TransformStreamHandle _lFootH;
    private TransformStreamHandle _rUpperH;
    private TransformStreamHandle _rLowerH;
    private TransformStreamHandle _rFootH;

    private AnimationScriptPlayable _footPlantPlayable;
    private bool _hasFootPlantJob;

    private Vector3 _refLeftFootLocal;
    private Vector3 _refRightFootLocal;

    private static readonly HumanBodyBones[] MotionTargetBones =
    {
        HumanBodyBones.Hips,
        HumanBodyBones.Spine,
        HumanBodyBones.Chest,
        HumanBodyBones.UpperChest,
        HumanBodyBones.Neck,
        HumanBodyBones.Head,
        HumanBodyBones.LeftShoulder,
        HumanBodyBones.RightShoulder,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightFoot
    };

    private static readonly HumanBodyBones[] Factor08Bones =
    {
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightFoot
    };

    private static readonly HumanBodyBones[] Factor04Bones =
    {
        HumanBodyBones.LeftShoulder,
        HumanBodyBones.RightShoulder,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightHand,
    };

    private static bool ContainsBone(HumanBodyBones[] list, HumanBodyBones bone)
    {
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] == bone) return true;
        }
        return false;
    }

    public void SetCurrentAnimationKey(string key)
    {
        _currentAnimationKey = key;
    }

    private void Start()
    {
        vrmToController = transform.GetComponent<VrmToController>();
    }

    public void Initialize(Animator targetAnimator, BodyMotion bodyMotion, Transform bodyRoot)
    {
        _animator = targetAnimator != null ? targetAnimator : GetComponentInChildren<Animator>();
        _bodyMotion = bodyMotion;
        _bodyRoot = bodyRoot;

        if (_animator == null)
        {
            enabled = false;
            return;
        }

        if (animations == null || animations.Length == 0)
        {
            enabled = false;
            return;
        }

        bool needTPose = false;
        foreach (var entry in animations)
        {
            if (entry != null && entry.referencePoseMode == ReferencePoseMode.TPose)
            {
                needTPose = true;
                break;
            }
        }

        if (needTPose && tposeClip == null)
        {
            enabled = false;
            return;
        }

        _keyToIndex.Clear();
        for (int i = 0; i < animations.Length; i++)
        {
            var entry = animations[i];
            if (entry == null) continue;
            if (string.IsNullOrEmpty(entry.key)) continue;

            if (!_keyToIndex.ContainsKey(entry.key))
            {
                _keyToIndex.Add(entry.key, i);
            }
        }

        CacheRigTransforms();
        BuildGraph();
        _initialized = true;
    }

    public void UpdateBodyMotionJob(BodyMotion motion)
    {
        if (!_initialized || motion == null) return;
        if (!_hasBodyJob || !_bodyJobPlayable.IsValid()) return;

        var job = _bodyJobPlayable.GetJobData<BodyMotion.RotationScaleJobRef>();
        motion.ApplyJobParams(ref job);
        _bodyJobPlayable.SetJobData(job);
    }

    private void Update()
    {
        if (!_initialized || !_graph.IsValid()) return;

        UpdateStateMixerWeights(Time.deltaTime);

        bool motionScaleEnabled = IsMotionScaleEnabledForCurrentAnimation();
        float bk01 = motionScaleEnabled ? ComputeBodyKey01() : 0f;

        if (_hasMotionScaleJob && _motionScalePlayable.IsValid())
        {
            UpdatePerBoneReferencePoseIfNeeded();

            var job = _motionScalePlayable.GetJobData<PerBoneMotionScaleJob>();
            job.BodyKey01 = bk01;
            _motionScalePlayable.SetJobData(job);
        }

        if (_hasFootPlantJob && _footPlantPlayable.IsValid())
        {
            var job = _footPlantPlayable.GetJobData<FootPlantJob>();
            job.Enabled = IsFootPlantEnabledForCurrentAnimation() ? 1 : 0;
            job.BodyKey01 = bk01;
            job.ContactHeight = Mathf.Max(1e-5f, _contactHeight);
            job.LegShrinkFactor = Mathf.Max(0f, _legShrinkFactor);
            job.RefLeftFootLocal = _refLeftFootLocal;
            job.RefRightFootLocal = _refRightFootLocal;
            _footPlantPlayable.SetJobData(job);
        }
    }

    private void OnDestroy()
    {
        if (_motionHandles.IsCreated) _motionHandles.Dispose();
        if (_motionRefRotations.IsCreated) _motionRefRotations.Dispose();
        if (_perBoneFactors.IsCreated) _perBoneFactors.Dispose();

        if (_graph.IsValid())
        {
            _graph.Destroy();
        }
    }

    private void CacheRigTransforms()
    {
        _rootTransform = (_bodyRoot != null) ? _bodyRoot : _animator.transform;

        _hipsT = _animator.GetBoneTransform(HumanBodyBones.Hips);
        _lUpperT = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        _lLowerT = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        _lFootT = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);

        _rUpperT = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        _rLowerT = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        _rFootT = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
    }

    private void BuildGraph()
    {
        if (_graph.IsValid())
        {
            _graph.Destroy();
        }

        if (_motionHandles.IsCreated) _motionHandles.Dispose();
        if (_motionRefRotations.IsCreated) _motionRefRotations.Dispose();
        if (_perBoneFactors.IsCreated) _perBoneFactors.Dispose();

        _graph = PlayableGraph.Create("HumanoidAnimationController");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        int stateCount = animations.Length;

        _stateMixer = AnimationMixerPlayable.Create(_graph, stateCount);
        _clipPlayables = new AnimationClipPlayable[stateCount];

        for (int i = 0; i < stateCount; i++)
        {
            SetupBranch(i);
        }

        Playable finalPlayable = _stateMixer;
        _hasBodyJob = false;

        if (_bodyMotion != null)
        {
            if (_bodyMotion.TryCreateJob(_animator, _bodyRoot, out var bodyJob))
            {
                _bodyJobPlayable = AnimationScriptPlayable.Create(_graph, bodyJob);
                _bodyJobPlayable.AddInput(finalPlayable, 0, 1f);
                finalPlayable = _bodyJobPlayable;
                _hasBodyJob = true;
            }
        }

        SetupPerBoneMotionScaleJob(ref finalPlayable);
        SetupFootPlantJob(ref finalPlayable);

        _output = AnimationPlayableOutput.Create(_graph, "HumanoidAnimationOutput", _animator);
        _output.SetSourcePlayable(finalPlayable);

        _activeAnimationIndex = GetCurrentAnimationIndex();
        _targetAnimationIndex = _activeAnimationIndex;
        _isTransitioning = false;
        _transitionTime = 0f;

        int count = _stateMixer.GetInputCount();
        for (int i = 0; i < count; i++)
        {
            _stateMixer.SetInputWeight(i, (i == _activeAnimationIndex) ? 1f : 0f);
        }

        ApplyClipSpeeds(activeIndex: _activeAnimationIndex, targetIndex: _activeAnimationIndex);

        _graph.Play();
    }

    private void SetupBranch(int index)
    {
        var entry = animations[index];
        if (entry == null || entry.clip == null)
        {
            var dummyClip = new AnimationClip();
            var dummyPlayable = AnimationClipPlayable.Create(_graph, dummyClip);
            dummyPlayable.SetApplyFootIK(false);
            dummyPlayable.SetTime(0.0);
            dummyPlayable.SetSpeed(0f);

            _clipPlayables[index] = dummyPlayable;

            _graph.Connect(dummyPlayable, 0, _stateMixer, index);
            _stateMixer.SetInputWeight(index, 0f);
            return;
        }

        var mainPlayable = AnimationClipPlayable.Create(_graph, entry.clip);
        mainPlayable.SetApplyFootIK(false);
        mainPlayable.SetTime(0.0);
        mainPlayable.SetSpeed(0f);

        _clipPlayables[index] = mainPlayable;

        _graph.Connect(mainPlayable, 0, _stateMixer, index);
        _stateMixer.SetInputWeight(index, 0f);
    }

    private void SetupPerBoneMotionScaleJob(ref Playable finalPlayable)
    {
        _hasMotionScaleJob = false;

        if (_animator == null) return;

        int boneCount = MotionTargetBones.Length;

        _motionHandles = new NativeArray<TransformStreamHandle>(boneCount, Allocator.Persistent);
        _motionRefRotations = new NativeArray<Quaternion>(boneCount, Allocator.Persistent);
        _perBoneFactors = new NativeArray<float>(boneCount, Allocator.Persistent);
        _motionBoneTransforms = new Transform[boneCount];

        for (int i = 0; i < boneCount; i++)
        {
            var hb = MotionTargetBones[i];
            var t = _animator.GetBoneTransform(hb);

            _motionBoneTransforms[i] = t;

            if (t != null)
            {
                _motionHandles[i] = _animator.BindStreamTransform(t);
            }
            else
            {
                _motionHandles[i] = default;
            }

            if (ContainsBone(Factor08Bones, hb))
            {
                _perBoneFactors[i] = 0.8f;
            }
            else if (ContainsBone(Factor04Bones, hb))
            {
                _perBoneFactors[i] = 0.5f;
            }
            else
            {
                _perBoneFactors[i] = 0.6f;
            }
        }

        _currentRefAnimationIndex = GetCurrentAnimationIndex();
        ResampleReferencePoseForAnimation(_currentRefAnimationIndex);

        var job = new PerBoneMotionScaleJob
        {
            Handles = _motionHandles,
            RefLocalRotations = _motionRefRotations,
            PerBoneFactor = _perBoneFactors,
            BodyKey01 = IsMotionScaleEnabledForCurrentAnimation() ? ComputeBodyKey01() : 0f
        };

        _motionScalePlayable = AnimationScriptPlayable.Create(_graph, job);
        _motionScalePlayable.AddInput(finalPlayable, 0, 1f);
        finalPlayable = _motionScalePlayable;

        _hasMotionScaleJob = true;
    }

    private void SetupFootPlantJob(ref Playable finalPlayable)
    {
        _hasFootPlantJob = false;

        if (_animator == null) return;
        if (_rootTransform == null || _hipsT == null) return;
        if (_lUpperT == null || _lLowerT == null || _lFootT == null) return;
        if (_rUpperT == null || _rLowerT == null || _rFootT == null) return;

        _rootH = _animator.BindStreamTransform(_rootTransform);
        _hipsH = _animator.BindStreamTransform(_hipsT);

        _lUpperH = _animator.BindStreamTransform(_lUpperT);
        _lLowerH = _animator.BindStreamTransform(_lLowerT);
        _lFootH = _animator.BindStreamTransform(_lFootT);

        _rUpperH = _animator.BindStreamTransform(_rUpperT);
        _rLowerH = _animator.BindStreamTransform(_rLowerT);
        _rFootH = _animator.BindStreamTransform(_rFootT);

        var job = new FootPlantJob
        {
            Enabled = IsFootPlantEnabledForCurrentAnimation() ? 1 : 0,
            BodyKey01 = IsMotionScaleEnabledForCurrentAnimation() ? ComputeBodyKey01() : 0f,
            ContactHeight = Mathf.Max(1e-5f, _contactHeight),
            LegShrinkFactor = Mathf.Max(0f, _legShrinkFactor),

            Root = _rootH,
            Hips = _hipsH,

            LUpper = _lUpperH,
            LLower = _lLowerH,
            LFoot = _lFootH,

            RUpper = _rUpperH,
            RLower = _rLowerH,
            RFoot = _rFootH,

            RefLeftFootLocal = _refLeftFootLocal,
            RefRightFootLocal = _refRightFootLocal
        };

        _footPlantPlayable = AnimationScriptPlayable.Create(_graph, job);
        _footPlantPlayable.AddInput(finalPlayable, 0, 1f);
        finalPlayable = _footPlantPlayable;

        _hasFootPlantJob = true;
    }

    private void UpdateStateMixerWeights(float deltaTime)
    {
        if (!_stateMixer.IsValid()) return;

        int desiredIndex = GetCurrentAnimationIndex();
        int count = _stateMixer.GetInputCount();

        if (!_isTransitioning)
        {
            if (desiredIndex != _activeAnimationIndex)
            {
                ResetClipTime(desiredIndex);

                _isTransitioning = true;
                _transitionTime = 0f;
                _targetAnimationIndex = desiredIndex;

                for (int i = 0; i < count; i++)
                {
                    _stateMixer.SetInputWeight(i, (i == _activeAnimationIndex) ? 1f : 0f);
                }

                ApplyClipSpeeds(activeIndex: _activeAnimationIndex, targetIndex: _targetAnimationIndex);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    _stateMixer.SetInputWeight(i, (i == _activeAnimationIndex) ? 1f : 0f);
                }

                ApplyClipSpeeds(activeIndex: _activeAnimationIndex, targetIndex: _activeAnimationIndex);
            }
        }
        else
        {
            float duration = GetTransitionDurationTo(_targetAnimationIndex);
            float t = 1f;
            if (duration > 0f)
            {
                _transitionTime += deltaTime;
                t = Mathf.Clamp01(_transitionTime / duration);
            }

            for (int i = 0; i < count; i++)
            {
                if (i == _activeAnimationIndex)
                {
                    _stateMixer.SetInputWeight(i, 1f - t);
                }
                else if (i == _targetAnimationIndex)
                {
                    _stateMixer.SetInputWeight(i, t);
                }
                else
                {
                    _stateMixer.SetInputWeight(i, 0f);
                }
            }

            ApplyClipSpeeds(activeIndex: _activeAnimationIndex, targetIndex: _targetAnimationIndex);

            if (t >= 1f)
            {
                _isTransitioning = false;
                _activeAnimationIndex = _targetAnimationIndex;
                _transitionTime = 0f;

                for (int i = 0; i < count; i++)
                {
                    _stateMixer.SetInputWeight(i, (i == _activeAnimationIndex) ? 1f : 0f);
                }

                ApplyClipSpeeds(activeIndex: _activeAnimationIndex, targetIndex: _activeAnimationIndex);
            }
        }
    }

    private void ResetClipTime(int index)
    {
        if (_clipPlayables == null) return;
        if (index < 0 || index >= _clipPlayables.Length) return;

        var p = _clipPlayables[index];
        if (!p.IsValid()) return;

        p.SetTime(0.0);
    }

    private void ApplyClipSpeeds(int activeIndex, int targetIndex)
    {
        if (_clipPlayables == null) return;

        for (int i = 0; i < _clipPlayables.Length; i++)
        {
            var p = _clipPlayables[i];
            if (!p.IsValid()) continue;

            bool shouldPlay = (i == activeIndex) || (i == targetIndex);
            p.SetSpeed(shouldPlay ? 1.0 : 0.0);
        }
    }

    private int GetCurrentAnimationIndex()
    {
        if (animations == null || animations.Length == 0) return 0;

        if (!string.IsNullOrEmpty(_currentAnimationKey) &&
            _keyToIndex.TryGetValue(_currentAnimationKey, out int idx))
        {
            if (idx >= 0 && idx < animations.Length) return idx;
        }

        return 0;
    }

    private float ComputeBodyKey01()
    {
        if (vrmToController == null) return 0f;
        return Mathf.Clamp01(vrmToController.bodyKey / 100f);
    }

    private bool IsMotionScaleEnabledForCurrentAnimation()
    {
        if (animations == null || animations.Length == 0) return false;

        int idx = GetCurrentAnimationIndex();
        if (idx < 0 || idx >= animations.Length) return false;

        var entry = animations[idx];
        if (entry == null) return false;

        return entry.referencePoseMode != ReferencePoseMode.None;
    }

    private bool IsFootPlantEnabledForCurrentAnimation()
    {
        if (animations == null || animations.Length == 0) return false;

        int idx = GetCurrentAnimationIndex();
        if (idx < 0 || idx >= animations.Length) return false;

        var entry = animations[idx];
        if (entry == null) return false;

        return entry.useIK;
    }

    private void UpdatePerBoneReferencePoseIfNeeded()
    {
        if (_motionBoneTransforms == null || !_motionRefRotations.IsCreated) return;
        if (animations == null || animations.Length == 0) return;

        int idx = GetCurrentAnimationIndex();
        if (idx == _currentRefAnimationIndex) return;

        ResampleReferencePoseForAnimation(idx);
        _currentRefAnimationIndex = idx;
    }

    private void ResampleReferencePoseForAnimation(int animIndex)
    {
        if (_motionBoneTransforms == null || !_motionRefRotations.IsCreated) return;
        if (animations == null || animIndex < 0 || animIndex >= animations.Length) return;

        var entry = animations[animIndex];
        if (entry == null) return;

        AnimationClip refClip = null;

        switch (entry.referencePoseMode)
        {
            case ReferencePoseMode.TPose:
                refClip = tposeClip;
                break;
            case ReferencePoseMode.FirstFrame:
                refClip = entry.clip;
                break;
            case ReferencePoseMode.None:
                return;
        }

        if (refClip == null) return;

        int n = _motionBoneTransforms.Length;
        var backupRot = new Quaternion[n];
        var backupPos = new Vector3[n];

        for (int i = 0; i < n; i++)
        {
            var t = _motionBoneTransforms[i];
            if (t != null)
            {
                backupRot[i] = t.localRotation;
                backupPos[i] = t.localPosition;
            }
            else
            {
                backupRot[i] = Quaternion.identity;
                backupPos[i] = Vector3.zero;
            }
        }

        Quaternion hipsRotBak = (_hipsT != null) ? _hipsT.localRotation : Quaternion.identity;
        Vector3 hipsPosBak = (_hipsT != null) ? _hipsT.localPosition : Vector3.zero;

        Quaternion lFootRotBak = (_lFootT != null) ? _lFootT.localRotation : Quaternion.identity;
        Vector3 lFootPosBak = (_lFootT != null) ? _lFootT.localPosition : Vector3.zero;

        Quaternion rFootRotBak = (_rFootT != null) ? _rFootT.localRotation : Quaternion.identity;
        Vector3 rFootPosBak = (_rFootT != null) ? _rFootT.localPosition : Vector3.zero;

        refClip.SampleAnimation(_animator.gameObject, 0f);

        for (int i = 0; i < n; i++)
        {
            var t = _motionBoneTransforms[i];
            if (t != null)
            {
                _motionRefRotations[i] = t.localRotation;
            }
        }

        if (_rootTransform != null && _lFootT != null && _rFootT != null)
        {
            _refLeftFootLocal = _rootTransform.InverseTransformPoint(_lFootT.position);
            _refRightFootLocal = _rootTransform.InverseTransformPoint(_rFootT.position);
        }

        for (int i = 0; i < n; i++)
        {
            var t = _motionBoneTransforms[i];
            if (t != null)
            {
                t.localRotation = backupRot[i];
                t.localPosition = backupPos[i];
            }
        }

        if (_hipsT != null)
        {
            _hipsT.localRotation = hipsRotBak;
            _hipsT.localPosition = hipsPosBak;
        }

        if (_lFootT != null)
        {
            _lFootT.localRotation = lFootRotBak;
            _lFootT.localPosition = lFootPosBak;
        }

        if (_rFootT != null)
        {
            _rFootT.localRotation = rFootRotBak;
            _rFootT.localPosition = rFootPosBak;
        }
    }

    struct PerBoneMotionScaleJob : IAnimationJob
    {
        public NativeArray<TransformStreamHandle> Handles;
        public NativeArray<Quaternion> RefLocalRotations;
        public NativeArray<float> PerBoneFactor;
        public float BodyKey01;

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            float bk01 = Mathf.Clamp01(BodyKey01);

            for (int i = 0; i < Handles.Length; i++)
            {
                var h = Handles[i];
                if (!h.IsValid(stream)) continue;

                var refRot = RefLocalRotations[i];
                var curRot = h.GetLocalRotation(stream);

                float factor = PerBoneFactor[i];
                float w = 1.0f - bk01 * factor;
                w = Mathf.Clamp01(w);

                var blended = Quaternion.Slerp(refRot, curRot, w);
                h.SetLocalRotation(stream, blended);
            }
        }
    }

    struct FootPlantJob : IAnimationJob
    {
        public int Enabled;
        public float BodyKey01;
        public float ContactHeight;
        public float LegShrinkFactor;

        public TransformStreamHandle Root;
        public TransformStreamHandle Hips;

        public TransformStreamHandle LUpper;
        public TransformStreamHandle LLower;
        public TransformStreamHandle LFoot;

        public TransformStreamHandle RUpper;
        public TransformStreamHandle RLower;
        public TransformStreamHandle RFoot;

        public Vector3 RefLeftFootLocal;
        public Vector3 RefRightFootLocal;

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            if (Enabled == 0) return;

            if (!Root.IsValid(stream) || !Hips.IsValid(stream)) return;
            if (!LUpper.IsValid(stream) || !LLower.IsValid(stream) || !LFoot.IsValid(stream)) return;
            if (!RUpper.IsValid(stream) || !RLower.IsValid(stream) || !RFoot.IsValid(stream)) return;

            var human = stream.AsHuman();

            float bk01 = Mathf.Clamp01(BodyKey01);

            Vector3 rootPos = Root.GetPosition(stream);
            Quaternion rootRot = Root.GetRotation(stream);
            Quaternion rootInv = Quaternion.Inverse(rootRot);

            Vector3 lFootPos0 = LFoot.GetPosition(stream);
            Vector3 rFootPos0 = RFoot.GetPosition(stream);

            float lc0 = ComputeContact01(lFootPos0.y, ContactHeight);
            float rc0 = ComputeContact01(rFootPos0.y, ContactHeight);
            float cMax0 = Mathf.Max(lc0, rc0);

            float minFootY0 = Mathf.Min(lFootPos0.y, rFootPos0.y);
            float pelvisDown = -minFootY0 * cMax0;

            Vector3 hipsPos = Hips.GetPosition(stream);
            hipsPos.y += pelvisDown;
            Hips.SetPosition(stream, hipsPos);

            Vector3 lFootPos = LFoot.GetPosition(stream);
            Vector3 rFootPos = RFoot.GetPosition(stream);

            float lc = ComputeContact01(lFootPos.y, ContactHeight);
            float rc = ComputeContact01(rFootPos.y, ContactHeight);

            float wLeg = Mathf.Clamp01(1f - bk01 * LegShrinkFactor);

            Vector3 lCurLocal = rootInv * (lFootPos - rootPos);
            Vector3 rCurLocal = rootInv * (rFootPos - rootPos);

            Vector3 lTarLocal = Vector3.Lerp(RefLeftFootLocal, lCurLocal, wLeg);
            Vector3 rTarLocal = Vector3.Lerp(RefRightFootLocal, rCurLocal, wLeg);

            Vector3 lTarget = rootPos + rootRot * lTarLocal;
            Vector3 rTarget = rootPos + rootRot * rTarLocal;

            lTarget.y = Mathf.Lerp(lFootPos.y, 0f, lc);
            rTarget.y = Mathf.Lerp(rFootPos.y, 0f, rc);

            ApplyHumanoidFootGoal(ref human, AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, lTarget, lc, LLower.GetPosition(stream));
            ApplyHumanoidFootGoal(ref human, AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, rTarget, rc, RLower.GetPosition(stream));
        }

        private static float ComputeContact01(float footY, float h)
        {
            float t = Mathf.Clamp01(footY / h);
            float s = t * t * (3f - 2f * t);
            return 1f - s;
        }

        private static void ApplyHumanoidFootGoal(
            ref AnimationHumanStream human,
            AvatarIKGoal goal,
            AvatarIKHint hint,
            Vector3 goalWorldPos,
            float contact01,
            Vector3 currentKneeWorldPos)
        {
            float w = Mathf.Clamp01(contact01);

            human.SetGoalWeightPosition(goal, w);
            human.SetGoalPosition(goal, goalWorldPos);

            float wh = w * 0.25f;
            human.SetHintWeightPosition(hint, wh);
            human.SetHintPosition(hint, currentKneeWorldPos);
        }
    }

    private float GetTransitionDurationTo(int targetIndex)
    {

        if (animations != null &&
            targetIndex >= 0 && targetIndex < animations.Length &&
            animations[targetIndex] != null)
        {
            var d = animations[targetIndex].transitionDuration;
            return Mathf.Max(0f, d);
        }

        return 0;
    }

}
