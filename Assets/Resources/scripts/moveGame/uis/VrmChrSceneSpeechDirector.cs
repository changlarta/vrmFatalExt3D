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

    private readonly HashSet<int> spokenBossIds = new HashSet<int>();

    private bool hasSpokenStart;
    private float muteUntilUnscaledTime;

    private void Awake()
    {
        Instance = this;

        if (dialog != null)
        {
            dialog.MuteRequested += MuteCurrentSpeech;
        }
    }

    private void OnDestroy()
    {
        if (dialog != null)
        {
            dialog.MuteRequested -= MuteCurrentSpeech;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ResetForNewGame()
    {
        hasSpokenStart = false;
        spokenBossIds.Clear();
    }

    public void BeginStartSpeech()
    {
        if (hasSpokenStart)
        {
            return;
        }

        hasSpokenStart = true;
        TryBeginSpeech("Start", false, string.Empty);
    }

    public void setBossSpeech(int id)
    {
        if (!spokenBossIds.Add(id))
        {
            return;
        }

        TryBeginSpeech("BossSpeech_" + id, true, string.Empty);
    }

    public void setFatigueSpeech()
    {
        int id = UnityEngine.Random.Range(1, 3);
        TryBeginSpeech("FatigueSpeech_" + id, true, string.Empty);
    }


    public void MuteCurrentSpeech()
    {
        if (dialog == null || !dialog.IsShowing)
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
        if (dialog == null)
        {
            return false;
        }

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
