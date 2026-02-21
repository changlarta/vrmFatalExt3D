// BodyMotion.cs
using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;

public sealed class BodyMotion : IDisposable
{
    private bool _refCaptured;

    private BodyHandles _boundHandles;

    // 明示的なキャラクタールート参照
    private Transform _bodyRootRef;

    // bones & refs
    public RigBones Bones;
    private float _refLegLengthL = 0f;
    private float _refLegLengthR = 0f;
    private Vector3 _refBodyRootLocalPos;

    // current params (0..1)
    private float _bodyAmount;
    private float _faceAmount;

    /// <summary>Body/Face のキー値をセット（0〜1）</summary>
    public void SetJobParams(float bodyKey01, float faceKey01)
    {
        _bodyAmount = bodyKey01;
        _faceAmount = faceKey01;
    }

    /// <summary>Humanoid のボーン構造をキャプチャし、スケーリング計算用の参照値を保存する。</summary>
    public bool TryCaptureReferences(Animator animator, Transform bodyRoot, out RigBones bones)
    {
        bones = default;
        if (!animator || animator.avatar == null || !animator.isHuman) return false;
        if (bodyRoot == null) return false;

        _bodyRootRef = bodyRoot;

        var hipsT = animator.GetBoneTransform(HumanBodyBones.Hips);
        var headT = animator.GetBoneTransform(HumanBodyBones.Head);
        var neckT = animator.GetBoneTransform(HumanBodyBones.Neck);
        var spineT = animator.GetBoneTransform(HumanBodyBones.Spine);
        var chestT = animator.GetBoneTransform(HumanBodyBones.Chest);
        var upperChestT = animator.GetBoneTransform(HumanBodyBones.UpperChest);

        var lShoulderT = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
        var rShoulderT = animator.GetBoneTransform(HumanBodyBones.RightShoulder);

        var lUpperArmT = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var rUpperArmT = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var lLowerArmT = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        var rLowerArmT = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        var lHandT = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var rHandT = animator.GetBoneTransform(HumanBodyBones.RightHand);

        var lLegT = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        var rLegT = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        var lShinT = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        var rShinT = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        var lFootT = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rFootT = animator.GetBoneTransform(HumanBodyBones.RightFoot);

        bones = new RigBones
        {
            Core = new CoreNodes
            {
                Hips = BoneNode.From(hipsT),
                Head = BoneNode.From(headT),
                Neck = BoneNode.From(neckT),
                Chest = BoneNode.From(chestT),
                UpperChest = BoneNode.From(upperChestT)
            },
            Left = new SideNodes
            {
                Shoulder = BoneNode.From(lShoulderT),
                UpperArm = BoneNode.From(lUpperArmT),
                LowerArm = BoneNode.From(lLowerArmT),
                Hand = BoneNode.From(lHandT),
                UpperLeg = BoneNode.From(lLegT),
                LowerLeg = BoneNode.From(lShinT),
                Foot = BoneNode.From(lFootT),
                Ankle = BoneNode.From(lFootT)
            },
            Right = new SideNodes
            {
                Shoulder = BoneNode.From(rShoulderT),
                UpperArm = BoneNode.From(rUpperArmT),
                LowerArm = BoneNode.From(rLowerArmT),
                Hand = BoneNode.From(rHandT),
                UpperLeg = BoneNode.From(rLegT),
                LowerLeg = BoneNode.From(rShinT),
                Foot = BoneNode.From(rFootT),
                Ankle = BoneNode.From(rFootT)
            }
        };

        Bones = bones;

        if (bones.Core.Hips.T != null && bones.Left.Ankle.T != null)
            _refLegLengthL = Vector3.Distance(bones.Core.Hips.T.position, bones.Left.Ankle.T.position);
        if (bones.Core.Hips.T != null && bones.Right.Ankle.T != null)
            _refLegLengthR = Vector3.Distance(bones.Core.Hips.T.position, bones.Right.Ankle.T.position);

        _refBodyRootLocalPos = bodyRoot.localPosition;
        return true;
    }

