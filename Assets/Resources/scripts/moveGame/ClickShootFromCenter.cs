using System.Collections.Generic;
using UnityEngine;

public class ClickShootFromCenter : MonoBehaviour
{
    [Header("Spawn / Move")]
    public float spawnDistance = 1.0f;
    public float speed = 20.0f;
    public float lifetime = 5.0f;
    public float cubeSize = 0.2f;

    public PlayerController player;

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

    // ---- 弾属性 ----
    private enum ShotSize { Small, Medium, Large }

    private const int DAMAGE_SMALL = 1;
    private const int DAMAGE_MEDIUM = 5;
    private const int DAMAGE_LARGE = 10;

    // -------------------------
    // Freeze（氷）: 共存可
    // -------------------------
    [Header("Freeze bullet")]
    public bool enableFreezeOnHit = true;

    // 決め打ち（ユーザー指定）
    private const float FREEZE_SMALL = 2f;
    private const float FREEZE_MEDIUM = 10f;
    private const float FREEZE_LARGE = 10f;

    [Header("Freeze bullet color")]
    public Color freezeBulletColor = new Color(0.4f, 0.85f, 1.0f, 1.0f);

    // -------------------------
    // Thunder（雷）: 共存可
    // -------------------------
    [Header("Thunder state")]
    public bool enableThunder = false; // 状態フラグ（外部からON/OFF）

    [Header("Thunder bullet blink")]
    [Min(0.1f)] public float thunderBlinkHz = 12f; // 点滅速度（Hz）
    [Range(0f, 1f)] public float thunderBlinkDuty = 0.5f; // 0.5=半分表示/半分非表示

    [Header("Thunder small homing")]
    [Tooltip("小弾が追尾する最大距離")]
    [Min(0f)] public float homingRange = 20f;
    [Tooltip("追尾の曲がりやすさ（大きいほど急旋回）")]
    [Min(0.01f)] public float homingTurnSpeed = 12f;
    [Tooltip("敵検索に使うLayerMask（未設定なら全レイヤーを探索）")]
    public LayerMask enemySearchMask = ~0;

    [Header("Thunder strike (on Medium/Large hit)")]
    [Tooltip("雷オブジェクトの生存時間（短いほど一瞬）")]
    [Min(0.01f)] public float lightningLifeSeconds = 0.18f;

    [Tooltip("雷の幅（X方向）")]
    [Min(0.01f)] public float lightningWidth = 1.2f;

    [Tooltip("雷の高さ（Y方向）")]
    [Min(0.1f)] public float lightningHeight = 9.0f;

    [Tooltip("雷の当たり判定の奥行（Z方向）")]
    [Min(0.01f)] public float lightningDepth = 0.8f;

    [Tooltip("雷の追加ダメージ（0なら弾ダメージと同じ）")]
    [Min(0)] public int lightningExtraDamage = 0;

    [Tooltip("雷用Shader名（下のShaderをプロジェクトに置いた場合この名前）")]
    public string lightningShaderName = "Custom/LightningBeamBuiltin";

    [Tooltip("雷見た目の強さ")]
    [Min(0f)] public float lightningIntensity = 2.5f;

    [Tooltip("雷の見た目色（雷球には色を付けないが、雷エフェクトの色は必要）")]
    public Color lightningColor = new Color(0.8f, 0.95f, 1.0f, 1.0f);

    // -------------------------
    // 内部
    // -------------------------
    private struct Projectile
    {
        public Transform tr;
        public Vector3 vel;
        public float age;

        public Renderer rend;  // 点滅用
        public bool isThunder;
        public bool isFreeze;

        public ShotSize sizeType;

        public float homingRange;
        public LayerMask enemyMask;
        public bool homingTurnedOnce;
    }

    private readonly List<Projectile> projectiles = new List<Projectile>(256);

    private float nextFireTime;
    private float chargeTimer;
    private bool firstShotPending = true;

    private Vector3 prevInertiaPos;
    private Vector3 inertiaVel;

