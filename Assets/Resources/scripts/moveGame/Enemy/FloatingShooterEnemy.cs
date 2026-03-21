using UnityEngine;

/// <summary>
/// 浮遊して弾を撃つ敵。
/// - y は [hoverMinY, hoverMaxY] の間をふらつく
/// - x,z は Player へ等速移動。ただし平面距離が stopRadius 以内なら止まる
/// - 弾は「3D方向（y込み）」でプレイヤーへ向けて直線等速で飛ぶ（重力なし）
/// - sizeMultiplier で見た目スケールを一括変更
/// </summary>
public sealed class FloatingShooterEnemy : MonoBehaviour
{
    [Header("Target")]
    public string targetTag = "Player";

    [Header("Size")]
    [Min(0.01f)] public float sizeMultiplier = 1.0f;

    [Header("Move (XZ)")]
    [Min(0.01f)] public float moveSpeed = 3.0f;
    [Min(0f)] public float stopRadius = 3.0f;

    [Header("Hover (Y)")]
    public float hoverMinY = 1.0f;
    public float hoverMaxY = 3.0f;
    [Min(0.01f)] public float hoverChangeInterval = 0.8f;
    [Min(0.01f)] public float hoverLerpSpeed = 4.0f;

    [Header("Shoot")]
    [Tooltip("弾Prefab（必須）")]
    public GameObject bulletPrefab;

    [Min(0.05f)] public float shootInterval = 2.0f;
    [Min(0.01f)] public float bulletSpeed = 10.0f;
    [Min(0.01f)] public float bulletLifeSeconds = 3.0f;
    [Min(1)] public int bulletDamage = 10;

    [Header("Shoot offset")]
    public Vector3 muzzleLocalOffset = new Vector3(0f, 0f, 0.5f);

    private Transform target;
    private float shootTimer;
    private float hoverTimer;
    private float hoverTargetY;

    private Vector3 initialLocalScale;

    // Freeze
    private Enemy enemy;

    void Awake()
    {
        enemy = GetComponent<Enemy>();

        initialLocalScale = transform.localScale;
        if (!IsFiniteVec3(initialLocalScale) ||
            Mathf.Abs(initialLocalScale.x) < 1e-6f ||
            Mathf.Abs(initialLocalScale.y) < 1e-6f ||
            Mathf.Abs(initialLocalScale.z) < 1e-6f)
        {
            initialLocalScale = Vector3.one;
        }

        ApplySize();

        hoverTargetY = Mathf.Clamp(transform.position.y, hoverMinY, hoverMaxY);
        shootTimer = 0f;
        hoverTimer = 0f;
    }

    void Start()
    {
        if (bulletPrefab == null)
        {
            Debug.LogError("FloatingShooterEnemy: bulletPrefab が未設定です。");
            enabled = false;
            return;
        }
        if (hoverMaxY < hoverMinY)
        {
            Debug.LogError("FloatingShooterEnemy: hoverMinY / hoverMaxY の指定が不正です。");
            enabled = false;
            return;
        }

        var go = GameObject.FindGameObjectWithTag(targetTag);
        target = (go != null) ? go.transform : null;
    }