    /// <summary>
    /// PlayableGraph を構築する側（HumanoidAnimationController）から呼ばれる。
    /// Animator のストリームに対するジョブを生成して返す。
    /// （初回だけボーン参照をキャプチャし、2回目以降は再利用）
    /// </summary>
    public bool TryCreateJob(Animator animator, Transform bodyRoot, out RotationScaleJobRef job)
    {
        if (!_refCaptured)
        {
            if (!SetupRotationScaleJobAndCaptureRefs(animator, bodyRoot, out job))
            {
                return false;
            }
            _refCaptured = true;
            return true;
        }

        _bodyRootRef = bodyRoot != null ? bodyRoot : _bodyRootRef;

        job = RotationScaleJobRef.Create(
            _boundHandles,
            _bodyAmount,
            _faceAmount
        );
        return true;
    }

    /// <summary>既存ジョブデータに Body/Face のパラメータを反映する。</summary>
    public void ApplyJobParams(ref RotationScaleJobRef job)
    {
        job.BodyAmount = _bodyAmount;
        job.faceAmount = _faceAmount;
    }

    /// <summary>体型スケールと root Y 補正を適用</summary>
    public void ApplyScalesAndPositions(float height01, float bodyKey01, Transform bodyRoot)
    {
        var b = Bones;
        float height = height01;
        float body = bodyKey01;

        float headScaleFactor = Mathf.LerpUnclamped(1.05f, 1.45f, height);
        float neckScaleFactor = Mathf.LerpUnclamped(0.92f, 0.88f, height);
        float chestScaleFactor = Mathf.LerpUnclamped(1f, 0.98f, height);
        float upperChestScaleFactor = Mathf.LerpUnclamped(1f, 0.98f, height);
        float shoulderScaleFactor = Mathf.LerpUnclamped(0.98f, 0.98f, height);
        float upperArmScaleFactor = Mathf.LerpUnclamped(0.93f, 0.9f, height);
        float lowerArmScaleFactor = Mathf.LerpUnclamped(0.93f, 0.9f, height);
        float handScaleFactor = Mathf.LerpUnclamped(1f, 1f, height);
        float upperLegTarget = Mathf.LerpUnclamped(0.82f, 0.7f, height);
        float upperLegTarget2 = Mathf.LerpUnclamped(0.9f, 0.9f, height);
        float lowerLegTarget = Mathf.LerpUnclamped(1f, 1f, height);
        float lowerLegTarget2 = Mathf.LerpUnclamped(1f, 1f, height);
        float footScaleFactor = Mathf.LerpUnclamped(1f, 0.9f, height);

        float shoulderPositionFactor = Mathf.LerpUnclamped(1, 4.5f + height * 2, body);
        float upperLegPositionFactor = Mathf.LerpUnclamped(1, 2f, body);

        if (b.Core.Head.T != null)
            b.Core.Head.scale = new Vector3(b.Core.Head.refScale.x * headScaleFactor, b.Core.Head.refScale.y * headScaleFactor, b.Core.Head.refScale.z * headScaleFactor);
        if (b.Core.Neck.T != null)
            b.Core.Neck.scale = new Vector3(b.Core.Neck.refScale.x * neckScaleFactor, b.Core.Neck.refScale.y * neckScaleFactor, b.Core.Neck.refScale.z * neckScaleFactor);
        if (b.Core.Chest.T != null)
            b.Core.Chest.scale = new Vector3(b.Core.Chest.refScale.x * chestScaleFactor, b.Core.Chest.refScale.y * chestScaleFactor, b.Core.Chest.refScale.z * chestScaleFactor);
        if (b.Core.UpperChest.T != null)
            b.Core.UpperChest.scale = new Vector3(b.Core.UpperChest.refScale.x * upperChestScaleFactor, b.Core.UpperChest.refScale.y * upperChestScaleFactor, b.Core.UpperChest.refScale.z * upperChestScaleFactor);

        if (b.Left.Shoulder.T != null)
            b.Left.Shoulder.scale = new Vector3(b.Left.Shoulder.refScale.x * shoulderScaleFactor, b.Left.Shoulder.refScale.y * shoulderScaleFactor, b.Left.Shoulder.refScale.z * shoulderScaleFactor);
        if (b.Right.Shoulder.T != null)
            b.Right.Shoulder.scale = new Vector3(b.Right.Shoulder.refScale.x * shoulderScaleFactor, b.Right.Shoulder.refScale.y * shoulderScaleFactor, b.Right.Shoulder.refScale.z * shoulderScaleFactor);

        if (b.Left.UpperArm.T != null)
            b.Left.UpperArm.scale = new Vector3(b.Left.UpperArm.refScale.x * upperArmScaleFactor, b.Left.UpperArm.refScale.y * upperArmScaleFactor, b.Left.UpperArm.refScale.z * upperArmScaleFactor);
        if (b.Right.UpperArm.T != null)
            b.Right.UpperArm.scale = new Vector3(b.Right.UpperArm.refScale.x * upperArmScaleFactor, b.Right.UpperArm.refScale.y * upperArmScaleFactor, b.Right.UpperArm.refScale.z * upperArmScaleFactor);

        if (b.Left.LowerArm.T != null)
            b.Left.LowerArm.scale = new Vector3(b.Left.LowerArm.refScale.x * lowerArmScaleFactor, b.Left.LowerArm.refScale.y * lowerArmScaleFactor, b.Left.LowerArm.refScale.z * lowerArmScaleFactor);
        if (b.Right.LowerArm.T != null)
            b.Right.LowerArm.scale = new Vector3(b.Right.LowerArm.refScale.x * lowerArmScaleFactor, b.Right.LowerArm.refScale.y * lowerArmScaleFactor, b.Right.LowerArm.refScale.z * lowerArmScaleFactor);

        if (b.Left.Hand.T != null)
            b.Left.Hand.scale = new Vector3(b.Left.Hand.refScale.x * handScaleFactor, b.Left.Hand.refScale.y * handScaleFactor, b.Left.Hand.refScale.z * handScaleFactor);
        if (b.Right.Hand.T != null)
            b.Right.Hand.scale = new Vector3(b.Right.Hand.refScale.x * handScaleFactor, b.Right.Hand.refScale.y * handScaleFactor, b.Right.Hand.refScale.z * handScaleFactor);

        if (b.Left.UpperLeg.T != null)
            b.Left.UpperLeg.scale = new Vector3(b.Left.UpperLeg.refScale.x * upperLegTarget2, b.Left.UpperLeg.refScale.y * upperLegTarget, b.Left.UpperLeg.refScale.z * upperLegTarget2);
        if (b.Right.UpperLeg.T != null)
            b.Right.UpperLeg.scale = new Vector3(b.Right.UpperLeg.refScale.x * upperLegTarget2, b.Right.UpperLeg.refScale.y * upperLegTarget, b.Right.UpperLeg.refScale.z * upperLegTarget2);

        if (b.Left.LowerLeg.T != null)
            b.Left.LowerLeg.scale = new Vector3(b.Left.LowerLeg.refScale.x * lowerLegTarget2, b.Left.LowerLeg.refScale.y * lowerLegTarget, b.Left.LowerLeg.refScale.z * lowerLegTarget2);
        if (b.Right.LowerLeg.T != null)
            b.Right.LowerLeg.scale = new Vector3(b.Right.LowerLeg.refScale.x * lowerLegTarget2, b.Right.LowerLeg.refScale.y * lowerLegTarget, b.Right.LowerLeg.refScale.z * lowerLegTarget2);

        if (b.Left.Shoulder.T != null)
            b.Left.Shoulder.position = new Vector3(b.Left.Shoulder.refLocalPos.x * shoulderPositionFactor, b.Left.Shoulder.refLocalPos.y + b.Left.Shoulder.refLocalPos.y * (1 - shoulderPositionFactor) * 0.08f, b.Left.Shoulder.refLocalPos.z);
        if (b.Right.Shoulder.T != null)
            b.Right.Shoulder.position = new Vector3(b.Right.Shoulder.refLocalPos.x * shoulderPositionFactor, b.Right.Shoulder.refLocalPos.y + b.Right.Shoulder.refLocalPos.y * (1 - shoulderPositionFactor) * 0.08f, b.Right.Shoulder.refLocalPos.z);

        if (b.Left.Foot.T != null)
            b.Left.Foot.scale = new Vector3(b.Left.Foot.refScale.x, b.Left.Foot.refScale.y * footScaleFactor, b.Left.Foot.refScale.z * footScaleFactor);
        if (b.Right.Foot.T != null)
            b.Right.Foot.scale = new Vector3(b.Right.Foot.refScale.x, b.Right.Foot.refScale.y * footScaleFactor, b.Right.Foot.refScale.z * footScaleFactor);

        if (b.Left.UpperLeg.T != null)
            b.Left.UpperLeg.position = new Vector3(b.Left.UpperLeg.refLocalPos.x * upperLegPositionFactor, b.Left.UpperLeg.refLocalPos.y, b.Left.UpperLeg.refLocalPos.z);
        if (b.Right.UpperLeg.T != null)
            b.Right.UpperLeg.position = new Vector3(b.Right.UpperLeg.refLocalPos.x * upperLegPositionFactor, b.Right.UpperLeg.refLocalPos.y, b.Right.UpperLeg.refLocalPos.z);

        // root Y 補正
        var target = bodyRoot != null ? bodyRoot : _bodyRootRef;
        if (target != null)
        {
            target.localPosition = _refBodyRootLocalPos + new Vector3(0f, -height * 0.06f, 0f);
            var scale = 1 - height * 0.18f;
            target.localScale = new Vector3(scale, scale, scale);
        }
    }

