using UnityEngine;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine.UI;

public enum BlushState
{
    None,
    blush_tap_face,
    blush_swaip_face,
    blush_tap,
    blush_swaip
}


public sealed class VrmChrSceneController : MonoBehaviour
{
    public static VrmChrSceneController Instance { get; private set; }

    public int coin = 0;
    public int workLv = 1;
    public float foodGauge = 80;
    public float loveGauge = 0;
    public float foodGaugeLine1 = 0;
    public float foodGaugePerTick = 0.1f;
    public WorkOutInfo workOutInfo;

    public byte[] CurrentVrmData { get; private set; }

    public float foodGaugeLine1X
    {
        get
        {
            float t = Mathf.Clamp01(foodGaugeLine1 / 100f);
            return Mathf.Lerp(-70, 100, t);
        }
    }

    public GameObject vrm;
    public CanvasUIController canvasUI;

    [HideInInspector] public VrmToController vrmToController;
    [HideInInspector] public CanvasUIController canvasUIController;

    [HideInInspector] public int kg100 = 0;
    [HideInInspector] public int kg100_2 = 0;

    [HideInInspector] public bool isShockState = false;
    [HideInInspector] public bool isFoodWait = false;
    [HideInInspector] public bool isEating = false;

    private Coroutine addWeightRoutine;
    private float addWeightTargetKg100_2;
    private bool hasAddWeightTarget = false;

    private float foodGaugeTimer = 0f;
    private const float FoodGaugeTickSeconds = 0.2f;

    private bool isWorkoutActive = false;
    private string workoutEventKey = null;

    private float lowFoodLineTimer = 0f;
    private float workoutFoodGaugePerTickAdd = 0f;

    public bool enableDevMood = false;
    public int defaultBackgroundIndex;

    private float stateFace3Key;
    private float stateBodyKey;
    private float stateBustKey;
    private float stateHeight;

    private float previewFace3Key;
    private float previewBodyKey;
    private float previewBustKey;
    private float previewHeight;

    private bool isPreviewActive = false;

    // ===== Added: Blush state =====

    private BlushState blushState = BlushState.None;
    private float blushTimer = 0f;
    private const float BlushHoldSeconds = 0.4f;

    private float workoutCoinToastTimer = 0f;

    public void SetBlushState(BlushState state)
    {
        blushState = state;
        if (state == BlushState.None)
        {
            blushTimer = 0f;
        }
        else
        {
            blushTimer = BlushHoldSeconds;
        }
    }

    private string GetBlushEventKey(BlushState state)
    {
        switch (state)
        {
            case BlushState.blush_tap_face: return "blush_tap_face";
            case BlushState.blush_swaip_face: return "blush_swaip_face";
            case BlushState.blush_tap: return "blush_tap";
            case BlushState.blush_swaip: return "blush_swaip";
            default: return null;
        }
    }
    // ===== /Added =====

