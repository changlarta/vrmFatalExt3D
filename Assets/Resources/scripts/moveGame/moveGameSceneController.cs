using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using TMPro;

public sealed class moveGameSceneController : MonoBehaviour
{
    public static moveGameSceneController Instance { get; private set; }

    [System.Serializable]
    private class ModeUIEntry
    {
        public GameObject target;
        public bool showWhenModeActive = true;
    }

    private enum SceneMode
    {
        None,
        Title,
        Game,
        GameOver,
        Clear
    }

    [Header("Required refs")]
    [SerializeField] private GroundStreamer groundStreamer;
    public PlayerController player;

    [Header("Loading UI")]
    [SerializeField] private GameObject loadingIndicatorObject;
    [SerializeField] private float loadingIndicatorRotateSpeed = 180f;

    [Header("Title presentation")]
    [SerializeField] private float titleAutoForwardSpeed = 12f;
    [SerializeField] private Vector3 titleStartPosition = new Vector3(0f, 20f, 0f);

    [Header("Mode UI - Title (Loading)")]
    [SerializeField] private List<ModeUIEntry> titleLoadingUIObjects = new List<ModeUIEntry>();

    [Header("Mode UI - Title (Ready)")]
    [SerializeField] private List<ModeUIEntry> titleReadyUIObjects = new List<ModeUIEntry>();

    [Header("Mode UI - Game")]
    [SerializeField] private List<ModeUIEntry> gameModeUIObjects = new List<ModeUIEntry>();

    [Header("Mode UI - Game Over")]
    [SerializeField] private List<ModeUIEntry> gameOverUIObjects = new List<ModeUIEntry>();

    [Header("Mode UI - Clear")]
    [SerializeField] private List<ModeUIEntry> clearUIObjects = new List<ModeUIEntry>();

    [SerializeField] private GameObject returnToTitleDialogObject;

    [Header("Loading Text")]
    [SerializeField] private GameObject loadingStatusTextObject;

    [Header("Game Over")]
    [SerializeField] private MoveGameContinueCountView continueCountView;

    [Header("Clear")]
    [SerializeField] private TextMeshProUGUI clearTitleTmp;
    [SerializeField] private TextMeshProUGUI clearContinueCountTmp;
    [SerializeField] private TextMeshProUGUI clearTimeTmp;

    private TextMeshProUGUI loadingStatusTmp;

    private bool loaded = false;
    private bool booted = false;
    private bool isLoading = false;
    private bool _isTransitioning = false;

    private int continueCount = 0;
    private float playTimerSeconds = 0f;

    private SceneMode mode = SceneMode.None;

    private Coroutine titleBodyVariantReloadCoroutine;

    private void Awake()
    {
        Instance = this;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        AudioManager.Instance.PlayBGM("bgm4");

        if (loadingStatusTextObject != null)
        {
            loadingStatusTmp = loadingStatusTextObject.GetComponent<TextMeshProUGUI>();
        }
    }

    private IEnumerator Start()
    {
        var ctr = player.GetComponent<VrmToController>();
        var playerCtr = player.GetComponent<PlayerController>();
        if (ctr == null)
        {
            Debug.LogError("[moveGameSceneController] VrmToController missing on player.vrmGameObject.");
            enabled = false;
            yield break;
        }

        ctr.meshPullEnabled = false;

        PrepareTitlePresentationBeforeHeavyLoad();

        loaded = false;
        isLoading = true;
        RefreshModeUI();

        if (loadingStatusTmp != null)
        {
            loadingStatusTmp.text = LocalizationManager.Instance.Get("moveGameLoadingTextPlayer");
        }
        yield return StartCoroutine(playerCtr.CoLoadCharacterOnce());

        if (loadingStatusTmp != null)
        {
            loadingStatusTmp.text = LocalizationManager.Instance.Get("moveGameLoadingTextEnemy");
        }
        if (!groundStreamer.EnsureHeavyTemplateShell())
        {
            Debug.LogError("[moveGameSceneController] Failed to create heavy template shell.");
            enabled = false;
            yield break;
        }

        var heavyTemplateCtrl = groundStreamer.HeavyTemplateController;
        if (heavyTemplateCtrl == null)
        {
            Debug.LogError("[moveGameSceneController] HeavyTemplateController is null.");
            enabled = false;
            yield break;
        }

        yield return StartCoroutine(heavyTemplateCtrl.CoReloadVrmForTemplateOnce());
        groundStreamer.CompleteHeavyTemplateLoad();

        isLoading = false;
        loaded = true;

        FinishInitialLoadingToTitle();
    }

    private void Update()
    {
        if (!enabled) return;

        if (mode == SceneMode.Title)
        {
            TickTitleAutoForward(Time.deltaTime);
        }

        if (isLoading && loadingIndicatorObject != null && loadingIndicatorObject.activeSelf)
        {
            loadingIndicatorObject.transform.Rotate(0f, 0f, -loadingIndicatorRotateSpeed * Time.deltaTime);
        }

        if (mode != SceneMode.Game) return;
        if (!booted) return;

        playTimerSeconds += Time.deltaTime;

        SupplyWorldBounds();
        CheckClearReached();
    }

