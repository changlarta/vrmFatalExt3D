using System.Collections.Generic;
using UnityEngine;

public class ClickShootFromCenter : MonoBehaviour
{
    [Header("Spawn / Move")]
    public float spawnDistance = 1.0f;
    public float speed = 20.0f;
    public float lifetime = 5.0f;
    public float cubeSize = 0.2f;

    [Header("Hold fire")]
    public float shotsPerSecond = 10.0f;

    [Header("Visual spin (does not affect trajectory)")]
    public float spinXDegPerSec = 360.0f;
    public float spinYDegPerSec = 360.0f;

    [Header("Inertia source")]
    [Tooltip("慣性を取る対象。未設定なら Camera.main.transform を使う")]
    public Transform inertiaSource;

    [Tooltip("慣性の乗り具合。1=そのまま、0=無効")]
    [Range(0f, 2f)] public float inertiaScale = 1.0f;

    [Header("Charge (stepwise)")]
    [Tooltip("2秒以上で半チャージ")]
    public float halfChargeSeconds = 2.0f;

    [Tooltip("5秒以上で最大チャージ")]
    public float fullChargeSeconds = 5.0f;

    [Tooltip("半チャージ時の最初の1発サイズ倍率")]
    public float halfChargeSizeMul = 10f;

    [Tooltip("最大チャージ時の最初の1発サイズ倍率")]
    public float fullChargeSizeMul = 30.0f;

    // ---- 追加：弾属性（publicフィールド名は変えない）----
    private enum ShotSize { Small, Medium, Large }

    private const int DAMAGE_SMALL = 1;
    private const int DAMAGE_MEDIUM = 20;
    private const int DAMAGE_LARGE = 50;

    private struct Projectile
    {
        public Transform tr;
        public Vector3 vel;
        public float age;
    }

    private readonly List<Projectile> projectiles = new List<Projectile>(256);

    // 発射制限
    private float nextFireTime;

    // チャージ（撃っていない＝ボタンを離している間）
    private float chargeTimer;

    // 次に撃つ「最初の1発」にチャージを適用するフラグ
    private bool firstShotPending = true;

    // 発射元速度推定
    private Vector3 prevInertiaPos;
    private Vector3 inertiaVel;

