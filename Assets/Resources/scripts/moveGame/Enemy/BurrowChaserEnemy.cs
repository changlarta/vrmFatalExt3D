using UnityEngine;

/// <summary>
/// 透明（本体見えない＆当たりなし）で出現 → 警告円柱だけ地面(y=0)に表示しつつ高速追尾
/// 追いついた判定が出る、または最大追尾時間に達すると「追加で」一定時間追尾 → その後、待機して次フェーズへ
/// 追尾停止 → 1秒後にy=-10へワープして当たり判定と見た目をON → y=0へせり上がり演出
/// その場で回転しながら待機 → 引っ込む演出（y=0→y=-10） → 再び透明追尾フェーズへ
/// </summary>
public sealed class BurrowChaserEnemy : MonoBehaviour
{
    [Header("Target")]
    public string targetTag = "Player";

    [Header("Materials (MUST set in Inspector)")]
    [Tooltip("警告（透明赤）のマテリアル（必須）")]
    public Material warningMaterial;

    [Header("Chase")]
    [Min(0.01f)] public float chaseSpeed = 18f;
    [Tooltip("追いついた判定距離（コライダーOFF運用なので距離で判定）")]
    [Min(0.01f)] public float catchDistance = 0.6f;

    [Tooltip("透明追尾フェーズで追いかける最大秒数。これを超えたら、追いついた時と同じ挙動へ移る")]
    [Min(0.01f)] public float maxHiddenChaseSeconds = 5f;

    [Header("Timings")]
    [Tooltip("追いついた判定が出ても、または最大追尾時間に達しても、この秒数だけ追加で追尾し続ける")]
    [Min(0f)] public float extraChaseAfterCatchSeconds = 2f;

    [Tooltip("追加追尾が終わった後、次の状態へ移るまで待つ（停止）")]
    [Min(0f)] public float afterCatchWaitSeconds = 3f;

    [Tooltip("追尾をやめてその場に止まってから、地下へ移動して出現準備に入るまでの時間")]
    [Min(0f)] public float stopBeforeBurrowSeconds = 1f;

    [Tooltip("地下→地上（生える）アニメ時間")]
    [Min(0.01f)] public float riseSeconds = 0.25f;

    [Tooltip("地上に出た後の待機時間（回転し続ける）")]
    [Min(0f)] public float emergedWaitSeconds = 5f;

    [Tooltip("地上→地下（引っ込む）アニメ時間")]
    [Min(0.01f)] public float retractSeconds = 0.25f;

    [Header("Burrow / Rise")]
    public float burrowY = -10f;
    public float emergeY = 0f;

    [Header("Warning Cylinder (on ground y=0)")]
    [Min(0.01f)] public float warningRadius = 0.8f;
    [Min(0.01f)] public float warningHeight = 0.15f;

    [Header("Spin While Emerged")]
    [Min(0f)] public float spinDegreesPerSecond = 360f;

    private Transform target;

    // 本体の「元の見た目＆当たり」をキャッシュしてON/OFFする
    private Collider[] cachedColliders;
    private Renderer[] cachedRenderers;

    // 警告円柱（見た目専用）
    private GameObject warningGO;

    private enum State
    {
        HiddenChase,          // 本体: OFF / 警告: ON / 高速追尾
        ExtraChaseAfterCatch, // 本体: OFF / 警告: ON / 追いついた後、または最大追尾時間後も追加追尾
        AfterCatchWait,       // 本体: OFF / 警告: ON / 停止して待つ
        StopAndPrepare,       // 本体: OFF / 警告: OFF / その場停止→burrowYへ
        Rising,               // 本体: ON  / 警告: OFF / burrowY→emergeYへアニメ上昇
        EmergedIdle,          // 本体: ON  / 警告: OFF / 回転しながら待機
        Retracting            // 本体: ON  / 警告: OFF / emergeY→burrowYへ引っ込み
    }

    private State state = State.HiddenChase;
    private float timer = 0f;

    private Vector3 animStartPos;
    private Vector3 animEndPos;

    // 「追いついた」判定を最初に検知した瞬間だけをトリガーにしたいのでフラグ
    private bool caughtTriggered = false;

