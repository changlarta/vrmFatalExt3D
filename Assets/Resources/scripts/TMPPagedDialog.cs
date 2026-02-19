using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TMPPagedDialog : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private int linesPerPage = 3;

    [Tooltip("このGameObjectの子0がTMP_Text（TextMeshProUGUI等）である前提")]
    [SerializeField] private int textChildIndex = 0;

    [Header("Auto Advance")]
    [Tooltip("ページ表示開始からこの秒数経過で自動で次へ送る（リアルタイム秒）")]
    [SerializeField] private float autoAdvanceSeconds = 5f;

    private TMP_Text viewTmp;     // 表示用
    private TMP_Text measureTmp;  // 計測用（不可視）

    private RectTransform _prevButtonRect;
    private Canvas _rootCanvas;

    private readonly List<string> pages = new();
    private int pageIndex = 0;

    private Coroutine typingCo;
    private Coroutine autoAdvanceCo;
    private bool isTyping = false;
    private bool requestFinish = false;

    private void Awake()
    {
        if (transform.childCount <= textChildIndex)
        {
            Debug.LogError($"{nameof(TMPPagedDialog)}: 子{textChildIndex}が存在しません。子0にTMP_Textが必要です。");
            enabled = false;
            return;
        }

        viewTmp = transform.GetChild(textChildIndex).GetComponent<TMP_Text>();
        if (viewTmp == null)
        {
            Debug.LogError($"{nameof(TMPPagedDialog)}: 子{textChildIndex}にTMP_Textが見つかりません。");
            enabled = false;
            return;
        }

        if (transform.childCount > 1)
        {
            _prevButtonRect = transform.GetChild(1) as RectTransform;
        }
        else
        {
            Debug.LogWarning($"{nameof(TMPPagedDialog)}: 子1(Prevボタン)が存在しません。");
        }

        // Canvas参照（Overlayならnull cameraでOK、Camera/Worldなら必要）
        _rootCanvas = GetComponentInParent<Canvas>();

        // 計測用TMPを作る（表示には使わない）
        measureTmp = CreateMeasurementTMP(viewTmp);

        // 初期は非表示
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!gameObject.activeInHierarchy) return;
        if (!IsPointerDownThisFrame()) return;

        // Prevボタン上のタップは Next にしない
        if (IsOverPrevButton()) return;

        if (!gameObject.activeSelf) return;

        // それ以外（他UIボタン含む）は全部 Next
        Next();
    }

    private TMP_Text CreateMeasurementTMP(TMP_Text src)
    {
        // 同じRect幅/設定で測りたいので複製
        var go = Instantiate(src.gameObject, src.transform.parent);
        go.name = $"{src.gameObject.name}_MEASURE_ONLY";

        // レイアウトに影響しないようにする（LayoutGroup配下対策）
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        // 不可視化（ただしActiveのまま。Inactiveだと計測が不安定になるケースがある）
        var t = go.GetComponent<TMP_Text>();
        t.text = string.Empty;
        t.raycastTarget = false;

        // 透明にする（見えないが計測はする）
        var c = t.color;
        c.a = 0f;
        t.color = c;

        // 念のため、表示用と同じサイズに揃える
        var srcRt = (RectTransform)src.transform;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = srcRt.anchorMin;
        rt.anchorMax = srcRt.anchorMax;
        rt.pivot = srcRt.pivot;
        rt.anchoredPosition = srcRt.anchoredPosition;
        rt.sizeDelta = srcRt.sizeDelta;
        rt.localRotation = srcRt.localRotation;
        rt.localScale = srcRt.localScale;

        return t;
    }

    // public 関数1：キーで開始
    public void Begin(string key)
    {
        string raw = LocalizationManager.Instance.Get(key);
        if (raw == null || raw == "") return;

        // 表示ON（レイアウト更新が必要）
        gameObject.SetActive(true);
        StopPageCoroutines();

        AudioManager.Instance.PlaySE("text_show");

        // レイアウト確定（UI環境差を吸収するために必ず実行）
        ForceLayoutNow();

        pages.Clear();
        pageIndex = 0;

        BuildPages(raw);

        if (pages.Count == 0)
        {
            Hide();
            return;
        }

        StartPageTyping(pageIndex);
    }

    // public 関数2：次へ
    public void Next()
    {
        if (!gameObject.activeSelf) return;
        if (pages.Count == 0) { Hide(); return; }

        if (isTyping)
        {
            // タイプ中は遷移しない。即完了のみ。
            requestFinish = true;
            return;
        }

        pageIndex++;
        if (pageIndex >= pages.Count)
        {
            Hide();
            return;
        }

        StartPageTyping(pageIndex);
    }

    // public 関数3：前へ
    public void Prev()
    {
        AudioManager.Instance.PlaySE("text_back");
        if (!gameObject.activeSelf) return;
        if (pages.Count == 0) return;
        if (pageIndex <= 0) return; // 最初は何もしない

        pageIndex = Mathf.Max(0, pageIndex - 1);
        StartPageTyping(pageIndex);
    }

    public void SetIconImage(String path)
    {
        var image = gameObject.transform.GetChild(2).GetComponent<Image>();
        image.sprite = Resources.Load<Sprite>(path);
    }

    private void Hide()
    {
        StopPageCoroutines();
        viewTmp.text = string.Empty;
        viewTmp.maxVisibleCharacters = 0;
        gameObject.SetActive(false);
    }

    private void StopPageCoroutines()
    {
        // typing
        if (typingCo != null)
        {
            StopCoroutine(typingCo);
            typingCo = null;
        }

        // auto advance
        if (autoAdvanceCo != null)
        {
            StopCoroutine(autoAdvanceCo);
            autoAdvanceCo = null;
        }

        isTyping = false;
        requestFinish = false;
    }

    private void StartPageTyping(int index)
    {
        StopPageCoroutines();

        string pageText = pages[index];

        viewTmp.text = pageText;
        viewTmp.ForceMeshUpdate();

        // タイプ表示は maxVisibleCharacters で制御（折り返し揺れを起こしにくい）
        viewTmp.maxVisibleCharacters = 0;

        typingCo = StartCoroutine(TypeCoroutine());

        // ページ表示開始から一定秒数で自動送り
        if (autoAdvanceSeconds > 0f)
        {
            autoAdvanceCo = StartCoroutine(AutoAdvanceCoroutine());
        }
    }

    private bool IsPointerDownThisFrame()
    {
        // まずマウス（PC/WebGL含む）
        if (Input.GetMouseButtonDown(0))
            return true;

        // タッチ（モバイル）
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            return true;
        return false;
    }

    private Vector2 GetPointerPosition()
    {
        if (Input.touchCount > 0)
            return Input.GetTouch(0).position;

        return Input.mousePosition;
    }

    private bool IsOverPrevButton()
    {
        if (_prevButtonRect == null) return false;

        Camera cam = null;
        if (_rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = _rootCanvas.worldCamera;

        return RectTransformUtility.RectangleContainsScreenPoint(_prevButtonRect, GetPointerPosition(), cam);
    }

    private IEnumerator TypeCoroutine()
    {
        isTyping = true;
        requestFinish = false;

        // textInfo.characterCount は ForceMeshUpdate後に確定する
        viewTmp.ForceMeshUpdate();
        int total = viewTmp.textInfo != null ? viewTmp.textInfo.characterCount : 0;

        // 空なら即終了
        if (total <= 0)
        {
            viewTmp.maxVisibleCharacters = int.MaxValue;
            isTyping = false;
            yield break;
        }

        int visible = 0;
        while (visible < total)
        {
            if (requestFinish)
            {
                viewTmp.maxVisibleCharacters = int.MaxValue; // 全表示
                break;
            }

            visible++;
            viewTmp.maxVisibleCharacters = visible;

            // 1フレーム1文字
            yield return null;
        }

        viewTmp.maxVisibleCharacters = int.MaxValue;
        isTyping = false;
        requestFinish = false;
    }

    private IEnumerator AutoAdvanceCoroutine()
    {
        // 「表示されてから5秒」なのでページ開始時点からカウント
        yield return new WaitForSecondsRealtime(autoAdvanceSeconds);

        if (!gameObject.activeSelf) yield break;
        if (pages.Count == 0) yield break;

        // タイプ中ならまず全文表示にして、完了を待つ
        if (isTyping)
        {
            requestFinish = true;
            while (isTyping)
            {
                if (!gameObject.activeSelf) yield break;
                yield return null;
            }
        }

        // 次へ（最終ならHideへ）
        if (gameObject.activeSelf)
        {
            Next();
        }
    }

    private void ForceLayoutNow()
    {
        // ダイアログがアクティブである前提
        Canvas.ForceUpdateCanvases();

        // 表示用
        if (viewTmp != null)
        {
            var rt = viewTmp.rectTransform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            viewTmp.ForceMeshUpdate();
        }

        // 計測用（表示用と同じRect条件にしておく）
        if (measureTmp != null)
        {
            var rt = measureTmp.rectTransform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            measureTmp.ForceMeshUpdate();
        }
    }

    // --------------------------
    // Paging
    // --------------------------

    private void BuildPages(string raw)
    {
        // \f を強制改ページとして扱う
        // 改ページで区切った各セクションを、さらに「見た目3行以内」に分割して pages に積む
        var sections = raw.Split('\f');

        foreach (var sec in sections)
        {
            AppendSectionAsPages(sec);
        }

        // 末尾が \f で終わる場合など、空が混ざり得るので除去
        pages.RemoveAll(p => string.IsNullOrEmpty(p));
    }

    private void AppendSectionAsPages(string section)
    {
        if (string.IsNullOrEmpty(section))
            return;

        int pos = 0;

        // ページ先頭に来た \n は空行になりやすいので除去（最低限の不自然空行対策）
        section = TrimLeadingNewLines(section);

        while (pos < section.Length)
        {
            // 次ページの先頭も同様に
            if (pos == 0)
                section = TrimLeadingNewLines(section);

            int len = FindMaxPrefixLengthThatFits(section, pos, linesPerPage);
            if (len <= 0)
            {
                // 計測が壊れても無限ループしないため、最低1文字進める
                len = 1;
            }

            string page = section.Substring(pos, len);

            // ページ先頭の \n は除去（posが進んだ後でも page が \n 始まりになるケースがある）
            page = TrimLeadingNewLines(page);

            if (!string.IsNullOrEmpty(page))
                pages.Add(page);

            pos += len;

            // 次ページ先頭の \n をスキップ
            while (pos < section.Length && section[pos] == '\n')
                pos++;
        }
    }

    private string TrimLeadingNewLines(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        int i = 0;
        while (i < s.Length && s[i] == '\n') i++;
        if (i <= 0) return s;
        return s.Substring(i);
    }

    /// <summary>
    /// section[pos..pos+len) が「見た目 maxLines 行以内」に収まる最大lenを返す（二分探索）。
    /// </summary>
    private int FindMaxPrefixLengthThatFits(string section, int pos, int maxLines)
    {
        int remaining = section.Length - pos;
        if (remaining <= 0) return 0;

        // まず upper bound：残り全部が入るならそれでOK
        if (MeasureLines(section.Substring(pos, remaining)) <= maxLines)
            return remaining;

        int lo = 1;          // 必ず1以上（0は意味がない）
        int hi = remaining;  // ここは入らないことがわかっている（上のifで弾いた）

        // 二分探索：入る最大
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2; // 上寄せ
            int lines = MeasureLines(section.Substring(pos, mid));

            if (lines <= maxLines)
                lo = mid;
            else
                hi = mid - 1;
        }

        return lo;
    }

    private int MeasureLines(string text)
    {
        if (measureTmp == null)
            measureTmp = CreateMeasurementTMP(viewTmp);

        measureTmp.text = text;
        measureTmp.ForceMeshUpdate();

        var info = measureTmp.textInfo;
        int lc = (info != null) ? info.lineCount : 0;

        // ここが重要：0行を「1行扱い」で通さない（ページ分割が破綻するため）
        // 0なら「測れていない」可能性があるので、最小でも再計測できる状態を作る。
        if (lc <= 0)
        {
            // できる範囲でレイアウトを再確定してもう一度測る
            ForceLayoutNow();

            measureTmp.text = text;
            measureTmp.ForceMeshUpdate();

            info = measureTmp.textInfo;
            lc = (info != null) ? info.lineCount : 0;

            // それでも0なら、測定不能として「超過扱い」に倒す（ページが巨大化するのを防ぐ）
            if (lc <= 0)
                return int.MaxValue;
        }

        return lc;
    }
}
