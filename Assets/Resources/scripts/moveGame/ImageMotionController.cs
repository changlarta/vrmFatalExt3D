using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("UI/Image Motion Controller")]
[RequireComponent(typeof(Image))]
public class ImageMotionController : BaseMeshEffect
{
    public enum ImageType
    {
        FirstStill,
        SecondBottomSwing,
        ThirdVerticalShake,
        FourthGrowLoop,
        FifthRotateLoop
    }

    [Serializable]
    private class ProfileSpriteSet
    {
        public string profileName;
        public Sprite firstSprite;
        public Sprite secondSprite;
        public Sprite thirdSprite;
        public Sprite fourthSprite;
        public Sprite fifthSprite;
    }

    [Header("表示する画像の種類")]
    [SerializeField] private ImageType imageType = ImageType.FirstStill;

    [Header("非表示状態")]
    [SerializeField] private bool isHide = false;

    [Header("5枚の画像(Sprite)")]
    [SerializeField] private Sprite firstSprite;
    [SerializeField] private Sprite secondSprite;
    [SerializeField] private Sprite thirdSprite;
    [SerializeField] private Sprite fourthSprite;
    [SerializeField] private Sprite fifthSprite;

    [Header("profileNameごとの画像上書き")]
    [SerializeField] private List<ProfileSpriteSet> profileSpriteSets = new List<ProfileSpriteSet>();

    [Header("画像ごとのYオフセット")]
    [SerializeField] private float firstYOffset = 0f;
    [SerializeField] private float secondYOffset = 0f;
    [SerializeField] private float thirdYOffset = 0f;
    [SerializeField] private float fourthYOffset = 0f;
    [SerializeField] private float fifthYOffset = 0f;

    [Header("Hide遷移")]
    [SerializeField] private float hideYOffset = -400f;
    [SerializeField] private float hideTransitionDuration = 0.35f;

    [Header("2枚目: 固定ライン基準で左右に歪みながら揺れる")]
    [SerializeField] private float secondSwingSpeed = 3.5f;
    [SerializeField] private float secondTopShiftRatio = 0.12f;
    [Range(0f, 1f)]
    [SerializeField] private float secondFixedLineHeightRatio = 0f;

    [Header("3枚目: 素早く上下に揺れる")]
    [SerializeField] private float thirdShakeAmplitude = 12f;
    [SerializeField] private float thirdShakeSpeed = 20f;

    [Header("4枚目: 0→1.2倍に拡大して少し止まる")]
    [SerializeField] private float fourthGrowDuration = 1.2f;
    [SerializeField] private float fourthHoldDuration = 0.35f;

    [Header("5枚目: ぐるぐる回転")]
    [SerializeField] private float fifthRotationSpeed = 180f;

    private const float FourthMaxScaleMultiplier = 1.2f;

    private Image targetImage;
    private RectTransform rectTransform;

    private ImageType appliedImageType;
    private bool appliedIsHide;

    private float motionTimer;

    private Vector3 playStartLocalPosition;
    private Vector3 playStartLocalScale;
    private Quaternion playStartLocalRotation;

    private bool isHideTransitioning;
    private float hideTransitionTimer;
    private float hideTransitionStartOffset;
    private float hideTransitionTargetOffset;
    private float currentHideOffset;
    private string currentProfileName = string.Empty;

    private readonly List<UIVertex> workVerts = new List<UIVertex>();

    protected override void Awake()
    {
        base.Awake();
        CacheComponents();
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        CacheComponents();

        if (Application.isPlaying)
        {
            CapturePlayStartTransform();
            InitializePlayState();
        }
        else
        {
            ApplySpriteOnlyForEditor();

            if (graphic != null)
            {
                graphic.SetVerticesDirty();
            }
        }
    }

    protected override void OnDisable()
    {
        if (Application.isPlaying && rectTransform != null)
        {
            rectTransform.localPosition = playStartLocalPosition;
            rectTransform.localScale = playStartLocalScale;
            rectTransform.localRotation = playStartLocalRotation;
        }

        if (graphic != null)
        {
            graphic.SetVerticesDirty();
        }

        base.OnDisable();
    }

