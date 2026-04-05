using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Enemy))]
public sealed class HeavyEnemyController : MonoBehaviour
{
    [Header("VRM (TEMPLATE ONLY: Reload is called once by controller)")]
    [SerializeField] private TextAsset character;
    [SerializeField] private GameObject vrmGameObject = null;

    [Header("BodyKey")]
    public float currentBodyKey = 60f;

    [Header("Movement (constant chase speed)")]
    [SerializeField] private float chaseSpeedXZ = 4.0f;
    [SerializeField] private float chaseSpeedY = 6.0f;
    [SerializeField] private float rotateLerp = 12f;

    private float stopDistanceXZ = 0.1f;
    private float stopDistanceY = 0.1f;

    [Header("Shoot")]
    [Tooltip("弾Prefab（必須）")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float shootInterval = 5.0f;
    [SerializeField] private float bulletSpeed = 10.0f;
    [SerializeField] private float bulletLifeSeconds = 3.0f;
    [SerializeField] private int bulletDamage = 10;
    [SerializeField] private Vector3 muzzleLocalOffset = new Vector3(0f, 0f, 0.5f);

    [Header("Transfer (player hit = no damage, transfer bodyKey)")]
    [SerializeField] private float transferSeconds = 0.35f;
    [SerializeField] private float transferCooldownSeconds = 0.5f;

    [Header("Fly Away After Transfer (演出は常に有効)")]
    [SerializeField] private float flyAwaySeconds = 1.2f;
    [SerializeField] private float flyAwayForwardSpeed = 10f;
    [SerializeField] private float flyAwayUpSpeed = 8f;

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
    private float shootTimer = 0f;

    private Camera playerCamera;
    private moveGameSceneCamera cameraController;
    private float movingLogTimer = 0f;

    private Coroutine scaleCo;

    public void Bind(PlayerController player, GroundStreamer ground)
    {
        currentBodyKey = UnityEngine.Random.Range(10f, 40f);
        injectedPlayer = player;
        injectedGround = ground;
    }

    public void SetBodyKeyImmediate(float v)
    {
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

    public IEnumerator CoReloadVrmForTemplateOnce()
    {
        if (vrmGameObject == null) { Debug.LogError("[HeavyEnemyController] vrmGameObject is null."); yield break; }
        if (character == null) { Debug.LogError("[HeavyEnemyController] character is null."); yield break; }

        ctr = vrmGameObject.GetComponent<VrmToController>();
        if (ctr == null) { Debug.LogError("[HeavyEnemyController] VrmToController missing on vrmGameObject."); yield break; }

        ctr.meshPullEnabled = false;

        ctr.blushValue = 0.5f;
        ctr.ReloadFromBytes(character.bytes, BodyVariant.Normal, 100, ctr.bodyKey, 30, 0.2f);

        while (!ctr.IsReady) yield return null;
    }

    private void Awake()
    {
        enemy = GetComponent<Enemy>();

        if (vrmGameObject != null)
            ctr = vrmGameObject.GetComponent<VrmToController>();

        baseScale = transform.localScale;

        ApplyBodyKey(currentBodyKey);

        state = State.Chase;
        flyAwayTimer = 0f;
        shootTimer = 0f;

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

        float dt = Time.deltaTime;

        Vector3 epos = transform.position;
        Vector3 ppos = pTr.position;

        // -------------------------
        // XZ追尾（currentBodyKeyに依存しない固定速度）
        // -------------------------
        Vector3 toXZ = ppos - epos;
        toXZ.y = 0f;

        float distXZ = toXZ.magnitude;
        Vector3 dirXZ = (distXZ > 1e-6f) ? (toXZ / distXZ) : Vector3.forward;

        bool moveXZ = distXZ > stopDistanceXZ;
        if (moveXZ)
        {
            float stepXZ = chaseSpeedXZ * dt;
            float canMove = Mathf.Max(0f, distXZ - stopDistanceXZ);
            if (stepXZ > canMove) stepXZ = canMove;

            epos += dirXZ * stepXZ;

            Quaternion targetRot = Quaternion.LookRotation(dirXZ, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotateLerp * dt);
        }

        // -------------------------
        // Y追尾（currentBodyKeyに依存しない固定速度）
        // -------------------------
        float dy = ppos.y - epos.y;
        bool moveY = Mathf.Abs(dy) > stopDistanceY;

        if (moveY)
        {
            epos.y = Mathf.MoveTowards(epos.y, ppos.y, chaseSpeedY * dt);
        }

        transform.position = epos;

        // -------------------------
        // 射撃（5秒ごとに3発）
        // -------------------------
        if (bulletPrefab != null)
        {
            shootTimer += dt;
            if (shootTimer >= shootInterval)
            {
                shootTimer -= shootInterval;
                FireTriple3D(pTr);
            }
        }

        // -------------------------
        // 常に飛行モーション
        // -------------------------
        ApplyFlyAnim();

        // -------------------------
        // Camera shake（XZ移動してる時だけ）
        // -------------------------
        if (playerCamera == null) playerCamera = Camera.main;
        if (cameraController == null) cameraController = FindFirstObjectByType<moveGameSceneCamera>();

        if (moveXZ)
        {
            movingLogTimer += dt;
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
        else
        {
            movingLogTimer = 0f;
        }
    }

    private void FireTriple3D(Transform target)
    {
        if (target == null) return;
        if (bulletPrefab == null) return;

        Vector3 origin = transform.TransformPoint(muzzleLocalOffset);

        // 0°方向は必ずプレイヤー方向（3D）
        Vector3 d = target.position - origin;
        float dsq = d.sqrMagnitude;
        if (dsq < 1e-8f) return;
        d /= Mathf.Sqrt(dsq);

        // d と直交する回転軸を作る
        Vector3 up = Vector3.up;
        Vector3 n = up - Vector3.Dot(up, d) * d;

        if (n.sqrMagnitude < 1e-8f)
        {
            n = Vector3.forward - Vector3.Dot(Vector3.forward, d) * d;
            if (n.sqrMagnitude < 1e-8f)
            {
                n = Vector3.right - Vector3.Dot(Vector3.right, d) * d;
                if (n.sqrMagnitude < 1e-8f) return;
            }
        }
        n.Normalize();

        // 3WAY
        float[] angles = { -15f, 0f, 15f };

        for (int i = 0; i < angles.Length; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(angles[i], n) * d;

            GameObject b = Instantiate(
                bulletPrefab,
                origin,
                Quaternion.LookRotation(dir, Vector3.up)
            );
            b.name = $"EnemyBullet_{Time.frameCount}_{i}";

            var bullet = b.GetComponent<SimpleStraightBullet>();
            if (bullet == null) bullet = b.AddComponent<SimpleStraightBullet>();
            bullet.Initialize(dir, bulletSpeed, bulletLifeSeconds, bulletDamage);
        }
    }

    private void StartFlyAway()
    {
        state = State.FlyAway;
        flyAwayTimer = 0f;

        transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        ApplyFlyAnim();
    }

    private void TickFlyAway()
    {
        flyAwayTimer += Time.deltaTime;

        Vector3 p = transform.position;
        p += Vector3.forward * (flyAwayForwardSpeed * Time.deltaTime);
        p += Vector3.up * (flyAwayUpSpeed * Time.deltaTime);
        transform.position = p;

        ApplyFlyAnim();

        if (flyAwayTimer >= flyAwaySeconds)
        {
            transform.position = GroundStreamer.HIDDEN_POS;

            state = State.Chase;
            flyAwayTimer = 0f;
            shootTimer = 0f;

            Disappeared?.Invoke(this);
        }
    }

    private void ApplyFlyAnim()
    {
        if (ctr == null) return;

        bool variant2 = (ctr.bodyKey > 25f);
        ctr.ApplyEvent(variant2 ? "moving_fly2" : "moving_fly1");
    }

    private void ApplyBodyKey(float bodyKey)
    {
        if (ctr == null) return;

        float v = Mathf.Clamp(bodyKey, 0f, 100f);

        ctr.bodyKey = v;
        ctr.lowKey = v * 0.3f;
        ctr.bustKey = 20f + 80f * (v / 100f);
    }

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

    private PlayerController GetPlayerOrNull()
    {
        if (injectedPlayer != null) return injectedPlayer;
        return moveGameSceneController.Instance.player.GetComponent<PlayerController>();
    }
}