    private void PrepareTitlePresentationBeforeHeavyLoad()
    {
        mode = SceneMode.Title;
        booted = false;

        player.SetGameplayEnabled(false);
        player.SetCharacterVisible(false);
        player.SetTransformForTitle(titleStartPosition, Quaternion.identity);

        groundStreamer.SetTitlePresentationMode(true);
        groundStreamer.SetFollowTarget(player.transform);
        groundStreamer.RebuildImmediate();

        RefreshModeUI();
    }

    private void FinishInitialLoadingToTitle()
    {
        mode = SceneMode.Title;
        booted = false;

        player.SetGameplayEnabled(false);
        player.SetCharacterVisible(false);

        continueCount = 0;
        playTimerSeconds = 0f;
        continueCountView?.SetContinueCount(continueCount);
        RefreshClearTexts();

        RefreshModeUI();
    }

    private void TickTitleAutoForward(float dt)
    {
        if (player == null) return;
        if (player.transform == null) return;

        Transform tr = player.transform;
        Vector3 p = tr.position;
        p += Vector3.forward * titleAutoForwardSpeed * dt;
        tr.position = p;
    }

    public void StartGameFromTitle()
    {
        if (!loaded) return;
        if (isLoading) return;
        if (mode != SceneMode.Title) return;

        continueCount = 0;
        playTimerSeconds = 0f;
        continueCountView?.SetContinueCount(continueCount);
        RefreshClearTexts();

        VrmChrSceneSpeechDirector speechDirector = VrmChrSceneSpeechDirector.Instance;
        if (speechDirector != null)
        {
            speechDirector.ResetForNewGame();
        }

        StartGameAtLogicalTile(0);

        if (speechDirector != null)
        {
            speechDirector.BeginStartSpeech();
        }
    }

    private void StartGameAtLogicalTile(int logicalTileIndex)
    {
        player.ResetForReload();
        player.SetCharacterVisible(true);
        player.SetGameplayEnabled(true);
        player.currentBodyKey = 0;

        groundStreamer.startTileIndexPublic = Mathf.Max(0, logicalTileIndex);
        groundStreamer.SetTitlePresentationMode(false);
        groundStreamer.SetFollowTarget(player.transform);
        groundStreamer.ReloadRebuildWorld();

        SupplyWorldBounds();
        player.ForceClampNow();

        booted = true;
        mode = SceneMode.Game;

        RefreshModeUI();
        AudioManager.Instance.PlayBGM("bgm5");
    }

    public void ReturnToTitleMode()
    {
        if (!loaded) return;
        if (isLoading) return;

        EnterTitleMode();
    }

    private void EnterTitleMode()
    {
        player.ResetForReload();
        player.SetGameplayEnabled(false);
        player.SetCharacterVisible(false);
        player.SetTransformForTitle(titleStartPosition, Quaternion.identity);

        groundStreamer.SetTitlePresentationMode(true);
        groundStreamer.SetFollowTarget(player.transform);
        groundStreamer.RebuildImmediate();

        booted = false;
        mode = SceneMode.Title;
        isLoading = false;

        continueCount = 0;
        playTimerSeconds = 0f;
        continueCountView?.SetContinueCount(continueCount);
        RefreshClearTexts();

        RefreshModeUI();
    }

    public void OnPlayerGameOver()
    {
        if (mode != SceneMode.Game) return;

        if (groundStreamer != null && groundStreamer.GetLogicalTileIndex() >= groundStreamer.GetClearTileIndex())
        {
            EnterClear();
            return;
        }

        booted = false;
        mode = SceneMode.GameOver;

        continueCountView?.SetContinueCount(continueCount);

        CloseReturnToTitleDialog();
        RefreshModeUI();
    }

    private void CheckClearReached()
    {
        if (mode != SceneMode.Game) return;
        if (groundStreamer == null) return;
        if (groundStreamer.GetLogicalTileIndex() < groundStreamer.GetClearTileIndex()) return;

        EnterClear();
    }

    private void EnterClear()
    {
        if (mode != SceneMode.Game) return;

        booted = false;
        mode = SceneMode.Clear;

        if (player != null)
        {
            player.SetGameplayEnabled(false);
        }

        RefreshClearTexts();
        CloseReturnToTitleDialog();
        RefreshModeUI();
    }

    private void RefreshModeUI()
    {
        bool showTitleLoading = (mode == SceneMode.Title) && isLoading;
        bool showTitleReady = (mode == SceneMode.Title) && !isLoading && loaded;
        bool showGame = (mode == SceneMode.Game);
        bool showGameOver = (mode == SceneMode.GameOver);
        bool showClear = (mode == SceneMode.Clear);

        SetObjectsActive(titleLoadingUIObjects, showTitleLoading);
        SetObjectsActive(titleReadyUIObjects, showTitleReady);
        SetObjectsActive(gameModeUIObjects, showGame);
        SetObjectsActive(gameOverUIObjects, showGameOver);
        SetObjectsActive(clearUIObjects, showClear);

        if (loadingIndicatorObject != null)
        {
            loadingIndicatorObject.SetActive(showTitleLoading);
        }
    }

