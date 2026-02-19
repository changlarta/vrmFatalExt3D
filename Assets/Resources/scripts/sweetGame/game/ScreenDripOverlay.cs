using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class ScreenDripOverlay : MonoBehaviour
{
    public bool enableDrips = true;
    public Sprite dripSprite;

    public Sprite steamSprite;

    private RectTransform _rootRt;

    private FogOverlayController _fog;
    private UiDripPool _drips;
    private UiSteamPool _steam;

    private void Awake()
    {
        _rootRt = GetComponent<RectTransform>();

        _drips = new UiDripPool(transform);
        _steam = new UiSteamPool(transform);
        _fog = new FogOverlayController(transform, _drips, _steam);

        _fog.Ensure();
        _drips.Ensure(dripSprite);
        _steam.Ensure(steamSprite);

        _fog.ResetState(clearMaskToFullFog: true);
        _drips.ClearAll();
        _steam.ClearAll();

        enableDrips = SettingStore.useSteamMode;
    }

    private void OnEnable()
    {
        if (_rootRt == null) _rootRt = GetComponent<RectTransform>();

        if (_fog == null)
        {
            _drips = new UiDripPool(transform);
            _steam = new UiSteamPool(transform);
            _fog = new FogOverlayController(transform, _drips, _steam);
        }

        _fog.Ensure();
        _drips.Ensure(dripSprite);
        _steam.Ensure(steamSprite);

        _fog.ResetState(clearMaskToFullFog: false);
    }

    private void Update()
    {
        if (_rootRt == null) _rootRt = GetComponent<RectTransform>();
        if (_rootRt == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float fogRate01 = GetFogRate01();
        float rate01 = GetRate01();

        _fog.UpdateFog(dt, fogRate01, _rootRt, enableDrips);

        // パーティクルの更新・発生は従来どおり rate01>0 と enableX を満たすときだけ
        bool runEffects = (rate01 > 0f) && enableDrips;

        _steam.Update(dt, rate01, runEffects && enableDrips, steamSprite, _rootRt);
        _drips.Update(dt, rate01, runEffects && enableDrips, dripSprite, _rootRt);
    }

    private float GetFogRate01()
    {
        var inst = VrmChrSceneController.Instance;
        if (inst == null || inst.vrmToController == null) return 0f;

        float bk = inst.vrmToController.bodyKey;

        const float minBk = 40f;
        const float maxBk = 80f;
        if (bk < minBk) return 0f;

        const float FogRiseSecAt25 = 180f;
        const float FogRiseSecAt80 = 60f;

        if (bk >= maxBk) return 1f;

        float t = Mathf.InverseLerp(minBk, maxBk, bk);
        float fogRiseSec = Mathf.Lerp(FogRiseSecAt25, FogRiseSecAt80, t);
        return FogRiseSecAt80 / fogRiseSec;
    }

    private float GetRate01()
    {
        var inst = VrmChrSceneController.Instance;
        if (inst == null || inst.vrmToController == null) return 0f;

        float bk = inst.vrmToController.bodyKey;

        const float minBk = 20f;
        const float maxBk = 80f;
        if (bk < minBk) return 0f;

        const float FogRiseSecAt25 = 180f;
        const float FogRiseSecAt80 = 60f;

        if (bk >= maxBk) return 1f;

        float t = Mathf.InverseLerp(minBk, maxBk, bk);
        float fogRiseSec = Mathf.Lerp(FogRiseSecAt25, FogRiseSecAt80, t);
        return FogRiseSecAt80 / fogRiseSec;
    }
}

/// <summary>
/// 曇りガラス：マスクを塗る（拭く）＋時間で白さ上昇＋拭いた箇所の回復
/// ・Fogの筆（太い）と、効果（滴/蒸気）を消す筆（細い）を分離
/// ・マスクは X/Y 別半径の楕円で塗り、画面上で“円”に見えるようにする
/// ・回復は carry 方式（段階デグレ防止）
/// ・bodyKeyで変えるのは速度だけ（状態＝進捗は変えない）
/// </summary>
public sealed class FogOverlayController
{
    // ===== 要件（固定）=====
    private const float FogMaxAlpha = 1f;
    private const float FogTargetAlpha = 0.2f;
    private const float FogRiseSecAt80 = 120f;

    // ===== マスク（固定）=====
    private const int FogMaskSize = 256;

    // ===== 筆（固定）=====
    private const float FogWipeRadiusPx = 100f;
    private const float EffectWipeRadiusPx = 22f;

    private readonly Transform _parent;
    private readonly UiDripPool _drips;
    private readonly UiSteamPool _steam;

    private Image _fogImg;
    private Material _fogMat;

    private Texture2D _maskTex;
    private Color32[] _mask;
    private bool _maskDirty;

    // 状態：bodyKeyに依存させない（速度のみ依存）
    private float _fogProgress01 = 0f;  // 0..1
    private float _recoverCarry = 0f;

    // 入力追跡
    private bool _hasPrevMouse;
    private Vector2 _prevMouse;

    private const int MaxTouches = 10;
    private readonly bool[] _hasPrevTouch = new bool[MaxTouches];
    private readonly Vector2[] _prevTouch = new Vector2[MaxTouches];

    private static readonly int FogAlphaId = Shader.PropertyToID("_FogAlpha");
    private static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");

    public FogOverlayController(Transform parent, UiDripPool drips, UiSteamPool steam)
    {
        _parent = parent;
        _drips = drips;
        _steam = steam;
    }

    public void Ensure()
    {
        if (_fogImg == null)
        {
            var t = _parent.Find("FogOverlay");
            if (t != null) _fogImg = t.GetComponent<Image>();
        }

        if (_fogImg == null)
        {
            var go = new GameObject("FogOverlay");
            go.transform.SetParent(_parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _fogImg = go.AddComponent<Image>();
            _fogImg.raycastTarget = false;
            _fogImg.color = Color.white;
        }

        // Fog は最背面
        _fogImg.transform.SetAsFirstSibling();

        if (_maskTex == null)
        {
            _maskTex = new Texture2D(FogMaskSize, FogMaskSize, TextureFormat.RGBA32, false, true);
            _maskTex.wrapMode = TextureWrapMode.Clamp;
            _maskTex.filterMode = FilterMode.Bilinear;
        }

        if (_mask == null || _mask.Length != FogMaskSize * FogMaskSize)
        {
            _mask = new Color32[FogMaskSize * FogMaskSize];
            var full = new Color32(255, 255, 255, 255);
            for (int i = 0; i < _mask.Length; i++) _mask[i] = full;
            _maskDirty = true;
        }

        if (_fogMat == null)
        {
            var shader = Shader.Find("UI/FogWipeOverlay");
            if (shader == null)
            {
                Debug.LogError("[Fog] Shader not found: UI/FogWipeOverlay");
                return;
            }
            _fogMat = new Material(shader);
        }

        _fogMat.SetTexture(MaskTexId, _maskTex);
        _fogMat.SetFloat(FogAlphaId, 0f);
        _fogImg.material = _fogMat;

        UploadMask(force: true);
    }

    public void ResetState(bool clearMaskToFullFog)
    {
        _hasPrevMouse = false;
        for (int i = 0; i < MaxTouches; i++) _hasPrevTouch[i] = false;

        _recoverCarry = 0f;

        if (clearMaskToFullFog && _mask != null)
        {
            var full = new Color32(255, 255, 255, 255);
            for (int i = 0; i < _mask.Length; i++) _mask[i] = full;
            _maskDirty = true;
            UploadMask(force: true);
        }
    }

    /// <summary>
    /// allowFogProgress=false の場合：
    /// ・ふき取り入力（Fogマスク削り＋滴/蒸気消去）は動かす
    /// ・曇りの進行（白さ上昇）と回復（マスク復元）は止める
    /// </summary>
    public void UpdateFog(float dt, float rate01, RectTransform rootRt, bool allowFogProgress)
    {
        if (_fogMat == null || _fogImg == null) return;
        if (rootRt == null) return;

        // (1) 現れる時の白さ：進捗を保持し、速度だけ rate01 で変える
        if (allowFogProgress && rate01 > 0f)
        {
            float dtEff = dt * rate01;
            float add = (FogRiseSecAt80 <= 1e-6f) ? 1f : (dtEff / FogRiseSecAt80);
            _fogProgress01 += add;
            if (_fogProgress01 > 1f) _fogProgress01 = 1f;
        }

        float fogAlpha = FogTargetAlpha * _fogProgress01;
        _fogMat.SetFloat(FogAlphaId, Mathf.Clamp01(fogAlpha) * FogMaxAlpha);

        // (2) 入力で拭く（常に動く）
        ProcessWipeInput(rootRt);

        // (3) 拭いた箇所が戻る：allowFogProgress のときだけ
        if (allowFogProgress)
        {
            RecoverMask(dt, rate01);
        }

        if (_maskDirty) UploadMask(force: false);
    }

    private void UploadMask(bool force)
    {
        if (!force && !_maskDirty) return;
        if (_maskTex == null || _mask == null) return;

        _maskTex.SetPixels32(_mask);
        _maskTex.Apply(false, false);
        _maskDirty = false;
    }

    private void RecoverMask(float dt, float rate01)
    {
        if (_mask == null) return;
        if (rate01 <= 0f) return;

        float perSec = 255f / Mathf.Max(1e-6f, FogRiseSecAt80);

        _recoverCarry += perSec * dt * rate01;

        int add = (int)_recoverCarry;
        if (add <= 0) return;
        _recoverCarry -= add;

        bool changed = false;
        for (int i = 0; i < _mask.Length; i++)
        {
            byte a = _mask[i].a;
            if (a >= 255) continue;

            int na = a + add;
            if (na > 255) na = 255;

            if (na != a)
            {
                _mask[i].a = (byte)na;
                changed = true;
            }
        }

        if (changed) _maskDirty = true;
    }

    private void ProcessWipeInput(RectTransform rootRt)
    {
        // Mouse（クリック不要）
        var ms = Mouse.current;
        if (ms != null)
        {
            Vector2 pos = ms.position.ReadValue();
            WipeStroke(rootRt, pos, ref _hasPrevMouse, ref _prevMouse);
        }

        // Touch（押下中のみ）
        var ts = Touchscreen.current;
        if (ts != null)
        {
            int n = Mathf.Min(MaxTouches, ts.touches.Count);
            for (int i = 0; i < n; i++)
            {
                var t = ts.touches[i];
                if (!t.press.isPressed)
                {
                    _hasPrevTouch[i] = false;
                    continue;
                }

                Vector2 pos = t.position.ReadValue();
                WipeStroke(rootRt, pos, ref _hasPrevTouch[i], ref _prevTouch[i]);
            }
        }
    }

    private void WipeStroke(RectTransform rootRt, Vector2 screenPos, ref bool hasPrev, ref Vector2 prev)
    {
        if (_mask == null) return;

        if (!TryScreenToMaskCoord(rootRt, screenPos, out int cx, out int cy, out int rx, out int ry))
            return;

        if (!hasPrev)
        {
            DrawEllipseAndWipeEffects(rootRt, cx, cy, rx, ry);
            hasPrev = true;
            prev = screenPos;
            return;
        }

        if (!TryScreenToMaskCoord(rootRt, prev, out int px, out int py, out _, out _))
        {
            DrawEllipseAndWipeEffects(rootRt, cx, cy, rx, ry);
            prev = screenPos;
            return;
        }

        DrawLine(rootRt, px, py, cx, cy, rx, ry);
        prev = screenPos;
    }

    // 画面上で“円”になるよう、マスク上は X/Y 別半径の楕円にする
    private bool TryScreenToMaskCoord(RectTransform rootRt, Vector2 screenPos, out int x, out int y, out int rxMask, out int ryMask)
    {
        x = y = 0;
        rxMask = ryMask = 1;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, screenPos, null, out var local))
            return false;

        var r = rootRt.rect;
        float w = r.width;
        float h = r.height;
        if (w <= 1e-6f || h <= 1e-6f) return false;

        float x01 = (local.x - r.xMin) / w;
        float y01 = (local.y - r.yMin) / h;
        if (x01 < 0f || x01 > 1f || y01 < 0f || y01 > 1f) return false;

        x = Mathf.RoundToInt(x01 * (FogMaskSize - 1));
        y = Mathf.RoundToInt(y01 * (FogMaskSize - 1));

        float sx = FogMaskSize / w;
        float sy = FogMaskSize / h;

        rxMask = Mathf.Max(1, Mathf.RoundToInt(FogWipeRadiusPx * sx));
        ryMask = Mathf.Max(1, Mathf.RoundToInt(FogWipeRadiusPx * sy));
        return true;
    }

    private void DrawLine(RectTransform rootRt, int x0, int y0, int x1, int y1, int rx, int ry)
    {
        int dx = x1 - x0;
        int dy = y1 - y0;
        int adx = Mathf.Abs(dx);
        int ady = Mathf.Abs(dy);

        int stepDen = Mathf.Max(1, Mathf.Max(rx, ry) / 2);
        int steps = Mathf.Max(1, Mathf.Max(adx, ady) / stepDen);

        for (int i = 0; i <= steps; i++)
        {
            float t = (steps == 0) ? 0f : (i / (float)steps);
            int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
            DrawEllipseAndWipeEffects(rootRt, x, y, rx, ry);
        }
    }

    private void DrawEllipseAndWipeEffects(RectTransform rootRt, int cx, int cy, int rx, int ry)
    {
        DrawEllipseMask(cx, cy, rx, ry);

        // 滴/蒸気の消去は “発生ON/OFF” に関係なく常に行う
        Vector2 centerCanvas = MaskToCanvas(rootRt, cx, cy);
        _drips.WipeInCircleCanvas(centerCanvas, EffectWipeRadiusPx);
        _steam.WipeInCircleCanvas(centerCanvas, EffectWipeRadiusPx);
    }

    private void DrawEllipseMask(int cx, int cy, int rx, int ry)
    {
        int xMin = Mathf.Max(0, cx - rx);
        int xMax = Mathf.Min(FogMaskSize - 1, cx + rx);
        int yMin = Mathf.Max(0, cy - ry);
        int yMax = Mathf.Min(FogMaskSize - 1, cy + ry);

        long rx2 = (long)rx * rx;
        long ry2 = (long)ry * ry;
        long rhs = rx2 * ry2;

        bool changed = false;

        for (int y = yMin; y <= yMax; y++)
        {
            int dy = y - cy;
            long dy2 = (long)dy * dy;
            int row = y * FogMaskSize;

            for (int x = xMin; x <= xMax; x++)
            {
                int dx = x - cx;
                long dx2 = (long)dx * dx;

                if (dx2 * ry2 + dy2 * rx2 > rhs) continue;

                int idx = row + x;

                if (_mask[idx].a != 0)
                {
                    _mask[idx].r = 255;
                    _mask[idx].g = 255;
                    _mask[idx].b = 255;
                    _mask[idx].a = 0;
                    changed = true;
                }
            }
        }

        if (changed) _maskDirty = true;
    }

    private Vector2 MaskToCanvas(RectTransform rootRt, int mx, int my)
    {
        var r = rootRt.rect;
        float w = r.width;
        float h = r.height;

        float inv = 1f / (FogMaskSize - 1);
        float x = (mx * inv) * w;
        float y = (my * inv) * h;
        return new Vector2(x, y);
    }
}