    public float ChargeTimerSeconds => chargeTimer;

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

        nextFireTime = Time.time;
        chargeTimer = 0f;
        firstShotPending = true;
    }

    public void ResetFoodAttributeFlags()
    {
        enableThunder = false;
        enableFreezeOnHit = false;
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

        var fullChargeSecondsX = (1 + 0.5f * player.currentBodyKey / 100) * fullChargeSeconds;
        var halfChargeSecondsX = (1 + 0.5f * player.currentBodyKey / 100) * halfChargeSeconds;

        // ---- チャージ：撃っていない間だけ蓄積 ----
        if (!mouseDown)
        {
            chargeTimer = Mathf.Min(fullChargeSecondsX, chargeTimer + dt);
            firstShotPending = true;
        }
        else
        {
            var shotsPerSecondX = shotsPerSecond / Mathf.Max(1, 1 + 0.5f * player.currentBodyKey / 100);
            if (shotsPerSecondX > 0f)
            {
                float interval = 1f / shotsPerSecondX;

                if (Time.time >= nextFireTime)
                {
                    ShotSize sizeType;
                    float sizeMul;
                    int damage;

                    if (firstShotPending && chargeTimer >= fullChargeSecondsX)
                    {
                        AudioManager.Instance.PlaySE("eat_sugar");
                        sizeType = ShotSize.Large;
                        sizeMul = fullChargeSizeMul;
                        damage = DAMAGE_LARGE;
                    }
                    else if (firstShotPending && chargeTimer >= halfChargeSecondsX)
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

                    firstShotPending = false;
                    chargeTimer = 0f;

                    nextFireTime = Time.time + interval;
                }
            }
        }

        // ---- 弾更新 ----
        for (int i = projectiles.Count - 1; i >= 0; i--)
        {
            Projectile p = projectiles[i];

            if (p.tr == null)
            {
                projectiles.RemoveAt(i);
                continue;
            }

            // 雷：点滅（表示ON/OFF）
            if (p.isThunder && p.rend != null)
            {
                float phase = Mathf.Repeat(Time.time * thunderBlinkHz, 1f);
                bool visible = phase < Mathf.Clamp01(thunderBlinkDuty);
                // material色は変えない（要件）
                p.rend.enabled = visible;
            }

            // 雷 + 小：追尾（range内の最も近いEnemyへ）
            // 小の雷弾：1秒後に、最寄り敵の方向へ「一度だけ」曲げる
            if (p.isThunder && p.sizeType == ShotSize.Small && !p.homingTurnedOnce && p.age >= 0.5f)
            {
                Enemy target = FindNearestEnemy(p.tr.position, p.homingRange, p.enemyMask);
                if (target != null)
                {
                    // ★ y込みの3D方向
                    Vector3 to = target.transform.position - p.tr.position;
                    float sq = to.sqrMagnitude;
                    if (sq > 1e-8f)
                    {
                        Vector3 dir3D = to / Mathf.Sqrt(sq);

                        // 速度の大きさは維持（3D）
                        float spd = p.vel.magnitude;
                        if (spd < 1e-4f) spd = speed;

                        // ★ 3Dで即差し替え（これ1回だけ）
                        p.vel = dir3D * spd;
                        p.homingTurnedOnce = true;
                    }
                }
            }

            // 移動
            p.tr.position += p.vel * dt;

            // 見た目回転
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

        int projectileLayer = LayerMask.NameToLayer(PROJECTILE_LAYER_NAME);
        if (projectileLayer < 0)
        {
            Debug.LogError($"[ClickShootFromCenter] Layer '{PROJECTILE_LAYER_NAME}' not found. Create it in Tags and Layers.");
            Destroy(proj);
            enabled = false;
            return;
        }
        proj.layer = projectileLayer;

        // Renderer取得（点滅用/氷色用）
        var rend = proj.GetComponent<Renderer>();

        bool isFreeze = enableFreezeOnHit;
        bool isThunder = enableThunder;

        // 氷：色変更（雷の色は無し。共存時は「氷色を優先」で固定）
        if (isFreeze && rend != null)
        {
            rend.material.color = freezeBulletColor;
        }

        // 命中処理
        var hit = proj.AddComponent<ProjectileHit>();
        hit.damage = damage;
        hit.sizeType = sizeType;

        // 氷（決め打ち）
        hit.enableFreezeOnHit = isFreeze;
        hit.freezeSecondsSmall = FREEZE_SMALL;
        hit.freezeSecondsMedium = FREEZE_MEDIUM;
        hit.freezeSecondsLarge = FREEZE_LARGE;

        // 雷
        hit.enableThunderOnHit = isThunder;
        hit.thunderBlinkHz = thunderBlinkHz;
        hit.thunderBlinkDuty = thunderBlinkDuty;

        // 雷（中/大命中で雷を落とす）設定
        hit.lightningLifeSeconds = lightningLifeSeconds;
        hit.lightningWidth = lightningWidth;
        hit.lightningHeight = lightningHeight;
        hit.lightningDepth = lightningDepth;
        hit.lightningExtraDamage = lightningExtraDamage;
        hit.lightningShaderName = lightningShaderName;
        hit.lightningIntensity = lightningIntensity;
        hit.lightningColor = lightningColor;

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
            age = 0f,
            rend = rend,
            isThunder = isThunder,
            isFreeze = isFreeze,
            sizeType = sizeType,
            homingRange = homingRange,
            enemyMask = enemySearchMask,
            homingTurnedOnce = false
        });
    }

    // -------------------------
    // Enemy探索（最も近い）
    // -------------------------
    private static Enemy FindNearestEnemy(Vector3 from, float range, LayerMask mask)
    {
        if (range <= 0f) return null;

        float bestSq = range * range;
        Enemy best = null;

        // ★ Trigger も拾う
        Collider[] hits = Physics.OverlapSphere(from, range, mask, QueryTriggerInteraction.Collide);

        for (int i = 0; i < hits.Length; i++)
        {
            var e = hits[i].GetComponentInParent<Enemy>();
            if (e == null) continue;
            if (e.CurrentHP <= 0) continue;

            float sq = (e.transform.position - from).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = e;
            }
        }
        return best;
    }

    // ---------------------------------------------------------
    // 弾の命中処理（同ファイル内）
    // ---------------------------------------------------------
    private sealed class ProjectileHit : MonoBehaviour
    {
        public int damage;
        public ShotSize sizeType;

        // 氷
        public bool enableFreezeOnHit;
        public float freezeSecondsSmall;
        public float freezeSecondsMedium;
        public float freezeSecondsLarge;

        // 雷
        public bool enableThunderOnHit;
        public float thunderBlinkHz;
        public float thunderBlinkDuty;

        // 雷落下（中/大）
        public float lightningLifeSeconds;
        public float lightningWidth;
        public float lightningHeight;
        public float lightningDepth;
        public int lightningExtraDamage;
        public string lightningShaderName;
        public float lightningIntensity;
        public Color lightningColor;

        private void OnTriggerEnter(Collider other)
        {
            var enemy = other.GetComponentInParent<Enemy>();
            if (enemy == null) return;

            Vector3 hitPoint = other.ClosestPoint(transform.position);

            // 通常ダメージ
            enemy.TakeDamage(damage, hitPoint);

            // 氷：サイズ別にフリーズ
            if (enableFreezeOnHit)
            {
                float sec = 0f;
                switch (sizeType)
                {
                    case ShotSize.Small: sec = freezeSecondsSmall; break;
                    case ShotSize.Medium: sec = freezeSecondsMedium; break;
                    case ShotSize.Large: sec = freezeSecondsLarge; break;
                }
                if (sec > 0f) enemy.FreezeForSeconds(sec);
            }

            // 雷：中/大 で雷を落とす
            if (enableThunderOnHit && (sizeType == ShotSize.Medium || sizeType == ShotSize.Large))
            {
                // 雷は hitPoint を中心に落とす（縦長）
                int strikeDamage = damage * Mathf.Max(0, lightningExtraDamage);
                AudioManager.Instance.PlaySE("thunder");
                LightningStrike.Spawn(
                    hitPoint,
                    lightningWidth,
                    lightningHeight,
                    lightningDepth,
                    lightningLifeSeconds,
                    strikeDamage,
                    lightningShaderName,
                    lightningIntensity,
                    lightningColor
                );
            }

            // Small だけ消える。Medium / Large は残す（貫通）
            if (sizeType == ShotSize.Small)
            {
                Destroy(gameObject);
            }
        }
    }

    // ---------------------------------------------------------
    // 雷（縦長の当たり判定 + Quad描画 + Shader）
    // ---------------------------------------------------------
    private sealed class LightningStrike : MonoBehaviour
    {
        private int damage;
        private float life;
        private float age;

        // 一度当てた敵には再ヒットしない
        private readonly HashSet<int> hitEnemyIds = new HashSet<int>(32);

        // 生成
        public static void Spawn(
            Vector3 center,
            float width,
            float height,
            float depth,
            float lifeSeconds,
            int damage,
            string shaderName,
            float intensity,
            Color color)
        {
            width = Mathf.Max(0.01f, width);
            height = Mathf.Max(0.1f, height);
            depth = Mathf.Max(0.01f, depth);
            lifeSeconds = Mathf.Max(0.02f, lifeSeconds);

            // ルート
            var root = new GameObject("LightningStrike");
            root.transform.position = new Vector3(center.x, 0f, center.z);

            // 当たり判定（縦長Box）
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(width, height, depth);
            box.center = new Vector3(0f, height * 0.5f, 0f); // 地面方向を想定して上に伸ばす

            // クリック弾と干渉したくなければレイヤー指定（任意）
            // root.layer = LayerMask.NameToLayer("Projectiles");

            // 見た目：Quad（縦長）
            var vis = GameObject.CreatePrimitive(PrimitiveType.Quad);
            vis.name = "LightningVisual";
            Destroy(vis.GetComponent<Collider>());
            vis.transform.SetParent(root.transform, false);
            vis.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            vis.transform.localScale = new Vector3(width, height, 1f);

            // カメラに正面向け（簡易）：常にY軸回りだけカメラへ向ける
            var billboard = vis.AddComponent<LightningBillboard>();
            billboard.onlyYaw = true;

            // マテリアル（Shaderが無いときは Unlit/Transparent へフォールバック）
            Shader sh = Shader.Find(shaderName);
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            var mr = vis.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var mat = new Material(sh);
                mr.material = mat;

                // 共通パラメータ（Shader側に無い場合は無視される）
                mr.material.SetColor("_Color", color);
                mr.material.SetFloat("_Intensity", intensity);
                mr.material.SetFloat("_Flicker", 1.0f);
            }

            var strike = root.AddComponent<LightningStrike>();
            strike.damage = damage;
            strike.life = lifeSeconds;
        }



        void Update()
        {
            age += Time.deltaTime;
            if (age >= life)
            {
                Destroy(gameObject);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var enemy = other.GetComponentInParent<Enemy>();
            if (enemy == null) return;

            int id = enemy.GetInstanceID();
            if (hitEnemyIds.Contains(id)) return;
            hitEnemyIds.Add(id);

            Vector3 hitPoint = other.ClosestPoint(transform.position);
            enemy.TakeDamage(damage, hitPoint);
        }
    }

    private sealed class LightningBillboard : MonoBehaviour
    {
        public bool onlyYaw = true;

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 to = cam.transform.position - transform.position;
            if (to.sqrMagnitude < 1e-6f) return;

            if (onlyYaw)
            {
                to.y = 0f;
                if (to.sqrMagnitude < 1e-6f) return;
                transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
            }
            else
            {
                transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
            }
        }
    }
}