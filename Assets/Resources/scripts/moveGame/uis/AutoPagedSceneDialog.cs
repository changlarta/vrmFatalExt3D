using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AutoPagedSceneDialog : MonoBehaviour
{
    [Serializable]
    public struct DialogPage
    {
        public string profileName;
        public string body;
        public bool useMotionVisual;
        public ImageMotionController.ImageType motionType;
        public Sprite staticIconSprite;
        public bool canMute;
        public bool showMuteButton;
    }

    [Header("Text")]
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private int linesPerPage = 3;
    [SerializeField] private float charactersPerSecond = 30f;
    [SerializeField] private float pageStaySeconds = 1.8f;

    [Header("Static Icon")]
    [SerializeField] private GameObject staticIconRoot;
    [SerializeField] private Image staticIconImage;

    [Header("Motion Visual")]
    [SerializeField] private ImageMotionController motionVisual;

    [Header("Mute Button")]
    [SerializeField] private Button muteButton;

    private TMP_Text measureText;
    private readonly List<DialogPage> pages = new List<DialogPage>();
    private Coroutine playCoroutine;

    public bool IsShowing
    {
        get { return gameObject.activeSelf; }
    }

    public int CurrentPageIndex { get; private set; } = -1;
    public DialogPage CurrentPage { get; private set; }

    public event Action MuteRequested;
    public event Action Closed;

    private void Awake()
    {
        measureText = CreateMeasurementTMP(bodyText);
        muteButton.onClick.AddListener(OnMuteButtonClicked);

        bodyText.text = string.Empty;
        bodyText.maxVisibleCharacters = 0;

        CurrentPage = default;
        ApplyIdleVisualState();

        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        muteButton.onClick.RemoveListener(OnMuteButtonClicked);
    }

    public void Begin(List<DialogPage> sectionList)
    {
        StopPlayCoroutine();

        gameObject.SetActive(true);
        ForceLayoutNow();

        pages.Clear();
        CurrentPageIndex = -1;
        CurrentPage = default;

        BuildPages(sectionList);

        if (pages.Count <= 0)
        {
            HideInternal(true);
            return;
        }

        playCoroutine = StartCoroutine(PlayPagesCoroutine());
    }

    public void CloseNow()
    {
        HideInternal(true);
    }

    private void OnMuteButtonClicked()
    {
        if (MuteRequested != null)
        {
            MuteRequested();
        }
    }

    private void StopPlayCoroutine()
    {
        if (playCoroutine != null)
        {
            StopCoroutine(playCoroutine);
            playCoroutine = null;
        }
    }

    private IEnumerator PlayPagesCoroutine()
    {
        for (int i = 0; i < pages.Count; i++)
        {
            CurrentPageIndex = i;
            CurrentPage = pages[i];

            ApplyVisualState(CurrentPage);
            yield return StartCoroutine(ShowPageCoroutine(CurrentPage.body));
        }

        HideInternal(true);
    }

    private IEnumerator ShowPageCoroutine(string pageText)
    {
        bodyText.text = pageText;
        bodyText.maxVisibleCharacters = 0;
        bodyText.ForceMeshUpdate();

        int totalCharacters = bodyText.textInfo.characterCount;

        if (totalCharacters <= 0 || charactersPerSecond <= 0f)
        {
            bodyText.maxVisibleCharacters = int.MaxValue;
        }
        else
        {
            float visibleProgress = 0f;

            while (bodyText.maxVisibleCharacters < totalCharacters)
            {
                visibleProgress += charactersPerSecond * Time.unscaledDeltaTime;

                int nextVisible = Mathf.Clamp(Mathf.FloorToInt(visibleProgress), 0, totalCharacters);
                if (nextVisible < 1)
                {
                    nextVisible = 1;
                }

                bodyText.maxVisibleCharacters = nextVisible;
                yield return null;
            }

            bodyText.maxVisibleCharacters = int.MaxValue;
        }

        if (pageStaySeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(pageStaySeconds);
        }
    }

    private void HideInternal(bool notifyClosed)
    {
        StopPlayCoroutine();

        bodyText.text = string.Empty;
        bodyText.maxVisibleCharacters = 0;

        CurrentPageIndex = -1;
        CurrentPage = default;

        ApplyIdleVisualState();
        gameObject.SetActive(false);

        if (notifyClosed && Closed != null)
        {
            Closed();
        }
    }

    private void ApplyIdleVisualState()
    {
        staticIconRoot.SetActive(false);
        motionVisual.Hide();
        motionVisual.gameObject.SetActive(false);
        muteButton.gameObject.SetActive(false);
    }

    private void ApplyVisualState(DialogPage page)
    {
        if (page.useMotionVisual)
        {
            staticIconRoot.SetActive(false);

            if (!motionVisual.gameObject.activeSelf)
            {
                motionVisual.gameObject.SetActive(true);
            }

            motionVisual.Play(page.motionType);
            muteButton.gameObject.SetActive(page.showMuteButton);
            return;
        }

        motionVisual.Hide();
        motionVisual.gameObject.SetActive(false);
        muteButton.gameObject.SetActive(false);

        if (page.staticIconSprite != null)
        {
            staticIconImage.sprite = page.staticIconSprite;
            staticIconRoot.SetActive(true);
        }
        else
        {
            staticIconRoot.SetActive(false);
        }
    }

    private TMP_Text CreateMeasurementTMP(TMP_Text source)
    {
        GameObject clone = Instantiate(source.gameObject, source.transform.parent);
        clone.name = source.gameObject.name + "_MEASURE_ONLY";

        LayoutElement layoutElement = clone.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = clone.AddComponent<LayoutElement>();
        }

        layoutElement.ignoreLayout = true;

        TMP_Text text = clone.GetComponent<TMP_Text>();
        text.text = string.Empty;
        text.raycastTarget = false;

        Color color = text.color;
        color.a = 0f;
        text.color = color;

        RectTransform srcRect = (RectTransform)source.transform;
        RectTransform dstRect = (RectTransform)clone.transform;

        dstRect.anchorMin = srcRect.anchorMin;
        dstRect.anchorMax = srcRect.anchorMax;
        dstRect.pivot = srcRect.pivot;
        dstRect.anchoredPosition = srcRect.anchoredPosition;
        dstRect.sizeDelta = srcRect.sizeDelta;
        dstRect.localRotation = srcRect.localRotation;
        dstRect.localScale = srcRect.localScale;

        return text;
    }

    private void ForceLayoutNow()
    {
        Canvas.ForceUpdateCanvases();

        LayoutRebuilder.ForceRebuildLayoutImmediate(bodyText.rectTransform);
        bodyText.ForceMeshUpdate();

        LayoutRebuilder.ForceRebuildLayoutImmediate(measureText.rectTransform);
        measureText.ForceMeshUpdate();
    }

    private void BuildPages(List<DialogPage> sectionList)
    {
        for (int i = 0; i < sectionList.Count; i++)
        {
            AppendSectionAsPages(sectionList[i]);
        }

        pages.RemoveAll(p => string.IsNullOrEmpty(p.body));
    }

    private void AppendSectionAsPages(DialogPage section)
    {
        if (string.IsNullOrEmpty(section.body))
        {
            return;
        }

        string text = TrimLeadingNewLines(section.body);
        int pos = 0;

        while (pos < text.Length)
        {
            int length = FindMaxPrefixLengthThatFits(text, pos, linesPerPage);

            if (length <= 0)
            {
                length = 1;
            }

            string pageText = text.Substring(pos, length);
            pageText = TrimLeadingNewLines(pageText);

            if (!string.IsNullOrEmpty(pageText))
            {
                DialogPage page = section;
                page.body = pageText;
                pages.Add(page);
            }

            pos += length;

            while (pos < text.Length && text[pos] == '\n')
            {
                pos++;
            }
        }
    }

    private string TrimLeadingNewLines(string text)
    {
        int index = 0;

        while (index < text.Length && text[index] == '\n')
        {
            index++;
        }

        if (index <= 0)
        {
            return text;
        }

        return text.Substring(index);
    }

    private int FindMaxPrefixLengthThatFits(string text, int start, int maxLines)
    {
        int remaining = text.Length - start;

        if (remaining <= 0)
        {
            return 0;
        }

        if (MeasureLines(text.Substring(start, remaining)) <= maxLines)
        {
            return remaining;
        }

        int low = 1;
        int high = remaining;

        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            int lines = MeasureLines(text.Substring(start, mid));

            if (lines <= maxLines)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    private int MeasureLines(string text)
    {
        measureText.text = text;
        measureText.ForceMeshUpdate();

        int lineCount = measureText.textInfo.lineCount;

        if (lineCount <= 0)
        {
            ForceLayoutNow();

            measureText.text = text;
            measureText.ForceMeshUpdate();

            lineCount = measureText.textInfo.lineCount;

            if (lineCount <= 0)
            {
                return int.MaxValue;
            }
        }

        return lineCount;
    }
}