    void Awake()
    {
        Instance = this;

        vrmToController = vrm.GetComponent<VrmToController>();
        canvasUIController = canvasUI.GetComponent<CanvasUIController>();

        if (vrmToController == null)
        {
            Debug.LogError("[VrmChrSceneController] VrmToController not found on vrm GameObject.");
            return;
        }

        // Consume selected VRM bytes from settings scene
        var data = SweetGameVrmStore.VrmData;
        if (data == null || data.Length == 0)
        {
            Debug.LogError("[VrmChrSceneController] SweetGameVrmStore.VrmData is empty. Select VRM in sweetGameSettings.");
            return;
        }

        // Initial load
        vrmToController.ReloadFromBytes(
            data,
            SweetGameVrmStore.bodyVariant,
            vrmToController.face3Key,
            SweetGameVrmStore.body,
            vrmToController.bustKey,
            SweetGameVrmStore.height
        );
        CurrentVrmData = data;
        defaultBackgroundIndex = SweetGameVrmStore.backgroundId;
        var speechCtr = gameObject.GetComponent<VrmChrSceneTextController>();
        speechCtr.UpdateSpeechCharacterType(SweetGameVrmStore.speechType);

        stateFace3Key = vrmToController.face3Key;
        stateBodyKey = SweetGameVrmStore.body;
        stateBustKey = vrmToController.bustKey;
        stateHeight = SweetGameVrmStore.height;

        previewFace3Key = stateFace3Key;
        previewBodyKey = stateBodyKey;
        previewBustKey = stateBustKey;
        previewHeight = stateHeight;


        float factor = 1f - stateHeight * 0.6f;
        if (Mathf.Approximately(factor, 0f))
        {
            addWeightTargetKg100_2 = 0f;
        }
        else
        {
            float key01 = stateBodyKey / 100f;
            float kg100Float = 4000f + (30000f - 4000f) * key01;
            addWeightTargetKg100_2 = kg100Float * factor + stateBustKey * 20f;
        }
        hasAddWeightTarget = true;


        ApplyVrmKeysForView();

        AudioManager.Instance.PlayBGM("bgm1");
    }

    void Update()
    {
        foodGaugeLine1 = Mathf.Max(0f, foodGaugeLine1);
        foodGaugePerTick = Mathf.Min(0.8f, Mathf.Max(0.1f, foodGaugePerTick));

        float line1AsFoodGauge = GetLine1AsFoodGauge();

        foodGaugeTimer += Time.deltaTime;
        while (foodGaugeTimer >= FoodGaugeTickSeconds)
        {
            foodGaugeTimer -= FoodGaugeTickSeconds;

            float effectivePerTick = foodGaugePerTick + (isWorkoutActive ? workoutFoodGaugePerTickAdd / 4 : 0f);
            foodGauge = Mathf.Max(0f, foodGauge - effectivePerTick);
            float t = Mathf.InverseLerp(-100, 100, -90);
            var warnPosition = Mathf.Clamp01(t) * 100f;

            if (foodGauge <= warnPosition)
            {
                if (loveGauge > 0)
                    loveGauge -= 0.2f;
            }
            if (foodGauge <= line1AsFoodGauge)
            {
                if (loveGauge > 0)
                    loveGauge -= 0.05f;
                AddWeight(-1f);
            }

            if (isWorkoutActive)
            {
                float delta = -workOutInfo.consumeCal;
                AddWeight(delta);
            }
        }



        bool isLowFood = (foodGauge <= line1AsFoodGauge);

        if (isLowFood)
        {
            lowFoodLineTimer += Time.deltaTime;

            if (lowFoodLineTimer >= 5)
            {
                foodGaugeLine1 = Mathf.Max(0f, foodGaugeLine1 - 0.5f);
                foodGaugePerTick = Mathf.Max(0.05f, foodGaugePerTick - 0.02f);
                lowFoodLineTimer = 0f;
            }
        }
        else
        {
            lowFoodLineTimer = 0f;
        }

        line1AsFoodGauge = GetLine1AsFoodGauge();

        if (isWorkoutActive)
        {
            if (foodGauge <= line1AsFoodGauge)
            {
                StopWorkout();
            }
        }

        if (vrmToController == null) return;

        if (blushState != BlushState.None)
        {
            blushTimer -= Time.deltaTime;
            if (blushTimer <= 0f)
            {
                blushState = BlushState.None;
                blushTimer = 0f;
            }
        }

        if (isPreviewActive)
        {
            kg100 = Mathf.RoundToInt(4000f + (30000f - 4000f) * previewBodyKey / 100f);
            kg100_2 = Mathf.RoundToInt(kg100 * (1f - previewHeight * 0.6f) + previewBustKey * 20f);
        }
        else
        {
            float kg2 = hasAddWeightTarget ? addWeightTargetKg100_2 : 0f;
            kg100_2 = Mathf.RoundToInt(kg2);

            float factor = 1f - stateHeight * 0.6f;
            if (Mathf.Approximately(factor, 0f))
            {
                kg100 = 0;
            }
            else
            {
                float kg1 = (kg2 - stateBustKey * 20f) / factor;
                kg100 = Mathf.RoundToInt(kg1);
            }
        }

        ApplyVrmKeysForView();

        if (isWorkoutActive)
        {
            workoutCoinToastTimer += Time.deltaTime;

            var second = 5 / Mathf.Max(0.01f, workOutInfo.consumeCal);
            while (workoutCoinToastTimer >= second)
            {
                workoutCoinToastTimer -= second;
                var addcoin = 2 * workLv;
                canvasUIController.calToastGenController.GenToast("+" + addcoin + "coin");
                coin += addcoin;
                AudioManager.Instance.PlaySE("add_money");
            }

            if (!string.IsNullOrEmpty(workoutEventKey))
            {
                vrmToController.ApplyEvent(workoutEventKey);
            }
            return;
        }

        if (blushState != BlushState.None)
        {
            string blushKey = GetBlushEventKey(blushState);
            if (!string.IsNullOrEmpty(blushKey))
            {
                vrmToController.ApplyEvent(blushKey);
            }
            return;
        }

        if (isEating)
        {
            if (stateBodyKey > 60)
            {
                vrmToController.ApplyEvent("foodEating3");
            }
            else if (stateBodyKey > 25)
            {
                vrmToController.ApplyEvent("foodEating2");
            }
            else
            {
                vrmToController.ApplyEvent("foodEating1");
            }
        }
        else if (isFoodWait)
        {
            if (stateBodyKey > 60)
            {
                vrmToController.ApplyEvent("foodWait3");
            }
            else if (stateBodyKey > 25)
            {
                vrmToController.ApplyEvent("foodWait2");
            }
            else
            {
                vrmToController.ApplyEvent("foodWait1");
            }
        }
        else if (isShockState)
        {
            if (stateBodyKey > 60)
            {
                vrmToController.ApplyEvent("shock3");
            }
            else if (stateBodyKey > 25)
            {
                vrmToController.ApplyEvent("shock2");
            }
            else
            {
                vrmToController.ApplyEvent("shock");
            }
        }
        else
        {
            if (stateBodyKey > 60 || foodGauge <= Mathf.InverseLerp(-1, 1, -0.9f) || (isLowFood && stateBodyKey > 25))
            {
                vrmToController.ApplyEvent("idle3");
            }
            else if (stateBodyKey > 25 || isLowFood)
            {
                vrmToController.ApplyEvent("idle2");
            }
            else
            {
                vrmToController.ApplyEvent("idle");
            }
        }
    }

