using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Enemy))]
public sealed class HeavyEnemyController : MonoBehaviour
{
    [Header("VRM (TEMPLATE ONLY: Reload is called once by controller)")]
    [SerializeField] private TextAsset character;               // テンプレート用
    [SerializeField] private GameObject vrmGameObject = null;   // VrmToControllerの付いたGO（無ければ無い）

    [Header("BodyKey")]
    public float currentBodyKey = 60f;

    [Header("Movement (XZ: keyが大きいほど遅い / 速度は1本)")]
    [SerializeField] private float speedAtKey0 = 6.0f;    // currentBodyKey=0 のXZ速度
    [SerializeField] private float speedAtKey100 = 1.5f;  // currentBodyKey=100 のXZ速度
    [SerializeField] private float rotateLerp = 12f;

    private float stopDistanceXZ = 0.1f; // XZ停止距離
    private float stopDistanceY = 0.1f;  // Y停止距離

    [Header("Movement Y (independent)")]
    [Tooltip("上下追跡の基準速度。上昇はkeyが0に近いほど速く(上限=この値)、下降はkeyが大きいほど速く(最大2倍)。")]
    [SerializeField] private float yChaseSpeed = 10.0f;

    [Header("Transfer (player hit = no damage, transfer bodyKey)")]
    [SerializeField] private float transferSeconds = 0.35f;
    [SerializeField] private float transferCooldownSeconds = 0.5f;

    [Header("Fly Away After Transfer (演出は常に有効)")]
    [SerializeField] private float flyAwaySeconds = 1.2f;
    [SerializeField] private float flyAwayForwardSpeed = 10f; // +Z方向
    [SerializeField] private float flyAwayUpSpeed = 8f;       // +Y方向

    private Vector3 baseScale = Vector3.one;

    public event Action<HeavyEnemyController> Disappeared;

    private PlayerController injectedPlayer;
    private GroundStreamer injectedGround;

    private Enemy enemy;
    private VrmToController ctr;

    private float lastTransferTime = -999f;
    private Coroutine transferCo;

    private enum State { Chase, FlyAway }
    private State state = State.Chase;

    private float flyAwayTimer = 0f;

    private Camera playerCamera;
    private moveGameSceneCamera cameraController;
    private float movingLogTimer = 0f;

    private Coroutine scaleCo;

    // =========================================================
    // Public API (GroundStreamer が呼ぶ)
    // =========================================================
    public void Bind(PlayerController player, GroundStreamer ground)
    {
        currentBodyKey = UnityEngine.Random.Range(15f, 80f);
        injectedPlayer = player;
        injectedGround = ground;
    }

    public void SetBodyKeyImmediate(float v)
    {
        currentBodyKey = Mathf.Clamp(v, 15f, 100f);
        ApplyBodyKey(currentBodyKey);
    }

