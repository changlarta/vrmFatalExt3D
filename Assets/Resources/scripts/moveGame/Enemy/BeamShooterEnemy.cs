using UnityEngine;

public sealed class BeamShooterEnemy : MonoBehaviour
{
    [Header("Target")]
    public string targetTag = "Player";

    [Header("Size")]
    [Min(0.01f)] public float sizeMultiplier = 1f;

    [Header("Move (X only)")]
    [Min(0.01f)] public float moveSpeed = 3f;
    [Min(0f)] public float stopRadiusX = 0.2f;

    [Header("State Durations")]
    [Min(0f)] public float chargeSeconds = 5f;
    [Min(0f)] public float firingSeconds = 5f;

    [Header("Beam/Warn Shape (Rectangular Prism)")]
    [Min(0.01f)] public float beamWidth = 0.35f;
    [Min(0.01f)] public float beamHeight = 0.35f;
    [Min(0.1f)] public float beamLength = 8f;

    [Header("Beam Origin")]
    [Tooltip("足元は常に y=0 前提。発射高さ = footYOffset")]
    public float footYOffset = 0.05f;

    [Tooltip("sizeMultiplier=1 のとき、中心から前方へ何mで muzzle(始点) にするか")]
    [Min(0f)] public float muzzleForwardOffsetAtSize1 = 1f;

    [Tooltip("食い込み防止の押し出し（sizeMultiplierで一緒に拡大）")]
    [Min(0f)] public float muzzlePushOut = 0.01f;

    [Header("Materials (MUST set in Inspector)")]
    public Material beamMaterial;
    public Material warningMaterial;

    [Header("Beam Damage (MUST true for this enemy)")]
    public bool enableBeamDamage = true;
    [Min(1)] public int beamDamage = 10;

    private Transform target;
    private Enemy enemy;

    private enum State { Idle, Charge, Firing }
    private State state;
    private float timer;
    private float idleSeconds = 2f;

    private enum BeamDir { Forward, Backward }
    private BeamDir dir;

    private GameObject warnGO, beamGO, hitGO;
    private BoxCollider hitCol;

    private Vector3 baseLocalScale;

    void Awake()
    {
        enemy = GetComponent<Enemy>();
        baseLocalScale = SanitizeScale(transform.localScale);
        ApplySize();
    }

    void Start()
    {
        if (beamMaterial == null || warningMaterial == null)
        {
            Debug.LogError("BeamShooterEnemy: beamMaterial と warningMaterial は必須です。");
            enabled = false;
            return;
        }
        if (!enableBeamDamage)
        {
            Debug.LogError("BeamShooterEnemy: enableBeamDamage=false は禁止。この敵では true 固定。");
            enabled = false;
            return;
        }

        target = FindTarget();
        dir = DecideDir();

        BuildObjects();
        SetState(State.Idle);
        UpdateBeamPlacementAndVisibility();
    }

    void Update()
    {
        ApplySize();

        if (enemy != null && enemy.IsFrozen)
        {
            SetActiveAll(false, false, false);
            return;
        }

        if (target == null)
        {
            target = FindTarget();
            if (target == null)
            {
                SetActiveAll(false, false, false);
                return;
            }
        }

        var newDir = DecideDir();
        if (newDir != dir)
        {
            dir = newDir;
            SetState(State.Idle);
        }

        float dt = Time.deltaTime;
        timer += dt;

        switch (state)
        {
            case State.Idle:
                MoveX(dt, moveSpeed);
                if (timer >= idleSeconds) SetState(State.Charge);
                break;

            case State.Charge:
                MoveX(dt, moveSpeed / 3f);
                if (timer >= chargeSeconds) SetState(State.Firing);
                break;

            case State.Firing:
                if (timer >= firingSeconds)
                {
                    idleSeconds = Random.Range(8f, 15f);
                    SetState(State.Idle);
                }
                break;
        }

        UpdateBeamPlacementAndVisibility();
    }