    private void ApplyVrmKeysForView()
    {
        if (vrmToController == null) return;

        if (isPreviewActive)
        {
            vrmToController.face3Key = previewFace3Key;
            vrmToController.bodyKey = previewBodyKey;
            vrmToController.lowKey = previewBodyKey * 0.3f;
            vrmToController.bustKey = previewBustKey;
            vrmToController.height = previewHeight;
        }
        else
        {
            vrmToController.face3Key = stateFace3Key;
            vrmToController.bodyKey = stateBodyKey;
            vrmToController.lowKey = stateBodyKey * 0.3f;
            vrmToController.bustKey = stateBustKey;
            vrmToController.height = stateHeight;
        }
    }

    private float GetLine1AsFoodGauge()
    {
        float t = Mathf.InverseLerp(-100, 100, foodGaugeLine1X);
        return Mathf.Clamp01(t) * 100f;
    }
    public void OnPreviewPointerDown()
    {
        isPreviewActive = true;
        ApplyVrmKeysForView();
    }

    public void OnPreviewPointerUp()
    {
        isPreviewActive = false;
        ApplyVrmKeysForView();
    }

    public void ToggleWorkout(WorkOutInfo info)
    {
        if (isWorkoutActive && workoutEventKey == info.id)
        {
            StopWorkout();
            return;
        }

        float line1AsFoodGauge = GetLine1AsFoodGauge();
        if (foodGauge <= line1AsFoodGauge)
        {
            return;
        }

        StartWorkoutInternal(info);
    }