    // protected void OnValidate()
    // {
    //     CacheComponents();
    //     ApplySpriteOnlyForEditor();

    //     if (graphic != null)
    //     {
    //         graphic.SetVerticesDirty();
    //     }
    // }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        CacheComponents();

        if (appliedImageType != imageType)
        {
            ApplyTypeImmediately(imageType);
        }

        if (appliedIsHide != isHide)
        {
            BeginHideTransition(isHide);
        }

        motionTimer += Time.deltaTime;

        if (isHideTransitioning)
        {
            UpdateHideTransition();
        }

        ApplyTransformAnimation();

        // if (imageType == ImageType.SecondBottomSwing && !isHide && graphic != null)
        // {
        //     graphic.SetVerticesDirty();
        // }
    }

    private void CacheComponents()
    {
        if (targetImage == null)
        {
            targetImage = GetComponent<Image>();
        }

        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }
    }

    private void CapturePlayStartTransform()
    {
        playStartLocalPosition = rectTransform.localPosition;
        playStartLocalScale = rectTransform.localScale;
        playStartLocalRotation = rectTransform.localRotation;
    }

    private void InitializePlayState()
    {
        appliedImageType = imageType;
        appliedIsHide = isHide;

        motionTimer = 0f;

        currentHideOffset = isHide ? hideYOffset : 0f;
        isHideTransitioning = false;
        hideTransitionTimer = 0f;
        hideTransitionStartOffset = currentHideOffset;
        hideTransitionTargetOffset = currentHideOffset;

        ApplySpriteByType(imageType);

        rectTransform.localPosition = GetBaseAnimatedPosition();
        rectTransform.localScale = playStartLocalScale;
        rectTransform.localRotation = playStartLocalRotation;

        if (!isHide && imageType == ImageType.FourthGrowLoop)
        {
            rectTransform.localScale = Vector3.zero;
        }

        if (graphic != null)
        {
            graphic.SetVerticesDirty();
        }
    }

    private void ApplyTypeImmediately(ImageType newType)
    {
        imageType = newType;
        appliedImageType = newType;
        motionTimer = 0f;

        ApplySpriteByType(newType);

        rectTransform.localRotation = playStartLocalRotation;
        rectTransform.localPosition = GetBaseAnimatedPosition();

        if (!isHide && newType == ImageType.FourthGrowLoop)
        {
            rectTransform.localScale = Vector3.zero;
        }
        else
        {
            rectTransform.localScale = playStartLocalScale;
        }

        if (graphic != null)
        {
            graphic.SetVerticesDirty();
        }
    }

    private void BeginHideTransition(bool hide)
    {
        isHide = hide;
        appliedIsHide = hide;

        isHideTransitioning = true;
        hideTransitionTimer = 0f;
        hideTransitionStartOffset = currentHideOffset;
        hideTransitionTargetOffset = hide ? hideYOffset : 0f;

        if (graphic != null)
        {
            graphic.SetVerticesDirty();
        }
    }

    private void UpdateHideTransition()
    {
        float duration = Mathf.Max(0.0001f, hideTransitionDuration);
        hideTransitionTimer += Time.deltaTime;

        float t = Mathf.Clamp01(hideTransitionTimer / duration);
        float eased = 1f - Mathf.Pow(1f - t, 3f);

        currentHideOffset = Mathf.LerpUnclamped(hideTransitionStartOffset, hideTransitionTargetOffset, eased);

        if (t >= 1f)
        {
            currentHideOffset = hideTransitionTargetOffset;
            isHideTransitioning = false;
        }
    }

    private void ApplySpriteOnlyForEditor()
    {
        if (targetImage != null)
        {
            targetImage.sprite = GetSpriteByType(currentProfileName, imageType);
        }
    }

    private void ApplySpriteByType(ImageType type)
    {
        if (targetImage != null)
        {
            targetImage.sprite = GetSpriteByType(currentProfileName, type);
        }
    }

    private Sprite GetSpriteByType(string profileName, ImageType type)
    {
        ProfileSpriteSet profileSet = GetProfileSpriteSet(profileName);
        Sprite profileSprite = GetProfileSpriteByType(profileSet, type);
        if (profileSprite != null)
        {
            return profileSprite;
        }

        return GetDefaultSpriteByType(type);
    }

    private ProfileSpriteSet GetProfileSpriteSet(string profileName)
    {
        if (string.IsNullOrEmpty(profileName))
        {
            return null;
        }

        for (int i = 0; i < profileSpriteSets.Count; i++)
        {
            ProfileSpriteSet set = profileSpriteSets[i];
            if (set == null)
            {
                continue;
            }

            if (set.profileName == profileName)
            {
                return set;
            }
        }

        return null;
    }

    private Sprite GetProfileSpriteByType(ProfileSpriteSet set, ImageType type)
    {
        if (set == null)
        {
            return null;
        }

        switch (type)
        {
            case ImageType.FirstStill:
                return set.firstSprite;
            case ImageType.SecondBottomSwing:
                return set.secondSprite;
            case ImageType.ThirdVerticalShake:
                return set.thirdSprite;
            case ImageType.FourthGrowLoop:
                return set.fourthSprite;
            case ImageType.FifthRotateLoop:
                return set.fifthSprite;
        }

        return null;
    }

    private Sprite GetDefaultSpriteByType(ImageType type)
    {
        switch (type)
        {
            case ImageType.FirstStill:
                return firstSprite;
            case ImageType.SecondBottomSwing:
                return secondSprite;
            case ImageType.ThirdVerticalShake:
                return thirdSprite;
            case ImageType.FourthGrowLoop:
                return fourthSprite;
            case ImageType.FifthRotateLoop:
                return fifthSprite;
        }

        return null;
    }

    private float GetCurrentTypeYOffset()
    {
        switch (imageType)
        {
            case ImageType.FirstStill:
                return firstYOffset;
            case ImageType.SecondBottomSwing:
                return secondYOffset;
            case ImageType.ThirdVerticalShake:
                return thirdYOffset;
            case ImageType.FourthGrowLoop:
                return fourthYOffset;
            case ImageType.FifthRotateLoop:
                return fifthYOffset;
        }

        return 0f;
    }

    private Vector3 GetBaseAnimatedPosition()
    {
        return playStartLocalPosition + new Vector3(0f, GetCurrentTypeYOffset() + currentHideOffset, 0f);
    }

    private void ApplyTransformAnimation()
    {
        switch (imageType)
        {
            case ImageType.FirstStill:
                ApplyFirstStill();
                break;
            case ImageType.SecondBottomSwing:
                // ApplySecondBottomSwing();
                ApplyFirstStill();
                break;
            case ImageType.ThirdVerticalShake:
                // ApplyThirdVerticalShake();
                ApplyFirstStill();
                break;
            case ImageType.FourthGrowLoop:
                // ApplyFourthGrowLoop();
                ApplyFirstStill();
                break;
            case ImageType.FifthRotateLoop:
                // ApplyFifthRotateLoop();
                ApplyFirstStill();
                break;
        }
    }

    private void ApplyFirstStill()
    {
        rectTransform.localPosition = GetBaseAnimatedPosition();
        rectTransform.localScale = playStartLocalScale;
        rectTransform.localRotation = playStartLocalRotation;
    }

    private void ApplySecondBottomSwing()
    {
        rectTransform.localPosition = GetBaseAnimatedPosition();
        rectTransform.localScale = playStartLocalScale;
        rectTransform.localRotation = playStartLocalRotation;
    }

    private void ApplyThirdVerticalShake()
    {
        float y = Mathf.Sin(motionTimer * thirdShakeSpeed) * thirdShakeAmplitude;

        rectTransform.localPosition = GetBaseAnimatedPosition() + new Vector3(0f, y, 0f);
        rectTransform.localScale = playStartLocalScale;
        rectTransform.localRotation = playStartLocalRotation;
    }

    private void ApplyFourthGrowLoop()
    {
        rectTransform.localPosition = GetBaseAnimatedPosition();
        rectTransform.localRotation = playStartLocalRotation;

        float cycle = fourthGrowDuration + fourthHoldDuration;

        if (cycle <= 0f)
        {
            rectTransform.localScale = playStartLocalScale * FourthMaxScaleMultiplier;
            return;
        }

        float t = motionTimer % cycle;

        if (t < fourthGrowDuration && fourthGrowDuration > 0f)
        {
            float normalized = t / fourthGrowDuration;
            float eased = 1f - Mathf.Pow(1f - normalized, 3f);
            Vector3 targetScale = playStartLocalScale * FourthMaxScaleMultiplier;
            rectTransform.localScale = Vector3.LerpUnclamped(Vector3.zero, targetScale, eased);
        }
        else
        {
            rectTransform.localScale = playStartLocalScale * FourthMaxScaleMultiplier;
        }
    }

    private void ApplyFifthRotateLoop()
    {
        rectTransform.localPosition = GetBaseAnimatedPosition();
        rectTransform.localScale = playStartLocalScale;

        float zAngle = motionTimer * fifthRotationSpeed;
        rectTransform.localRotation = playStartLocalRotation * Quaternion.Euler(0f, 0f, zAngle);
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive())
        {
            return;
        }

        if (imageType != ImageType.SecondBottomSwing)
        {
            return;
        }

        if (isHide && !isHideTransitioning)
        {
            return;
        }

        workVerts.Clear();
        vh.GetUIVertexStream(workVerts);

        if (workVerts.Count == 0)
        {
            return;
        }

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        float minX = float.MaxValue;
        float maxX = float.MinValue;

        for (int i = 0; i < workVerts.Count; i++)
        {
            Vector3 p = workVerts[i].position;

            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
        }

        float width = maxX - minX;
        float height = maxY - minY;

        if (width <= 0f || height <= 0f)
        {
            return;
        }

        float fixedLineY = minY + height * secondFixedLineHeightRatio;
        float wave = Mathf.Sin(motionTimer * secondSwingSpeed);
        float topShift = width * secondTopShiftRatio * wave;

        for (int i = 0; i < workVerts.Count; i++)
        {
            UIVertex v = workVerts[i];

            float influence = Mathf.InverseLerp(fixedLineY, maxY, v.position.y);
            influence = Mathf.Clamp01(influence);

            v.position.x += topShift * influence;
            workVerts[i] = v;
        }

        vh.Clear();
        vh.AddUIVertexTriangleStream(workVerts);
    }

    public void Play(ImageType newType)
    {
        Play(string.Empty, newType);
    }

    public void Play(string profileName, ImageType newType)
    {
        currentProfileName = profileName ?? string.Empty;

        if (!Application.isPlaying)
        {
            imageType = newType;
            ApplySpriteOnlyForEditor();

            if (graphic != null)
            {
                graphic.SetVerticesDirty();
            }

            return;
        }

        imageType = newType;
        appliedImageType = newType;
        isHide = false;
        appliedIsHide = false;

        motionTimer = 0f;

        isHideTransitioning = false;
        hideTransitionTimer = 0f;
        hideTransitionStartOffset = 0f;
        hideTransitionTargetOffset = 0f;
        currentHideOffset = 0f;

        ApplySpriteByType(newType);

        rectTransform.localRotation = playStartLocalRotation;
        rectTransform.localPosition = GetBaseAnimatedPosition();

        if (newType == ImageType.FourthGrowLoop)
        {
            rectTransform.localScale = Vector3.zero;
        }
        else
        {
            rectTransform.localScale = playStartLocalScale;
        }

        if (graphic != null)
        {
            graphic.SetVerticesDirty();
        }
    }

    public void Hide()
    {
        SetHide(true);
    }

    public void SetImageType(ImageType newType)
    {
        if (!Application.isPlaying)
        {
            imageType = newType;
            ApplySpriteOnlyForEditor();

            if (graphic != null)
            {
                graphic.SetVerticesDirty();
            }

            return;
        }

        ApplyTypeImmediately(newType);
    }

    public void SetHide(bool hide)
    {
        if (!Application.isPlaying)
        {
            isHide = hide;

            if (graphic != null)
            {
                graphic.SetVerticesDirty();
            }

            return;
        }

        BeginHideTransition(hide);
    }
}