    void Start()
    {
        if (inertiaSource == null)
        {
            var cam = Camera.main;
            if (cam != null) inertiaSource = cam.transform;
        }

        if (inertiaSource != null)
        {
            prevInertiaPos = inertiaSource.position;
            inertiaVel = Vector3.zero;
        }

        nextFireTime = Time.time;   // 初回にバーストしないよう現在時刻から開始
        chargeTimer = 0f;
        firstShotPending = true;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // ---- 発射元速度推定 ----
        if (inertiaSource != null && dt > 1e-6f)
        {
            Vector3 cur = inertiaSource.position;
            inertiaVel = (cur - prevInertiaPos) / dt;
            prevInertiaPos = cur;
        }
        else
        {
            inertiaVel = Vector3.zero;
        }

        bool mouseDown = Input.GetMouseButton(0);

        // ---- チャージ：撃っていない間（ボタンを離している間）だけ蓄積 ----
        if (!mouseDown)
        {
            chargeTimer = Mathf.Min(fullChargeSeconds, chargeTimer + dt);
            firstShotPending = true; // 次に撃ち始めた最初の1発にチャージを乗せる
        }
        else
        {
            // ---- 発射（レート上限厳守：連打でも押しっぱなしでも shotsPerSecond を超えない）----
            if (shotsPerSecond > 0f)
            {
                float interval = 1f / shotsPerSecond;

                // このフレームで「撃てる」なら1発だけ撃つ（同フレームの追い打ちバーストをしない）
                if (Time.time >= nextFireTime)
                {
                    ShotSize sizeType;
                    float sizeMul;
                    int damage;

                    if (firstShotPending && chargeTimer >= fullChargeSeconds)
                    {
                        // 元のSE名を維持
                        AudioManager.Instance.PlaySE("eat_sugar");

                        sizeType = ShotSize.Large;
                        sizeMul = fullChargeSizeMul;
                        damage = DAMAGE_LARGE;
                    }
                    else if (firstShotPending && chargeTimer >= halfChargeSeconds)
                    {
                        AudioManager.Instance.PlaySE("titleButton");

                        sizeType = ShotSize.Medium;
                        sizeMul = halfChargeSizeMul;
                        damage = DAMAGE_MEDIUM;
                    }
                    else
                    {
                        AudioManager.Instance.PlaySE("text_back");

                        sizeType = ShotSize.Small;
                        sizeMul = 1f;
                        damage = DAMAGE_SMALL;
                    }

                    FireOnce(sizeMul, sizeType, damage);

                    // 最初の1発だけチャージ適用
                    firstShotPending = false;

                    // 撃ったらチャージ消費
                    chargeTimer = 0f;

                    nextFireTime = Time.time + interval;
                }
            }
        }

        // ---- 弾更新 ----
        for (int i = projectiles.Count - 1; i >= 0; i--)
        {
            Projectile p = projectiles[i];

            // 命中などでDestroyされていたら掃除
            if (p.tr == null)
            {
                projectiles.RemoveAt(i);
                continue;
            }

            p.tr.position += p.vel * dt;

            p.tr.Rotate(Vector3.right, spinXDegPerSec * dt, Space.Self);
            p.tr.Rotate(Vector3.up, spinYDegPerSec * dt, Space.Self);

            p.age += dt;
            if (p.age >= lifetime)
            {
                Destroy(p.tr.gameObject);
                projectiles.RemoveAt(i);
                continue;
            }

            projectiles[i] = p;
        }
    }

    private const string PROJECTILE_LAYER_NAME = "Projectiles";

    private void FireOnce(float sizeMul, ShotSize sizeType, int damage)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Ray centerRay = cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
        Vector3 spawnPos = centerRay.GetPoint(spawnDistance);

        Ray clickRay = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 dir = clickRay.direction.normalized;

        GameObject proj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        proj.transform.position = spawnPos;
        proj.transform.localScale = Vector3.one * (cubeSize * sizeMul);

        // ---- 追加：レイヤー確定（ここが今回の本題）----
        int projectileLayer = LayerMask.NameToLayer(PROJECTILE_LAYER_NAME);
        if (projectileLayer < 0)
        {
            Debug.LogError($"[ClickShootFromCenter] Layer '{PROJECTILE_LAYER_NAME}' not found. Create it in Tags and Layers.");
            Destroy(proj);
            enabled = false; // フォールバック禁止：成立条件が満たせないなら止める
            return;
        }
        proj.layer = projectileLayer;

        // 命中処理
        var hit = proj.AddComponent<ProjectileHit>();
        hit.damage = damage;
        hit.sizeType = sizeType;

        var col = proj.GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        var rb = proj.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        Vector3 bulletVel = dir * speed + inertiaVel * inertiaScale;

        projectiles.Add(new Projectile
        {
            tr = proj.transform,
            vel = bulletVel,
            age = 0f
        });
    }


    // ---------------------------------------------------------
    // 弾の命中処理（同ファイル内で完結）
    // Enemy が存在する前提で、存在しない場合は何もしない。
    // ---------------------------------------------------------
    private sealed class ProjectileHit : MonoBehaviour
    {
        public int damage;
        public ShotSize sizeType;

        private bool consumed;

        private void OnTriggerEnter(Collider other)
        {
            if (consumed) return;

            // Enemy クラス（別ファイル）にだけ反応
            var enemy = other.GetComponentInParent<Enemy>();
            if (enemy == null) return;

            consumed = true;

            Vector3 hitPoint = other.ClosestPoint(transform.position);
            enemy.TakeDamage(damage, hitPoint);

            Destroy(gameObject);
        }
    }
}
