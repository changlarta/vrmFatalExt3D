using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ホース専用Spawner（slackベース）
/// + 見た目専用の液体ストリーム（ゲーム影響ゼロ）
///
/// 重要（あなたの要求）:
/// - ホースは伸びない: 終点を半径 (L - epsilon) にクランプ
/// - ホースは直線にならない: slack の下限で常に少し垂れる
/// - 液体はホース移動に“固体のように追従しない”
///   → 液体は履歴点列として保持し、重力で落下。新しく出る分だけノズル位置が変わる
/// - 液体は軽量: 最大32点の配列 + UIメッシュ1つ（生成/破棄なし、GameObject増殖なし）
/// </summary>
public class HoseLongPressDragSpawner : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    // ====== Item/UI wiring ======
    private ItemInfo itemInfo;
    private mainSideBar owner;
    private int itemIndex = -1;

    private Canvas canvas;
    private RectTransform canvasRect;
    private CameraSwing cameraSwing;

    private Image sourceImage;
    private Selectable selectable;

    private VrmToController vrm;

    // ====== Drag state ======
    private bool isDragging;
    private Vector2 currentPointerScreenPos;
    private Coroutine dragTickRoutine;

    // ====== Scene params ======
    private const float MaxDistance = 1000f;
    private const float DestroyDelay = 1.5f;

    private float gravity = 800f;
    private float lookDepth = 5f;

    // ====== Infinite eat tick ======
    private const float EatStepSeconds = 0.96f;
    private bool wasHitLastFrame;
    private float hitEnterUnscaledTime;

    // ====== token ======
    private int lookToken;
    private int currentToken;
    private bool ownsSceneFlags;

    // ====== Hose params ======
    private static readonly Vector2 HoseStartLocal = new Vector2(-250f, 300f);

    private float hoseLength;

    [Header("Hose Length (L)")]
    [SerializeField] private float extraLengthRatio = 0.20f; // d0の20%を余らせる
    [SerializeField] private float extraLengthMin = 80f;     // 最低でも+80px

    [Header("Non-stretch Clamp")]
    [SerializeField] private float lengthEpsilon = 8f;

    [Header("Never Straight (Slack floor)")]
    [SerializeField] private float slackMin = 20f;

    [Header("Sag Visual Scale")]
    [SerializeField] private float sagScale = 0.60f;

    // ====== Generated objects ======
    private RectTransform hoseRoot;
    private RectTransform hoseNozzle;
    private HoseArcRenderer hoseRenderer;

    // ====== Liquid VFX (visual only) ======
    private HoseLiquidStreamVFX liquidVfx;

    // ====== Public init ======
    public void Initialize(ItemInfo info, int index, mainSideBar owner)
    {
        itemInfo = info;
        itemIndex = index;
        this.owner = owner;

        if (sourceImage != null) sourceImage.sprite = info.icon;
        SetPriceText();
        ApplyLockVisual();
    }

    // ====== Unity ======
    private void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvasRect = canvas.transform as RectTransform;

        sourceImage = GetComponent<Image>();
        selectable = GetComponent<Selectable>();

        var cam = Camera.main;
        if (cam != null) cameraSwing = cam.GetComponent<CameraSwing>();
    }

    private void Start()
    {
        SetPriceText();
        ApplyLockVisual();

        var inst = VrmChrSceneController.Instance;
        if (inst != null) vrm = inst.vrmToController;
    }

    private void LateUpdate()
    {
        if (cameraSwing == null) return;

        // 既存実装に合わせてズームでパラメータを変える（不要なら固定でもOK）
        float z = cameraSwing.positionZ;
        gravity = 800 - z * 400;
        lookDepth = 3 + z * 13;

        // 液体はUI上で落ちるので、同じ gravity を渡しておく（見た目用）
        if (liquidVfx != null) liquidVfx.SetGravity(Mathf.Max(0f, gravity * 2.0f)); // 液体は少し強めに落とすと“流れ”が出やすい
    }

    private void OnDisable()
    {
        StopDragTick();
        DestroyHoseImmediate();

        if (ownsSceneFlags)
        {
            SetSceneFlagsNone();
            ownsSceneFlags = false;
        }

        if (vrm != null) vrm.ClearLookAtOverrideWorld();
    }

    // ====== Input ======
    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        if (hoseRoot != null) ReleaseCurrentHoseToFall();

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
                if (owner != null && itemIndex >= 0) owner.SetItemUnlocked(itemIndex, true);
                ApplyLockVisual();
            }
        }

        lookToken++;
        currentToken = lookToken;

        isDragging = true;
        ownsSceneFlags = true;

        wasHitLastFrame = false;
        hitEnterUnscaledTime = 0f;

        CreateHose();

        currentPointerScreenPos = eventData.position;

        if (TryGetCanvasLocal(currentPointerScreenPos, out var mouseLocal))
        {
            float d0 = Vector2.Distance(HoseStartLocal, mouseLocal);
            float extra = Mathf.Max(extraLengthMin, d0 * Mathf.Max(0f, extraLengthRatio));
            hoseLength = Mathf.Max(1f, d0 + extra);

            Vector2 endLocal = ClampEndToLength(mouseLocal, hoseLength, lengthEpsilon);
            SetNozzleLocal(endLocal);
            RedrawHose();
        }

        // 液体は「食べてない時に常に流す」なので初期はONにしておき、Tick側でhitに応じて切替
        if (liquidVfx != null) liquidVfx.SetEmitting(true);

        AudioManager.Instance.PlaySE("pay");
        StartDragTick();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (!isDragging) return;

        currentPointerScreenPos = eventData.position;

        if (hoseRoot == null) return;
        if (!TryGetCanvasLocal(currentPointerScreenPos, out var mouseLocal)) return;

        Vector2 endLocal = ClampEndToLength(mouseLocal, hoseLength, lengthEpsilon);
        SetNozzleLocal(endLocal);
        RedrawHose();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        isDragging = false;
        StopDragTick();

        if (hoseRoot == null || hoseNozzle == null || hoseRenderer == null)
        {
            CleanupSceneFlagsAndLook();
            return;
        }

        if (ownsSceneFlags && currentToken == lookToken) SetSceneFlagsFalling();

        // 落下中は食べてない扱いなので液体は流し続ける（演出）
        if (liquidVfx != null) liquidVfx.SetEmitting(true);

        var rootGo = hoseRoot.gameObject;
        var nozzleRt = hoseNozzle;
        int token = currentToken;
        float L = hoseLength;

        hoseRoot = null;
        hoseNozzle = null;
        hoseRenderer = null;
        liquidVfx = null;

        StartCoroutine(FallNozzleAndDestroy(rootGo, nozzleRt, token, L));
    }

    // ====== Drag loop ======
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
            // 判定・視線はマウス位置（演出の液体はゲーム影響なしだが、ON/OFF条件はこのhitを使う）
            bool hit = CheckHoverOn3D(currentPointerScreenPos);

            // 食べていない時に常に流す
            if (liquidVfx != null) liquidVfx.SetEmitting(!hit);

            HandleEatProgressInfinite(hit);

            if (currentToken == lookToken)
            {
                if (ownsSceneFlags) SetSceneFlagsDragging(hit);
                UpdateLookTarget(currentPointerScreenPos);
            }

            yield return null;
        }

        dragTickRoutine = null;
    }

    // ====== Core hose math ======
    private static Vector2 ClampEndToLength(Vector2 desiredEnd, float L, float eps)
    {
        float maxR = Mathf.Max(0.1f, L - Mathf.Max(0f, eps));
        Vector2 v = desiredEnd - HoseStartLocal;
        float d = v.magnitude;
        if (d <= maxR || d < 0.0001f) return desiredEnd;
        return HoseStartLocal + v * (maxR / d);
    }

    private void RedrawHose()
    {
        if (hoseRenderer == null || hoseNozzle == null) return;

        Vector2 start = HoseStartLocal;
        Vector2 end = hoseNozzle.anchoredPosition;

        float d = Vector2.Distance(start, end);

        float slack;
        if (hoseLength <= 0f) slack = slackMin;
        else
        {
            float L = hoseLength;
            slack = Mathf.Sqrt(Mathf.Max(0f, L * L - d * d));
            slack = Mathf.Max(slack, Mathf.Max(0f, slackMin));
        }

        float sag = slack * Mathf.Max(0f, sagScale);
        hoseRenderer.DrawSlackBezier(start, end, sag);
    }

    // ====== Falling ======
    private void ReleaseCurrentHoseToFall()
    {
        if (hoseRoot == null || hoseNozzle == null) return;

        isDragging = false;
        StopDragTick();

        if (ownsSceneFlags && currentToken == lookToken) SetSceneFlagsFalling();
        if (liquidVfx != null) liquidVfx.SetEmitting(true);

        var rootGo = hoseRoot.gameObject;
        var nozzleRt = hoseNozzle;
        int token = currentToken;
        float L = hoseLength;

        hoseRoot = null;
        hoseNozzle = null;
        hoseRenderer = null;
        liquidVfx = null;

        StartCoroutine(FallNozzleAndDestroy(rootGo, nozzleRt, token, L));
    }

    private IEnumerator FallNozzleAndDestroy(GameObject rootGo, RectTransform nozzle, int token, float L)
    {
        float t = 0f;
        float v = 0f;

        var renderer = rootGo != null ? rootGo.GetComponentInChildren<HoseArcRenderer>(true) : null;

        while (t < DestroyDelay)
        {
            if (rootGo == null || nozzle == null) yield break;

            float dt = Time.deltaTime;
            v += gravity * dt;

            Vector2 desired = nozzle.anchoredPosition + Vector2.down * (v * dt);
            Vector2 clamped = ClampEndToLength(desired, L, lengthEpsilon);
            nozzle.anchoredPosition = clamped;

            if (renderer != null)
            {
                float d = Vector2.Distance(HoseStartLocal, nozzle.anchoredPosition);
                float slack = Mathf.Sqrt(Mathf.Max(0f, L * L - d * d));
                slack = Mathf.Max(slack, Mathf.Max(0f, slackMin));
                float sag = slack * Mathf.Max(0f, sagScale);
                renderer.DrawSlackBezier(HoseStartLocal, nozzle.anchoredPosition, sag);
            }

            if (token == lookToken)
            {
                UpdateLookTargetFromRect(nozzle);
                if (ownsSceneFlags) SetSceneFlagsFalling();
            }

            t += dt;
            yield return null;
        }

        if (rootGo != null)
        {
            AudioManager.Instance.PlaySE("delete_item");
            Destroy(rootGo);
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

    // ====== Eating (infinite) ======
    private void HandleEatProgressInfinite(bool hitNow)
    {
        if (!hitNow)
        {
            if (wasHitLastFrame) hitEnterUnscaledTime = 0f;
            wasHitLastFrame = false;
            return;
        }

        var inst = VrmChrSceneController.Instance;
        var speechInst = VrmChrSceneTextController.Instance;

        if (!wasHitLastFrame) hitEnterUnscaledTime = Time.unscaledTime;

        float elapsed = Time.unscaledTime - hitEnterUnscaledTime;
        if (elapsed >= EatStepSeconds)
        {
            hitEnterUnscaledTime = Time.unscaledTime;

            if (inst != null)
            {
                inst.foodGauge = Mathf.Min(100, inst.foodGauge + itemInfo.careStomach);
                inst.loveGauge = Mathf.Min(100, inst.loveGauge + itemInfo.cal / 100f);
                inst.foodGaugeLine1 = Mathf.Min(100, inst.foodGaugeLine1 + itemInfo.addMaxStomach);
                inst.foodGaugePerTick = Mathf.Clamp(inst.foodGaugePerTick + itemInfo.addStomachSpeed, 0.05f, 0.8f);

                speechInst.setFoodSpeech(itemInfo.id);

                if (itemInfo.bustCal != 0)
                {
                    float key = inst.vrmToController.bustKey;
                    inst.SetBustKeyImmediate(Mathf.Clamp(key + itemInfo.bustCal, 0, 100));
                }

                if (itemInfo.faceCal != 0)
                {
                    float key = inst.vrmToController.face3Key;
                    inst.SetFace3KeyImmediate(Mathf.Clamp(key + itemInfo.faceCal, key > 30 ? 30 : 0, 100));
                }

                if (itemInfo.isDrinkSE == EatSE.drink) AudioManager.Instance.PlaySE("eat_drink");
                else if (itemInfo.isDrinkSE == EatSE.snack) AudioManager.Instance.PlaySE("eat_snack");
                else if (itemInfo.isDrinkSE == EatSE.drag) AudioManager.Instance.PlaySE("eat_drag");
                else if (itemInfo.isDrinkSE == EatSE.sugar) AudioManager.Instance.PlaySE("eat_sugar");
                else AudioManager.Instance.PlaySE("eat_soft");

                int cal = itemInfo.cal;
                inst.AddWeight(cal);
                inst.canvasUIController.calToastGenController.GenToast("+" + cal + "0kcal");
            }
        }

        wasHitLastFrame = true;
    }

    // ====== Hose creation ======
    private void CreateHose()
    {
        if (canvasRect == null) return;

        // コンテナ（Graphic無し）
        var containerGo = new GameObject("HoseContainer_" + gameObject.name, typeof(RectTransform));
        var containerRt = containerGo.GetComponent<RectTransform>();
        containerRt.SetParent(canvasRect, false);
        containerRt.anchorMin = new Vector2(0.5f, 0.5f);
        containerRt.anchorMax = new Vector2(0.5f, 0.5f);
        containerRt.pivot = canvasRect.pivot;
        containerRt.sizeDelta = canvasRect.rect.size;
        containerRt.anchoredPosition = Vector2.zero;
        containerRt.localScale = Vector3.one;
        containerRt.SetAsLastSibling();

        // 液体（背面）
        var liquidGo = new GameObject("LiquidStream", typeof(RectTransform), typeof(HoseLiquidStreamVFX));
        var liquidRt = liquidGo.GetComponent<RectTransform>();
        liquidRt.SetParent(containerRt, false);
        liquidRt.anchorMin = containerRt.anchorMin;
        liquidRt.anchorMax = containerRt.anchorMax;
        liquidRt.pivot = containerRt.pivot;
        liquidRt.sizeDelta = containerRt.sizeDelta;
        liquidRt.anchoredPosition = Vector2.zero;
        liquidRt.localScale = Vector3.one;
        liquidRt.SetAsFirstSibling(); // 背面へ

        // ホース線（前面）
        var hoseLineGo = new GameObject("HoseLine",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(UILineGraphic),
            typeof(HoseArcRenderer));

        var hoseLineRt = hoseLineGo.GetComponent<RectTransform>();
        hoseLineRt.SetParent(containerRt, false);
        hoseLineRt.anchorMin = containerRt.anchorMin;
        hoseLineRt.anchorMax = containerRt.anchorMax;
        hoseLineRt.pivot = containerRt.pivot;
        hoseLineRt.sizeDelta = containerRt.sizeDelta;
        hoseLineRt.anchoredPosition = Vector2.zero;
        hoseLineRt.localScale = Vector3.one;
        hoseLineRt.SetAsLastSibling(); // 最前面へ

        var line = hoseLineGo.GetComponent<UILineGraphic>();
        line.raycastTarget = false;

        line.color = new Color32(0, 60, 70, 255);

        // ノズル（位置だけ）
        var nozzleGo = new GameObject("Nozzle", typeof(RectTransform));
        var nozzleRt = nozzleGo.GetComponent<RectTransform>();
        nozzleRt.SetParent(containerRt, false);
        nozzleRt.anchorMin = new Vector2(0.5f, 0.5f);
        nozzleRt.anchorMax = new Vector2(0.5f, 0.5f);
        nozzleRt.pivot = containerRt.pivot;
        nozzleRt.sizeDelta = new Vector2(1f, 1f);
        nozzleRt.anchoredPosition = HoseStartLocal;

        // 参照を設定
        hoseRoot = containerRt;
        hoseNozzle = nozzleRt;
        hoseRenderer = hoseLineGo.GetComponent<HoseArcRenderer>();

        liquidVfx = liquidGo.GetComponent<HoseLiquidStreamVFX>();
        liquidVfx.BindEmitter(nozzleRt);

        // 液体色：#FFE7B1 = RGB(255,231,177)
        liquidVfx.SetBaseColor(new Color32(255, 230, 200, 255));

        hoseLength = 0f;
    }

    private void SetNozzleLocal(Vector2 canvasLocal)
    {
        if (hoseNozzle == null) return;
        hoseNozzle.anchoredPosition = canvasLocal;
    }

    private void DestroyHoseImmediate()
    {
        if (hoseRoot != null) Destroy(hoseRoot.gameObject);
        hoseRoot = null;
        hoseNozzle = null;
        hoseRenderer = null;
        liquidVfx = null;
    }

    // ====== Coordinate helpers ======
    private bool TryGetCanvasLocal(Vector2 screenPos, out Vector2 localPos)
    {
        localPos = default;
        if (canvasRect == null) return false;

        Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? canvas.worldCamera
            : null;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, screenPos, uiCam, out localPos);
    }

    // ====== 3D hover check ======
    private bool CheckHoverOn3D(Vector2 screenPos)
    {
        var cam = Camera.main;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(screenPos);
        int layerMask = 1 << LayerMask.NameToLayer("Default");

        if (Physics.Raycast(ray, out RaycastHit hit, MaxDistance, layerMask, QueryTriggerInteraction.Ignore))
        {
            var col = hit.collider;
            if (col != null && col.CompareTag("Facemouth")) return true;
        }
        return false;
    }

    // ====== Look control ======
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
        if (rt == null || canvas == null) return;

        Camera uiCam = (canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : canvas.worldCamera;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(uiCam, rt.position);
        UpdateLookTarget(screenPos);
    }

    // ====== Scene flags ======
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

    private void CleanupSceneFlagsAndLook()
    {
        HideFoodInfo();

        if (ownsSceneFlags)
        {
            SetSceneFlagsNone();
            ownsSceneFlags = false;
        }

        if (vrm != null && currentToken == lookToken) vrm.ClearLookAtOverrideWorld();
    }

    // ====== UI (price/lock/info) ======
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

    private bool IsLocked()
    {
        if (owner == null) return true;
        if (itemIndex < 0) return true;
        return !owner.IsItemUnlocked(itemIndex);
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
public class HoseLiquidStreamVFX : MonoBehaviour
{
    // 物理点（固定上限）
    [SerializeField] private int maxControlPoints = 20;

    // 表示の最大長さ（寿命で制御）
    [SerializeField] private float pointLifetime = 3f; // seconds

    // UI上の重力
    [SerializeField] private float gravity = 200f; // px/s^2

    // 一定流量：1秒あたり何点生成するか（流れの“量”）
    // ※点は「見える粒」ではなく制御点。描画は補間するので、この値で流量を安定させる。
    [SerializeField] private float emitRate = 15f; // points/sec

    // 描画（帯）
    [SerializeField] private float widthStart = 10f;
    [SerializeField] private float widthEnd = 5f;
    [SerializeField] private float alphaStart = 1f;
    [SerializeField] private float alphaEnd = 0.0f;

    // 補間の細かさ
    [SerializeField] private int splineSubdiv = 4;

    private RectTransform emitter;
    private bool emitting = true;

    // Control points: newest at [0], oldest at [count-1]
    private Vector2[] pos;
    private Vector2[] vel;
    private float[] age;
    private int count;

    // 一定流量用の時間積算
    private float emitAcc = 0f;

    // ノズル移動時に「同じ生成数を移動区間に配分」するための前フレーム位置
    private Vector2 lastEmitterPos;
    private bool hasLastEmitterPos;

    // 描画
    private LiquidRibbonGraphic ribbon;
    private readonly List<Vector2> renderPts = new List<Vector2>(256);

    private Color baseColor = Color.white;

    private void Awake()
    {
        int n = Mathf.Max(4, maxControlPoints);
        pos = new Vector2[n];
        vel = new Vector2[n];
        age = new float[n];
        count = 0;

        var go = new GameObject("LiquidRibbon", typeof(RectTransform), typeof(CanvasRenderer), typeof(LiquidRibbonGraphic));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);

        var selfRt = GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = selfRt != null ? selfRt.pivot : new Vector2(0.5f, 0.5f);
        rt.sizeDelta = selfRt != null ? selfRt.sizeDelta : Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;

        ribbon = go.GetComponent<LiquidRibbonGraphic>();
        ribbon.SetStyle(widthStart, widthEnd, alphaStart, alphaEnd);
        ribbon.color = baseColor;
        ribbon.SetPath(renderPts);
    }

    public void BindEmitter(RectTransform nozzle)
    {
        emitter = nozzle;

        // 状態を初期化（生成量が移動に依存しないように、ここで必ずリセット）
        emitAcc = 0f;
        hasLastEmitterPos = false;

        count = 0;
        renderPts.Clear();
        if (ribbon != null) ribbon.SetPath(renderPts);
    }

    public void SetEmitting(bool on) => emitting = on;
    public void SetGravity(float g) => gravity = Mathf.Max(0f, g);

    public void SetBaseColor(Color c)
    {
        baseColor = c;
        if (ribbon != null) ribbon.color = c;
    }

    private void Update()
    {
        if (emitter == null || ribbon == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        StepPoints(dt);

        if (emitting)
        {
            EmitByRate(dt, emitter.anchoredPosition);
        }

        BuildRenderPoints();
        ribbon.SetStyle(widthStart, widthEnd, alphaStart, alphaEnd);
        ribbon.SetPath(renderPts);
    }

    /// <summary>
    /// 既に出た液体を重力で落下 + 寿命で消す（発生後はノズル移動に追従しない）。
    /// </summary>
    private void StepPoints(float dt)
    {
        for (int i = 0; i < count; i++)
        {
            age[i] += dt;
            vel[i] += Vector2.down * gravity * dt;
            pos[i] += vel[i] * dt;
        }

        while (count > 0 && age[count - 1] > pointLifetime)
        {
            count--;
        }

        if (count == 0)
        {
            // 流れが完全に消えたら前位置の拘束も外す
            hasLastEmitterPos = false;
        }
    }

    /// <summary>
    /// 一定流量（points/sec）で点を出す。
    /// ノズルが動いた場合でも「点数は増やさず」移動区間に配分することで、
    /// “移動で流量が増える（二重処理）”を発生させない。
    /// </summary>
    private void EmitByRate(float dt, Vector2 currentEmitterPos)
    {
        float rate = Mathf.Max(0f, emitRate);
        if (rate <= 0f) return;

        emitAcc += dt * rate;
        int n = Mathf.FloorToInt(emitAcc);
        if (n <= 0)
        {
            // 流量は一定なので「移動したから出す」はしない
            if (!hasLastEmitterPos) { lastEmitterPos = currentEmitterPos; hasLastEmitterPos = true; }
            return;
        }

        emitAcc -= n;

        // 初回は現在位置から開始
        if (!hasLastEmitterPos)
        {
            lastEmitterPos = currentEmitterPos;
            hasLastEmitterPos = true;
        }

        Vector2 from = lastEmitterPos;
        Vector2 to = currentEmitterPos;

        // n個を[0..1]に均等配分。動いていれば移動区間に散らし、止まっていれば全部同一点に出る。
        for (int i = 1; i <= n; i++)
        {
            float t = (n == 1) ? 1f : (i / (float)n);
            Vector2 p = Vector2.LerpUnclamped(from, to, t);

            // 初速は弱い下向き（噴射にならない）
            PushFront(p, Vector2.down * 50f);
        }

        lastEmitterPos = currentEmitterPos;
    }

    /// <summary>
    /// newestを先頭に詰める（max32想定なのでO(N)シフトで十分軽い）
    /// </summary>
    private void PushFront(Vector2 p, Vector2 initialVel)
    {
        int cap = pos.Length;
        int newCount = Mathf.Min(count + 1, cap);

        for (int i = newCount - 1; i >= 1; i--)
        {
            pos[i] = pos[i - 1];
            vel[i] = vel[i - 1];
            age[i] = age[i - 1];
        }

        pos[0] = p;
        vel[0] = initialVel;
        age[0] = 0f;

        count = newCount;
    }

    /// <summary>
    /// 制御点列から Catmull-Rom で描画点列を作る（見た目は滑らか、保持点は少ない）。
    /// </summary>
    private void BuildRenderPoints()
    {
        renderPts.Clear();
        if (count <= 1) return;

        if (count == 2)
        {
            renderPts.Add(pos[0]);
            renderPts.Add(pos[1]);
            return;
        }

        int subdiv = Mathf.Clamp(splineSubdiv, 1, 12);

        for (int i = 0; i < count - 1; i++)
        {
            Vector2 p0 = pos[Mathf.Max(i - 1, 0)];
            Vector2 p1 = pos[i];
            Vector2 p2 = pos[i + 1];
            Vector2 p3 = pos[Mathf.Min(i + 2, count - 1)];

            int jMax = (i == count - 2) ? subdiv : subdiv - 1;
            for (int j = 0; j <= jMax; j++)
            {
                float t = j / (float)subdiv;
                renderPts.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }
}


/// <summary>
/// 連続した帯（リボン）を描くUI Graphic。
/// - 点列を入力として、線分ごとに四角形を貼って帯にする
/// - t(0..1)で太さ・アルファを変える（上が太く濃く、下が細く薄い）
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class LiquidRibbonGraphic : MaskableGraphic
{
    private readonly List<Vector2> pts = new List<Vector2>(256);

    private float widthStart = 10f;
    private float widthEnd = 2f;
    private float alphaStart = 0.85f;
    private float alphaEnd = 0.0f;

    public void SetStyle(float w0, float w1, float a0, float a1)
    {
        widthStart = Mathf.Max(0.5f, w0);
        widthEnd = Mathf.Max(0.5f, w1);
        alphaStart = Mathf.Clamp01(a0);
        alphaEnd = Mathf.Clamp01(a1);
        SetVerticesDirty();
    }

    public void SetPath(List<Vector2> source)
    {
        pts.Clear();
        if (source != null)
        {
            for (int i = 0; i < source.Count; i++) pts.Add(source[i]);
        }
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (pts.Count < 2) return;

        Color baseCol = color;

        int segCount = pts.Count - 1;
        for (int i = 0; i < segCount; i++)
        {
            Vector2 a = pts[i];
            Vector2 b = pts[i + 1];

            Vector2 dir = b - a;
            float len = dir.magnitude;
            if (len < 0.001f) continue;
            dir /= len;

            Vector2 n = new Vector2(-dir.y, dir.x);

            float t0 = (segCount <= 1) ? 0f : (i / (float)segCount);
            float t1 = (segCount <= 1) ? 1f : ((i + 1) / (float)segCount);

            float w0 = Mathf.Lerp(widthStart, widthEnd, t0) * 0.5f;
            float w1 = Mathf.Lerp(widthStart, widthEnd, t1) * 0.5f;

            Color c0 = baseCol; c0.a = baseCol.a * Mathf.Lerp(alphaStart, alphaEnd, t0);
            Color c1 = baseCol; c1.a = baseCol.a * Mathf.Lerp(alphaStart, alphaEnd, t1);

            Vector2 aL = a - n * w0;
            Vector2 aR = a + n * w0;
            Vector2 bL = b - n * w1;
            Vector2 bR = b + n * w1;

            int idx = vh.currentVertCount;

            vh.AddVert(aL, c0, Vector2.zero);
            vh.AddVert(aR, c0, Vector2.zero);
            vh.AddVert(bR, c1, Vector2.zero);
            vh.AddVert(bL, c1, Vector2.zero);

            vh.AddTriangle(idx + 0, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx + 0);
        }
    }
}

/// <summary>
/// Canvas上にポリラインを描く（ホース用）
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class UILineGraphic : MaskableGraphic
{
    [SerializeField] private float thickness = 12f;

    public float Thickness
    {
        get => thickness;
        set { thickness = Mathf.Max(0.5f, value); SetVerticesDirty(); }
    }

    private readonly List<Vector2> points = new List<Vector2>(64);

    public void SetPoints(IReadOnlyList<Vector2> src)
    {
        points.Clear();
        if (src != null)
        {
            for (int i = 0; i < src.Count; i++) points.Add(src[i]);
        }
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (points.Count < 2) return;

        float half = thickness * 0.5f;
        var col = color;

        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[i + 1];
            Vector2 dir = b - a;
            float len = dir.magnitude;
            if (len < 0.001f) continue;
            dir /= len;

            Vector2 n = new Vector2(-dir.y, dir.x) * half;

            int idx = vh.currentVertCount;
            vh.AddVert(a - n, col, Vector2.zero);
            vh.AddVert(a + n, col, Vector2.zero);
            vh.AddVert(b + n, col, Vector2.zero);
            vh.AddVert(b - n, col, Vector2.zero);

            vh.AddTriangle(idx + 0, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx + 0);
        }
    }
}

/// <summary>
/// slack（余り長さ）→ sag（垂れ）で二次ベジェを描画するホースレンダラ
/// </summary>
[RequireComponent(typeof(UILineGraphic))]
public class HoseArcRenderer : MonoBehaviour
{
    [SerializeField] private int segments = 32;

    private UILineGraphic line;
    private readonly List<Vector2> tmp = new List<Vector2>(64);

    private void Awake()
    {
        line = GetComponent<UILineGraphic>();
        line.raycastTarget = false;
    }

    public void DrawSlackBezier(Vector2 start, Vector2 end, float sag)
    {
        if (line == null) return;

        Vector2 mid = (start + end) * 0.5f;
        Vector2 control = mid + Vector2.down * Mathf.Max(0f, sag);

        int n = Mathf.Max(2, segments);
        tmp.Clear();

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            float u = 1f - t;
            Vector2 p = (u * u) * start + (2f * u * t) * control + (t * t) * end;
            tmp.Add(p);
        }

        line.SetPoints(tmp);
    }
}
