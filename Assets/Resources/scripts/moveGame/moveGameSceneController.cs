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
        GameOver
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

    [SerializeField] private GameObject returnToTitleDialogObject;

    [Header("Loading Text")]
    [SerializeField] private GameObject loadingStatusTextObject;

    private TextMeshProUGUI loadingStatusTmp;

    private bool loaded = false;
    private bool booted = false;
    private bool isLoading = false;
    private bool _isTransitioning = false;

    private SceneMode mode = SceneMode.None;

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

        SupplyWorldBounds();
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

        StartGameAtLogicalTile(0);
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

        RefreshModeUI();
    }

    public void OnPlayerGameOver()
    {
        if (mode != SceneMode.Game) return;

        booted = false;
        mode = SceneMode.GameOver;

        CloseReturnToTitleDialog();
        RefreshModeUI();
    }

    private void RefreshModeUI()
    {
        bool showTitleLoading = (mode == SceneMode.Title) && isLoading;
        bool showTitleReady = (mode == SceneMode.Title) && !isLoading && loaded;
        bool showGame = (mode == SceneMode.Game);
        bool showGameOver = (mode == SceneMode.GameOver);

        SetObjectsActive(titleLoadingUIObjects, showTitleLoading);
        SetObjectsActive(titleReadyUIObjects, showTitleReady);
        SetObjectsActive(gameModeUIObjects, showGame);
        SetObjectsActive(gameOverUIObjects, showGameOver);

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

    public void ReloadGame()
    {
        if (mode != SceneMode.Game && mode != SceneMode.GameOver) return;

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
        if (mode != SceneMode.Game && mode != SceneMode.GameOver) return;
        AudioManager.Instance.PlayBGM("bgm4");

        CloseReturnToTitleDialog();
        ReturnToTitleMode();
    }
}