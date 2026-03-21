using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PlayerBarsAutoBuild : MonoBehaviour
{
    [Header("Required")]
    public GameObject target;                 // PlayerController が付いている
    public ClickShootFromCenter chargeSource; // チャージ参照元（必須）

    [Header("HP Gauge Size (px)")]
    [SerializeField] private float hpWidth = 220f;
    [SerializeField] private float hpHeight = 18f;

    [Header("HP Overflow overlay (left aligned, slight overlap)")]
    [SerializeField] private float hpOverflowYOffset = -6f;

    [Header("Charge Gauge (fixed, under HP)")]
    [SerializeField] private float chargeTopOffset = 10f;
    [SerializeField] private float chargeWidth = 200f;
    [SerializeField] private float chargeHeight = 12f;

    [Header("Fatigue Gauge (moved further down)")]
    [SerializeField] private float fatigueTopOffsetFromCharge = 10f;
    [SerializeField] private float fatigueWidth = 180f;
    [SerializeField] private float fatigueHeight = 12f;

    [Header("Common Colors")]
    [SerializeField] private Color backColor = new Color(0f, 0f, 0f, 0.45f);

    [Header("HP Colors")]
    [SerializeField] private Color hpGreen = Color.green;
    [SerializeField] private Color hpYellow = Color.yellow;
    [SerializeField] private Color hpRed = Color.red;

    [Header("Charge Colors")]
    [SerializeField] private Color chargeGray = new Color(0.70f, 0.70f, 0.70f, 1f); // 灰色（固定）

    [Header("Fatigue Colors")]
    [SerializeField] private Color fatigueYellow = Color.yellow;
    [SerializeField] private Color fatigueHotOrange = new Color(1f, 0.35f, 0.05f, 1f);

    [Header("Frame")]
    [SerializeField] private float frameThickness = 1.5f;
    [Range(0.05f, 0.95f)]
    [SerializeField] private float frameShade = 0.35f;

    [Header("Charge marker")]
    [SerializeField] private float markerThickness = 1.5f;
    [SerializeField] private Color markerColor = new Color(1f, 1f, 1f, 0.65f);

    private PlayerController player;
    private Sprite whiteSquareSprite;

    // HP main
    private RectTransform hpRoot;
    private Image hpFill;
    private Image[] hpFrameLines;

    // HP overflow
    private RectTransform hpOverflowRoot;
    private Image hpOverflowFill;
    private RectTransform hpOverflowFrameFitRoot;
    private Image[] hpOverflowFrameLines;

    // Charge
    private RectTransform chargeRoot;
    private Image chargeFill;
    private Image[] chargeFrameLines;
    private RectTransform chargeHalfMarkerRt;

    // Fatigue
    private RectTransform fatigueRoot;
    private Image fatigueFill;
    private Image[] fatigueFrameLines;

    // --------- edge detection (sound triggers) ---------
    private bool prevHpWasRed = false;
    private float prevChargeSec = 0f;

    private float prevCharge01 = 0f;
    private bool halfSeFired = false;
    private bool fullSeFired = false;

    private void Awake()
    {
        if (GetComponent<RectTransform>() == null)
        {
            Debug.LogError("[PlayerBarsAutoBuild] RectTransform is required (UI object only).");
            enabled = false; return;
        }
        if (target == null)
        {
            Debug.LogError("[PlayerBarsAutoBuild] target is null.");
            enabled = false; return;
        }
        player = target.GetComponent<PlayerController>();
        if (player == null)
        {
            Debug.LogError("[PlayerBarsAutoBuild] PlayerController missing on target.");
            enabled = false; return;
        }
        if (chargeSource == null)
        {
            Debug.LogError("[PlayerBarsAutoBuild] chargeSource is null (assign ClickShootFromCenter).");
            enabled = false; return;
        }
        if (transform.childCount != 0)
        {
            Debug.LogError("[PlayerBarsAutoBuild] This component requires zero children (auto-build only).");
            enabled = false; return;
        }
        if (frameThickness <= 0f || markerThickness <= 0f)
        {
            Debug.LogError("[PlayerBarsAutoBuild] thickness must be > 0.");
            enabled = false; return;
        }

        whiteSquareSprite = Create1x1WhiteSprite();
        Build();
    }

    private void Update()
    {
        // ---------------- HP ----------------
        int max = Mathf.Max(1, player.maxHP);
        int hp = Mathf.Clamp(player.CurrentHP, 0, max * 2);

        int main = Mathf.Min(hp, max);
        int over = Mathf.Max(0, hp - max);

        float mainRatio = (float)main / max;
        hpFill.fillAmount = Mathf.Clamp01(mainRatio);

        bool hpIsRed = mainRatio <= 0.20f;

        Color hpColor;
        if (hpIsRed) hpColor = hpRed;
        else if (mainRatio <= 0.50f) hpColor = hpYellow;
        else hpColor = hpGreen;

        hpFill.color = hpColor;
        SetLineColors(hpFrameLines, Darken(hpColor, frameShade));

        // HP red entered -> SE
        if (!prevHpWasRed && hpIsRed)
        {
            AudioManager.Instance.PlaySE("warn");
        }
        prevHpWasRed = hpIsRed;

        bool showOver = over > 0;
        hpOverflowRoot.gameObject.SetActive(showOver);

        if (showOver)
        {
            float overRatio = Mathf.Clamp01((float)over / max);
            hpOverflowFill.fillAmount = overRatio;
            hpOverflowFill.color = hpGreen;

            hpOverflowFrameFitRoot.sizeDelta = new Vector2(hpWidth * overRatio, hpHeight);
            SetLineColors(hpOverflowFrameLines, Darken(hpGreen, frameShade));
        }

        // ---------------- Charge ----------------
        float mul = 1f + 0.5f * (player.currentBodyKey / 100f);
        float fullX = Mathf.Max(1e-6f, mul * chargeSource.fullChargeSeconds);
        float halfX = Mathf.Clamp(mul * chargeSource.halfChargeSeconds, 0f, fullX);

        float chargeSec = Mathf.Clamp(chargeSource.ChargeTimerSeconds, 0f, fullX);

        // 割合で判定（重要）
        float charge01 = Mathf.Clamp01(chargeSec / fullX);
        float half01 = Mathf.Clamp01(halfX / fullX);

        // 表示
        chargeFill.fillAmount = charge01;
        // halfマーカー
        PlaceMarker(chargeHalfMarkerRt, chargeWidth, half01);

        // チャージが「減った」＝発射などでリセットされた、の検出でラッチ解除
        // （fullX変動で微妙に下がる場合があるので、少し余裕を持たせる）
        if (charge01 + 1e-4f < prevCharge01)
        {
            halfSeFired = false;
            fullSeFired = false;
        }

        // half跨ぎ（1回だけ）
        if (!halfSeFired && prevCharge01 < half01 && charge01 >= half01)
        {
            AudioManager.Instance.PlaySE("card");
            halfSeFired = true;
        }

        // max到達（1回だけ）
        if (!fullSeFired && prevCharge01 < 1f && charge01 >= 1f - 1e-6f)
        {
            AudioManager.Instance.PlaySE("card");
            fullSeFired = true;
        }

        prevCharge01 = charge01;

        // ---------------- Fatigue ----------------
        bool showFatigue = player.fatigue > 0f;
        fatigueRoot.gameObject.SetActive(showFatigue);

        if (showFatigue)
        {
            float f01 = Mathf.Clamp01(player.fatigue / 100f);
            fatigueFill.fillAmount = f01;

            bool hot = player.exhausted || (player.fatigue > 50f);
            Color fColor = hot ? fatigueHotOrange : fatigueYellow;

            fatigueFill.color = fColor;
            SetLineColors(fatigueFrameLines, Darken(fColor, frameShade));
        }
    }

    private void Build()
    {
        // HP root
        hpRoot = CreateRoot("HP_Main", (RectTransform)transform,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            Vector2.zero, new Vector2(hpWidth, hpHeight));

        CreateSimpleImage(hpRoot, "Back", backColor);
        hpFill = CreateFilledImage(hpRoot, "Fill", hpGreen);
        hpFrameLines = CreateFrame(hpRoot, "Frame", Color.black, frameThickness);

        // HP overflow
        hpOverflowRoot = CreateRoot("HP_Overflow", hpRoot,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, hpOverflowYOffset), new Vector2(hpWidth, hpHeight));

        hpOverflowFill = CreateFilledImage(hpOverflowRoot, "Fill", hpGreen);

        hpOverflowFrameFitRoot = CreateRoot("FrameFit", hpOverflowRoot,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            Vector2.zero, new Vector2(0f, hpHeight));

        hpOverflowFrameLines = CreateFrame(hpOverflowFrameFitRoot, "Frame", Color.black, frameThickness);
        hpOverflowRoot.gameObject.SetActive(false);

        // Charge (always visible)
        chargeRoot = CreateRoot("Charge", hpRoot,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, -(hpHeight + chargeTopOffset)), new Vector2(chargeWidth, chargeHeight));

        CreateSimpleImage(chargeRoot, "Back", backColor);
        chargeFill = CreateFilledImage(chargeRoot, "Fill", chargeGray);
        chargeFrameLines = CreateFrame(chargeRoot, "Frame", Color.black, frameThickness);

        chargeHalfMarkerRt = CreateMarker(chargeRoot, "HalfMarker", markerColor, markerThickness, chargeHeight);

        // Fatigue (moved further down, hidden when fatigue==0)
        fatigueRoot = CreateRoot("Fatigue", chargeRoot,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, -(chargeHeight + fatigueTopOffsetFromCharge)), new Vector2(fatigueWidth, fatigueHeight));

        CreateSimpleImage(fatigueRoot, "Back", backColor);
        fatigueFill = CreateFilledImage(fatigueRoot, "Fill", fatigueYellow);
        fatigueFrameLines = CreateFrame(fatigueRoot, "Frame", Color.black, frameThickness);

        fatigueRoot.gameObject.SetActive(false);
    }

    // ---------- helpers ----------

    private static void PlaceMarker(RectTransform marker, float totalWidth, float t01)
    {
        marker.anchoredPosition = new Vector2(totalWidth * t01, 0f);
    }

    private RectTransform CreateMarker(RectTransform parent, string name, Color color, float thickness, float h)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(thickness, h);

        var img = go.GetComponent<Image>();
        img.sprite = whiteSquareSprite;
        img.type = Image.Type.Simple;
        img.color = color;

        return rt;
    }

    private RectTransform CreateRoot(
        string name, RectTransform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return rt;
    }

    private Image CreateSimpleImage(RectTransform parent, string name, Color color)
    {
        var img = CreateImageBase(parent, name, color);
        img.type = Image.Type.Simple;
        return img;
    }

    private Image CreateFilledImage(RectTransform parent, string name, Color color)
    {
        var img = CreateImageBase(parent, name, color);
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillClockwise = true;
        img.fillAmount = 0f;
        return img;
    }

    private Image CreateImageBase(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.sprite = whiteSquareSprite;
        img.type = Image.Type.Simple;
        img.color = color;
        return img;
    }

    private Image[] CreateFrame(RectTransform parent, string name, Color color, float thickness)
    {
        var frame = new GameObject(name, typeof(RectTransform));
        frame.transform.SetParent(parent, false);

        var frt = (RectTransform)frame.transform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;

        var top = CreateLine(frt, "Top", color,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, thickness));

        var bottom = CreateLine(frt, "Bottom", color,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, thickness));

        var left = CreateLine(frt, "Left", color,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
            new Vector2(thickness, 0f));

        var right = CreateLine(frt, "Right", color,
            new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
            new Vector2(thickness, 0f));

        return new[] { top, bottom, left, right };
    }

    private Image CreateLine(
        RectTransform parent, string name, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = sizeDelta;

        var img = go.GetComponent<Image>();
        img.sprite = whiteSquareSprite;
        img.type = Image.Type.Simple;
        img.color = color;
        return img;
    }

    private static void SetLineColors(Image[] lines, Color c)
    {
        for (int i = 0; i < lines.Length; i++) lines[i].color = c;
    }

    private static Color Darken(Color c, float mul)
    {
        return new Color(c.r * mul, c.g * mul, c.b * mul, c.a);
    }

    private static Sprite Create1x1WhiteSprite()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }
}