    /// <summary>ジョブ用の BodyHandles を構築</summary>
    private bool SetupRotationScaleJobAndCaptureRefs(Animator animator, Transform bodyRoot, out RotationScaleJobRef job)
    {
        job = default;
        if (animator == null || animator.avatar == null || !animator.isHuman) return false;
        if (bodyRoot == null) return false;

        _bodyRootRef = bodyRoot;

        var hipsT = animator.GetBoneTransform(HumanBodyBones.Hips);
        var headT = animator.GetBoneTransform(HumanBodyBones.Head);
        var neckT = animator.GetBoneTransform(HumanBodyBones.Neck);
        var spineT = animator.GetBoneTransform(HumanBodyBones.Spine);
        var chestT = animator.GetBoneTransform(HumanBodyBones.Chest);
        var upperChestT = animator.GetBoneTransform(HumanBodyBones.UpperChest);

        var lShoulderT = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
        var rShoulderT = animator.GetBoneTransform(HumanBodyBones.RightShoulder);

        var lUpperArmT = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var rUpperArmT = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var lLowerArmT = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        var rLowerArmT = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        var lHandT = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var rHandT = animator.GetBoneTransform(HumanBodyBones.RightHand);

        var lLegT = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        var rLegT = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        var lShinT = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        var rShinT = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        var lFootT = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rFootT = animator.GetBoneTransform(HumanBodyBones.RightFoot);

        _boundHandles = new BodyHandles
        {
            Core = new CoreHandles
            {
                Spine = spineT != null ? animator.BindStreamTransform(spineT) : default,
                Chest = chestT != null ? animator.BindStreamTransform(chestT) : default,
                UpperChest = upperChestT != null ? animator.BindStreamTransform(upperChestT) : default,
                Neck = neckT != null ? animator.BindStreamTransform(neckT) : default,
                Head = headT != null ? animator.BindStreamTransform(headT) : default,
                Hips = hipsT != null ? animator.BindStreamTransform(hipsT) : default,
            },
            Left = new SideHandles
            {
                Shoulder = lShoulderT != null ? animator.BindStreamTransform(lShoulderT) : default,
                UpperArm = lUpperArmT != null ? animator.BindStreamTransform(lUpperArmT) : default,
                LowerArm = lLowerArmT != null ? animator.BindStreamTransform(lLowerArmT) : default,
                Hand = lHandT != null ? animator.BindStreamTransform(lHandT) : default,
                UpperLeg = lLegT != null ? animator.BindStreamTransform(lLegT) : default,
                LowerLeg = lShinT != null ? animator.BindStreamTransform(lShinT) : default,
                Foot = lFootT != null ? animator.BindStreamTransform(lFootT) : default
            },
            Right = new SideHandles
            {
                Shoulder = rShoulderT != null ? animator.BindStreamTransform(rShoulderT) : default,
                UpperArm = rUpperArmT != null ? animator.BindStreamTransform(rUpperArmT) : default,
                LowerArm = rLowerArmT != null ? animator.BindStreamTransform(rLowerArmT) : default,
                Hand = rHandT != null ? animator.BindStreamTransform(rHandT) : default,
                UpperLeg = rLegT != null ? animator.BindStreamTransform(rLegT) : default,
                LowerLeg = rShinT != null ? animator.BindStreamTransform(rShinT) : default,
                Foot = rFootT != null ? animator.BindStreamTransform(rFootT) : default
            }
        };

        _refBodyRootLocalPos = bodyRoot.localPosition;

        job = RotationScaleJobRef.Create(
            _boundHandles,
            _bodyAmount,
            _faceAmount
        );
        return true;
    }

