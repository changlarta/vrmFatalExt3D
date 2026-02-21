using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class configSideBar : MonoBehaviour
{
    private Transform content;
    private RectTransform contentRect;
    private TextMeshProUGUI langeText;
    private TextMeshProUGUI scaleText;
    private TextMeshProUGUI yjsButtonText;
    private TextMeshProUGUI steamButtonText;
    private Transform yjsSection;

    // 追加：音声セクション
    private TextMeshProUGUI audioText;
    private Slider seSlider;
    private Slider bgmSlider;

    int secretCount = 0;

    void Start()
    {
        content = transform.GetChild(0).GetChild(0).GetChild(0);
        contentRect = content.GetComponent<RectTransform>();

        var fullScreenSection = content.GetChild(1);
        var fullBt1 = fullScreenSection.GetChild(1).AddComponent<Button>();
        fullBt1.onClick.AddListener(() =>
        {
            var fm = FullscreenManager.Instance;
            fm.SetFullscreen();
        });
        var fullBt2 = fullScreenSection.GetChild(2).AddComponent<Button>();
        fullBt2.onClick.AddListener(() =>
        {
            var fm = FullscreenManager.Instance;
            fm.SetWindowed();
        });

        // 追加された音声セクション（content.GetChild(1)）
        var audioSection = content.GetChild(2);
        audioText = audioSection.GetChild(0).GetComponent<TextMeshProUGUI>();

        seSlider = audioSection.GetChild(1).GetComponent<Slider>();
        bgmSlider = audioSection.GetChild(2).GetComponent<Slider>();

        // 初期値はどちらも 0.8
        seSlider.minValue = 0f;
        seSlider.maxValue = 1f;
        seSlider.value = AudioManager.Instance.GetSEVolume();

        bgmSlider.minValue = 0f;
        bgmSlider.maxValue = 1f;
        bgmSlider.value = AudioManager.Instance.GetBGMVolume();

        seSlider.onValueChanged.AddListener(v => AudioManager.Instance.SetSEVolume(v));
        bgmSlider.onValueChanged.AddListener(v => AudioManager.Instance.SetBGMVolume(v));

        var langSection = content.GetChild(3);
        langeText = langSection.GetChild(2).GetComponent<TextMeshProUGUI>();

        if (content.childCount < 5) return;

        var scaleSection = content.GetChild(4);
        scaleText = scaleSection.GetChild(0).GetComponent<TextMeshProUGUI>();

        if (content.childCount < 6) return;

        var steamSection = content.GetChild(5);
        steamButtonText = steamSection.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>();

        if (content.childCount < 7) return;

        yjsSection = content.GetChild(6);
        yjsButtonText = yjsSection.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>();
    }

    void Update()
    {
        audioText.text = LocalizationManager.Instance.Get("audioSettingText");
        langeText.text = LocalizationManager.Instance.Get("languageSettingText");
        if (scaleText != null)
            scaleText.text = LocalizationManager.Instance.Get("scaleSettingText");

        if (steamButtonText != null)
        {
            if (SettingStore.useSteamMode)
            {
                steamButtonText.text = LocalizationManager.Instance.Get("steamSettingTextOff");
            }
            else
            {
                steamButtonText.text = LocalizationManager.Instance.Get("steamSettingTextOn");
            }
        }

        var yjsText = IsYjsStore.isYjsMode ? LocalizationManager.Instance.Get("yjsConfigOff") : LocalizationManager.Instance.Get("yjsConfigOn");
        if (yjsButtonText != null)
            yjsButtonText.text = yjsText;
    }

    public void ClickSteamButton()
    {
        var newMode = !SettingStore.useSteamMode;
        SettingStore.useSteamMode = newMode;
        var inst = VrmChrSceneController.Instance;
        var ctr = inst.vrmToController;

        var ctr2 = inst.canvasUIController.gameObject.transform.GetChild(1).GetComponent<ScreenDripOverlay>();
        ctr2.enableDrips = newMode;
    }

    public void ClickSecreButton()
    {
        AudioManager.Instance.PlaySE("nya-n");
        secretCount++;
        if (secretCount > 114.514f)
        {
            yjsSection.gameObject.SetActive(!yjsSection.gameObject.activeSelf);
        }
    }
}