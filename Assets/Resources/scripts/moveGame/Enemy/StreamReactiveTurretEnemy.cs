using UnityEngine;

public sealed class StreamReactiveTurretEnemy : MonoBehaviour
{
    [Header("Refs")]
    public string targetTag = "Player";
    public GameObject bulletPrefab;

    [Header("Heights (fixed)")]
    public float turretY = 1f;
    [Min(0.01f)] public float riseSeconds = 1f;
    [Min(0.01f)] public float descendSeconds = 1f;

    [Header("Y follow (anti-bury)")]
    [Tooltip("外部テレポート等でYがズレても、この速度で目標Yへ戻る（units/sec）")]
    [Min(0.01f)] public float yRecoverSpeed = 30f;

    [Header("Tiles condition (fixed)")]
    private const int ACTIVATE_FROM_TILE = 8;

    [Header("Dash")]
    [Min(0.01f)] public float dashSpeed = 18f;
    [Min(0.001f)] public float arriveRadius = 0.25f;

    [Header("Spin while dashing (yaw)")]
    public float dashSpinDegPerSec = 540f;

    [Header("Shoot")]
    [Min(0.05f)] public float shootInterval = 2.0f;
    [Min(0.01f)] public float bulletSpeed = 10.0f;
    [Min(0.01f)] public float bulletLifeSeconds = 3.0f;
    [Min(1)] public int bulletDamage = 10;
    public Vector3 muzzleLocalOffset = new Vector3(0f, 0f, 0.5f);

    [Header("Size")]
    [Min(0.01f)] public float sizeMultiplier = 1.0f;

    private enum State { WaitingTurret, Rising, Dashing, Descending, ActiveTurret }
    private State state;

    private GroundStreamer groundStreamer;
    private Transform player;
    private Enemy enemy;

    private float stateTimer;
    private float shootTimer;

    private Vector3 initialLocalScale;
    private Vector3 dashTarget;

    private bool activatedLatched = false;
    private Vector3 fixedPos;

    private float chosenRiseY;
    private int chosenDashToTile;

    // ★追加：アニメ補間の起点Y（外部テレポートの影響を受けない）
    private float stateStartY;

    void Awake()
    {
        enemy = GetComponent<Enemy>();
        initialLocalScale = transform.localScale;

        state = State.WaitingTurret;
        stateTimer = 0f;
        shootTimer = 0f;

        activatedLatched = false;
        chosenRiseY = 0f;
        chosenDashToTile = 0;
        stateStartY = 0f;
    }

    void Start()
    {
        groundStreamer = FindAnyObjectByType<GroundStreamer>();
        if (groundStreamer == null)
        {
            Debug.LogError("StreamReactiveTurretEnemy: GroundStreamer が見つかりません。");
            enabled = false;
            return;
        }

        if (bulletPrefab == null)
        {
            Debug.LogError("StreamReactiveTurretEnemy: bulletPrefab が未設定です。");
            enabled = false;
            return;
        }

        var go = GameObject.FindGameObjectWithTag(targetTag);
        if (go == null)
        {
            Debug.LogError($"StreamReactiveTurretEnemy: Tag '{targetTag}' のオブジェクトが見つかりません。");
            enabled = false;
            return;
        }
        player = go.transform;

        // 初期固定位置（Yだけ砲台高さへ）
        Vector3 p = transform.position;
        fixedPos = new Vector3(p.x, turretY, p.z);
        transform.position = fixedPos;

        stateStartY = transform.position.y;
    }