    void Awake()
    {
        cachedColliders = GetComponentsInChildren<Collider>(includeInactive: true);
        cachedRenderers = GetComponentsInChildren<Renderer>(includeInactive: true);
    }

    void Start()
    {
        if (warningMaterial == null)
        {
            Debug.LogError("BurrowChaserEnemy: warningMaterial は必須です。");
            enabled = false;
            return;
        }

        var go = GameObject.FindGameObjectWithTag(targetTag);
        target = (go != null) ? go.transform : null;

        BuildWarningCylinder();

        // 初期状態：本体 OFF、警告 ON
        SetBodyEnabled(false);
        SetWarningVisible(true);
        EnterState(State.HiddenChase);
    }

    void Update()
    {
        if (!enabled) return;

        if (target == null)
        {
            var go = GameObject.FindGameObjectWithTag(targetTag);
            target = (go != null) ? go.transform : null;
            if (target == null)
            {
                SetBodyEnabled(false);
                SetWarningVisible(false);
                return;
            }
        }

        float dt = Time.deltaTime;
        timer += dt;

        // 警告は常に地面(y=0)＆自分のx,zに追従（ただしActiveのときだけ見える）
        UpdateWarningTransform();

        switch (state)
        {
            case State.HiddenChase:
                {
                    ChaseTarget(dt, chaseSpeed);

                    // 追いつき検知（初回だけトリガー）
                    // または最大追尾時間に達したら、追いついた時と同じ挙動へ
                    if ((!caughtTriggered && IsCaught()) || timer >= maxHiddenChaseSeconds)
                    {
                        caughtTriggered = true;
                        EnterState(State.ExtraChaseAfterCatch);
                    }
                    break;
                }

            case State.ExtraChaseAfterCatch:
                {
                    // 追いついた後、または最大追尾時間到達後も追加追尾
                    ChaseTarget(dt, chaseSpeed);

                    if (timer >= extraChaseAfterCatchSeconds)
                    {
                        EnterState(State.AfterCatchWait);
                    }
                    break;
                }

            case State.AfterCatchWait:
                {
                    // 停止して待つ（警告は出しっぱなし）
                    if (timer >= afterCatchWaitSeconds)
                    {
                        EnterState(State.StopAndPrepare);
                    }
                    break;
                }

            case State.StopAndPrepare:
                {
                    // その場停止、一定時間後にburrowYへワープして本体ON → Risingへ
                    if (timer >= stopBeforeBurrowSeconds)
                    {
                        var p = transform.position;
                        p.y = burrowY;
                        transform.position = p;

                        SetBodyEnabled(true);     // ここで当たり＆見た目 ON
                        SetWarningVisible(false); // 警告は消す

                        animStartPos = transform.position;
                        animEndPos = new Vector3(animStartPos.x, emergeY, animStartPos.z);

                        EnterState(State.Rising);
                    }
                    break;
                }

            case State.Rising:
                {
                    AnimateY(animStartPos, animEndPos, riseSeconds);

                    if (IsAnimFinished(riseSeconds))
                    {
                        transform.position = animEndPos;

                        if (AudioManager.Instance != null)
                        {
                            AudioManager.Instance.PlaySE("eat_drag");
                        }

                        EnterState(State.EmergedIdle);
                    }
                    break;
                }

            case State.EmergedIdle:
                {
                    // 地上で回転
                    Spin(dt);

                    if (timer >= emergedWaitSeconds)
                    {
                        // 引っ込みへ
                        animStartPos = transform.position;
                        animEndPos = new Vector3(animStartPos.x, burrowY, animStartPos.z);
                        EnterState(State.Retracting);
                    }
                    break;
                }

            case State.Retracting:
                {
                    // 引っ込み中も回転（不要なら Spin(dt) を消す）
                    Spin(dt);

                    AnimateY(animStartPos, animEndPos, retractSeconds);

                    if (IsAnimFinished(retractSeconds))
                    {
                        transform.position = animEndPos;

                        // 地下に戻ったら透明追尾へ戻す
                        SetBodyEnabled(false);
                        SetWarningVisible(true);
                        caughtTriggered = false; // 次サイクルに備えて解除
                        EnterState(State.HiddenChase);
                    }
                    break;
                }
        }
    }