/// <summary>
/// UI水滴プール（Canvas上のImageを再利用）
/// ・時間進行は dt*rate01（rate01==0で停止＝状態維持）
/// ・スポーンも dt*rate01（rate01==0で止まる）
/// </summary>
public sealed class UiDripPool
{
    private const float SpawnIntervalSecAt80 = 2f;
    private const float LifetimeSec = 8f;
    private const float FadePortion = 0.2f;
    private const int MaxDrops = 48;
    private const float StartSpeedPxSec = 0f;
    private const float AccelPxSec2 = 5f;
    private const float SizePx = 20f;
    private const float SpawnIntervalSecAt25 = 8f;

    private readonly Transform _parent;

    private Image[] _imgs;
    private RectTransform[] _rts;
    private bool[] _active;
    private float[] _age;
    private float[] _vel;
    private int[] _free;
    private int _freeTop;
    private float _spawnTimer;

    private Sprite _lastSprite;
    private Vector2 _size;

    public UiDripPool(Transform parent)
    {
        _parent = parent;
        _size = new Vector2(SizePx, SizePx);
    }

    public void Ensure(Sprite sprite)
    {
        if (_imgs != null) return;

        _imgs = new Image[MaxDrops];
        _rts = new RectTransform[MaxDrops];
        _active = new bool[MaxDrops];
        _age = new float[MaxDrops];
        _vel = new float[MaxDrops];

        _free = new int[MaxDrops];
        _freeTop = 0;

        for (int i = _parent.childCount - 1; i >= 0; i--)
        {
            var ch = _parent.GetChild(i);
            if (ch != null && ch.name.StartsWith("Drop_"))
                Object.Destroy(ch.gameObject);
        }

        for (int i = 0; i < MaxDrops; i++)
        {
            var go = new GameObject($"Drop_{i:00}");
            go.transform.SetParent(_parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            img.enabled = false;

            _imgs[i] = img;
            _rts[i] = rt;

            _active[i] = false;
            _age[i] = 0f;
            _vel[i] = 0f;

            _free[_freeTop++] = i;
        }

        _lastSprite = sprite;
    }

    public void ClearAll()
    {
        if (_active == null) return;

        for (int i = 0; i < MaxDrops; i++)
            if (_active[i]) Deactivate(i);

        _freeTop = 0;
        for (int i = 0; i < MaxDrops; i++)
            _free[_freeTop++] = i;

        _spawnTimer = 0f;
    }

    public void Update(float dt, float rate01, bool enabled, Sprite sprite, RectTransform rootRt)
    {
        if (_imgs == null) return;
        if (rootRt == null) return;

        // sprite差し替え（enabled=falseでも反映はしておく）
        if (_lastSprite != sprite)
        {
            _lastSprite = sprite;
            for (int i = 0; i < MaxDrops; i++) _imgs[i].sprite = sprite;
        }

        float rate = Mathf.Max(0f, rate01);
        float dtSpawn = dt * rate;
        float dtSim = dt;

        float w = rootRt.rect.width;
        float h = rootRt.rect.height;

        // spawn interval
        const float fogRiseSecAt25 = 180f;
        const float fogRiseSecAt80 = 60f;

        float fogRiseSec = (rate01 > 1e-6f) ? (fogRiseSecAt80 / rate01) : fogRiseSecAt25;
        float t = Mathf.InverseLerp(fogRiseSecAt25, fogRiseSecAt80, fogRiseSec);
        float spawnIntervalSec = Mathf.Lerp(SpawnIntervalSecAt25, SpawnIntervalSecAt80, t);

        bool allowSpawn = enabled && (sprite != null) && (dtSpawn > 0f);

        // spawn（スポーンだけ止める）
        if (allowSpawn)
        {
            _spawnTimer += dtSpawn;
            while (_spawnTimer >= spawnIntervalSec && _freeTop > 0)
            {
                _spawnTimer -= spawnIntervalSec;
                SpawnOne(w, h, sprite);
            }
        }

        float fadeSec = LifetimeSec * FadePortion;
        float fadeInEnd = fadeSec;
        float fadeOutStart = LifetimeSec - fadeSec;

        for (int i = 0; i < MaxDrops; i++)
        {
            if (!_active[i]) continue;

            _age[i] += dtSim;
            _vel[i] += AccelPxSec2 * dtSim;

            var rt = _rts[i];
            var p = rt.anchoredPosition;
            p.y -= _vel[i] * dtSim;
            rt.anchoredPosition = p;

            float a = 1f;

            if (fadeSec > 0f && _age[i] < fadeInEnd)
                a = Mathf.Clamp01(_age[i] / fadeSec);

            if (fadeSec > 0f && _age[i] > fadeOutStart)
            {
                float t2 = Mathf.Clamp01((_age[i] - fadeOutStart) / fadeSec);
                float outA = 1f - t2;
                if (outA < a) a = outA;
            }

            var img = _imgs[i];
            var c = img.color;
            c.a = a;
            img.color = c;

            if (_age[i] >= LifetimeSec || p.y < -_size.y - 10f)
                Deactivate(i);
        }
    }

    public void WipeInCircleCanvas(Vector2 centerCanvas, float radiusPx)
    {
        if (_active == null) return;

        float r2 = radiusPx * radiusPx;

        for (int i = 0; i < MaxDrops; i++)
        {
            if (!_active[i]) continue;

            Vector2 p = _rts[i].anchoredPosition;
            Vector2 d = p - centerCanvas;
            if (d.sqrMagnitude <= r2)
            {
                Deactivate(i);
            }
        }
    }

    private void SpawnOne(float canvasW, float canvasH, Sprite sprite)
    {
        if (_freeTop <= 0) return;

        int idx = _free[--_freeTop];

        float x = Random.Range(0f, canvasW);
        float y = Random.Range(0f, canvasH);

        _active[idx] = true;
        _age[idx] = 0f;
        _vel[idx] = StartSpeedPxSec;

        var rt = _rts[idx];
        rt.sizeDelta = _size;
        rt.anchoredPosition = new Vector2(x, y);

        var img = _imgs[idx];
        img.sprite = sprite;
        img.color = new Color(1f, 1f, 1f, 0f);
        img.enabled = true;
    }

    private void Deactivate(int i)
    {
        _active[i] = false;
        _age[i] = 0f;
        _vel[i] = 0f;

        var img = _imgs[i];
        if (img != null)
        {
            var c = img.color;
            c.a = 0f;
            img.color = c;
            img.enabled = false;
        }

        _free[_freeTop++] = i;
    }
}

/// <summary>
/// UI蒸気プール（上昇）
/// ・時間進行は dt*rate01（rate01==0で停止＝状態維持）
/// ・スポーンも dt*rate01
/// </summary>
public sealed class UiSteamPool
{
    private const float SpawnIntervalSecAt80 = 4f;
    private const float LifetimeSec = 20f;
    private const float FadePortion = 1f;
    private const int MaxParticles = 12;
    private const float StartSpeedPxSec = 0f;
    private const float AccelPxSec2 = 7f;
    private const float SizePx = 120f;
    private const float SpawnIntervalSecAt25 = 16f;

