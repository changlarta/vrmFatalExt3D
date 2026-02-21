using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class LongPressDragSpawner : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    private ItemInfo itemInfo = default;

    private Canvas canvas;
    private CameraSwing cameraSwing;

    private RectTransform previewInstance;
    private RectTransform previewMaskRect;
    private RectTransform previewImageRect;
    private Image previewImage;
    private Vector2 currentPointerScreenPos;

    private Image sourceImage;
    private RectTransform sourceRect;
    private Selectable selectable;

    private bool isDragging = false;
    private Coroutine dragTickRoutine;

    private VrmToController vrm;

    private const float MaxDistance = 1000f;

    private const float AlphaNormal = 0.8f;
    private const float AlphaHit = 0.25f;

    private const float DestroyDelay = 1.5f;

    private float gravity = 800f;

    private float lookDepth = 5f;

    private Vector2 basePreviewSize;
    private float previewScale = 1f;

    private bool ownsSceneFlags = false;

    private const float EatStepSeconds = 0.96f;

    private int eatStage = 0;
    private bool wasHitLastFrame = false;
    private float hitEnterUnscaledTime = 0f;

    private int lookToken = 0;
    private int currentToken = 0;

    private mainSideBar owner = null;
    private int itemIndex = -1;

    private bool IsLocked()
    {
        if (owner == null) return true;
        if (itemIndex < 0) return true;
        return !owner.IsItemUnlocked(itemIndex);
    }

    public void Initialize(ItemInfo info, int index, mainSideBar owner)
    {
        itemInfo = info;
        itemIndex = index;
        this.owner = owner;

        if (sourceImage != null)
        {
            sourceImage.sprite = info.icon;
        }

        SetPriceText();
        ApplyLockVisual();
    }

    void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
        sourceImage = GetComponent<Image>();
        sourceRect = GetComponent<RectTransform>();
        selectable = GetComponent<Selectable>();

        var cam = Camera.main;
        if (cam != null) cameraSwing = cam.GetComponent<CameraSwing>();
    }

    void Start()
    {
        SetPriceText();
        ApplyLockVisual();

        var inst = VrmChrSceneController.Instance;
        vrm = inst.vrmToController;
    }

    void OnEnable()
    {
        ApplyLockVisual();
    }

    private void OnDisable()
    {
        StopDragTick();

        if (ownsSceneFlags)
        {
            SetSceneFlagsNone();
            ownsSceneFlags = false;
        }

        if (vrm != null) vrm.ClearLookAtOverrideWorld();
    }

    private void LateUpdate()
    {
        if (cameraSwing == null) return;

        var z = cameraSwing.positionZ;

        gravity = 800 - z * 400;
        lookDepth = 3 + z * 13;

        float t = Mathf.Clamp01(z);
        previewScale = Mathf.Lerp(1.3f, 0.4f, t);

        if (previewInstance != null)
        {
            previewInstance.sizeDelta = basePreviewSize * previewScale;
            ApplyEatStageVisual();
        }
    }

    private void ReleaseCurrentPreviewToFall()
    {
        if (previewInstance == null) return;

        var inst = VrmChrSceneController.Instance;

        isDragging = false;
        StopDragTick();

        if (inst != null && ownsSceneFlags && currentToken == lookToken)
            SetSceneFlagsFalling();

        var rt = previewInstance;
        var token = currentToken;

        StartCoroutine(FallAndDestroy(rt, token));

        previewInstance = null;
        previewMaskRect = null;
        previewImageRect = null;
        previewImage = null;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        if (previewInstance != null)
            ReleaseCurrentPreviewToFall();

        var inst = VrmChrSceneController.Instance;
        int value = itemInfo.price;

        if (inst != null && inst.coin < value)
        {
            isDragging = false;
            AudioManager.Instance.PlaySE("beep");
            ShowFoodInfo();
            return;
        }

        if (inst != null)
        {
            inst.coin -= value;

            if (IsLocked())
            {
                if (owner != null && itemIndex >= 0)
                {
                    owner.SetItemUnlocked(itemIndex, true);
                }
                ApplyLockVisual();
            }
        }

        lookToken++;
        currentToken = lookToken;

        currentPointerScreenPos = eventData.position;
        isDragging = true;
        ownsSceneFlags = true;

        eatStage = 0;
        wasHitLastFrame = false;
        hitEnterUnscaledTime = 0f;

        previewInstance = null;
        previewMaskRect = null;
        previewImageRect = null;
        previewImage = null;

        CreatePreview();
        UpdatePreviewPosition(currentPointerScreenPos);
        ApplyEatStageVisual();

        AudioManager.Instance.PlaySE("pay");
        StartDragTick();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (!isDragging) return;

        currentPointerScreenPos = eventData.position;

        if (previewInstance != null)
            UpdatePreviewPosition(currentPointerScreenPos);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        isDragging = false;
        StopDragTick();

        if (previewInstance == null)
        {
            HideFoodInfo();

            if (ownsSceneFlags)
            {
                SetSceneFlagsNone();
                ownsSceneFlags = false;
            }

            if (vrm != null && currentToken == lookToken)
            {
                vrm.ClearLookAtOverrideWorld();
            }

            return;
        }

        if (ownsSceneFlags && currentToken == lookToken) SetSceneFlagsFalling();

        var rt = previewInstance;
        var token = currentToken;

        StartCoroutine(FallAndDestroy(rt, token));

        previewInstance = null;
        previewMaskRect = null;
        previewImageRect = null;
        previewImage = null;
    }

    private void StartDragTick()
    {
        if (dragTickRoutine != null) return;
        dragTickRoutine = StartCoroutine(DragTick());
        ShowFoodInfo();
    }

    private void StopDragTick()
    {
        if (dragTickRoutine == null) return;
        StopCoroutine(dragTickRoutine);
        dragTickRoutine = null;
        HideFoodInfo();
    }

    private IEnumerator DragTick()
    {
        while (isDragging)
        {
            if (previewInstance == null)
            {
                yield return null;
                continue;
            }

            bool hit = UpdateHoverAlpha(currentPointerScreenPos);
            HandleEatProgress(hit);

            if (previewInstance != null && currentToken == lookToken)
            {
                if (ownsSceneFlags) SetSceneFlagsDragging(hit);
                UpdateLookTarget(currentPointerScreenPos);
            }

            yield return null;
        }

        dragTickRoutine = null;
    }

    private void HandleEatProgress(bool hitNow)
    {
        if (previewInstance == null) return;

        int eatSteps = itemInfo.eatSteps;
        if (eatSteps <= 0) return;
        if (eatStage >= eatSteps) return;

        if (hitNow)
        {
            var inst = VrmChrSceneController.Instance;
            var speechInst = VrmChrSceneTextController.Instance;
            if (!wasHitLastFrame)
            {
                hitEnterUnscaledTime = Time.unscaledTime;
            }

            float elapsed = Time.unscaledTime - hitEnterUnscaledTime;

            if (elapsed >= EatStepSeconds)
            {
                eatStage = Mathf.Min(eatStage + 1, eatSteps);
                ApplyEatStageVisual();
                hitEnterUnscaledTime = Time.unscaledTime;

                inst.isFirstEated = true;

                if (inst != null)
                {
                    inst.foodGauge = Mathf.Min(100, inst.foodGauge + itemInfo.careStomach);
                    inst.loveGauge = Mathf.Min(100, inst.loveGauge + itemInfo.cal / 100f);
                    inst.foodGaugeLine1 = Mathf.Min(100, inst.foodGaugeLine1 + itemInfo.addMaxStomach);
                    inst.foodGaugePerTick = Mathf.Clamp(inst.foodGaugePerTick + itemInfo.addStomachSpeed, 0.05f, 0.8f);

                    speechInst.setFoodSpeech(itemInfo.id);

                    if (itemInfo.bustCal != 0)
                    {
                        var key = inst.vrmToController.bustKey;
                        var x = Mathf.Clamp(key + itemInfo.bustCal, 0, 100);
                        inst.SetBustKeyImmediate(x);
                    }

                    if (itemInfo.faceCal != 0)
                    {
                        var key = inst.vrmToController.face3Key;
                        var x = Mathf.Clamp(key + itemInfo.faceCal, key > 30 ? 30 : 0, 100);
                        inst.SetFace3KeyImmediate(x);
                    }

                    if (itemInfo.isDrinkSE == EatSE.drink)
                    {
                        AudioManager.Instance.PlaySE("eat_drink");
                    }
                    else if (itemInfo.isDrinkSE == EatSE.snack)
                    {
                        AudioManager.Instance.PlaySE("eat_snack");
                    }
                    else if (itemInfo.isDrinkSE == EatSE.drag)
                    {
                        AudioManager.Instance.PlaySE("eat_drag");
                    }
                    else if (itemInfo.isDrinkSE == EatSE.sugar)
                    {
                        AudioManager.Instance.PlaySE("eat_sugar");
                    }
                    else
                    {
                        AudioManager.Instance.PlaySE("eat_soft");
                    }

                    int cal = itemInfo.cal;
                    inst.AddWeight(cal);
                    inst.canvasUIController.calToastGenController.GenToast("+" + cal + "0kcal");
                }

                if (eatStage >= eatSteps)
                {
                    var token = currentToken;

                    Destroy(previewInstance.gameObject);
                    previewInstance = null;
                    previewMaskRect = null;
                    previewImageRect = null;
                    previewImage = null;

                    if (vrm != null && token == lookToken) vrm.ClearLookAtOverrideWorld();

                    if (ownsSceneFlags && token == lookToken)
                    {
                        SetSceneFlagsNone();
                        ownsSceneFlags = false;
                    }

                    StopDragTick();
                }
            }
        }
        else
        {
            if (wasHitLastFrame)
            {
                hitEnterUnscaledTime = 0f;
            }
        }

        wasHitLastFrame = hitNow;
    }

    private void ApplyEatStageVisual()
    {
        if (previewInstance == null || previewMaskRect == null || previewImageRect == null) return;

        int eatSteps = itemInfo.eatSteps;
        if (eatSteps <= 0) return;

        float remain01 = (eatSteps - Mathf.Clamp(eatStage, 0, eatSteps)) / (float)eatSteps;

        float fullH = previewInstance.sizeDelta.y;
        float visibleH = Mathf.Max(0f, fullH * remain01);

        var imgSd = previewImageRect.sizeDelta;
        imgSd.y = fullH;
        previewImageRect.sizeDelta = imgSd;

        var maskSd = previewMaskRect.sizeDelta;
        maskSd.y = visibleH;
        previewMaskRect.sizeDelta = maskSd;
    }

    private void UpdateLookTarget(Vector2 screenPos)
    {
        if (vrm == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(screenPos);
        Vector3 worldPos = ray.GetPoint(lookDepth);
        vrm.SetLookAtOverrideWorld(worldPos);
    }

    private void UpdateLookTargetFromRect(RectTransform rt)
    {
        if (rt == null) return;
        if (canvas == null) return;

        Camera uiCam = (canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : canvas.worldCamera;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(uiCam, rt.position);

        UpdateLookTarget(screenPos);
    }

    private bool UpdateHoverAlpha(Vector2 screenPos)
    {
        if (previewImage == null) return false;

        bool hit = CheckHoverOn3D(screenPos);
        SetPreviewAlpha(hit ? AlphaHit : AlphaNormal);
        return hit;
    }

    private void SetPreviewAlpha(float a)
    {
        if (previewImage == null) return;
        var c = previewImage.color;
        c.a = a;
        previewImage.color = c;
    }

    private bool CheckHoverOn3D(Vector2 screenPos)
    {
        var cam = Camera.main;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(screenPos);

        int layerMask = 1 << LayerMask.NameToLayer("Default");

        if (Physics.Raycast(ray, out RaycastHit hit, MaxDistance, layerMask, QueryTriggerInteraction.Ignore))
        {
            var col = hit.collider;
            if (col != null && col.CompareTag("Facemouth"))
                return true;
        }

        return false;
    }

    private void SetSceneFlagsDragging(bool hitNow)
    {
        var inst = VrmChrSceneController.Instance;
        if (inst == null) return;

        if (hitNow)
        {
            inst.isEating = true;
            inst.isFoodWait = false;
        }
        else
        {
            inst.isFoodWait = true;
            inst.isEating = false;
        }
    }

    private void SetSceneFlagsFalling()
    {
        var inst = VrmChrSceneController.Instance;
        if (inst == null) return;

        inst.isFoodWait = true;
        inst.isEating = false;
    }

    private void SetSceneFlagsNone()
    {
        var inst = VrmChrSceneController.Instance;
        if (inst == null) return;

        inst.isFoodWait = false;
        inst.isEating = false;
    }

    private IEnumerator FallAndDestroy(RectTransform rt, int token)
    {
        float t = 0f;
        float v = 0f;

        while (t < DestroyDelay)
        {
            if (rt == null) yield break;

            float dt = Time.deltaTime;

            v += gravity * dt;
            rt.anchoredPosition += Vector2.down * (v * dt);

            if (token == lookToken)
            {
                UpdateLookTargetFromRect(rt);
                if (ownsSceneFlags) SetSceneFlagsFalling();
            }

            t += dt;
            yield return null;
        }

        if (rt != null)
        {
            var inst = VrmChrSceneController.Instance;
            var speechInst = VrmChrSceneTextController.Instance;
            inst.dropCount++;
            if (!inst.isFirstEated && inst.dropCount >= 10)
            {
                speechInst.setDropSpeech(inst.dropCount);
            }

            AudioManager.Instance.PlaySE("delete_item");
            Destroy(rt.gameObject);
        }

        if (token == lookToken)
        {
            if (vrm != null) vrm.ClearLookAtOverrideWorld();

            if (ownsSceneFlags)
            {
                SetSceneFlagsNone();
                ownsSceneFlags = false;
            }
        }
    }

    private void CreatePreview()
    {
        if (previewInstance != null) return;
        if (canvas == null) return;
        if (sourceImage == null || sourceRect == null) return;

        GameObject root = new GameObject("DragPreview_" + gameObject.name, typeof(RectTransform));
        RectTransform rootRt = root.GetComponent<RectTransform>();

        rootRt.SetParent(canvas.transform, false);
        rootRt.SetAsLastSibling();

        rootRt.pivot = sourceRect.pivot;
        rootRt.anchorMin = sourceRect.anchorMin;
        rootRt.anchorMax = sourceRect.anchorMax;

        Vector2 baseSize = sourceRect.rect.size;
        basePreviewSize = baseSize * 1.5f;
        rootRt.sizeDelta = basePreviewSize * previewScale;

        GameObject maskGo = new GameObject("Mask", typeof(RectTransform), typeof(RectMask2D));
        RectTransform maskRt = maskGo.GetComponent<RectTransform>();
        maskRt.SetParent(rootRt, false);

        maskRt.anchorMin = new Vector2(0f, 0f);
        maskRt.anchorMax = new Vector2(1f, 0f);
        maskRt.pivot = new Vector2(0.5f, 0f);
        maskRt.anchoredPosition = Vector2.zero;
        maskRt.sizeDelta = new Vector2(0f, rootRt.sizeDelta.y);

        GameObject imgGo = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform imgRt = imgGo.GetComponent<RectTransform>();
        Image img = imgGo.GetComponent<Image>();

        imgRt.SetParent(maskRt, false);

        imgRt.anchorMin = new Vector2(0f, 0f);
        imgRt.anchorMax = new Vector2(1f, 0f);
        imgRt.pivot = new Vector2(0.5f, 0f);
        imgRt.anchoredPosition = Vector2.zero;
        imgRt.sizeDelta = new Vector2(0f, rootRt.sizeDelta.y);

        img.sprite = sourceImage.sprite;
        img.type = sourceImage.type;
        img.preserveAspect = sourceImage.preserveAspect;
        img.raycastTarget = false;

        img.color = IsLocked()
            ? new Color(0f, 0f, 0f, AlphaNormal)
            : new Color(1f, 1f, 1f, AlphaNormal);

        previewInstance = rootRt;
        previewMaskRect = maskRt;
        previewImageRect = imgRt;
        previewImage = img;
    }

    private void UpdatePreviewPosition(Vector2 screenPos)
    {
        if (canvas == null || previewInstance == null) return;

        RectTransform canvasRect = canvas.transform as RectTransform;

        Camera uiCam = (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            ? null
            : canvas.worldCamera;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPos,
            uiCam,
            out Vector2 localPos))
        {
            localPos.y -= 220f;
            previewInstance.anchoredPosition = localPos;
        }
    }

    private void SetPriceText()
    {
        if (transform == null) return;
        if (transform.childCount <= 2) return;

        var child = transform.GetChild(2);
        if (child == null) return;

        var tmp = child.GetComponent<TextMeshProUGUI>();
        if (tmp == null) return;

        tmp.text = itemInfo.price.ToString();
    }

    private void ApplyLockVisual()
    {
        if (sourceImage == null) return;

        float a = sourceImage.color.a;
        bool locked = IsLocked();

        Color target = locked ? new Color(0f, 0f, 0f, a) : new Color(1f, 1f, 1f, a);

        sourceImage.color = target;

        if (selectable != null)
        {
            var cb = selectable.colors;
            cb.normalColor = target;
            cb.highlightedColor = target;
            cb.pressedColor = target;
            cb.selectedColor = target;
            cb.disabledColor = target;
            selectable.colors = cb;
        }
    }

    private void ShowFoodInfo()
    {
        var inst = VrmChrSceneController.Instance;
        if (inst == null) return;
        inst.canvasUIController.foodInfo.setInfo(itemInfo, itemInfo.icon, IsLocked());
    }

    private void HideFoodInfo()
    {
        var inst = VrmChrSceneController.Instance;
        if (inst == null) return;
        inst.canvasUIController.foodInfo.hide();
    }
}