    private static void SetObjectsActive(List<ModeUIEntry> list, bool modeActive)
    {
        if (list == null) return;

        for (int i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            if (entry == null || entry.target == null) continue;

            entry.target.SetActive(modeActive && entry.showWhenModeActive);
        }
    }

    private void SupplyWorldBounds()
    {
        float laneHalf = GroundStreamer.LANE_WIDTH * 0.5f;
        float minZ = groundStreamer.GetGroundMinZ();
        float maxZ = groundStreamer.GetGroundMaxZ();

        player.SetWorldBounds(
             laneHalfExtent: laneHalf,
             xMargin: 0f,
             zClampEnabled: true,
             minZ: minZ,
             maxZ: maxZ
         );
    }

    private void RefreshClearTexts()
    {
        if (clearTitleTmp != null)
        {
            clearTitleTmp.text = GetRequiredLocalized("moveGameClearTitle");
        }

        if (clearContinueCountTmp != null)
        {
            clearContinueCountTmp.text = string.Format(
                GetRequiredLocalized("moveGameClearContinueCount"),
                continueCount
            );
        }

        if (clearTimeTmp != null)
        {
            clearTimeTmp.text = string.Format(
                GetRequiredLocalized("moveGameClearTime"),
                FormatClock(playTimerSeconds)
            );
        }
    }

    private static string FormatClock(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
        int minutes = totalSeconds / 60;
        int remainSeconds = totalSeconds % 60;
        return $"{minutes:00}:{remainSeconds:00}";
    }

    private static string GetRequiredLocalized(string key)
    {
        if (LocalizationManager.Instance == null)
        {
            Debug.LogError($"[moveGameSceneController] LocalizationManager.Instance is null. key={key}");
            return string.Empty;
        }

        string text = LocalizationManager.Instance.Get(key);
        if (string.IsNullOrEmpty(text) || text == key)
        {
            Debug.LogError($"[moveGameSceneController] Missing localization text. key={key}");
        }

        return text;
    }

    public void ReloadGame()
    {
        if (mode != SceneMode.Game && mode != SceneMode.GameOver) return;

        if (mode == SceneMode.GameOver)
        {
            continueCount++;
        }

        int retryStartTile = groundStreamer.GetRetryRestartDistance();

        StartGameAtLogicalTile(retryStartTile);

        CloseReturnToTitleDialog();

        AudioManager.Instance.PlayBGM("bgm5");
    }

    public IEnumerator LoadSceneMoveGame()
    {
        var ctr = player != null ? player.GetComponent<VrmToController>() : null;
        if (ctr != null)
        {
            var task = ctr.ShutdownForSceneLeaveAsync();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.Exception != null)
            {
                Debug.LogException(task.Exception);
            }
        }

        AudioManager.Instance.StopBGM();

        SceneManager.LoadScene("title");
    }

    public void OnTapStartGameMoveGame()
    {
        if (_isTransitioning) return;
        _isTransitioning = true;

        AudioManager.Instance.PlaySE("titleButton");
        StartCoroutine(LoadSceneMoveGame());
    }

    public void ToggleReturnToTitleDialog()
    {
        if (returnToTitleDialogObject == null) return;

        bool next = !returnToTitleDialogObject.activeSelf;
        returnToTitleDialogObject.SetActive(next);
    }

    public void CloseReturnToTitleDialog()
    {
        if (returnToTitleDialogObject == null) return;

        returnToTitleDialogObject.SetActive(false);
    }

    public void OnTapReturnToTitleMode()
    {
        if (mode != SceneMode.Game && mode != SceneMode.GameOver && mode != SceneMode.Clear) return;
        AudioManager.Instance.PlayBGM("bgm4");

        CloseReturnToTitleDialog();
        ReturnToTitleMode();
    }

    public bool RequestTitleBodyVariantReload(BodyVariant bodyVariant)
    {
        if (!loaded) return false;
        if (isLoading) return false;
        if (mode != SceneMode.Title) return false;
        if (player == null) return false;

        if (titleBodyVariantReloadCoroutine != null)
        {
            StopCoroutine(titleBodyVariantReloadCoroutine);
        }

        titleBodyVariantReloadCoroutine = StartCoroutine(CoReloadTitlePlayerBodyVariant(bodyVariant));
        return true;
    }

    private IEnumerator CoReloadTitlePlayerBodyVariant(BodyVariant bodyVariant)
    {
        var playerCtr = player.GetComponent<PlayerController>();
        if (playerCtr == null)
        {
            Debug.LogError("[moveGameSceneController] PlayerController missing on player.");
            yield break;
        }

        isLoading = true;
        RefreshModeUI();

        if (loadingStatusTmp != null)
        {
            loadingStatusTmp.text = LocalizationManager.Instance.Get("moveGameLoadingTextPlayer");
        }

        yield return null;
        player.gameObject.SetActive(true);
        yield return StartCoroutine(playerCtr.CoReloadBodyVariantForTitle(bodyVariant));
        player.gameObject.SetActive(false);

        isLoading = false;
        RefreshModeUI();

        titleBodyVariantReloadCoroutine = null;
    }
}