    private void UpdateBeamPlacementAndVisibility()
    {
        Vector3 fwd = (dir == BeamDir.Forward) ? Vector3.forward : Vector3.back;
        transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        float sm = Mathf.Max(0.01f, sizeMultiplier);

        // muzzle(始点)：前方オフセットも押し出しもサイズに連動
        float muzzleForward = (muzzleForwardOffsetAtSize1 + muzzlePushOut) * sm;
        Vector3 muzzle = new Vector3(transform.position.x, footYOffset, transform.position.z) + fwd * muzzleForward;

        // ★根本修正：ビーム自体が親スケールで伸びるので、中心オフセットも sm を掛ける
        float halfLenWorld = beamLength * sm * 0.5f;
        Vector3 center = muzzle + fwd * halfLenWorld;

        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

        PlaceBox(warnGO, center, rot, beamWidth, beamHeight, beamLength);
        PlaceBox(beamGO, center, rot, beamWidth, beamHeight, beamLength);

        if (hitGO != null)
        {
            hitGO.transform.SetPositionAndRotation(center, rot);
            if (hitCol != null)
            {
                hitCol.size = new Vector3(beamWidth, beamHeight, beamLength);
                hitCol.center = Vector3.zero;
            }
        }

        switch (state)
        {
            case State.Idle: SetActiveAll(false, false, false); break;
            case State.Charge: SetActiveAll(true, false, false); break;
            case State.Firing: SetActiveAll(false, true, true); break;
        }
    }

    private static void PlaceBox(GameObject go, Vector3 pos, Quaternion rot, float w, float h, float len)
    {
        if (!go) return;
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = new Vector3(w, h, len); // 親スケールで世界サイズは拡大される（仕様A）
    }

    private void SetState(State s)
    {
        if (state == s) return;
        state = s;
        timer = 0f;

        if (state == State.Charge) AudioManager.Instance.PlaySE("beam");
        if (state == State.Firing) AudioManager.Instance.PlaySE("thunder");
    }

    private void MoveX(float dt, float speed)
    {
        if (!target) return;

        float dx = target.position.x - transform.position.x;
        float stopR = Mathf.Max(0f, stopRadiusX);
        if (Mathf.Abs(dx) <= stopR) return;

        float dirSign = Mathf.Sign(dx);
        float step = dirSign * speed * dt;

        float canMove = Mathf.Abs(dx) - stopR;
        if (Mathf.Abs(step) > canMove) step = dirSign * canMove;

        var p = transform.position;
        p.x += step;
        transform.position = p;
    }

    private BeamDir DecideDir()
    {
        if (!target) return BeamDir.Forward;
        return (target.position.z >= transform.position.z) ? BeamDir.Forward : BeamDir.Backward;
    }

    private Transform FindTarget()
    {
        var go = GameObject.FindGameObjectWithTag(targetTag);
        return go ? go.transform : null;
    }

    private void BuildObjects()
    {
        warnGO = CreateCube("BeamWarning", warningMaterial);
        beamGO = CreateCube("BeamVisual", beamMaterial);

        hitGO = new GameObject("BeamHitbox");
        hitGO.transform.SetParent(transform, false);
        hitGO.transform.localScale = Vector3.one;

        hitCol = hitGO.AddComponent<BoxCollider>();
        hitCol.isTrigger = true;

        var dd = hitGO.AddComponent<DamageDealer>();
        dd.useTrigger = true;
        dd.damage = Mathf.Max(1, beamDamage);

        SetActiveAll(false, false, false);
    }

    private GameObject CreateCube(string name, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(transform, true);

        var col = go.GetComponent<Collider>();
        if (col) Destroy(col);

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        return go;
    }

    private void SetActiveAll(bool warn, bool beam, bool hit)
    {
        if (warnGO) warnGO.SetActive(warn);
        if (beamGO) beamGO.SetActive(beam);
        if (hitGO) hitGO.SetActive(hit);
    }

    private void ApplySize()
    {
        float sm = Mathf.Max(0.01f, float.IsFinite(sizeMultiplier) ? sizeMultiplier : 1f);
        transform.localScale = baseLocalScale * sm;
    }

    private static Vector3 SanitizeScale(Vector3 s)
    {
        bool ok = float.IsFinite(s.x) && float.IsFinite(s.y) && float.IsFinite(s.z)
                  && Mathf.Abs(s.x) > 1e-6f && Mathf.Abs(s.y) > 1e-6f && Mathf.Abs(s.z) > 1e-6f;
        return ok ? s : Vector3.one;
    }
}