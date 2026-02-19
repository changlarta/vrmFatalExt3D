using UnityEngine;

/// <summary>
/// マイクラのスライム風：Playerタグへ向かってジャンプ移動＋スカッシュ&ストレッチ。
/// 地面は y=0 固定。bounds を毎フレーム触らず、NaNを出さないようにガードした安定版。
/// </summary>
public sealed class SlimeJumpMover : MonoBehaviour
{
    [Header("Target")]
    public string targetTag = "Player";
    public float retargetInterval = 0.25f;

    [Header("Size")]
    [Min(0.01f)]
    public float sizeMultiplier = 1.0f; // 追加：全体サイズ倍率

    [Header("Jump motion")]
    public float hopDistance = 2.0f;
    public float hopHeight = 1.1f;
    public float turnSpeed = 10f;

    [Header("Timing")]
    public float preJumpSeconds = 0.45f;
    public float airTimeSeconds = 0.55f;
    public float landSquashSeconds = 0.12f;
    public float recoverSeconds = 0.20f;
    public float restSeconds = 0.18f;

    [Header("Squash & Stretch")]
    public float squashY = 0.67f;
    public float stretchY = 1.5f;
    [Range(0f, 1f)] public float volumePreserve = 1.0f;

    [Header("Grounding")]
    [Min(0f)]
    public float bottomPadding = 0.0f; // 0でOK。ちょい浮かせたい時だけ

    // ---- internal ----
    private enum Phase { PreJump, Air, LandSquash, Recover, Rest }
    private Phase phase;
    private float timer;

    private Transform cachedTarget;
    private float retargetTimer;

    private Vector3 initialLocalScale;      // 元のスケール（sizeMultiplier未反映）
    private Vector3 hopStart;               // ground基準の開始位置（yも含む）
    private Vector3 hopEnd;                 // ground基準の終了位置（yも含む）

    // 「ローカル空間での半高さ」：Collider/Rendererから推定（Update中にboundsを触らない）
    private float localHalfHeight = 0.5f;   // 推定できない場合のデフォルト

