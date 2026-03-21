using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[DisallowMultipleComponent]
public sealed class PlayerImage : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController player;
    [SerializeField] private Image targetImage;

    [Header("Sprites (0-24, 25-49, 50-74, 75-100)")]
    [SerializeField] private Sprite sprite0;
    [SerializeField] private Sprite sprite1;
    [SerializeField] private Sprite sprite2;
    [SerializeField] private Sprite sprite3;

    [Header("Pickup stretch animation")]
    [SerializeField] private float maxScaleX = 1.1f;
    [SerializeField] private float stretchDuration = 0.5f;

    private int lastIndex = -1;
    private RectTransform rectTransformCache;
    private Coroutine stretchCo;

    private void Reset()
    {
        targetImage = GetComponent<Image>();
    }

    private void Awake()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();

        if (targetImage != null)
            rectTransformCache = targetImage.rectTransform;
    }

    private void Start()
    {
        RefreshImmediately();
        ApplyScaleX(1f);
    }

    private void Update()
    {
        RefreshIfNeeded();
    }

    public void RefreshImmediately()
    {
        lastIndex = -1;
        RefreshIfNeeded();
    }

    private void RefreshIfNeeded()
    {
        if (player == null || targetImage == null)
            return;

        int index = GetSpriteIndex(player.currentBodyKey);

        if (index == lastIndex)
            return;

        lastIndex = index;
        targetImage.sprite = GetSpriteByIndex(index);
    }

    private int GetSpriteIndex(float currentBodyKey)
    {
        float clamped = Mathf.Clamp(currentBodyKey, 0f, 100f);

        if (clamped < 10f) return 0;
        if (clamped < 25f) return 1;
        if (clamped < 60f) return 2;
        return 3;
    }

    private Sprite GetSpriteByIndex(int index)
    {
        switch (index)
        {
            case 0: return sprite0;
            case 1: return sprite1;
            case 2: return sprite2;
            case 3: return sprite3;
            default: return sprite0;
        }
    }

    public void PlayPickupStretch()
    {
        if (rectTransformCache == null)
        {
            if (targetImage == null) return;
            rectTransformCache = targetImage.rectTransform;
            if (rectTransformCache == null) return;
        }

        if (stretchCo != null)
            StopCoroutine(stretchCo);

        stretchCo = StartCoroutine(CoPickupStretch());
    }

    private IEnumerator CoPickupStretch()
    {
        float duration = Mathf.Max(0.01f, stretchDuration);
        float half = duration * 0.5f;

        float startAmount = GetCurrentStretchAmount01();

        float t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / half);

            float amount01 = Mathf.Lerp(startAmount, 1f, u);
            ApplyStretchAmount(amount01);

            yield return null;
        }

        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / half);

            float amount01 = Mathf.Lerp(1f, 0f, u);
            ApplyStretchAmount(amount01);

            yield return null;
        }

        ApplyStretchAmount(0f);
        stretchCo = null;
    }

    private float GetCurrentStretchAmount01()
    {
        if (rectTransformCache == null)
            return 0f;

        float x = rectTransformCache.localScale.x;
        float maxX = Mathf.Max(1f, maxScaleX);

        return Mathf.InverseLerp(1f, maxX, x);
    }

    private void ApplyStretchAmount(float amount01)
    {
        amount01 = Mathf.Clamp01(amount01);

        float x = Mathf.Lerp(1f, Mathf.Max(1f, maxScaleX), amount01);
        ApplyScaleX(x);
    }

    private void ApplyScaleX(float x)
    {
        if (rectTransformCache == null)
            return;

        Vector3 s = rectTransformCache.localScale;
        s.x = -Mathf.Min(x, Mathf.Max(1f, maxScaleX)); // 1.05超え防止
        s.y = 1f;
        s.z = 1f;
        rectTransformCache.localScale = s;
    }
}