    public void Dispose()
    {
        // NativeArray などは使わなくなったので破棄処理も不要
    }

    // ────────────────────────────────────────────────────────────────────────
    // Data structures & Job
    // ────────────────────────────────────────────────────────────────────────

    internal struct SideHandles
    {
        public TransformStreamHandle Shoulder;
        public TransformStreamHandle UpperArm;
        public TransformStreamHandle LowerArm;
        public TransformStreamHandle Hand;
        public TransformStreamHandle UpperLeg;
        public TransformStreamHandle LowerLeg;
        public TransformStreamHandle Foot;
    }

    internal struct CoreHandles
    {
        public TransformStreamHandle Spine;
        public TransformStreamHandle Chest;
        public TransformStreamHandle UpperChest;
        public TransformStreamHandle Neck;
        public TransformStreamHandle Head;
        public TransformStreamHandle Hips;
    }

    internal struct BodyHandles
    {
        public CoreHandles Core;
        public SideHandles Left;
        public SideHandles Right;
    }

    public struct BoneNode
    {
        public Transform T;
        public Vector3 refScale;
        public Vector3 refLocalPos;

        public Vector3 position
        {
            get => T != null ? T.localPosition : Vector3.zero;
            set { if (T != null) T.localPosition = value; }
        }

        public Vector3 scale
        {
            get => T != null ? T.localScale : Vector3.one;
            set { if (T != null) T.localScale = value; }
        }

