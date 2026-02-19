using UnityEngine;

public sealed class moveGameSceneController : MonoBehaviour
{
    public static moveGameSceneController Instance { get; private set; }

    [Header("Required refs")]
    [SerializeField] private GroundStreamer groundStreamer;
    public PlayerController player; // PlayerController は vrmGameObject 側に付ける

    private void Awake()
    {
        Instance = this;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
    }

    private void Start()
    {
        if (groundStreamer == null)
        {
            Debug.LogError("[moveGameSceneController] groundStreamer is null.");
            enabled = false;
            return;
        }

        if (player == null)
        {
            Debug.LogError("[moveGameSceneController] player is null.");
            enabled = false;
            return;
        }

        if (player.VrmTransform == null)
        {
            Debug.LogError("[moveGameSceneController] player.VrmTransform is null.");
            enabled = false;
            return;
        }

        // Ground follow target is VRM transform (player itself)
        groundStreamer.SetFollowTarget(player.VrmTransform);
        groundStreamer.RebuildImmediate();

        // 初回 bounds を必ず供給（ここが無いと player 側は動かない）
        SupplyWorldBounds();
        player.EnsureInitialized(); // player 内で必須参照未設定なら自分で停止
        player.ForceClampNow();
    }

    private void Update()
    {
        if (!enabled) return;

        groundStreamer.Tick();
        SupplyWorldBounds();
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
}
