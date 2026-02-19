using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Unity.Mathematics;

public class SliderController : MonoBehaviour
{
    public enum StepSe
    {
        None,
        Kati,
        body,
    }

    [Header("スライダー")]
    [SerializeField] Slider inputSlider;
    [SerializeField] Slider displaySlider;

    [Header("補間後の値を通知するイベント")]
    [SerializeField] UnityEvent<float> onValueChangedSmoothed;

    [Tooltip("差分が最大のときにかける時間（秒）")]
    [SerializeField] float maxSmoothTime = 0.2f;

    [Header("一定刻みごとの効果音")]
    [SerializeField] StepSe stepSe = StepSe.None;

    float stepSize = 0.1f;
    float notifyThreshold = 0.0001f;

    public bool useCurrentValue = false;
    public bool isPointerDown;

    float currentValue;
    float targetValue;

    float startValue;
    float duration;
    float elapsedTime;

    public bool isAnimating;
    float lastNotifiedValue;

    RectTransform inputRect;
    Canvas parentCanvas;
    Camera uiCamera;
    bool usingTouch;
    int activeFingerId = -1;

    int lastStepIndex;
    bool stepIndexInitialized;

    float nextSeTime;

    void Awake()
    {
        if (inputSlider == null || displaySlider == null)
        {
            Debug.LogWarning($"{nameof(SliderController)}: Slider が設定されていません。");
            enabled = false;
            return;
        }

        inputRect = inputSlider.GetComponent<RectTransform>();
        parentCanvas = inputSlider.GetComponentInParent<Canvas>();
        uiCamera = ResolveUICamera(parentCanvas);

        if (useCurrentValue)
        {
            currentValue = inputSlider.value;
            targetValue = currentValue;
            startValue = currentValue;
            displaySlider.value = currentValue;
        }
        else
        {
            currentValue = displaySlider.value;
            targetValue = currentValue;
            startValue = currentValue;
        }

        duration = 0f;
        elapsedTime = 0f;
        isAnimating = false;

        lastNotifiedValue = currentValue;
        if (onValueChangedSmoothed != null && useCurrentValue)
        {
            onValueChangedSmoothed.Invoke(currentValue);
        }

        InitStepIndex(currentValue);

        nextSeTime = 0f;

        inputSlider.onValueChanged.AddListener(OnInputSliderChanged);
    }

    void OnDestroy()
    {
        if (inputSlider != null)
        {
            inputSlider.onValueChanged.RemoveListener(OnInputSliderChanged);
        }
    }