    private void ForceAnimatorAndSkinnedUpdate()
    {
        var animators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            animators[i].cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        var skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            skinned[i].updateWhenOffscreen = true;
        }
    }

    /// <summary>
    /// テンプレート専用：controller が「1回だけ」呼ぶ。
    /// ここで重いロード完了（IsReady）まで待てる。
    /// </summary>
    public IEnumerator CoReloadVrmForTemplateOnce()
    {
        if (vrmGameObject == null) { Debug.LogError("[HeavyEnemyController] vrmGameObject is null."); yield break; }
        if (character == null) { Debug.LogError("[HeavyEnemyController] character is null."); yield break; }

        ctr = vrmGameObject.GetComponent<VrmToController>();
        if (ctr == null) { Debug.LogError("[HeavyEnemyController] VrmToController missing on vrmGameObject."); yield break; }

        // このミニゲームでは MeshPull を完全オフ（既に動作確認済みの止血）
        ctr.meshPullEnabled = false;

        ctr.blushValue = 0.5f;
        ctr.ReloadFromBytes(character.bytes, BodyVariant.Normal, 100, ctr.bodyKey, 30, 0.2f);

        while (!ctr.IsReady) yield return null;
    }

    // =========================================================
    // Unity
    // =========================================================
    private void Awake()
    {
        enemy = GetComponent<Enemy>();

        if (vrmGameObject != null)
            ctr = vrmGameObject.GetComponent<VrmToController>();

        baseScale = transform.localScale;

        ApplyBodyKey(currentBodyKey);

        state = State.Chase;
        flyAwayTimer = 0f;

        playerCamera = Camera.main;
        cameraController = FindFirstObjectByType<moveGameSceneCamera>();
    }

    private void OnEnable()
    {
        if (scaleCo != null) StopCoroutine(scaleCo);
        scaleCo = StartCoroutine(CoApplyScaleWhenReady(transform));

        ForceAnimatorAndSkinnedUpdate();
    }

    private IEnumerator CoApplyScaleWhenReady(Transform root)
    {
        while (ctr != null && !ctr.IsReady)
            yield return null;

        root.localScale = baseScale * 1.2f;
    }

    private void Update()
    {
        // ★Enemy.HIDDEN_POS を廃止したので GroundStreamer.HIDDEN_POS を使う
        if (transform.position == GroundStreamer.HIDDEN_POS) return;

        if (state == State.FlyAway)
        {
            TickFlyAway();
            return;
        }

        if (enemy != null && enemy.IsFrozen) return;

        var player = GetPlayerOrNull();
        if (player == null) return;

        var pTr = player.VrmTransform;
        if (pTr == null) return;

        Vector3 epos = transform.position;
        Vector3 ppos = pTr.position;

        // -------------------------
        // XZ
        // -------------------------
        Vector3 toXZ = ppos - epos;
        toXZ.y = 0f;

        float distXZ = toXZ.magnitude;
        Vector3 dirXZ = (distXZ > 1e-6f) ? (toXZ / distXZ) : Vector3.forward;

        float tKey = Mathf.Clamp01(currentBodyKey / 100f);
        float speedXZ = Mathf.Lerp(speedAtKey0, speedAtKey100, tKey);

        bool moveXZ = (distXZ > stopDistanceXZ);
        if (moveXZ)
        {
            epos += dirXZ * (speedXZ * Time.deltaTime);

            Quaternion targetRot = Quaternion.LookRotation(dirXZ, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotateLerp * Time.deltaTime);
        }

        // -------------------------
        // Y（上昇/下降で速度が別）
        // -------------------------
        float dy = ppos.y - epos.y;
        bool moveY = Mathf.Abs(dy) > stopDistanceY;

        if (moveY)
        {
            float upSpeed = yChaseSpeed * Mathf.Lerp(0.5f, 1.0f, 1.0f - tKey);
            if (upSpeed > yChaseSpeed) upSpeed = yChaseSpeed;

            float downSpeed = yChaseSpeed * Mathf.Lerp(1.0f, 2.0f, tKey);

            float step = (dy > 0f) ? upSpeed : downSpeed;
            epos.y = Mathf.MoveTowards(epos.y, ppos.y, step * Time.deltaTime);
        }

        transform.position = epos;

        // -------------------------
        // アニメ
        // -------------------------
        bool movingXZ2 = distXZ > 1e-6f;
        bool movingY2 = Mathf.Abs(dy) > 1e-6f;

        if (!movingXZ2 && !movingY2)
        {
            movingLogTimer = 0f;
            ApplyIdleAnim();
        }
        else
        {
            ApplyMoveAnimByKey();
        }

        // -------------------------
        // Camera shake（XZ移動してる時だけ）
        // -------------------------
        if (playerCamera == null) playerCamera = Camera.main;
        if (cameraController == null) cameraController = FindFirstObjectByType<moveGameSceneCamera>();

        if (movingXZ2)
        {
            movingLogTimer += Time.deltaTime;
            const float limit = 0.6f;

            if (movingLogTimer >= limit)
            {
                movingLogTimer -= limit;

                if (currentBodyKey > 25f)
                {
                    Vector3 cpos = playerCamera.transform.position;

                    bool inBox =
                        Mathf.Abs(epos.x - cpos.x) <= 20f &&
                        Mathf.Abs(epos.z - cpos.z) <= 20f;

                    if (inBox)
                    {
                        cameraController.TriggerShake(
                            1f + 0.5f * Mathf.Max(0f, (currentBodyKey - 25f) / 75f)
                        );
                    }
                }
            }
        }
    }

    // =========================================================
    // Fly Away（演出は常に維持）
    // =========================================================
    private void StartFlyAway()
    {
        state = State.FlyAway;
        flyAwayTimer = 0f;

        transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

        bool variant2 = (ctr.bodyKey > 25f);
        ctr.ApplyEvent(variant2 ? "moving_fly2" : "moving_fly1");
    }

    private void TickFlyAway()
    {
        flyAwayTimer += Time.deltaTime;

        Vector3 p = transform.position;
        p += Vector3.forward * (flyAwayForwardSpeed * Time.deltaTime);
        p += Vector3.up * (flyAwayUpSpeed * Time.deltaTime);
        transform.position = p;

        bool variant2 = (ctr.bodyKey > 25f);
        ctr.ApplyEvent(variant2 ? "moving_fly2" : "moving_fly1");

        if (flyAwayTimer >= flyAwaySeconds)
        {
            // ★Enemy.HIDDEN_POS を廃止したので GroundStreamer.HIDDEN_POS を使う
            transform.position = GroundStreamer.HIDDEN_POS;

            state = State.Chase;
            flyAwayTimer = 0f;

            Disappeared?.Invoke(this);
        }
    }

    // =========================================================
    // Animation (ApplyEvent)
    // =========================================================
    private void ApplyMoveAnimByKey()
    {
        bool variant2 = (ctr.bodyKey > 25f);

        if (currentBodyKey >= 70f)
        {
            if (currentBodyKey < 85f)
                ctr.ApplyEvent(variant2 ? "moving_jogging2" : "moving_jogging1");
            else
                ctr.ApplyEvent(variant2 ? "moving_walk2" : "moving_walk1");
            return;
        }

        if (currentBodyKey <= 50f)
        {
            ctr.ApplyEvent(variant2 ? "moving_fly2" : "moving_fly1");
        }
        else
        {
            ctr.ApplyEvent(variant2 ? "moving_jogging2" : "moving_jogging1");
        }
    }

    private void ApplyIdleAnim()
    {
        bool variant2 = (ctr.bodyKey > 25f);
        ctr.ApplyEvent(variant2 ? "moving_idol2" : "moving_idol1");
    }

    // =========================================================
    // BodyKey -> VRM
    // =========================================================
    private void ApplyBodyKey(float bodyKey)
    {
        float v = Mathf.Clamp(bodyKey, 0f, 100f);

        ctr.bodyKey = v;
        ctr.lowKey = v * 0.3f;
        ctr.bustKey = 20f + 80f * (v / 100f);
    }

    // =========================================================
    // Transfer: player触れたら bodyKey を移す（ダメージ無し）
    // =========================================================
    private void OnTriggerEnter(Collider other)
    {
        if (state != State.Chase) return;
        if (other == null) return;

        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        if (Time.time - lastTransferTime < transferCooldownSeconds) return;
        lastTransferTime = Time.time;

        float amount = Mathf.Max(0f, currentBodyKey);
        if (amount <= 0f) return;

        if (transferCo != null) StopCoroutine(transferCo);

        AudioManager.Instance.PlaySE("heal");
        AudioManager.Instance.PlaySE("slider_22");
        transferCo = StartCoroutine(CoTransferThenFlyAway(player, amount));
    }

    private IEnumerator CoTransferThenFlyAway(PlayerController player, float amount)
    {
        player.AddBodyKeyAnimated(amount, transferSeconds);

        float start = currentBodyKey;
        float t = 0f;
        float dur = Mathf.Max(0.01f, transferSeconds);

        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);

            currentBodyKey = Mathf.Lerp(start, 0f, u);
            ApplyBodyKey(currentBodyKey);

            yield return null;
        }

        currentBodyKey = 0f;
        ApplyBodyKey(currentBodyKey);

        StartFlyAway();
        transferCo = null;
    }

    // =========================================================
    // Helpers
    // =========================================================
    private PlayerController GetPlayerOrNull()
    {
        if (injectedPlayer != null) return injectedPlayer;
        return moveGameSceneController.Instance.player.GetComponent<PlayerController>();
    }
}