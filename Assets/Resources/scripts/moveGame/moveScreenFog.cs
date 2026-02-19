using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class moveScreenFog : MonoBehaviour
{
    public float rate01 = 0f;   // 0..1 : 「現在の曇り」（スポーン量/頻度にだけ使う）
    public Sprite dripSprite;
    public Sprite steamSprite;

    private RectTransform _rootRt;

    private RectTransform _fxRoot;
    private RectTransform _dripRoot;
    private RectTransform _steamRoot;

    private Image[] _dripImgs;
    private RectTransform[] _dripRts;
    private bool[] _dripActive;
    private float[] _dripAge;
    private float[] _dripVel;
    private int[] _dripFree;
    private int _dripFreeTop;
    private float _dripSpawnTimer;
    private Sprite _dripLastSprite;

    private Image[] _steamImgs;
    private RectTransform[] _steamRts;
    private bool[] _steamActive;
    private float[] _steamAge;
    private float[] _steamVel;
    private int[] _steamFree;
    private int _steamFreeTop;
    private float _steamSpawnTimer;
    private Sprite _steamLastSprite;

    private enum FxPhase : byte { FadeIn = 0, Hold = 1, FadeOut = 2 }

    private FxPhase[] _dripPhase;
    private float[] _dripFadeOutStartAge;

    private FxPhase[] _steamPhase;
    private float[] _steamFadeOutStartAge;

    private bool _inited;

    // r の立ち上がり検知用（別タイマーではなく状態）
    private float _prevR = 0f;

    private void Awake()
    {
        _rootRt = GetComponent<RectTransform>();
        if (_rootRt == null)
        {
            Debug.LogError("[moveScreenFog] RectTransform missing.");
            enabled = false;
            return;
        }

        CreateUi();
        BuildPools();
        ClearAll();

        _inited = true;
    }

    private void Update()
    {
        if (!_inited) return;

        var inst = moveGameSceneController.Instance;
        var f = inst.player.fatigue / 100f;
        rate01 = f;

        float lr = Mathf.Lerp(350f, 270f, inst.player.currentBodyKey / 100f);
        ApplyRootStretchLeftRight(lr);

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float r = rate01;
        if (r < 0f) r = 0f;
        else if (r > 1f) r = 1f;

        // フォールバック禁止：r>0 で必要Spriteが無ければ明示エラーで停止
        if (r > 0f && (dripSprite == null || steamSprite == null))
        {
            Debug.LogError("[moveScreenFog] dripSprite and steamSprite are required when rate01>0.");
            enabled = false;
            return;
        }

        // r==0 でも既存は寿命まで動かして表示する（スポーンだけ止める）
        UpdateDrips(dt, r);
        UpdateSteam(dt, r);

        _prevR = r;
    }

    private void CreateUi()
    {
        _fxRoot = new GameObject("FxRoot", typeof(RectTransform)).GetComponent<RectTransform>();
        _fxRoot.SetParent(transform, false);
        StretchFull(_fxRoot);

        _dripRoot = new GameObject("Drips", typeof(RectTransform)).GetComponent<RectTransform>();
        _dripRoot.SetParent(_fxRoot, false);
        StretchFull(_dripRoot);

        _steamRoot = new GameObject("Steam", typeof(RectTransform)).GetComponent<RectTransform>();
        _steamRoot.SetParent(_fxRoot, false);
        StretchFull(_steamRoot);
    }

    private void BuildPools()
    {
        _dripImgs = new Image[48];
        _dripRts = new RectTransform[48];
        _dripActive = new bool[48];
        _dripAge = new float[48];
        _dripVel = new float[48];
        _dripFree = new int[48];
        _dripFreeTop = 0;

        _dripPhase = new FxPhase[48];
        _dripFadeOutStartAge = new float[48];

        for (int i = 0; i < 48; i++)
        {
            var go = new GameObject($"Drop_{i:00}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_dripRoot, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            img.enabled = false;

            _dripImgs[i] = img;
            _dripRts[i] = rt;

            _dripActive[i] = false;
            _dripAge[i] = 0f;
            _dripVel[i] = 0f;
            _dripPhase[i] = FxPhase.FadeIn;
            _dripFadeOutStartAge[i] = 0f;

            _dripFree[_dripFreeTop++] = i;
        }

        _steamImgs = new Image[12];
        _steamRts = new RectTransform[12];
        _steamActive = new bool[12];
        _steamAge = new float[12];
        _steamVel = new float[12];
        _steamFree = new int[12];
        _steamFreeTop = 0;

        _steamPhase = new FxPhase[12];
        _steamFadeOutStartAge = new float[12];

        for (int i = 0; i < 12; i++)
        {
            var go = new GameObject($"Steam_{i:00}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_steamRoot, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            img.enabled = false;

            _steamImgs[i] = img;
            _steamRts[i] = rt;

            _steamActive[i] = false;
            _steamAge[i] = 0f;
            _steamVel[i] = 0f;
            _steamPhase[i] = FxPhase.FadeIn;
            _steamFadeOutStartAge[i] = 0f;

            _steamFree[_steamFreeTop++] = i;
        }

        _dripLastSprite = null;
        _steamLastSprite = null;
        _dripSpawnTimer = 0f;
        _steamSpawnTimer = 0f;
        _prevR = 0f;
    }

    private void ClearAll()
    {
        for (int i = 0; i < 48; i++)
        {
            _dripActive[i] = false;
            _dripAge[i] = 0f;
            _dripVel[i] = 0f;
            _dripPhase[i] = FxPhase.FadeIn;
            _dripFadeOutStartAge[i] = 0f;

            var img = _dripImgs[i];
            img.enabled = false;
            var c = img.color;
            c.a = 0f;
            img.color = c;
        }

        _dripFreeTop = 0;
        for (int i = 0; i < 48; i++) _dripFree[_dripFreeTop++] = i;
        _dripSpawnTimer = 0f;

        for (int i = 0; i < 12; i++)
        {
            _steamActive[i] = false;
            _steamAge[i] = 0f;
            _steamVel[i] = 0f;
            _steamPhase[i] = FxPhase.FadeIn;
            _steamFadeOutStartAge[i] = 0f;

            var img = _steamImgs[i];
            img.enabled = false;
            var c = img.color;
            c.a = 0f;
            img.color = c;
        }

        _steamFreeTop = 0;
        for (int i = 0; i < 12; i++) _steamFree[_steamFreeTop++] = i;
        _steamSpawnTimer = 0f;

        _prevR = 0f;
    }

    private void UpdateDrips(float dt, float r)
    {
        if (_dripLastSprite != dripSprite)
        {
            _dripLastSprite = dripSprite;
            for (int i = 0; i < 48; i++) _dripImgs[i].sprite = dripSprite;
        }

        Rect rect = _rootRt.rect;
        float w = rect.width;
        float h = rect.height;
        if (w <= 1e-6f || h <= 1e-6f)
        {
            Debug.LogError("[moveScreenFog] Root RectTransform has invalid size.");
            enabled = false;
            return;
        }

        float spawnIntervalSec = Mathf.Lerp(4f, 1f, r);

        if (r > 0f)
        {
            // 立ち上がり時だけ「最初の1回」を早める（0秒即スポーンはしない）
            // 例：interval=2s のとき、timer を 90% まで進めておく → 初回は約0.6s後
            if (_prevR <= 0f)
            {
                float warm = spawnIntervalSec * 0.90f;
                if (_dripSpawnTimer < warm) _dripSpawnTimer = warm;
            }

            _dripSpawnTimer += dt * r;

            while (_dripSpawnTimer >= spawnIntervalSec && _dripFreeTop > 0)
            {
                _dripSpawnTimer -= spawnIntervalSec;
                SpawnOneDrip(w, h);
            }
        }

        for (int i = 0; i < 48; i++)
        {
            if (!_dripActive[i]) continue;

            _dripAge[i] += dt;
            _dripVel[i] += 3f * dt;

            var rt = _dripRts[i];
            var p = rt.anchoredPosition;
            p.y -= _dripVel[i] * dt;
            rt.anchoredPosition = p;

            float life = 8f;
            float fadeIn = 0.35f;
            float fadeOut = 0.60f;
            float baseAlpha = 0.5f;

            if (_dripPhase[i] != FxPhase.FadeOut && _dripAge[i] >= life - fadeOut)
            {
                _dripPhase[i] = FxPhase.FadeOut;
                _dripFadeOutStartAge[i] = _dripAge[i];
            }

            if (_dripPhase[i] != FxPhase.FadeOut && p.y < -20f - 10f)
            {
                _dripPhase[i] = FxPhase.FadeOut;
                _dripFadeOutStartAge[i] = _dripAge[i];
            }

            if (_dripPhase[i] == FxPhase.FadeIn && _dripAge[i] >= fadeIn)
            {
                _dripPhase[i] = FxPhase.Hold;
            }

            float a = 0f;
            if (_dripPhase[i] == FxPhase.FadeIn)
            {
                float t = (fadeIn <= 1e-6f) ? 1f : Mathf.Clamp01(_dripAge[i] / fadeIn);
                a = baseAlpha * t;
            }
            else if (_dripPhase[i] == FxPhase.Hold)
            {
                a = baseAlpha;
            }
            else
            {
                float t = (fadeOut <= 1e-6f) ? 1f : Mathf.Clamp01((_dripAge[i] - _dripFadeOutStartAge[i]) / fadeOut);
                a = baseAlpha * (1f - t);
            }

            var img = _dripImgs[i];
            var c = img.color;
            c.a = a;
            img.color = c;

            if (_dripPhase[i] == FxPhase.FadeOut && a <= 0.001f)
            {
                DeactivateDrip(i);
            }
        }
    }

    private void SpawnOneDrip(float canvasW, float canvasH)
    {
        int idx = _dripFree[--_dripFreeTop];

        _dripActive[idx] = true;
        _dripAge[idx] = 0f;
        _dripVel[idx] = 0f;

        _dripPhase[idx] = FxPhase.FadeIn;
        _dripFadeOutStartAge[idx] = 0f;

        var rt = _dripRts[idx];
        rt.sizeDelta = new Vector2(20f, 20f);
        rt.anchoredPosition = new Vector2(Random.Range(0f, canvasW), Random.Range(0f, canvasH));

        var img = _dripImgs[idx];
        img.sprite = dripSprite;
        img.color = new Color(1f, 1f, 1f, 0f);
        img.enabled = true;
    }

    private void DeactivateDrip(int i)
    {
        _dripActive[i] = false;
        _dripAge[i] = 0f;
        _dripVel[i] = 0f;
        _dripPhase[i] = FxPhase.FadeIn;
        _dripFadeOutStartAge[i] = 0f;

        var img = _dripImgs[i];
        var c = img.color;
        c.a = 0f;
        img.color = c;
        img.enabled = false;

        _dripFree[_dripFreeTop++] = i;
    }

    private void UpdateSteam(float dt, float r)
    {
        if (_steamLastSprite != steamSprite)
        {
            _steamLastSprite = steamSprite;
            for (int i = 0; i < 12; i++) _steamImgs[i].sprite = steamSprite;
        }

        Rect rect = _rootRt.rect;
        float w = rect.width;
        float h = rect.height;
        if (w <= 1e-6f || h <= 1e-6f)
        {
            Debug.LogError("[moveScreenFog] Root RectTransform has invalid size.");
            enabled = false;
            return;
        }

        float spawnIntervalSec = Mathf.Lerp(1f, 0.5f, r);

        if (r > 0f)
        {
            // 立ち上がり時だけ暖機。interval=0.5s のとき初回は約0.15s後
            if (_prevR <= 0f)
            {
                float warm = spawnIntervalSec * 0.90f;
                if (_steamSpawnTimer < warm) _steamSpawnTimer = warm;
            }

            _steamSpawnTimer += dt * r;

            while (_steamSpawnTimer >= spawnIntervalSec && _steamFreeTop > 0)
            {
                _steamSpawnTimer -= spawnIntervalSec;
                SpawnOneSteam(w, h);
            }
        }

        for (int i = 0; i < 12; i++)
        {
            if (!_steamActive[i]) continue;

            _steamAge[i] += dt;
            _steamVel[i] += 4f * dt;

            var rt = _steamRts[i];
            var p = rt.anchoredPosition;
            p.y += _steamVel[i] * dt;
            rt.anchoredPosition = p;

            float life = 20f;
            float fadeIn = 0.70f;
            float fadeOut = 1.20f;
            float baseAlpha = 0.1f;

            if (_steamPhase[i] != FxPhase.FadeOut && _steamAge[i] >= life - fadeOut)
            {
                _steamPhase[i] = FxPhase.FadeOut;
                _steamFadeOutStartAge[i] = _steamAge[i];
            }

            if (_steamPhase[i] != FxPhase.FadeOut && p.y > h + 60f + 10f)
            {
                _steamPhase[i] = FxPhase.FadeOut;
                _steamFadeOutStartAge[i] = _steamAge[i];
            }

            if (_steamPhase[i] == FxPhase.FadeIn && _steamAge[i] >= fadeIn)
            {
                _steamPhase[i] = FxPhase.Hold;
            }

            float a = 0f;
            if (_steamPhase[i] == FxPhase.FadeIn)
            {
                float t = (fadeIn <= 1e-6f) ? 1f : Mathf.Clamp01(_steamAge[i] / fadeIn);
                a = baseAlpha * t;
            }
            else if (_steamPhase[i] == FxPhase.Hold)
            {
                a = baseAlpha;
            }
            else
            {
                float t = (fadeOut <= 1e-6f) ? 1f : Mathf.Clamp01((_steamAge[i] - _steamFadeOutStartAge[i]) / fadeOut);
                a = baseAlpha * (1f - t);
            }

            var img = _steamImgs[i];
            var c = img.color;
            c.a = a;
            img.color = c;

            if (_steamPhase[i] == FxPhase.FadeOut && a <= 0.001f)
            {
                DeactivateSteam(i);
            }
        }
    }

    private void SpawnOneSteam(float canvasW, float canvasH)
    {
        int idx = _steamFree[--_steamFreeTop];

        _steamActive[idx] = true;
        _steamAge[idx] = 0f;
        _steamVel[idx] = 0f;

        _steamPhase[idx] = FxPhase.FadeIn;
        _steamFadeOutStartAge[idx] = 0f;

        var rt = _steamRts[idx];
        rt.sizeDelta = new Vector2(80f, 80f);
        rt.anchoredPosition = new Vector2(Random.Range(0f, canvasW), Random.Range(0f, canvasH));

        var img = _steamImgs[idx];
        img.sprite = steamSprite;
        img.color = new Color(1f, 1f, 1f, 0f);
        img.enabled = true;
    }

    private void DeactivateSteam(int i)
    {
        _steamActive[i] = false;
        _steamAge[i] = 0f;
        _steamVel[i] = 0f;

        _steamPhase[i] = FxPhase.FadeIn;
        _steamFadeOutStartAge[i] = 0f;

        var img = _steamImgs[i];
        var c = img.color;
        c.a = 0f;
        img.color = c;
        img.enabled = false;

        _steamFree[_steamFreeTop++] = i;
    }

    private void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private void ApplyRootStretchLeftRight(float leftRight)
    {
        if (_rootRt == null) return;

        _rootRt.anchorMin = Vector2.zero;
        _rootRt.anchorMax = Vector2.one;
        _rootRt.pivot = new Vector2(0.5f, 0.5f);
        var min = _rootRt.offsetMin;
        var max = _rootRt.offsetMax;

        min.x = leftRight;
        max.x = -leftRight;

        _rootRt.offsetMin = min;
        _rootRt.offsetMax = max;
    }
}