    void Update()
    {
        UpdatePointerDownState();

        if (isAnimating)
        {
            if (duration <= 0f)
            {
                float prev = currentValue;

                currentValue = targetValue;
                displaySlider.value = currentValue;

                QueueStepSeIfCrossed(prev, currentValue);
                NotifyIfChanged();

                isAnimating = false;
            }
            else
            {
                elapsedTime += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedTime / duration);
                float eased = 1f - (1f - t) * (1f - t);

                float before = currentValue;

                currentValue = Mathf.Lerp(startValue, targetValue, eased);
                displaySlider.value = currentValue;

                QueueStepSeIfCrossed(before, currentValue);
                NotifyIfChanged();

                if (t >= 1f)
                {
                    isAnimating = false;
                    elapsedTime = 0f;
                    duration = 0f;
                    currentValue = targetValue;
                }
            }
        }
    }

    private void OnInputSliderChanged(float value)
    {
        float diff = Mathf.Abs(value - currentValue);

        if (diff <= 0f || maxSmoothTime <= 0f)
        {
            isAnimating = false;
            duration = 0f;
            elapsedTime = 0f;

            float prev = currentValue;

            currentValue = value;
            targetValue = value;
            startValue = value;

            displaySlider.value = currentValue;

            QueueStepSeIfCrossed(prev, currentValue);
            NotifyIfChanged();
            return;
        }

        startValue = currentValue;
        targetValue = value;

        duration = maxSmoothTime * diff;
        elapsedTime = 0f;
        isAnimating = true;
    }

    public void SetValueFromController(float value, bool alsoUpdateInputSlider = true)
    {
        isAnimating = false;
        duration = 0f;
        elapsedTime = 0f;

        currentValue = value;
        targetValue = value;
        startValue = value;

        displaySlider.value = currentValue;

        if (alsoUpdateInputSlider && inputSlider != null)
        {
            inputSlider.SetValueWithoutNotify(value);
        }

        lastNotifiedValue = currentValue;
        InitStepIndex(currentValue);

    }

    public float GetCurrentValue()
    {
        return currentValue;
    }

    private void NotifyIfChanged()
    {
        if (onValueChangedSmoothed == null) return;
        if (Mathf.Abs(currentValue - lastNotifiedValue) < notifyThreshold) return;

        lastNotifiedValue = currentValue;
        onValueChangedSmoothed.Invoke(currentValue);
    }

    void UpdatePointerDownState()
    {
        if (inputRect == null)
        {
            isPointerDown = false;
            usingTouch = false;
            activeFingerId = -1;
            return;
        }

        if (usingTouch)
        {
            bool found = false;
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.fingerId != activeFingerId) continue;

                found = true;
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    usingTouch = false;
                    activeFingerId = -1;
                    isPointerDown = false;
                }
                else
                {
                    isPointerDown = true;
                }
                break;
            }

            if (!found)
            {
                usingTouch = false;
                activeFingerId = -1;
                isPointerDown = false;
            }

            return;
        }

        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase != TouchPhase.Began) continue;

                if (IsOverInputSlider(t.position))
                {
                    usingTouch = true;
                    activeFingerId = t.fingerId;
                    isPointerDown = true;
                    return;
                }
            }
        }

        if (!isPointerDown)
        {
            if (Input.GetMouseButtonDown(0) && IsOverInputSlider(Input.mousePosition))
            {
                isPointerDown = true;
                return;
            }
        }
        else
        {
            if (Input.GetMouseButtonUp(0))
            {
                isPointerDown = false;
                return;
            }

            if (!Input.GetMouseButton(0))
            {
                isPointerDown = false;
            }
        }
    }

    bool IsOverInputSlider(Vector2 screenPos)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(inputRect, screenPos, uiCamera);
    }

    static Camera ResolveUICamera(Canvas canvas)
    {
        if (canvas == null) return null;
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return canvas.worldCamera;
    }

    void InitStepIndex(float value)
    {
        if (stepSize <= 0f)
        {
            stepIndexInitialized = false;
            lastStepIndex = 0;
            return;
        }

        lastStepIndex = GetStepIndex(value);
        stepIndexInitialized = true;
    }

    int GetStepIndex(float value)
    {
        if (stepSize <= 0f) return 0;
        return Mathf.FloorToInt(value / stepSize);
    }

    void QueueStepSeIfCrossed(float prevValue, float newValue)
    {
        if (stepSe == StepSe.None) return;
        if (!stepIndexInitialized) { InitStepIndex(prevValue); return; }
        if (stepSize <= 0f) return;

        int prevIdx = lastStepIndex;
        int newIdx = GetStepIndex(newValue);
        if (newIdx == prevIdx) return;

        int dir = (newIdx > prevIdx) ? 1 : -1;

        float now = Time.unscaledTime;
        if (0.03 > 0f && now < nextSeTime)
        {
            lastStepIndex = newIdx;
            return;
        }

        PlayStepSe(newValue);
        nextSeTime = now + Mathf.Max(0f, 0.03f);

        lastStepIndex = newIdx;
    }

    void PlayStepSe(float newValue)
    {
        if (stepSe == StepSe.None) return;
        if (stepSe == StepSe.Kati)
        {
            AudioManager.Instance.PlaySE("click2");
        }
        if (stepSe == StepSe.body)
        {
            AudioManager.Instance.PlaySE("tap2", 1, 1 - Mathf.Clamp01(newValue));
        }

    }
}