    void Update()
    {
        transform.localScale = initialLocalScale * sizeMultiplier;

        if (enemy != null && enemy.IsFrozen) return;

        float dt = Time.deltaTime;

        float minZ = groundStreamer.GetGroundMinZ();
        float tileLen = groundStreamer.tileLength;

        int tileFromFront = GetTileIndexFromFront(minZ, tileLen, transform.position.z);

        switch (state)
        {
            case State.WaitingTurret:
                {
                    // x,zは外部変更を許容。yだけ turretY へ戻す
                    PullYToTarget(dt, turretY);

                    // Waiting中は「現在のx,z」を固定砲台位置として採用しておく（外部テレポート追従）
                    fixedPos = new Vector3(transform.position.x, turretY, transform.position.z);

                    if (!activatedLatched && tileFromFront >= ACTIVATE_FROM_TILE)
                    {
                        activatedLatched = true;

                        chosenRiseY = Random.Range(2f, 8f);
                        chosenDashToTile = Random.Range(2, 7);

                        EnterState(State.Rising);
                    }
                    break;
                }

            case State.Rising:
                {
                    stateTimer += dt;
                    float u = riseSeconds <= 1e-6f ? 1f : Mathf.Clamp01(stateTimer / riseSeconds);

                    // 外部テレポートされても起点は stateStartY で固定
                    float y = Mathf.Lerp(stateStartY, chosenRiseY, u);
                    transform.position = new Vector3(transform.position.x, y, transform.position.z);

                    if (u >= 1f)
                    {
                        dashTarget = ComputeDashTarget(minZ, tileLen, chosenDashToTile);
                        EnterState(State.Dashing);
                    }
                    break;
                }

            case State.Dashing:
                {
                    // 旋回（見た目）
                    transform.Rotate(0f, dashSpinDegPerSec * dt, 0f, Space.Self);

                    // 目標のYへも常に寄せる（埋まり対策）
                    PullYToTarget(dt, chosenRiseY);

                    Vector3 p = transform.position;
                    Vector3 target = new Vector3(dashTarget.x, transform.position.y, dashTarget.z);

                    Vector3 to = target - p;
                    float dist = to.magnitude;

                    if (dist <= arriveRadius)
                    {
                        transform.position = new Vector3(dashTarget.x, transform.position.y, dashTarget.z);
                        EnterState(State.Descending);
                    }
                    else
                    {
                        Vector3 dir = to / dist;
                        float step = dashSpeed * dt;
                        if (step > dist) step = dist;
                        transform.position = p + dir * step;
                    }
                    break;
                }

            case State.Descending:
                {
                    stateTimer += dt;
                    float u = descendSeconds <= 1e-6f ? 1f : Mathf.Clamp01(stateTimer / descendSeconds);

                    float y = Mathf.Lerp(stateStartY, turretY, u);
                    transform.position = new Vector3(transform.position.x, y, transform.position.z);

                    if (u >= 1f)
                    {
                        // 到着位置を固定（ただし以後も外部テレポートは許容し、yだけ戻す）
                        fixedPos = new Vector3(transform.position.x, turretY, transform.position.z);
                        state = State.ActiveTurret;
                        stateTimer = 0f;
                        shootTimer = 0f;
                    }
                    break;
                }

            case State.ActiveTurret:
                {
                    // x,zは外部変更を許容。yだけ turretY へ戻す
                    PullYToTarget(dt, turretY);

                    // Active中も「現在のx,z」を砲台固定位置として更新（外部テレポート追従）
                    fixedPos = new Vector3(transform.position.x, turretY, transform.position.z);

                    shootTimer += dt;
                    if (shootTimer >= shootInterval)
                    {
                        shootTimer -= shootInterval;
                        FireOnce3D();
                    }
                    break;
                }
        }
    }

    // ★状態遷移を統一（アニメ起点Yをここで取る）
    private void EnterState(State next)
    {
        state = next;
        stateTimer = 0f;
        stateStartY = transform.position.y; // 外部テレポートされていても、その瞬間を起点にする
    }

    private void PullYToTarget(float dt, float targetY)
    {
        float spd = Mathf.Max(0.01f, yRecoverSpeed);
        Vector3 p = transform.position;
        float y = Mathf.MoveTowards(p.y, targetY, spd * dt);
        transform.position = new Vector3(p.x, y, p.z);
    }

    private int GetTileIndexFromFront(float groundMinZ, float tileLen, float z)
    {
        float dz = z - groundMinZ;
        int idx0 = Mathf.FloorToInt(dz / tileLen);
        return idx0 + 1;
    }

    private Vector3 ComputeDashTarget(float groundMinZ, float tileLen, int dashTile)
    {
        float zCenter = groundMinZ + (dashTile - 1) * tileLen + tileLen * 0.5f;
        return new Vector3(transform.position.x, chosenRiseY, zCenter);
    }

    private void FireOnce3D()
    {
        Vector3 muzzleWorld = transform.TransformPoint(muzzleLocalOffset);

        Vector3 dir = player.position - muzzleWorld;
        float len = dir.magnitude;
        if (len <= 1e-6f) return;
        dir /= len;

        GameObject b = Instantiate(bulletPrefab, muzzleWorld, Quaternion.LookRotation(dir, Vector3.up));
        b.name = $"EnemyBullet_{Time.frameCount}";

        var bullet = b.GetComponent<SimpleStraightBullet>();
        if (bullet == null) bullet = b.AddComponent<SimpleStraightBullet>();
        bullet.Initialize(dir, bulletSpeed, bulletLifeSeconds, bulletDamage);
    }
}