    private void EnterState(State s)
    {
        state = s;
        timer = 0f;

        switch (state)
        {
            case State.HiddenChase:
                SetBodyEnabled(false);
                SetWarningVisible(true);
                break;

            case State.ExtraChaseAfterCatch:
                SetBodyEnabled(false);
                SetWarningVisible(true);
                break;

            case State.AfterCatchWait:
                SetBodyEnabled(false);
                SetWarningVisible(true);
                break;

            case State.StopAndPrepare:
                SetBodyEnabled(false);
                SetWarningVisible(false);
                break;

            case State.Rising:
                SetBodyEnabled(true);
                SetWarningVisible(false);
                break;

            case State.EmergedIdle:
                SetBodyEnabled(true);
                SetWarningVisible(false);
                break;

            case State.Retracting:
                SetBodyEnabled(true);
                SetWarningVisible(false);
                break;
        }
    }

    private void ChaseTarget(float dt, float speed)
    {
        if (target == null) return;

        Vector3 p = transform.position;
        Vector3 tp = target.position;

        // y は固定（透明追尾中も高さを変えない）
        Vector3 next = Vector3.MoveTowards(p, new Vector3(tp.x, p.y, tp.z), speed * dt);
        transform.position = next;
    }

    private bool IsCaught()
    {
        if (target == null) return false;

        Vector3 p = transform.position;
        Vector3 tp = target.position;

        // 水平距離（x,z）
        Vector2 a = new Vector2(p.x, p.z);
        Vector2 b = new Vector2(tp.x, tp.z);
        float d = Vector2.Distance(a, b);

        return d <= Mathf.Max(0.01f, catchDistance);
    }

    private void Spin(float dt)
    {
        transform.Rotate(0f, spinDegreesPerSecond * dt, 0f, Space.World);
    }

    private void AnimateY(Vector3 from, Vector3 to, float seconds)
    {
        float t = (seconds <= 1e-6f) ? 1f : Mathf.Clamp01(timer / seconds);
        float eased = Mathf.SmoothStep(0f, 1f, t);
        transform.position = Vector3.LerpUnclamped(from, to, eased);
    }

    private bool IsAnimFinished(float seconds)
    {
        if (seconds <= 1e-6f) return true;
        return timer >= seconds - 1e-6f;
    }

    private void SetBodyEnabled(bool on)
    {
        if (cachedColliders != null)
        {
            for (int i = 0; i < cachedColliders.Length; i++)
            {
                var c = cachedColliders[i];
                if (c == null) continue;
                if (warningGO != null && c.transform.IsChildOf(warningGO.transform)) continue;
                c.enabled = on;
            }
        }

        if (cachedRenderers != null)
        {
            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                var r = cachedRenderers[i];
                if (r == null) continue;
                if (warningGO != null && r.transform.IsChildOf(warningGO.transform)) continue;
                r.enabled = on;
            }
        }
    }

    private void BuildWarningCylinder()
    {
        warningGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        warningGO.name = "WarningCylinder";
        warningGO.transform.SetParent(transform, worldPositionStays: true);

        var col = warningGO.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var mr = warningGO.GetComponent<MeshRenderer>();
        mr.sharedMaterial = warningMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        UpdateWarningTransform();
    }

    private void UpdateWarningTransform()
    {
        if (warningGO == null) return;

        float radius = Mathf.Max(0.01f, warningRadius);
        float height = Mathf.Max(0.01f, warningHeight);

        Vector3 p = transform.position;

        // 円柱は中心が高さ/2なので地面に置く
        warningGO.transform.position = new Vector3(p.x, 0f + height * 0.5f, p.z);
        warningGO.transform.rotation = Quaternion.identity;

        // Unity Cylinder: 高さは localScale.y * 2
        warningGO.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
    }

    private void SetWarningVisible(bool on)
    {
        if (warningGO != null) warningGO.SetActive(on);
    }
}