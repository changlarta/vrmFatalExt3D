using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public sealed class PagedPanel : MonoBehaviour
{
    [Serializable]
    private sealed class Page
    {
        public Sprite image;
        public string textKey;
    }

    [SerializeField] private List<Page> pages = new List<Page>();

    private Image _image;
    private TMP_Text _bodyText;
    private Button _button;
    private TMP_Text _buttonLabel;

    private int _pageIndex;

    private void Awake()
    {
        // 固定構造前提：探索しない
        _image = transform.GetChild(0).GetComponent<Image>();
        _bodyText = transform.GetChild(1).GetComponent<TMP_Text>();

        Transform buttonRoot = transform.GetChild(2);
        _button = buttonRoot.GetComponent<Button>();
        _buttonLabel = buttonRoot.GetChild(0).GetComponent<TMP_Text>();

        _button.onClick.AddListener(OnButtonClicked);

        // 本文テキストにリンククリック処理を付与（同一ファイル内の内部コンポーネント）
        var catcher = _bodyText.gameObject.GetComponent<LinkClickCatcher>();
        if (catcher == null) catcher = _bodyText.gameObject.AddComponent<LinkClickCatcher>();
        catcher.Init(_bodyText, OnLinkClicked);
    }

    private void OnEnable()
    {
        // 表示した瞬間に必ず1ページ目
        ResetToFirstPage();
        ApplyPage();
    }

    /// <summary>
    /// 表示状態を反転する（非表示→表示の瞬間に1ページ目へ戻す）
    /// </summary>
    public void ToggleVisibility()
    {
        AudioManager.Instance.PlaySE("click");
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        ResetToFirstPage();
        ApplyPage();
    }

    private void OnButtonClicked()
    {
        AudioManager.Instance.PlaySE("click");

        if (pages == null || pages.Count == 0)
        {
            gameObject.SetActive(false);
            return;
        }

        bool isLastPage = (_pageIndex >= pages.Count - 1);

        if (!isLastPage)
        {
            _pageIndex++;
            ApplyPage();
            return;
        }

        gameObject.SetActive(false);
    }

    private void ResetToFirstPage()
    {
        _pageIndex = 0;
    }

    private void ApplyPage()
    {
        if (pages == null || pages.Count == 0)
            return;

        if (_pageIndex < 0) _pageIndex = 0;
        if (_pageIndex > pages.Count - 1) _pageIndex = pages.Count - 1;

        Page page = pages[_pageIndex];

        // Image
        _image.sprite = page.image;
        _image.enabled = (page.image != null);

        // Body Text (key -> localized -> link formatted)
        string localizedBody = LocalizationManager.Instance.Get(page.textKey);
        _bodyText.text = ConvertMarkdownLinksToTMP(localizedBody);

        // Button label (magic string keys)
        bool isLast = (_pageIndex == pages.Count - 1);
        _buttonLabel.text = LocalizationManager.Instance.Get(isLast ? "PagedPanelClose" : "PagedPanelNext");
    }

    private void OnLinkClicked(string url)
    {
        // 実装統一：ここは必ず Application.OpenURL
        Application.OpenURL(url);
    }

    private static string ConvertMarkdownLinksToTMP(string src)
    {
        if (string.IsNullOrEmpty(src))
            return string.Empty;

        // http(s) のみをリンク扱い（曖昧なURL解釈はしない）
        const string pattern = @"\[(.*?)\]\((https?://[^\s)]+)\)";

        return Regex.Replace(src, pattern, match =>
        {
            string label = match.Groups[1].Value;
            string url = match.Groups[2].Value;

            // 青+下線（色はマジック値）
            return "<color=#007AFF><u><link=\"" + url + "\">" + label + "</link></u></color>";
        });
    }

    private sealed class LinkClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        private TMP_Text _text;
        private Action<string> _onClicked;

        public void Init(TMP_Text text, Action<string> onClicked)
        {
            _text = text;
            _onClicked = onClicked;
            _text.raycastTarget = true;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // クリック位置からリンクを特定
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(_text, eventData.position, eventData.pressEventCamera);
            if (linkIndex == -1)
                return;

            TMP_LinkInfo linkInfo = _text.textInfo.linkInfo[linkIndex];
            string url = linkInfo.GetLinkID();

            if (string.IsNullOrEmpty(url))
                return;

            _onClicked?.Invoke(url);
        }
    }
}
