using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class VrmChrSceneSpeechDirector : MonoBehaviour
{
    [Serializable]
    private class SpeechProfile
    {
        public string name;
        public bool useMotionVisual;
        public ImageMotionController.ImageType motionType = ImageMotionController.ImageType.FirstStill;
        public Sprite staticIconSprite;
        public bool canMute;
    }

    public static VrmChrSceneSpeechDirector Instance { get; private set; }

    [Header("Dialog")]
    [SerializeField] private AutoPagedSceneDialog dialog;

    [Header("{name} Profiles")]
    [SerializeField] private List<SpeechProfile> speechProfiles = new List<SpeechProfile>();

    [Header("Mute")]
    [SerializeField] private string muteSpeechKey = "Common_MuteSpeech";
    [SerializeField] private float muteBlockSeconds = 30f;

    [Header("Debug")]
    [SerializeField] private List<string> debugSpeechKeys = new List<string>();

    private SpeechCharacterType speechCharacterType = SpeechCharacterType.None;

    private readonly HashSet<int> spokenFoodIds = new HashSet<int>();
    private readonly HashSet<int> spokenDropIds = new HashSet<int>();
    private readonly HashSet<int> spokenWeightIds = new HashSet<int>();

    private bool cursorInitialized;
    private int cursorWeight;
    private bool isReady;

    private float muteUntilUnscaledTime;
    private int debugSpeechIndex;

    private void Awake()
    {
        Instance = this;
        dialog.MuteRequested += MuteCurrentSpeech;
    }

    private void OnDestroy()
    {
        dialog.MuteRequested -= MuteCurrentSpeech;
    }

    private void Update()
    {
        speechCharacterType = SweetGameVrmStore.speechType;

        VrmChrSceneController scene = VrmChrSceneController.Instance;
        if (scene == null || scene.vrmToController == null)
        {
            return;
        }

        if (!isReady)
        {
            isReady = scene.vrmToController.IsReady;
            if (!isReady)
            {
                return;
            }

            cursorWeight = Mathf.RoundToInt(scene.vrmToController.bodyKey);
            cursorInitialized = true;

            if (speechCharacterType != SpeechCharacterType.None)
            {
                TryBeginSpeech(speechCharacterType + "_Start", false, string.Empty);
            }

            return;
        }

        SetWeightSpeech(scene.vrmToController.bodyKey);
    }

    public void UpdateSpeechCharacterType(SpeechCharacterType type)
    {
        if (type == SpeechCharacterType.None)
        {
            return;
        }

        speechCharacterType = type;
    }

    public void SetWeightSpeech(float weight)
    {
        if (speechCharacterType == SpeechCharacterType.None)
        {
            return;
        }

        if (dialog.IsShowing)
        {
            return;
        }

        int current = Mathf.RoundToInt(weight);

        if (!cursorInitialized)
        {
            cursorWeight = current;
            cursorInitialized = true;
            return;
        }

        if (current == cursorWeight)
        {
            return;
        }

        cursorWeight += current > cursorWeight ? 1 : -1;

        if (cursorWeight < 1 || cursorWeight > 100)
        {
            return;
        }

        if (!spokenWeightIds.Add(cursorWeight))
        {
            return;
        }

        TryBeginSpeech(speechCharacterType + "_WeightSpeech_" + cursorWeight, false, string.Empty);
    }

    public void setFoodSpeech(int id)
    {
        if (speechCharacterType == SpeechCharacterType.None)
        {
            return;
        }

        if (dialog.IsShowing)
        {
            return;
        }

        if (!spokenFoodIds.Add(id))
        {
            return;
        }

        TryBeginSpeech(speechCharacterType + "_FoodSpeech_" + id, false, string.Empty);
    }

    public void setDropSpeech(int id)
    {
        if (speechCharacterType == SpeechCharacterType.None)
        {
            return;
        }

        if (dialog.IsShowing)
        {
            return;
        }

        if (!spokenDropIds.Add(id))
        {
            return;
        }

        TryBeginSpeech(speechCharacterType + "_DropSpeech_" + id, false, string.Empty);
    }

    public void DebugPlayNextSpeech()
    {
        if (debugSpeechKeys.Count <= 0)
        {
            return;
        }

        if (debugSpeechIndex >= debugSpeechKeys.Count)
        {
            debugSpeechIndex = 0;
        }

        string key = debugSpeechKeys[debugSpeechIndex];
        debugSpeechIndex++;

        if (debugSpeechIndex >= debugSpeechKeys.Count)
        {
            debugSpeechIndex = 0;
        }

        TryBeginSpeech(key, true, string.Empty);
    }

    public void ResetDebugSpeechIndex()
    {
        debugSpeechIndex = 0;
    }

    public void MuteCurrentSpeech()
    {
        if (!dialog.IsShowing)
        {
            return;
        }

        AutoPagedSceneDialog.DialogPage currentPage = dialog.CurrentPage;

        if (!currentPage.useMotionVisual)
        {
            return;
        }

        if (!currentPage.canMute)
        {
            return;
        }

        muteUntilUnscaledTime = Time.unscaledTime + muteBlockSeconds;

        string raw = LocalizationManager.Instance.Get(muteSpeechKey);
        if (string.IsNullOrEmpty(raw))
        {
            dialog.CloseNow();
            return;
        }

        List<AutoPagedSceneDialog.DialogPage> sections = BuildDialogSections(raw, string.Empty, false);

        if (sections.Count <= 0)
        {
            dialog.CloseNow();
            return;
        }

        for (int i = 0; i < sections.Count; i++)
        {
            AutoPagedSceneDialog.DialogPage page = sections[i];
            page.canMute = false;
            page.showMuteButton = false;
            sections[i] = page;
        }

        AudioManager.Instance.PlaySE("text_show");
        dialog.Begin(sections);
    }

    private bool TryBeginSpeech(string key, bool allowInterruptCurrentDialog, string defaultProfileName)
    {
        if (!allowInterruptCurrentDialog && dialog.IsShowing)
        {
            return false;
        }

        string raw = LocalizationManager.Instance.Get(key);
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        bool skipMutedSections = Time.unscaledTime < muteUntilUnscaledTime;
        List<AutoPagedSceneDialog.DialogPage> sections = BuildDialogSections(raw, defaultProfileName, skipMutedSections);

        if (sections.Count <= 0)
        {
            return false;
        }

        AudioManager.Instance.PlaySE("text_show");
        dialog.Begin(sections);
        return true;
    }

    private List<AutoPagedSceneDialog.DialogPage> BuildDialogSections(string raw, string defaultProfileName, bool skipMutedSections)
    {
        List<AutoPagedSceneDialog.DialogPage> sections = new List<AutoPagedSceneDialog.DialogPage>();

        string currentProfileName = defaultProfileName;
        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];

            if (c == '{')
            {
                int closeIndex = raw.IndexOf('}', i + 1);
                if (closeIndex > i + 1)
                {
                    AppendSectionIfNeeded(sections, builder, currentProfileName, skipMutedSections);
                    currentProfileName = raw.Substring(i + 1, closeIndex - i - 1);
                    i = closeIndex;
                    continue;
                }
            }

            if (c == '\f')
            {
                AppendSectionIfNeeded(sections, builder, currentProfileName, skipMutedSections);
                continue;
            }

            builder.Append(c);
        }

        AppendSectionIfNeeded(sections, builder, currentProfileName, skipMutedSections);

        return sections;
    }

    private void AppendSectionIfNeeded(
        List<AutoPagedSceneDialog.DialogPage> sections,
        StringBuilder builder,
        string profileName,
        bool skipMutedSections)
    {
        if (builder.Length <= 0)
        {
            return;
        }

        string body = builder.ToString();
        builder.Length = 0;

        bool useMotionVisual;
        ImageMotionController.ImageType motionType;
        Sprite staticIconSprite;
        bool canMute;

        ResolveProfile(profileName, out useMotionVisual, out motionType, out staticIconSprite, out canMute);

        if (skipMutedSections && canMute)
        {
            return;
        }

        AutoPagedSceneDialog.DialogPage page = new AutoPagedSceneDialog.DialogPage();
        page.profileName = profileName;
        page.body = body;
        page.useMotionVisual = useMotionVisual;
        page.motionType = motionType;
        page.staticIconSprite = staticIconSprite;
        page.canMute = canMute;
        page.showMuteButton = useMotionVisual && canMute;

        sections.Add(page);
    }

    private void ResolveProfile(
        string profileName,
        out bool useMotionVisual,
        out ImageMotionController.ImageType motionType,
        out Sprite staticIconSprite,
        out bool canMute)
    {
        useMotionVisual = false;
        motionType = ImageMotionController.ImageType.FirstStill;
        staticIconSprite = null;
        canMute = false;

        for (int i = 0; i < speechProfiles.Count; i++)
        {
            SpeechProfile profile = speechProfiles[i];

            if (profile.name != profileName)
            {
                continue;
            }

            useMotionVisual = profile.useMotionVisual;
            motionType = profile.motionType;
            staticIconSprite = profile.staticIconSprite;
            canMute = profile.canMute;
            return;
        }
    }
}