    void Update()
    {
        if (!enabled) return;

        // size 追従（凍結でも見た目反映は許可）
        ApplySize();

        // ★ Freeze中：移動・ホバー・射撃・回転は止める（タイマーも進めない）
        if (enemy != null && enemy.IsFrozen)
        {
            return;
        }

        if (target == null)
        {
            var go = GameObject.FindGameObjectWithTag(targetTag);
            target = (go != null) ? go.transform : null;
            if (target == null) return;
        }

        float dt = Time.deltaTime;

        // --- hover Y ---
        hoverTimer += dt;
        if (hoverTimer >= hoverChangeInterval)
        {
            hoverTimer = 0f;
            hoverTargetY = Random.Range(hoverMinY, hoverMaxY);
        }

        Vector3 p = transform.position;
        p.y = Mathf.Lerp(p.y, hoverTargetY, 1f - Mathf.Exp(-hoverLerpSpeed * dt));

        // --- move XZ (stopRadius以内で止まる) ---
        Vector3 to = target.position - p;
        to.y = 0f;

        float sq = to.sqrMagnitude;
        float stopR = Mathf.Max(0f, stopRadius);
        if (sq > stopR * stopR + 1e-6f)
        {
            Vector3 dir = to / Mathf.Sqrt(sq);
            Vector3 step = dir * moveSpeed * dt;

            float dist = Mathf.Sqrt(sq);
            float canMove = Mathf.Max(0f, dist - stopR);
            if (step.magnitude > canMove) step = dir * canMove;

            p.x += step.x;
            p.z += step.z;

            if (dir.sqrMagnitude > 1e-8f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        transform.position = p;

        // --- shoot ---
        shootTimer += dt;
        if (shootTimer >= shootInterval)
        {
            shootTimer -= shootInterval;
            FireOnce3D();
        }

        // 念のため NaN ガード
        if (!IsFiniteVec3(transform.position))
        {
            Vector3 q = transform.position;
            transform.position = new Vector3(0f, Mathf.Clamp(q.y, hoverMinY, hoverMaxY), 0f);
        }
        if (!IsFiniteVec3(transform.localScale))
        {
            transform.localScale = initialLocalScale * Mathf.Max(0.01f, sizeMultiplier);
        }
    }

    private void ApplySize()
    {
        float sm = Mathf.Max(0.01f, IsFinite(sizeMultiplier) ? sizeMultiplier : 1f);
        Vector3 s = initialLocalScale * sm;
        if (!IsFiniteVec3(s)) s = Vector3.one * sm;
        transform.localScale = s;
    }

    private void FireOnce3D()
    {
        if (target == null) return;

        // 敵中心から発射
        Vector3 origin = transform.position;

        // 0°方向：必ずプレイヤー方向（3D）
        Vector3 d = target.position - origin;
        float dsq = d.sqrMagnitude;
        if (dsq < 1e-8f) return;
        d /= Mathf.Sqrt(dsq);

        // 「最も水平に近い平面」の法線 n を作る
        // n = normalize( up - (up·d) d )  （upのd直交成分）
        Vector3 up = Vector3.up;
        Vector3 n = up - Vector3.Dot(up, d) * d;

        // 真上/真下（nがほぼ0）なら、近くの安定な軸に寄せる
        // ここでは d と直交する任意の法線を作る（upが使えないので forward/right を試す）
        if (n.sqrMagnitude < 1e-8f)
        {
            // forward と直交成分を取る
            n = Vector3.forward - Vector3.Dot(Vector3.forward, d) * d;
            if (n.sqrMagnitude < 1e-8f)
            {
                // forward もダメなら right
                n = Vector3.right - Vector3.Dot(Vector3.right, d) * d;
                if (n.sqrMagnitude < 1e-8f) return;
            }
        }
        n.Normalize();

        // 8方向：平面内で d を n 軸回りに 45°刻み回転（0°がプレイヤー方向）
        for (int i = 0; i < 8; i++)
        {
            float ang = 45f * i;
            Vector3 dir = Quaternion.AngleAxis(ang, n) * d;

            GameObject b = Instantiate(bulletPrefab, origin, Quaternion.LookRotation(dir, Vector3.up));
            b.name = $"EnemyBullet_{Time.frameCount}_{i}";

            var bullet = b.GetComponent<SimpleStraightBullet>();
            if (bullet == null) bullet = b.AddComponent<SimpleStraightBullet>();
            bullet.Initialize(dir, bulletSpeed, bulletLifeSeconds, bulletDamage);
        }
    }

    private static bool IsFinite(float v) => !(float.IsNaN(v) || float.IsInfinity(v));
    private static bool IsFiniteVec3(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
}