        public static BoneNode From(Transform t)
        {
            return new BoneNode
            {
                T = t,
                refScale = t != null ? t.localScale : Vector3.one,
                refLocalPos = t != null ? t.localPosition : Vector3.one
            };
        }
    }

    public struct SideNodes
    {
        public BoneNode Shoulder;
        public BoneNode UpperArm;
        public BoneNode LowerArm;
        public BoneNode Hand;
        public BoneNode UpperLeg;
        public BoneNode LowerLeg;
        public BoneNode Foot;
        public BoneNode Ankle;
    }

    public struct CoreNodes
    {
        public BoneNode Hips;
        public BoneNode Head;
        public BoneNode Neck;
        public BoneNode Chest;
        public BoneNode UpperChest;
    }

    public struct RigBones
    {
        public CoreNodes Core;
        public SideNodes Left;
        public SideNodes Right;
    }

    public struct RotationScaleJobRef : IAnimationJob
    {
        // 回転スケールは廃止し、BodyHandles + パラメータだけを持つ
        private BodyHandles _bound;

        public float BodyAmount;
        public float faceAmount;

        private RotationScaleJobRef(
            BodyHandles bound,
            float bodyAmount,
            float faceAmount)
        {
            _bound = bound;
            BodyAmount = bodyAmount;
            this.faceAmount = faceAmount;
        }

        internal static RotationScaleJobRef Create(
            BodyHandles bound,
            float bodyAmount,
            float faceAmount)
        {
            return new RotationScaleJobRef(bound, bodyAmount, faceAmount);
        }

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            // ここからは BodyAmount / faceAmount を使った追加回転のみ

            if (_bound.Core.Hips.IsValid(stream))
            {
                var rUp = _bound.Core.UpperChest.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(7f * BodyAmount, 0f, 0f);
                _bound.Core.UpperChest.SetLocalRotation(stream, rUp * qAdd);
            }