    private readonly Transform _parent;

    private Image[] _imgs;
    private RectTransform[] _rts;
    private bool[] _active;
    private float[] _age;
    private float[] _vel;
    private int[] _free;
    private int _freeTop;
    private float _spawnTimer;

    private Sprite _lastSprite;
    private Vector2 _size;

    public UiSteamPool(Transform parent)
    {
        _parent = parent;
        _size = new Vector2(SizePx, SizePx);
    }

    public void Ensure(Sprite sprite)
    {
        if (_imgs != null) return;

        _imgs = new Image[MaxParticles];
        _rts = new RectTransform[MaxParticles];
        _active = new bool[MaxParticles];
        _age = new float[MaxParticles];
        _vel = new float[MaxParticles];

        _free = new int[MaxParticles];
        _freeTop = 0;

        for (int i = _parent.childCount - 1; i >= 0; i--)
        {
            var ch = _parent.GetChild(i);
            if (ch != null && ch.name.StartsWith("Steam_"))
                Object.Destroy(ch.gameObject);
        }

        for (int i = 0; i < MaxParticles; i++)
        {
            var go = new GameObject($"Steam_{i:00}");
            go.transform.SetParent(_parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            img.enabled = false;

            _imgs[i] = img;
            _rts[i] = rt;

            _active[i] = false;
            _age[i] = 0f;
            _vel[i] = 0f;

            _free[_freeTop++] = i;
        }

        _lastSprite = sprite;
    }

    public void ClearAll()
    {
        if (_active == null) return;

        for (int i = 0; i < MaxParticles; i++)
            if (_active[i]) Deactivate(i);

        _freeTop = 0;
        for (int i = 0; i < MaxParticles; i++)
            _free[_freeTop++] = i;

        _spawnTimer = 0f;
    }

    public void Update(float dt, float rate01, bool enabled, Sprite sprite, RectTransform rootRt)
    {
        if (_imgs == null) return;
        if (rootRt == null) return;

        // sprite差し替え（enabled=falseでも反映はしておく）
        if (_lastSprite != sprite)
        {
            _lastSprite = sprite;
            for (int i = 0; i < MaxParticles; i++) _imgs[i].sprite = sprite;
        }

        float rate = Mathf.Max(0f, rate01);
        float dtSpawn = dt * rate;
        float dtSim = dt;

        float w = rootRt.rect.width;
        float h = rootRt.rect.height;

        const float fogRiseSecAt25 = 180f;
        const float fogRiseSecAt80 = 60f;

        float fogRiseSec = (rate01 > 1e-6f) ? (fogRiseSecAt80 / rate01) : fogRiseSecAt25;
        float t = Mathf.InverseLerp(fogRiseSecAt25, fogRiseSecAt80, fogRiseSec);
        float spawnIntervalSec = Mathf.Lerp(SpawnIntervalSecAt25, SpawnIntervalSecAt80, t);

        bool allowSpawn = enabled && (sprite != null) && (dtSpawn > 0f);

        // spawn（スポーンだけ止める）
        if (allowSpawn)
        {
            _spawnTimer += dtSpawn;
            while (_spawnTimer >= spawnIntervalSec && _freeTop > 0)
            {
                _spawnTimer -= spawnIntervalSec;
                SpawnOne(w, h, sprite);
            }
        }

        float fadeSec = LifetimeSec * FadePortion;
        float fadeInEnd = fadeSec;
        float fadeOutStart = LifetimeSec - fadeSec;

        for (int i = 0; i < MaxParticles; i++)
        {
            if (!_active[i]) continue;

            _age[i] += dtSim;
            _vel[i] += AccelPxSec2 * dtSim;

            var rt = _rts[i];
            var p = rt.anchoredPosition;
            p.y += _vel[i] * dtSim;
            rt.anchoredPosition = p;

            float a = 1f;

            if (fadeSec > 0f && _age[i] < fadeInEnd)
                a = Mathf.Clamp01(_age[i] / fadeSec);

            if (fadeSec > 0f && _age[i] > fadeOutStart)
            {
                float t2 = Mathf.Clamp01((_age[i] - fadeOutStart) / fadeSec);
                float outA = 1f - t2;
                if (outA < a) a = outA;
            }

            var img = _imgs[i];
            var c = img.color;
            c.a = a;
            img.color = c;

            if (_age[i] >= LifetimeSec || p.y > h + _size.y + 10f)
                Deactivate(i);
        }
    }

    public void WipeInCircleCanvas(Vector2 centerCanvas, float radiusPx)
    {
        if (_active == null) return;

        float r2 = radiusPx * radiusPx;

        for (int i = 0; i < MaxParticles; i++)
        {
            if (!_active[i]) continue;

            Vector2 p = _rts[i].anchoredPosition;
            Vector2 d = p - centerCanvas;
            if (d.sqrMagnitude <= r2)
            {
                Deactivate(i);
            }
        }
    }

    private void SpawnOne(float canvasW, float canvasH, Sprite sprite)
    {
        if (_freeTop <= 0) return;

        int idx = _free[--_freeTop];

        float x = Random.Range(0f, canvasW);
        float y = Random.Range(0f, canvasH);

        _active[idx] = true;
        _age[idx] = 0f;
        _vel[idx] = StartSpeedPxSec;

        var rt = _rts[idx];
        rt.sizeDelta = _size;
        rt.anchoredPosition = new Vector2(x, y);

        var img = _imgs[idx];
        img.sprite = sprite;
        img.color = new Color(1f, 1f, 1f, 0f);
        img.enabled = true;
    }

    private void Deactivate(int i)
    {
        _active[i] = false;
        _age[i] = 0f;
        _vel[i] = 0f;

        var img = _imgs[i];
        if (img != null)
        {
            var c = img.color;
            c.a = 0f;
            img.color = c;
            img.enabled = false;
        }

        _free[_freeTop++] = i;
    }
}