    private void StartWorkoutInternal(WorkOutInfo info)
    {
        StopWorkout();

        workOutInfo = info;
        isWorkoutActive = true;
        workoutCoinToastTimer = 0f;
        workoutEventKey = info.id;

        workoutFoodGaugePerTickAdd = info.consumeCal / 20f;

        isShockState = false;
        isFoodWait = false;
        isEating = false;
    }

    private void StopWorkout()
    {
        if (!isWorkoutActive) return;

        isWorkoutActive = false;
        workoutEventKey = null;

        workoutFoodGaugePerTickAdd = 0f;
    }

    public void SetFace3KeyImmediate(float face3Key)
    {
        stateFace3Key = Mathf.Clamp(face3Key, 0f, 100f);
        if (!isPreviewActive && vrmToController != null)
        {
            vrmToController.face3Key = stateFace3Key;
        }
    }

    public void SetBustKeyImmediate(float bustKey)
    {
        stateBustKey = Mathf.Clamp(bustKey, 0f, 100f);
        if (!isPreviewActive && vrmToController != null)
        {
            vrmToController.bustKey = stateBustKey;
        }
    }

    public void SetBodyKeyImmediate(float bodyKey)
    {
        if (addWeightRoutine != null)
        {
            StopCoroutine(addWeightRoutine);
            addWeightRoutine = null;
        }

        hasAddWeightTarget = false;
        stateBodyKey = Mathf.Clamp(bodyKey, -2f, 100f);

        // 手動変更は「その時点の体型」を新しい基準にする（既存変数を使うだけ）
        float factor = 1f - stateHeight * 0.6f;
        if (Mathf.Approximately(factor, 0f))
        {
            addWeightTargetKg100_2 = 0f;
        }
        else
        {
            float key01 = stateBodyKey / 100f;
            float kg100Float = 4000f + (30000f - 4000f) * key01;
            addWeightTargetKg100_2 = kg100Float * factor + stateBustKey * 20f;
        }
        hasAddWeightTarget = true;

        if (!isPreviewActive && vrmToController != null)
        {
            vrmToController.bodyKey = stateBodyKey;
            vrmToController.lowKey = stateBodyKey * 0.3f;
        }
    }

    public void AddWeight(float addWeight)
    {
        addWeight = addWeight * SweetGameVrmStore.weightChangeScale;

        float factor = 1f - stateHeight * 0.6f;
        if (Mathf.Approximately(factor, 0f)) return;

        float currentKg100 = 4000f + (30000f - 4000f) * (stateBodyKey / 100f);
        float currentKg100_2 = currentKg100 * factor + stateBustKey * 20f;

        if (!hasAddWeightTarget)
        {
            addWeightTargetKg100_2 = currentKg100_2;
            hasAddWeightTarget = true;
        }

        addWeightTargetKg100_2 += addWeight;

        // ★不変条件：bodyKey=-2 相当より下には落ちない
        float minKg100_2 = CalcKg100_2FromBodyKey(-2f, stateHeight, stateBustKey);
        if (addWeightTargetKg100_2 < minKg100_2)
            addWeightTargetKg100_2 = minKg100_2;

        RecalcBodyKeyFromTargetWeight(animate: true);
    }