            if (_bound.Core.Spine.IsValid(stream))
            {
                var rUp = _bound.Core.Spine.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(-2f * BodyAmount, 0f, 0f);
                _bound.Core.Spine.SetLocalRotation(stream, rUp * qAdd);
            }
            if (_bound.Core.Chest.IsValid(stream))
            {
                var rUp = _bound.Core.Chest.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(5f * BodyAmount, 0f, 0f);
                _bound.Core.Chest.SetLocalRotation(stream, rUp * qAdd);
            }
            if (_bound.Core.UpperChest.IsValid(stream))
            {
                var rUp = _bound.Core.UpperChest.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(-13f * BodyAmount, 0f, 0f);
                _bound.Core.UpperChest.SetLocalRotation(stream, rUp * qAdd);
            }
            if (_bound.Core.Neck.IsValid(stream))
            {
                var rUp = _bound.Core.Neck.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(faceAmount * BodyAmount * -12f, 0f, 0f);
                _bound.Core.Neck.SetLocalRotation(stream, rUp * qAdd);
            }
            if (_bound.Core.Head.IsValid(stream))
            {
                var rUp = _bound.Core.Head.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(faceAmount * BodyAmount * -15f, 0f, 0f);
                _bound.Core.Head.SetLocalRotation(stream, rUp * qAdd);
            }

            // 腕
            if (_bound.Left.Shoulder.IsValid(stream))
            {
                var rUp = _bound.Left.Shoulder.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(16f * BodyAmount, 0f, 0f);
                _bound.Left.Shoulder.SetLocalRotation(stream, rUp * qAdd);
            }
            if (_bound.Right.Shoulder.IsValid(stream))
            {
                var rUp = _bound.Right.Shoulder.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(16f * BodyAmount, 0f, 0f);
                _bound.Right.Shoulder.SetLocalRotation(stream, rUp * qAdd);
            }

            if (_bound.Left.UpperArm.IsValid(stream))
            {
                var rUp = _bound.Left.UpperArm.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(20 * BodyAmount, 0f, 0f);
                _bound.Left.UpperArm.SetLocalRotation(stream, rUp * qAdd);
            }
            if (_bound.Right.UpperArm.IsValid(stream))
            {
                var rUp = _bound.Right.UpperArm.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(20 * BodyAmount, 0f, 0f);
                _bound.Right.UpperArm.SetLocalRotation(stream, rUp * qAdd);
            }

            if (_bound.Left.LowerArm.IsValid(stream))
            {
                var rUp = _bound.Left.LowerArm.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(4 * BodyAmount, 0f, 0f);
                _bound.Left.LowerArm.SetLocalRotation(stream, rUp * qAdd);
            }
            if (_bound.Right.LowerArm.IsValid(stream))
            {
                var rUp = _bound.Right.LowerArm.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(4 * BodyAmount, 0f, 0f);
                _bound.Right.LowerArm.SetLocalRotation(stream, rUp * qAdd);
            }
            if (_bound.Left.Hand.IsValid(stream))
            {
                var rUp = _bound.Left.Hand.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(-25 * BodyAmount, 0f, 0f);
                _bound.Left.Hand.SetLocalRotation(stream, rUp * qAdd);
            }
            if (_bound.Right.Hand.IsValid(stream))
            {
                var rUp = _bound.Right.Hand.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(-25 * BodyAmount, 0f, 0f);
                _bound.Right.Hand.SetLocalRotation(stream, rUp * qAdd);
            }

            // 足
            if (_bound.Left.UpperLeg.IsValid(stream))
            {
                var rUp = _bound.Left.UpperLeg.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(0, 0 * BodyAmount, 0 * BodyAmount);
                _bound.Left.UpperLeg.SetLocalRotation(stream, rUp * qAdd);
            }
            if (_bound.Right.UpperLeg.IsValid(stream))
            {
                var rUp = _bound.Right.UpperLeg.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(0, -0 * BodyAmount, -0 * BodyAmount);
                _bound.Right.UpperLeg.SetLocalRotation(stream, rUp * qAdd);
            }

            if (_bound.Left.LowerLeg.IsValid(stream))
            {
                var rUp = _bound.Left.LowerLeg.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(0, 4 * BodyAmount, 4 * BodyAmount);
                _bound.Left.LowerLeg.SetLocalRotation(stream, rUp * qAdd);
            }
            if (_bound.Right.LowerLeg.IsValid(stream))
            {
                var rUp = _bound.Right.LowerLeg.GetLocalRotation(stream);
                var qAdd = Quaternion.Euler(0, -4 * BodyAmount, -4 * BodyAmount);
                _bound.Right.LowerLeg.SetLocalRotation(stream, rUp * qAdd);
            }
        }
    }
}