    void Awake()
    {
        initialLocalScale = transform.localScale;
        if (!IsFiniteVec3(initialLocalScale) || Mathf.Abs(initialLocalScale.x) < 1e-6f || Mathf.Abs(initialLocalScale.y) < 1e-6f || Mathf.Abs(initialLocalScale.z) < 1e-6f)
        {
            // 変な初期スケールが入っていても死なないように
            initialLocalScale = Vector3.one;
        }

        ComputeLocalHalfHeightOnce();

        phase = Phase.PreJump;
        timer = 0f;

        AcquireTarget(true);

        // 初期位置を地面へ
        ApplySquashStretch(1f);
        SnapToGround();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        timer += dt;

        // ターゲット再探索（毎フレームFindしない）
        retargetTimer += dt;
        if (retargetTimer >= retargetInterval)
        {
            retargetTimer = 0f;
            AcquireTarget(false);
        }

        Vector3 dir = GetPlanarDirToTarget(cachedTarget);

        // 向き追従（滑らか）
        if (dir.sqrMagnitude > 1e-8f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
            float k = 1f - Mathf.Exp(-turnSpeed * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, k);
        }

        switch (phase)
        {
            case Phase.PreJump:
                {
                    float u = Safe01(preJumpSeconds, timer);

                    // 溜め：1→squash
                    float yMul = Mathf.Lerp(1f, squashY, EaseInOut(u));
                    ApplySquashStretch(yMul);
                    SnapToGround();

                    if (u >= 1f)
                    {
                        hopStart = GetGroundedPosition();

                        // hop end は開始時に確定（途中で揺れない）
                        Vector3 step = (dir.sqrMagnitude > 1e-8f) ? dir * hopDistance : Vector3.zero;
                        hopEnd = hopStart + step;

                        timer = 0f;
                        phase = Phase.Air;
                    }
                    break;
                }

            case Phase.Air:
                {
                    float u = Safe01(airTimeSeconds, timer);

                    // 空中：中央で最大ストレッチ
                    float yMul = Mathf.Lerp(squashY, stretchY, AirStretch(u));
                    ApplySquashStretch(yMul);

                    // 水平はS字で
                    Vector3 horiz = Vector3.Lerp(hopStart, hopEnd, SmoothStep01(u));

                    // 高さはSin
                    float h = Mathf.Sin(u * Mathf.PI) * hopHeight;

                    // 地面基準 + 高さ（底面がy=0に合うよう center.y を決める）
                    Vector3 p = new Vector3(horiz.x, 0f, horiz.z);
                    transform.position = new Vector3(p.x, GroundCenterY() + h, p.z);

                    if (u >= 1f)
                    {
                        // 着地を確定
                        transform.position = new Vector3(hopEnd.x, GroundCenterY(), hopEnd.z);

                        timer = 0f;
                        phase = Phase.LandSquash;
                    }
                    break;
                }

            case Phase.LandSquash:
                {
                    float u = Safe01(landSquashSeconds, timer);

                    // 着地：stretch→squash
                    float yMul = Mathf.Lerp(stretchY, squashY, EaseOut(u));
                    ApplySquashStretch(yMul);
                    SnapToGround();

                    if (u >= 1f)
                    {
                        timer = 0f;
                        phase = Phase.Recover;
                    }
                    break;
                }

            case Phase.Recover:
                {
                    float u = Safe01(recoverSeconds, timer);

                    // ばね戻り
                    float yMul = Mathf.Lerp(squashY, 1f, Spring01(u));
                    ApplySquashStretch(yMul);
                    SnapToGround();

                    if (u >= 1f)
                    {
                        timer = 0f;
                        phase = Phase.Rest;
                    }
                    break;
                }

            case Phase.Rest:
                {
                    float u = Safe01(restSeconds, timer);

                    ApplySquashStretch(1f);
                    SnapToGround();

                    if (u >= 1f)
                    {
                        timer = 0f;
                        phase = Phase.PreJump;
                    }
                    break;
                }
        }

        // 最終ガード：万一NaNが出たら即座に復旧
        if (!IsFiniteVec3(transform.localScale))
        {
            transform.localScale = Vector3.Scale(initialLocalScale, Vector3.one * Mathf.Max(0.01f, sizeMultiplier));
        }
        if (!IsFiniteVec3(transform.position))
        {
            Vector3 p = transform.position;
            transform.position = new Vector3(p.x, GroundCenterY(), p.z);
        }
    }

    // ---------------- Target ----------------

    private void AcquireTarget(bool force)
    {
        if (!force && cachedTarget != null) return;

        var go = GameObject.FindGameObjectWithTag(targetTag);
        cachedTarget = (go != null) ? go.transform : null;
    }

    private Vector3 GetPlanarDirToTarget(Transform target)
    {
        if (target == null) return Vector3.zero;

        Vector3 d = target.position - transform.position;
        d.y = 0f;

        float sq = d.sqrMagnitude;
        if (sq < 1e-8f) return Vector3.zero;

        return d / Mathf.Sqrt(sq);
    }

    // ---------------- Scale / Ground ----------------

    private void ApplySquashStretch(float yMul)
    {
        // NaN/Inf ガード
        if (!IsFinite(yMul)) yMul = 1f;

        // 下限
        yMul = Mathf.Max(0.05f, yMul);

        float sm = Mathf.Max(0.01f, IsFinite(sizeMultiplier) ? sizeMultiplier : 1f);

        // 体積保存っぽいXZ補正
        float xzMul = 1f;
        if (volumePreserve > 1e-6f)
        {
            // yMulが小さいと大きくなるが有限
            float inv = 1f / yMul;
            xzMul = Mathf.Pow(inv, 0.5f * Mathf.Clamp01(volumePreserve));
            if (!IsFinite(xzMul)) xzMul = 1f;
        }

        Vector3 baseS = initialLocalScale * sm;

        // baseSが変でも死なない
        if (!IsFiniteVec3(baseS)) baseS = Vector3.one * sm;

        Vector3 newScale = new Vector3(baseS.x * xzMul, baseS.y * yMul, baseS.z * xzMul);
        if (!IsFiniteVec3(newScale)) newScale = Vector3.one * sm;

        transform.localScale = newScale;
    }

