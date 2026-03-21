// VrmExpressionController.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class VrmExpressionController : MonoBehaviour
{
    [Header("Links")]
    [SerializeField] private VrmToController vrmTo;

    [Header("Transition")]

    // ランタイム専用フラグ：インスペクターには出さない
    [HideInInspector] public bool enableBlink = false;
    [HideInInspector] public bool enableMouthLoop = false;

    public enum SavedExpression
    {
        Neutral = 0,
        Neutral2,
        Neutral3,
        MouthA_BrowSorrow,
        JoySoft,
        AngrySharp,
        Sorrow,
        SurpriseOpen,
        Shock,
        Shock2,
        Shock3,
        FoodWait1,
        FoodWait2,
        FoodWait3,
        FoodEating1,
        FoodEating2,
        FoodEating3,
        Tired,
        joggingTired,
        blush_tap_face,
        blush_swaip_face,
        blush_tap,
        blush_swaip,
    }

    [Header("Debug: All Shapes Sliders (exclude Fcl_ALL_* and Fcl_HA_*)")]
    public bool debugAllShapesOverride = false;

    [Serializable]
    public class ShapeSlider
    {
        public string shapeName;
        [Range(0f, 1f)] public float value01;
    }

    public List<ShapeSlider> allShapeSliders = new List<ShapeSlider>();

    private static readonly string[] KnownShapeNames =
    {
        "Basis",
        "Fcl_ALL_Angry","Fcl_ALL_Fun","Fcl_ALL_Joy","Fcl_ALL_Neutral","Fcl_ALL_Sorrow","Fcl_ALL_Surprised",
        "Fcl_BRW_Angry","Fcl_BRW_Fun","Fcl_BRW_Sorrow","Fcl_BRW_Surprised",
        "Fcl_EYE_Angry","Fcl_EYE_Close","Fcl_EYE_Close_L","Fcl_EYE_Close_R","Fcl_EYE_Fun","Fcl_EYE_Highlight_Hide",
        "Fcl_EYE_Iris_Hide","Fcl_EYE_Joy","Fcl_EYE_Joy_L","Fcl_EYE_Joy_R","Fcl_EYE_Natural","Fcl_EYE_Sorrow",
        "Fcl_EYE_Spread","Fcl_EYE_Surprised",
        "Fcl_MTH_A","Fcl_MTH_Angry","Fcl_MTH_Close","Fcl_MTH_Down","Fcl_MTH_E","Fcl_MTH_Fun","Fcl_MTH_I",
        "Fcl_MTH_Joy","Fcl_MTH_Large","Fcl_MTH_Neutral","Fcl_MTH_O","Fcl_MTH_Small","Fcl_MTH_Up",
        "Fcl_HA_Fung1","Fcl_HA_Fung1_Low","Fcl_HA_Fung1_Up","Fcl_HA_Fung2","Fcl_HA_Fung2_Low","Fcl_HA_Fung2_Up",
        "Fcl_HA_Fung3","Fcl_HA_Fung3_Low","Fcl_HA_Fung3_Up","Fcl_HA_Hide","Fcl_HA_Short","Fcl_HA_Short_Low","Fcl_HA_Short_Up",
        "Fcl_MTH_SkinFung","Fcl_MTH_SkinFung_L","Fcl_MTH_SkinFung_R"
    };

    private readonly Dictionary<string, List<(SkinnedMeshRenderer smr, int index)>> _nameToPairs = new();
    private readonly List<(SkinnedMeshRenderer smr, int index)> _allPairs = new();

    private Coroutine _transitionRoutine;
    private SavedExpression _lastAppliedPreset;
    private bool _cacheBuilt;
    private bool _prevDebugOverride;

    // 「このコンポーネントが最後に確定させた表情」を保持（全 _allPairs を持つ）
    private Dictionary<(SkinnedMeshRenderer smr, int index), float> _holdTarget01;

    // ★追加：このコンポーネントが直近に SafeSet した値(0..1)を保持
    // これを次の遷移開始点として使う
    private Dictionary<(SkinnedMeshRenderer smr, int index), float> _lastSet01;

    // ===== まばたき用 =====
    private Coroutine _blinkRoutine;
    private float _blink01; // 0〜1, 0=ベース, 1=最大閉じ

    private readonly List<(SkinnedMeshRenderer smr, int index)> _eyeClosePairs = new();
    private readonly List<(SkinnedMeshRenderer smr, int index)> _eyeJoyPairs = new();
    private readonly HashSet<(SkinnedMeshRenderer smr, int index)> _eyeCloseSet = new();
    private readonly HashSet<(SkinnedMeshRenderer smr, int index)> _eyeJoySet = new();

    // ===== 口開閉用 =====
    private Coroutine _mouthRoutine;
    private float _mouth01; // 0〜1, 0=ベース, 1=最大開き

    private readonly List<(SkinnedMeshRenderer smr, int index)> _mouthOPairs = new();
    private readonly HashSet<(SkinnedMeshRenderer smr, int index)> _mouthOSet = new();

    private void Awake()
    {
        if (vrmTo == null) vrmTo = GetComponent<VrmToController>();

        _holdTarget01 = new Dictionary<(SkinnedMeshRenderer smr, int index), float>();
        _lastSet01 = new Dictionary<(SkinnedMeshRenderer smr, int index), float>();

        _lastAppliedPreset = GetCurrentPreset();
        _blink01 = 0f;
        _mouth01 = 0f;
    }

    private void Update()
    {
        if (vrmTo == null || !vrmTo.IsReady)
        {
            _cacheBuilt = false;
            StopBlink();
            StopMouth();
            return;
        }

        if (!_cacheBuilt)
        {
            BuildBlendShapeCache();
            BuildDebugSliders();
            _cacheBuilt = true;
            ApplyPresetInstant(GetCurrentPreset());
        }

        if (debugAllShapesOverride)
        {
            // デバッグ操作中は自動制御を無効化
            StopBlink();
            StopMouth();
            ApplyDebugSlidersEveryFrame();
            _prevDebugOverride = debugAllShapesOverride;
            return;
        }

        if (_prevDebugOverride)
        {
            ApplyPresetInstant(GetCurrentPreset());
        }

        var currentPreset = GetCurrentPreset();

        if (_lastAppliedPreset != currentPreset)
        {
            StartTransitionTo(currentPreset);
            _lastAppliedPreset = currentPreset;
        }
        else
        {
            ApplyHoldEveryFrame();
        }

        _prevDebugOverride = debugAllShapesOverride;

        // まばたき & 口開閉 コルーチンの開始 / 停止管理
        UpdateBlinkCoroutineState();
        UpdateMouthCoroutineState();
    }

    private SavedExpression GetCurrentPreset()
    {
        if (vrmTo == null) return SavedExpression.Neutral;
        return vrmTo.expressionPreset;
    }

    private void BuildBlendShapeCache()
    {
        _nameToPairs.Clear();
        _allPairs.Clear();
        _eyeClosePairs.Clear();
        _eyeJoyPairs.Clear();
        _eyeCloseSet.Clear();
        _eyeJoySet.Clear();
        _mouthOPairs.Clear();
        _mouthOSet.Clear();

        var smrs = vrmTo.VrmFaceSmrs;
        foreach (var smr in smrs)
        {
            var mesh = smr.sharedMesh;
            if (mesh == null) continue;

            for (int i = 0; i < mesh.blendShapeCount; ++i)
            {
                var name = mesh.GetBlendShapeName(i);
                if (string.IsNullOrEmpty(name)) continue;

                var key = (smr, i);
                _allPairs.Add(key);

                if (!_nameToPairs.TryGetValue(name, out var list))
                {
                    list = new List<(SkinnedMeshRenderer, int)>();
                    _nameToPairs[name] = list;
                }
                list.Add(key);
            }
        }

        // 目関連のキャッシュ
        if (_nameToPairs.TryGetValue("Fcl_EYE_Close", out var closeList))
        {
            _eyeClosePairs.AddRange(closeList);
            foreach (var p in closeList) _eyeCloseSet.Add(p);
        }

        if (_nameToPairs.TryGetValue("Fcl_EYE_Joy", out var joyList))
        {
            _eyeJoyPairs.AddRange(joyList);
            foreach (var p in joyList) _eyeJoySet.Add(p);
        }

        // 口開閉用キャッシュ (Fcl_MTH_O)
        // ※元コード通り（ここは今回の本題では触らない）
        if (_nameToPairs.TryGetValue("Fcl_MTH_A", out var mouthList))
        {
            _mouthOPairs.AddRange(mouthList);
            foreach (var p in mouthList) _mouthOSet.Add(p);
        }

        // _holdTarget01 を「全ペア持つ」形で初期化し直す
        _holdTarget01.Clear();
        foreach (var p in _allPairs) _holdTarget01[p] = 0f;

        // ★追加：_lastSet01 も全ペア持つ形で初期化
        _lastSet01.Clear();
        foreach (var p in _allPairs) _lastSet01[p] = 0f;
    }

    private void BuildDebugSliders()
    {
        allShapeSliders = KnownShapeNames
            .Where(n => !(n.StartsWith("Fcl_ALL_", StringComparison.Ordinal) || n.StartsWith("Fcl_HA_", StringComparison.Ordinal)))
            .Select(n => new ShapeSlider { shapeName = n, value01 = 0f })
            .ToList();
    }

    private readonly struct Op
    {
        public readonly string name;
        public readonly float w01;
        public Op(string n, float w01) { name = n; this.w01 = Mathf.Clamp01(w01); }
    }

    private readonly struct Recipe
    {
        public readonly Op[] ops;
        public readonly float? transitionOverrideSeconds;

        public Recipe(Op[] ops, float? transitionOverrideSeconds = null)
        {
            this.ops = ops ?? Array.Empty<Op>();
            this.transitionOverrideSeconds = transitionOverrideSeconds;
        }
    }

    private Recipe GetRecipe(SavedExpression preset)
    {
        switch (preset)
        {
            case SavedExpression.MouthA_BrowSorrow:
                return new Recipe(
                    new[] {
                        new Op("Fcl_MTH_A",0.8f),
                        new Op("Fcl_BRW_Sorrow",0.7f),
                    }
                );

            case SavedExpression.JoySoft:
                return new Recipe(
                    new[] {
                        new Op("Fcl_MTH_Joy",0.75f),
                        new Op("Fcl_EYE_Joy",0.70f),
                    }
                );

            case SavedExpression.AngrySharp:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Angry",0.80f),
                        new Op("Fcl_EYE_Angry",0.65f),
                        new Op("Fcl_MTH_Angry",0.40f),
                    }
                );

            case SavedExpression.Sorrow:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.80f),
                        new Op("Fcl_EYE_Sorrow",0.70f),
                        new Op("Fcl_MTH_Neutral",1.0f),
                    }
                );

            case SavedExpression.SurpriseOpen:
                return new Recipe(
                    new[] {
                        new Op("Fcl_MTH_O",0.85f),
                        new Op("Fcl_EYE_Surprised",0.85f),
                    }
                );

            case SavedExpression.Tired:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.4f),
                        new Op("Fcl_EYE_Joy",0.1f),
                        new Op("Fcl_EYE_Close",0.1f),
                        new Op("Fcl_MTH_Large",0.2f),
                    }
                );

            case SavedExpression.joggingTired:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.4f),
                        new Op("Fcl_EYE_Joy",1f),
                        new Op("Fcl_MTH_Large",0.2f),
                        new Op("Fcl_MTH_O",0.5f),
                    }
                );

            case SavedExpression.Neutral:
                return new Recipe(
                    new[] {
                        new Op("Fcl_MTH_Neutral",1f),
                        new Op("Fcl_EYE_Natural",1f),
                        new Op("Fcl_MTH_Fun",0.15f),
                    }
                );

            case SavedExpression.Neutral2:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.3f),
                        new Op("Fcl_BRW_Angry",0.1f),
                        new Op("Fcl_EYE_Joy",0.1f),
                    }
                );

            case SavedExpression.Neutral3:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.4f),
                        new Op("Fcl_BRW_Angry",0.15f),
                        new Op("Fcl_EYE_Joy",0.3f),
                        new Op("Fcl_MTH_Angry",0.3f),
                        new Op("Fcl_MTH_O",0.1f),
                    }
                );

            case SavedExpression.Shock:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.3f),
                        new Op("Fcl_BRW_Surprised",0.5f),
                        new Op("Fcl_BRW_Fun",0.2f),
                        new Op("Fcl_EYE_Spread",0.7f),
                        new Op("Fcl_EYE_Surprised",0.7f),
                        new Op("Fcl_EYE_Close",0.2f),
                        new Op("Fcl_MTH_O",0.1f),
                        new Op("Fcl_MTH_Small",0.2f),
                    }
                );

            case SavedExpression.Shock2:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.5f),
                        new Op("Fcl_BRW_Surprised",0.1f),
                        new Op("Fcl_EYE_Spread",0.4f),
                        new Op("Fcl_EYE_Surprised",1f),
                        new Op("Fcl_EYE_Close",0.1f),
                        new Op("Fcl_MTH_I",0.3f),
                    }
                );

            case SavedExpression.Shock3:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.7f),
                        new Op("Fcl_EYE_Joy",0.3f),
                        new Op("Fcl_EYE_Surprised",1f),
                        new Op("Fcl_MTH_Angry",0.4f),
                        new Op("Fcl_MTH_I",0.35f),
                        new Op("Fcl_MTH_A",0.2f),
                        new Op("Fcl_MTH_Small",0.2f),
                    }
                );

            case SavedExpression.FoodWait1:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Fun",0.6f),
                        new Op("Fcl_EYE_Spread",0.5f),
                        new Op("Fcl_EYE_Surprised",0.3f),
                        new Op("Fcl_MTH_Joy",0.4f),
                        new Op("Fcl_MTH_Small",0.3f),
                    }
                );

            case SavedExpression.FoodWait2:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Fun",0.3f),
                        new Op("Fcl_BRW_Sorrow",0.2f),
                        new Op("Fcl_BRW_Angry",0.1f),
                        new Op("Fcl_EYE_Spread",0.5f),
                        new Op("Fcl_EYE_Joy",0.1f),
                        new Op("Fcl_MTH_Joy",0.4f),
                        new Op("Fcl_MTH_Angry",0.15f),
                    }
                );

            case SavedExpression.FoodWait3:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Fun",0.3f),
                        new Op("Fcl_BRW_Sorrow",0.3f),
                        new Op("Fcl_BRW_Angry",0.1f),
                        new Op("Fcl_EYE_Spread",0.5f),
                        new Op("Fcl_EYE_Joy",0.3f),
                        new Op("Fcl_MTH_Joy",0.4f),
                        new Op("Fcl_MTH_Angry",0.15f),
                    }
                );

            case SavedExpression.FoodEating1:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.3f),
                        new Op("Fcl_EYE_Joy",1f),
                        new Op("Fcl_MTH_Fun",0.2f),
                    }
                );

            case SavedExpression.FoodEating2:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.3f),
                        new Op("Fcl_EYE_Joy",1f),
                        new Op("Fcl_MTH_Fun",0.2f),
                    }
                );

            case SavedExpression.FoodEating3:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.3f),
                        new Op("Fcl_EYE_Joy",1f),
                        new Op("Fcl_MTH_Fun",0.2f),
                    }
                );

            case SavedExpression.blush_swaip_face:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.4f),
                        new Op("Fcl_EYE_Joy",0.2f),
                        new Op("Fcl_MTH_Angry",0.2f),
                    }
                    , transitionOverrideSeconds: 0.1f
                );
            case SavedExpression.blush_tap_face:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.4f),
                        new Op("Fcl_EYE_Joy",1f),
                    }
                    , transitionOverrideSeconds: 0.1f
                );
            case SavedExpression.blush_swaip:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.3f),
                        new Op("Fcl_EYE_Close",0.3f),
                        new Op("Fcl_MTH_Angry",0.4f),
                    }
                    , transitionOverrideSeconds: 0.1f
                );
            case SavedExpression.blush_tap:
                return new Recipe(
                    new[] {
                        new Op("Fcl_BRW_Sorrow",0.4f),
                        new Op("Fcl_EYE_Joy",1f),
                        new Op("Fcl_MTH_Angry",0.4f),
                        new Op("Fcl_MTH_A",0.2f),
                    }
                    , transitionOverrideSeconds: 0.1f
                );

            default:
                return new Recipe(Array.Empty<Op>());
        }
    }

    private Dictionary<(SkinnedMeshRenderer smr, int index), float> BuildPose01FromRecipe(SavedExpression preset)
    {
        var pose01 = new Dictionary<(SkinnedMeshRenderer smr, int index), float>(_allPairs.Count);
        foreach (var p in _allPairs) pose01[p] = 0f;

        // ★変更：Recipe から ops を取り出す
        var recipe = GetRecipe(preset);

        foreach (var op in recipe.ops)
        {
            if (_nameToPairs.TryGetValue(op.name, out var list))
            {
                float w01 = Mathf.Clamp01(op.w01);
                foreach (var p in list)
                    pose01[p] = w01;
            }
        }

        return pose01;
    }

    // ★追加：このコンポーネント経由で値をセットする統一関数（0..1）
    private void Set01((SkinnedMeshRenderer smr, int index) p, float value01)
    {
        float v01 = Mathf.Clamp01(value01);

        if (_lastSet01 != null)
        {
            // _allPairs のキー前提だが、安全に TryGetValue せず代入（キーが無ければ追加）
            _lastSet01[p] = v01;
        }

        SafeSet(p, v01 * 100f);
    }

    private void ApplyPresetInstant(SavedExpression preset)
    {
        if (!_cacheBuilt) return;

        foreach (var p in _allPairs)
            Set01(p, 0f);

        // 「このコンポーネントが確定させた表情」を更新（全ペア保持）
        _holdTarget01 = BuildPose01FromRecipe(preset);

        foreach (var kv in _holdTarget01)
            Set01(kv.Key, kv.Value);
    }

    private void StartTransitionTo(SavedExpression target)
    {
        if (!_cacheBuilt) return;

        // ★本修正：
        // 遷移開始点は「直近にこのコンポーネントが SafeSet した値(_lastSet01)」
        // （SafeSet に入れる値をローカルに保持→次の開始点にする）
        var start01 = new Dictionary<(SkinnedMeshRenderer smr, int index), float>(_allPairs.Count);
        foreach (var p in _allPairs)
        {
            if (_lastSet01 != null && _lastSet01.TryGetValue(p, out var v)) start01[p] = Mathf.Clamp01(v);
            else start01[p] = 0f;
        }

        var target01 = BuildPose01FromRecipe(target);

        var recipe = GetRecipe(target);
        float seconds = recipe.transitionOverrideSeconds ?? 0.3f;

        if (_transitionRoutine != null) StopCoroutine(_transitionRoutine);
        _transitionRoutine = StartCoroutine(TransitionCoroutine(start01, target01, Mathf.Max(0.01f, seconds)));
    }

    private IEnumerator TransitionCoroutine(
        Dictionary<(SkinnedMeshRenderer smr, int index), float> start01,
        Dictionary<(SkinnedMeshRenderer smr, int index), float> target01,
        float seconds)
    {
        if (!_cacheBuilt) yield break;

        float t = 0f;
        while (t < seconds)
        {
            float a = t / seconds;
            float u = a * a * (3f - 2f * a); // smoothstep

            foreach (var p in _allPairs)
            {
                float s = start01[p];
                float e = target01[p];
                float v01 = Mathf.LerpUnclamped(s, e, u);

                // ★SafeSet に入れる値(v01)を _lastSet01 に保持し続ける
                Set01(p, v01);
            }

            t += Time.deltaTime;
            yield return null;
        }

        foreach (var p in _allPairs)
            Set01(p, target01[p]);

        // 遷移完了＝確定表情を更新（次の遷移開始点になる）
        _holdTarget01 = target01;
        _transitionRoutine = null;
    }

    private void ApplyHoldEveryFrame()
    {
        if (_holdTarget01 == null || _holdTarget01.Count == 0) return;

        bool blinkActive =
            enableBlink &&
            !debugAllShapesOverride &&
            _blink01 > 0f &&
            _cacheBuilt &&
            (_eyeClosePairs.Count > 0 || _eyeJoyPairs.Count > 0);

        bool mouthActive =
            enableMouthLoop &&
            !debugAllShapesOverride &&
            _cacheBuilt &&
            _mouthOPairs.Count > 0;

        Dictionary<SkinnedMeshRenderer, (float closeBase, float joyBase)> eyeBasePerSmr = null;

        if (blinkActive)
        {
            eyeBasePerSmr = new Dictionary<SkinnedMeshRenderer, (float closeBase, float joyBase)>();

            foreach (var p in _eyeClosePairs)
            {
                if (!_holdTarget01.TryGetValue(p, out var base01)) continue;
                var smr = p.smr;
                if (!eyeBasePerSmr.TryGetValue(smr, out var v)) v = (0f, 0f);
                if (base01 > v.closeBase) v.closeBase = base01;
                eyeBasePerSmr[smr] = v;
            }

            foreach (var p in _eyeJoyPairs)
            {
                if (!_holdTarget01.TryGetValue(p, out var base01)) continue;
                var smr = p.smr;
                if (!eyeBasePerSmr.TryGetValue(smr, out var v)) v = (0f, 0f);
                if (base01 > v.joyBase) v.joyBase = base01;
                eyeBasePerSmr[smr] = v;
            }
        }

        foreach (var kv in _holdTarget01)
        {
            var p = kv.Key;
            float base01 = kv.Value;
            float final01 = base01;

            if (blinkActive && eyeBasePerSmr != null && eyeBasePerSmr.TryGetValue(p.smr, out var baseEye))
            {
                bool isClose = _eyeCloseSet.Contains(p);
                bool isJoy = _eyeJoySet.Contains(p);

                if (isClose || isJoy)
                {
                    float closeBase = baseEye.closeBase;
                    float joyBase = baseEye.joyBase;
                    float freeCap = Mathf.Max(0f, 1f - (closeBase + joyBase));

                    if (isClose)
                    {
                        float targetClose = closeBase + freeCap * _blink01;
                        final01 = targetClose;
                    }
                    else if (isJoy)
                    {
                        final01 = joyBase;
                    }
                }
            }

            if (mouthActive && _mouthOSet.Contains(p))
            {
                final01 = Mathf.Lerp(base01, 1f, _mouth01);
            }

            Set01(p, final01);
        }
    }

    private void ApplyDebugSlidersEveryFrame()
    {
        if (!_cacheBuilt) return;

        foreach (var p in _allPairs)
            Set01(p, 0f);

        foreach (var s in allShapeSliders)
        {
            if (s == null) continue;
            if (_nameToPairs.TryGetValue(s.shapeName, out var list))
            {
                float w01 = Mathf.Clamp01(s.value01);
                foreach (var p in list)
                    Set01(p, w01);
            }
        }
    }

    // ===== まばたき制御 =====

    private void UpdateBlinkCoroutineState()
    {
        if (vrmTo == null || !_cacheBuilt)
        {
            StopBlink();
            return;
        }

        bool shouldBlink = enableBlink && !debugAllShapesOverride;

        if (shouldBlink)
        {
            if (_blinkRoutine == null)
            {
                _blinkRoutine = StartCoroutine(BlinkLoop());
            }
        }
        else
        {
            StopBlink();
        }
    }

    private IEnumerator BlinkLoop()
    {
        _blink01 = 0f;

        while (CanContinueBlink())
        {
            float wait = UnityEngine.Random.Range(0.1f, 7f);
            float tWait = 0f;
            while (tWait < wait)
            {
                if (!CanContinueBlink()) { _blink01 = 0f; _blinkRoutine = null; yield break; }
                tWait += Time.deltaTime;
                yield return null;
            }

            const float total = 0.2f;
            const float half = total * 0.5f;

            float t = 0f;
            while (t < half)
            {
                if (!CanContinueBlink()) { _blink01 = 0f; _blinkRoutine = null; yield break; }
                _blink01 = Mathf.Clamp01(t / half);
                t += Time.deltaTime;
                yield return null;
            }
            _blink01 = 1f;

            t = 0f;
            while (t < half)
            {
                if (!CanContinueBlink()) { _blink01 = 0f; _blinkRoutine = null; yield break; }
                _blink01 = Mathf.Clamp01(1f - (t / half));
                t += Time.deltaTime;
                yield return null;
            }
            _blink01 = 0f;
        }

        _blink01 = 0f;
        _blinkRoutine = null;
    }

    private bool CanContinueBlink()
    {
        return vrmTo != null && vrmTo.IsReady && enableBlink && !debugAllShapesOverride;
    }

    private void StopBlink()
    {
        if (_blinkRoutine != null)
        {
            try { StopCoroutine(_blinkRoutine); } catch { }
            _blinkRoutine = null;
        }
        _blink01 = 0f;
    }

    // ===== 口開閉制御 =====

    private void UpdateMouthCoroutineState()
    {
        if (vrmTo == null || !_cacheBuilt)
        {
            StopMouth();
            return;
        }

        bool shouldMouthLoop = enableMouthLoop && !debugAllShapesOverride;

        if (shouldMouthLoop)
        {
            if (_mouthRoutine == null)
            {
                _mouthRoutine = StartCoroutine(MouthLoop());
            }
        }
        else
        {
            StopMouth();
        }
    }

    private IEnumerator MouthLoop()
    {
        _mouth01 = 0f;

        while (CanContinueMouthLoop())
        {
            const float half = 0.32f;

            float t = 0f;
            while (t < half * 2f)
            {
                if (!CanContinueMouthLoop()) { _mouth01 = 0f; _mouthRoutine = null; yield break; }
                _mouth01 = Mathf.Clamp01(1f - (t / half)) * 0.8f;
                t += Time.deltaTime;
                yield return null;
            }
            _mouth01 = 0f;

            t = 0f;
            while (t < half)
            {
                if (!CanContinueMouthLoop()) { _mouth01 = 0f; _mouthRoutine = null; yield break; }
                _mouth01 = Mathf.Clamp01(t / half) * 0.8f;
                t += Time.deltaTime;
                yield return null;
            }
            _mouth01 = 0.8f;
        }

        _mouth01 = 0f;
        _mouthRoutine = null;
    }

    private bool CanContinueMouthLoop()
    {
        return vrmTo != null && vrmTo.IsReady && enableMouthLoop && !debugAllShapesOverride;
    }

    private void StopMouth()
    {
        if (_mouthRoutine != null)
        {
            try { StopCoroutine(_mouthRoutine); } catch { }
            _mouthRoutine = null;
        }
        _mouth01 = 0f;
    }

    // ===== 共通ユーティリティ =====

    private static void SafeSet((SkinnedMeshRenderer smr, int index) p, float weight)
    {
        try { p.smr.SetBlendShapeWeight(p.index, Mathf.Clamp(weight, 0f, 100f)); } catch { }
    }

    private static float SafeGet((SkinnedMeshRenderer smr, int index) p)
    {
        try { return p.smr.GetBlendShapeWeight(p.index); } catch { return 0f; }
    }
}