    private IEnumerator AnimateBodyKey(float from, float to, float seconds)
    {
        if (seconds <= 1e-6f)
        {
            stateBodyKey = to;
            addWeightRoutine = null;
            yield break;
        }

        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / seconds);
            stateBodyKey = Mathf.Lerp(from, to, u);
            yield return null;
        }

        stateBodyKey = to;
        addWeightRoutine = null;
    }

    public void ChangeHeight(float height)
    {
        StopBodyKeyAnimation();

        // 現在のターゲット（無ければ現状から作る）
        float targetKg100_2;
        if (hasAddWeightTarget)
        {
            targetKg100_2 = addWeightTargetKg100_2;
        }
        else
        {
            float factor = 1f - stateHeight * 0.6f;
            if (Mathf.Approximately(factor, 0f)) return;

            float currentKg100 = 4000f + (30000f - 4000f) * (stateBodyKey / 100f);
            targetKg100_2 = currentKg100 * factor + stateBustKey * 20f;
        }

        stateHeight = height;

        float minKg100_2 = CalcKg100_2FromBodyKey(-2f, stateHeight, stateBustKey);
        addWeightTargetKg100_2 = Mathf.Max(targetKg100_2, minKg100_2);
        hasAddWeightTarget = true;

        RecalcBodyKeyFromTargetWeight(animate: false);

        if (!isPreviewActive && vrmToController != null)
        {
            vrmToController.height = stateHeight;
            vrmToController.bodyKey = stateBodyKey;
            vrmToController.lowKey = stateBodyKey * 0.3f;
        }
    }



    public void ChangeCup(float bustKey)
    {
        StopBodyKeyAnimation();

        float targetKg100_2 = hasAddWeightTarget ? addWeightTargetKg100_2 : 0f;

        stateBustKey = bustKey;

        hasAddWeightTarget = true;
        addWeightTargetKg100_2 = targetKg100_2;

        RecalcBodyKeyFromTargetWeight(animate: false);

        if (!isPreviewActive && vrmToController != null)
        {
            vrmToController.bustKey = stateBustKey;
            vrmToController.bodyKey = stateBodyKey;
            vrmToController.lowKey = stateBodyKey * 0.3f;
        }
    }


    private void StopBodyKeyAnimation()
    {
        if (addWeightRoutine != null)
        {
            StopCoroutine(addWeightRoutine);
            addWeightRoutine = null;
        }
    }

    private float CalcBodyKeyFromTargetKg100_2(float targetKg100_2, float height, float bustKey)
    {
        float factor = 1f - height * 0.6f;
        if (Mathf.Approximately(factor, 0f))
        {
            return stateBodyKey;
        }

        float kg100NewFloat = (targetKg100_2 - bustKey * 20f) / factor;
        float keyNew = (kg100NewFloat - 4000f) / (30000f - 4000f);
        return Mathf.Clamp(keyNew * 100f, -2f, 100f);
    }

    private void ApplyBodyKeyTarget(float bodyKeyTarget, bool animate, float seconds = 0.3f)
    {
        StopBodyKeyAnimation();

        if (!animate || seconds <= 1e-6f)
        {
            stateBodyKey = bodyKeyTarget;
            return;
        }

        addWeightRoutine = StartCoroutine(AnimateBodyKey(stateBodyKey, bodyKeyTarget, seconds));
    }

    private void RecalcBodyKeyFromTargetWeight(bool animate)
    {
        if (!hasAddWeightTarget)
        {
            float factor = 1f - stateHeight * 0.6f;
            if (!Mathf.Approximately(factor, 0f))
            {
                float currentKg100 = 4000f + (30000f - 4000f) * (stateBodyKey / 100f);
                addWeightTargetKg100_2 = currentKg100 * factor + stateBustKey * 20f;
            }
            hasAddWeightTarget = true;
        }

        float bodyKeyTarget = CalcBodyKeyFromTargetKg100_2(addWeightTargetKg100_2, stateHeight, stateBustKey);
        ApplyBodyKeyTarget(bodyKeyTarget, animate);
    }

    private float CalcKg100_2FromBodyKey(float bodyKey, float height, float bustKey)
    {
        float factor = 1f - height * 0.6f;
        if (Mathf.Approximately(factor, 0f)) return 0f;

        float key01 = bodyKey / 100f;
        float kg100Float = 4000f + (30000f - 4000f) * key01;
        return kg100Float * factor + bustKey * 20f;
    }

}