    private void SnapToGround()
    {
        Vector3 p = transform.position;
        transform.position = new Vector3(p.x, GroundCenterY(), p.z);
    }

    private Vector3 GetGroundedPosition()
    {
        Vector3 p = transform.position;
        return new Vector3(p.x, GroundCenterY(), p.z);
    }

    // 「底面が y=0 に接するための center.y」
    private float GroundCenterY()
    {
        float halfWorld = HalfHeightWorld();
        if (!IsFinite(halfWorld)) halfWorld = 0.5f;
        return halfWorld + bottomPadding;
    }

    // Update中にboundsを触らず、lossyScaleから安定して求める
    private float HalfHeightWorld()
    {
        float sy = Mathf.Abs(transform.lossyScale.y);
        if (!IsFinite(sy) || sy < 1e-8f) sy = 1f;
        return localHalfHeight * sy;
    }

    private void ComputeLocalHalfHeightOnce()
    {
        // 初期時だけ bounds 参照して「ローカル半高さ」を推定、以降は lossyScale だけで計算
        float denom = Mathf.Abs(transform.lossyScale.y);
        if (!IsFinite(denom) || denom < 1e-8f) denom = 1f;

        var col = GetComponentInChildren<Collider>();
        if (col != null)
        {
            float ext = col.bounds.extents.y;
            if (IsFinite(ext) && ext > 1e-6f)
            {
                localHalfHeight = ext / denom;
                localHalfHeight = Mathf.Clamp(localHalfHeight, 0.01f, 100f);
                return;
            }
        }

        var rend = GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            float ext = rend.bounds.extents.y;
            if (IsFinite(ext) && ext > 1e-6f)
            {
                localHalfHeight = ext / denom;
                localHalfHeight = Mathf.Clamp(localHalfHeight, 0.01f, 100f);
                return;
            }
        }

        // 推定不能：既定値
        localHalfHeight = 0.5f;
    }

    // ---------------- Curves ----------------

    private static float Safe01(float duration, float time)
    {
        if (!IsFinite(duration) || duration <= 1e-6f) return 1f;
        if (!IsFinite(time)) return 1f;
        return Mathf.Clamp01(time / duration);
    }

    private static float SmoothStep01(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    private static float EaseInOut(float x)
    {
        x = Mathf.Clamp01(x);
        return x < 0.5f
            ? 2f * x * x
            : 1f - Mathf.Pow(-2f * x + 2f, 2f) / 2f;
    }

    private static float EaseOut(float x)
    {
        x = Mathf.Clamp01(x);
        return 1f - (1f - x) * (1f - x);
    }

    private static float AirStretch(float u)
    {
        // 0..1..0 の山（中央で最大）
        u = Mathf.Clamp01(u);
        float s = Mathf.Sin(u * Mathf.PI);
        float v = Mathf.Pow(s, 0.85f);
        return Mathf.Clamp01(v);
    }

    private static float Spring01(float x)
    {
        x = Mathf.Clamp01(x);
        float osc = Mathf.Sin(x * Mathf.PI * 2.2f);
        float damp = Mathf.Lerp(1f, 0.15f, x);
        float v = x + osc * 0.20f * damp;
        return Mathf.Clamp01(v);
    }

    // ---------------- Finite guards ----------------

    private static bool IsFinite(float v) => !(float.IsNaN(v) || float.IsInfinity(v));

    private static bool IsFiniteVec3(Vector3 v)
        => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
}
