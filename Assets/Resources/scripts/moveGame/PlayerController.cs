using UnityEngine;
using System.Collections;
using Unity.Mathematics;

public interface IDamageable
{
    bool CanTakeDamage { get; }
    void ApplyDamage(DamageInfo info);
}

public struct DamageInfo
{
    public int amount;
    public Vector3 hitPoint;
    public Vector3 attackerWorldPos;
}

public sealed class PlayerController : MonoBehaviour, IDamageable
{
    [Header("Required refs")]
    public TextAsset character;
    public GameObject vrmGameObject;

    [SerializeField] private ClickShootFromCenter clickShootFromCenter;

    [Header("Camera (required, no fallback)")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private moveGameSceneCamera cameraController;

    [SerializeField] private bool moveInWorldSpace = true;

    [SerializeField] private GroundStreamer groundStreamer;
    [SerializeField] private PlayerImage playerImage;

    private VrmToController ctr1;

    private float clampXHalf = 50f;
    private float clampXMargin = 0f;

    private bool groundZClampEnabled = false;
    private float groundMinZ = 0f;
    private float groundMaxZ = 0f;

    private bool boundsReady = false;
    private bool gameplayEnabled = false;
    private bool isGameOver = false;

    private Vector3 moveDir = Vector3.zero;
    private bool dashRequested = true;
    private bool jumpQueued = false;

    private enum JumpState { None, Rising, Falling }
    private JumpState jumpState = JumpState.None;
    private float jumpTimer = 0f;
    private float fallVel = 0f;

    private float movingLogTimer = 0f;
    private bool landingShakeFired = false;

    public float speedx = 1f;

    private float moveDecayTimer = 0f;

    private bool bodyKeyAnimating = false;
    private float bodyKeyStart = 0f;
    private float bodyKeyTarget = 0f;
    private float bodyKeyElapsed = 0f;
    private float bodyKeyDuration = 0f;

    private Vector3 airVel = Vector3.zero;
    private float airSpeedFactor = 0f;

    private enum ForcedMoveState { None, ZForwardBurst }
    private ForcedMoveState forcedMoveState = ForcedMoveState.None;

    [Header("Forced Z Burst")]
    [SerializeField] private float forcedZBurstSpeed = 20f;
    [SerializeField] private float forcedZBurstSeconds = 1f;

    private float forcedMoveTimer = 0f;

    [Header("Fatigue (0..100)")]
    [Range(0f, 100f)] public float fatigue = 0f;
    public float fatigueRecoverPerSec = 25f;
    public bool exhausted = false;

    public float currentBodyKey = 0f;
    private float lastObservedCurrentBodyKey = 0f;
    private bool isWritingCurrentBodyKeyFromAnim = false;

    [Header("HP")]
    public int maxHP = 100;
    public int CurrentHP = 0;

    [Header("Hit reaction")]
    public float hitStunSeconds = 0.5f;
    public float invincibleSeconds = 2.0f;
    public float knockbackDuration = 0.12f;
    public float blinkInterval = 0.1f;

    private bool invincible = false;
    private float controlLockTimer = 0f;

    private Coroutine invincibleCo;
    private Coroutine knockbackCo;

    private bool initialized = false;

    public Transform VrmTransform => vrmGameObject != null ? vrmGameObject.transform : null;

    public void SetWorldBounds(float laneHalfExtent, float xMargin, bool zClampEnabled, float minZ, float maxZ)
    {
        if (laneHalfExtent < 0f) { Debug.LogError("[PlayerController] laneHalfExtent < 0"); enabled = false; return; }
        if (zClampEnabled && maxZ < minZ) { Debug.LogError("[PlayerController] maxZ < minZ"); enabled = false; return; }

        clampXHalf = laneHalfExtent;
        clampXMargin = Mathf.Max(0f, xMargin);

        groundZClampEnabled = zClampEnabled;
        groundMinZ = minZ;
        groundMaxZ = maxZ;

        boundsReady = true;
    }

    public void SetGameplayEnabled(bool enabledValue)
    {
        gameplayEnabled = enabledValue;

        if (!gameplayEnabled)
        {
            moveDir = Vector3.zero;
            dashRequested = false;
            jumpQueued = false;
            forcedMoveState = ForcedMoveState.None;
            forcedMoveTimer = 0f;
            controlLockTimer = 0f;
        }
    }

    public void SetCharacterVisible(bool visible)
    {
        SetVisualChildrenActive(visible);
    }

    public void SetTransformForTitle(Vector3 position, Quaternion rotation)
    {
        if (vrmGameObject == null) return;

        Transform tr = vrmGameObject.transform;
        tr.position = position;
        tr.rotation = rotation;
    }

    public IEnumerator CoLoadCharacterOnce()
    {
        if (initialized) yield break;

        if (vrmGameObject == null) { Debug.LogError("[PlayerController] vrmGameObject is null."); enabled = false; yield break; }
        if (character == null) { Debug.LogError("[PlayerController] character is null."); enabled = false; yield break; }

        ctr1 = vrmGameObject.GetComponent<VrmToController>();
        if (ctr1 == null) { Debug.LogError("[PlayerController] VrmToController missing on vrmGameObject."); enabled = false; yield break; }

        if (playerCamera == null) { Debug.LogError("[PlayerController] playerCamera is null."); enabled = false; yield break; }
        if (cameraController == null) { Debug.LogError("[PlayerController] cameraController is null."); enabled = false; yield break; }

        ctr1.blushValue = 0.5f;
        ctr1.meshPullEnabled = false;
        ctr1.ReloadFromBytes(character.bytes, BodyVariant.Cooking, 100, ctr1.bodyKey, 30, 0.2f);

        while (!ctr1.IsReady) yield return null;

        currentBodyKey = Mathf.Clamp(ctr1.bodyKey, 0f, 100f);
        lastObservedCurrentBodyKey = currentBodyKey;
        ApplyBodyKey(currentBodyKey);

        HardResetRuntimeState();

        boundsReady = false;
        gameplayEnabled = false;

        initialized = true;
    }

    public void ResetForReload()
    {
        if (!initialized) return;

        if (invincibleCo != null) StopCoroutine(invincibleCo);
        if (knockbackCo != null) StopCoroutine(knockbackCo);

        invincibleCo = null;
        knockbackCo = null;

        HardResetRuntimeState();

        Transform tr = vrmGameObject.transform;
        tr.position = new Vector3(0f, 20f, 0f);
        tr.rotation = Quaternion.identity;

        boundsReady = false;
    }

    public void StartForcedZBurst()
    {
        if (!initialized) return;
        if (!gameplayEnabled) return;
        if (vrmGameObject == null) return;
        if (jumpState != JumpState.None) return;
        if (forcedMoveState != ForcedMoveState.None) return;

        AudioManager.Instance.PlaySE("dash");

        forcedMoveState = ForcedMoveState.ZForwardBurst;
        forcedMoveTimer = 0f;

        moveDir = Vector3.zero;
        dashRequested = false;
        jumpQueued = false;

        airVel = Vector3.zero;
        airSpeedFactor = 0f;

        movingLogTimer = 0f;
        landingShakeFired = false;

        moveDecayTimer = 0f;
    }

    private void TickForcedMove(float dt)
    {
        if (forcedMoveState == ForcedMoveState.None) return;
        if (vrmGameObject == null) { forcedMoveState = ForcedMoveState.None; return; }

        forcedMoveTimer += dt;

        Transform tr = vrmGameObject.transform;

        Vector3 p = tr.position;
        p += Vector3.forward * forcedZBurstSpeed * dt;

        float y = p.y;
        p = ClampToBounds(p);
        p.y = y;
        tr.position = p;

        Vector3 look = Vector3.forward;
        look.y = 0f;
        if (look.sqrMagnitude > 1e-12f)
        {
            Quaternion targetRot = Quaternion.LookRotation(look, Vector3.up);
            tr.rotation = Quaternion.Slerp(tr.rotation, targetRot, 20f * dt);
        }

        ctr1.ApplyEvent(ctr1.bodyKey > 25f ? "moving_fly2" : "moving_fly1");

        if (forcedMoveTimer >= forcedZBurstSeconds)
        {
            forcedMoveState = ForcedMoveState.None;
            forcedMoveTimer = 0f;
        }
    }

    private void HardResetRuntimeState()
    {
        moveDir = Vector3.zero;
        dashRequested = true;
        jumpQueued = false;

        jumpState = JumpState.Falling;
        jumpTimer = 0f;
        fallVel = 0f;

        movingLogTimer = 0f;
        landingShakeFired = false;

        moveDecayTimer = 0f;

        airVel = Vector3.zero;
        airSpeedFactor = 0f;

        bodyKeyAnimating = false;
        bodyKeyStart = currentBodyKey;
        bodyKeyTarget = currentBodyKey;
        bodyKeyElapsed = 0f;
        bodyKeyDuration = 0f;

        fatigue = 0f;
        exhausted = false;

        CurrentHP = Mathf.Max(1, maxHP);

        invincible = false;
        controlLockTimer = 0f;

        forcedMoveState = ForcedMoveState.None;
        forcedMoveTimer = 0f;

        isGameOver = false;
        SetVisualChildrenActive(true);

        if (clickShootFromCenter != null)
        {
            clickShootFromCenter.ResetFoodAttributeFlags();
        }
    }

    private void Update()
    {
        if (!initialized) return;
        if (!gameplayEnabled) return;
        if (!boundsReady) return;

        float dt = Time.deltaTime;

        DetectAndApplyExternalBodyKeyWrite();

        if (forcedMoveState != ForcedMoveState.None)
        {
            AdvanceBodyKeyAnimation(dt);
            TickForcedMove(dt);
            ForceClampNow();
            return;
        }

        if (controlLockTimer > 0f)
        {
            controlLockTimer -= dt;
            moveDir = Vector3.zero;
            dashRequested = false;
            jumpQueued = false;
        }
        else
        {
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            dashRequested = !shiftHeld;

            if (Input.GetKeyDown(KeyCode.Space))
                jumpQueued = true;

            float x = Input.GetAxisRaw("Horizontal");
            float z = Input.GetAxisRaw("Vertical");
            Vector3 input = new Vector3(x, 0f, z);

            Vector3 worldDir = Vector3.zero;
            if (input.sqrMagnitude > 0.0001f)
            {
                Vector3 localDir = input.normalized;

                Vector3 forward = playerCamera.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude <= 1e-12f)
                {
                    Debug.LogError("[PlayerController] camera forward is invalid (zero).");
                    enabled = false;
                    return;
                }
                forward.Normalize();

                Vector3 right = Vector3.Cross(Vector3.up, forward);
                if (right.sqrMagnitude <= 1e-12f)
                {
                    Debug.LogError("[PlayerController] camera right is invalid (zero).");
                    enabled = false;
                    return;
                }
                right.Normalize();

                worldDir = right * localDir.x + forward * localDir.z;
                worldDir.y = 0f;

                if (worldDir.sqrMagnitude <= 1e-12f)
                {
                    Debug.LogError("[PlayerController] computed worldDir is invalid (zero).");
                    enabled = false;
                    return;
                }
                worldDir.Normalize();
            }

            if (!moveInWorldSpace)
            {
                worldDir = vrmGameObject.transform.TransformDirection(worldDir);
                worldDir.y = 0f;
                if (worldDir.sqrMagnitude <= 1e-12f)
                {
                    Debug.LogError("[PlayerController] TransformDirection produced invalid direction.");
                    enabled = false;
                    return;
                }
                worldDir.Normalize();
            }

            SetMoveDirection(worldDir);
        }

        ApplyFatigueAndRestrictions(dt);
        TickCharacter(dt);
        AdvanceBodyKeyAnimation(dt);

        ForceClampNow();
    }

    private void DetectAndApplyExternalBodyKeyWrite()
    {
        if (isWritingCurrentBodyKeyFromAnim) return;

        if (Mathf.Abs(currentBodyKey - lastObservedCurrentBodyKey) > 1e-6f)
        {
            bodyKeyAnimating = false;
            bodyKeyElapsed = 0f;
            bodyKeyDuration = 0f;

            currentBodyKey = Mathf.Clamp(currentBodyKey, 0f, 100f);
            lastObservedCurrentBodyKey = currentBodyKey;

            ApplyBodyKey(currentBodyKey);
        }
    }

    private Vector3 ClampToBounds(Vector3 pos)
    {
        float halfX = Mathf.Max(0f, clampXHalf - clampXMargin);
        pos.x = Mathf.Clamp(pos.x, -halfX, halfX);

        if (groundZClampEnabled)
            pos.z = Mathf.Clamp(pos.z, groundMinZ, groundMaxZ);

        return pos;
    }

    public void ForceClampNow()
    {
        if (vrmGameObject == null) return;
        Transform t = vrmGameObject.transform;
        t.position = ClampToBounds(t.position);
    }

    private void SetMoveDirection(Vector3 worldDir)
    {
        worldDir.y = 0f;
        moveDir = (worldDir.sqrMagnitude > 1e-8f) ? worldDir.normalized : Vector3.zero;
    }

    private float GetBodyKeyTargetForFatigue()
    {
        return bodyKeyAnimating ? bodyKeyTarget : currentBodyKey;
    }

    private void ApplyFatigueAndRestrictions(float dt)
    {
        float bkTarget = GetBodyKeyTargetForFatigue();
        float bk01 = Mathf.Clamp01(bkTarget / 100f);
        float bodyKeyMult = Mathf.Lerp(0.01f, 0.8f, bk01);

        bool hasMoveInput = moveDir.sqrMagnitude > 1e-8f;

        if (exhausted)
        {
            moveDir = Vector3.zero;
            dashRequested = false;
            jumpQueued = false;

            fatigue -= fatigueRecoverPerSec * dt;
            if (fatigue <= 0f)
            {
                fatigue = 0f;
                exhausted = false;
            }
            return;
        }

        bool forceWalkByFatigue = (fatigue >= 50f);
        bool dashEffective = dashRequested && !forceWalkByFatigue;

        bool canIncreaseOnDash = (bkTarget >= 25f);

        if (jumpState == JumpState.None && hasMoveInput)
        {
            if (dashEffective)
            {
                if (canIncreaseOnDash) fatigue += 6f * bodyKeyMult * dt;
            }
            else
            {
                if (canIncreaseOnDash) fatigue += 3f * bodyKeyMult * dt;
            }
        }
        else if (jumpState == JumpState.Falling && hasMoveInput)
        {
            if (canIncreaseOnDash) fatigue += 1f * bodyKeyMult * dt;
        }
        else if (jumpState == JumpState.Rising)
        {
            if (canIncreaseOnDash) fatigue += 6f * bodyKeyMult * dt;
        }
        else if (jumpState == JumpState.None)
        {
            fatigue -= fatigueRecoverPerSec * dt;
        }

        fatigue = Mathf.Clamp(fatigue, 0f, 100f);

        if (fatigue >= 100f)
        {
            fatigue = 100f;
            exhausted = true;

            moveDir = Vector3.zero;
            dashRequested = false;
            jumpQueued = false;
        }
        else
        {
            if (forceWalkByFatigue) dashRequested = false;
        }
    }

    private void TickCharacter(float dt)
    {
        Transform tr = vrmGameObject.transform;

        tr.position = ClampToBounds(tr.position);

        bool hasInputDir = moveDir.sqrMagnitude > 1e-8f;
        bool isAir = (jumpState != JumpState.None);

        if (!isAir && jumpQueued)
        {
            jumpQueued = false;

            if (tr.position.y <= 0f + 1e-4f)
            {
                jumpState = JumpState.Rising;
                jumpTimer = 0f;
                fallVel = 0f;
                landingShakeFired = false;
                isAir = true;

                airVel = Vector3.zero;
                airSpeedFactor = -1f;
            }
        }
        else
        {
            jumpQueued = false;
        }

        float bk = ctr1.bodyKey / 100f;
        float t = 1f - bk;
        float baseSpeed = 0.08f - 0.072f * (1f - (t * t));

        bool dash = dashRequested;

        if (!isAir)
        {
            bool isMoving = hasInputDir;

            float mul = dash ? 4f : 1f;
            float speed = mul * baseSpeed * speedx;

            if (isMoving)
            {
                tr.position += moveDir * speed;
                tr.position = ClampToBounds(tr.position);

                Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                float rotFactor = speed * 50f;
                tr.rotation = Quaternion.Slerp(tr.rotation, targetRot, rotFactor * dt);
            }

            if (isMoving)
            {
                movingLogTimer += dt;
                float limit = dash ? 0.4f : 0.6f;

                if (movingLogTimer >= limit)
                {
                    movingLogTimer -= limit;

                    if (ctr1.bodyKey > 25f)
                    {
                        Vector3 cpos = playerCamera.transform.position;
                        Vector3 ppos = tr.position;
                        bool inBox =
                            Mathf.Abs(ppos.x - cpos.x) <= 20f &&
                            Mathf.Abs(ppos.z - cpos.z) <= 20f;

                        if (inBox)
                        {
                            cameraController.TriggerShake(1 + 0.5f * Mathf.Max(0, (currentBodyKey - 25) / 75));
                        }
                    }
                }
            }
            else
            {
                movingLogTimer = 0f;
            }

            if (isMoving)
            {
                moveDecayTimer += dt;
                AddBodyKeyDelta(-0f);
            }
            else
            {
                moveDecayTimer = 0f;
            }

            airVel = Vector3.zero;
            airSpeedFactor = 0f;
        }
        else
        {
            float baseSpeed2 = 0.64f - 0.54f * (1f - (t * t));
            float airBaseSpeed = baseSpeed2 * speedx;

            if (jumpState == JumpState.Rising)
            {
                if (hasInputDir)
                {
                    tr.position += moveDir * airBaseSpeed;
                    tr.position = ClampToBounds(tr.position);

                    Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                    float rotFactor = airBaseSpeed * 10f;
                    tr.rotation = Quaternion.Slerp(tr.rotation, targetRot, rotFactor * dt);

                    airVel = moveDir * (airBaseSpeed * 0.5f);
                }

                moveDecayTimer = 0f;
            }
            else
            {
                if (hasInputDir)
                {
                    Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                    float rotFactor = airBaseSpeed * 10f;
                    tr.rotation = Quaternion.Slerp(tr.rotation, targetRot, rotFactor * dt);
                }

                if (airVel.sqrMagnitude < 1e-10f)
                {
                    Vector3 fwd = tr.forward;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude <= 1e-12f)
                    {
                        Debug.LogError("[PlayerController] tr.forward invalid during air.");
                        enabled = false;
                        return;
                    }
                    fwd.Normalize();
                    airVel = fwd * airBaseSpeed;
                }

                if (hasInputDir)
                {
                    airVel += moveDir * (1.8f * dt);

                    float v = airVel.magnitude;
                    float minV = airBaseSpeed * 0.5f;
                    float maxV = airBaseSpeed * 2.0f;
                    float clampedV = Mathf.Clamp(v, minV, maxV);

                    if (v <= 1e-12f)
                    {
                        Debug.LogError("[PlayerController] airVel magnitude invalid.");
                        enabled = false;
                        return;
                    }

                    airVel = airVel / v * clampedV;
                }

                float speedMulNow = Mathf.Clamp(airVel.magnitude / Mathf.Max(1e-8f, airBaseSpeed), 0.5f, 2.0f);
                float u = Mathf.Clamp01((speedMulNow - 0.5f) / 1.5f);
                airSpeedFactor = Mathf.Clamp(2f * u - 1f, -1f, 1f);

                tr.position += airVel;
                tr.position = ClampToBounds(tr.position);

                moveDecayTimer = 0f;
            }
        }

        if (isAir)
        {
            Vector3 pos = tr.position;

            if (jumpState == JumpState.Rising)
            {
                jumpTimer += dt;
                float upSpeed = 20.0f - (1f - t) * 15.0f;
                pos.y += upSpeed * dt;

                if (jumpTimer >= 1f)
                {
                    jumpState = JumpState.Falling;
                    fallVel = 0f;
                    jumpTimer = 0f;
                }
            }
            else
            {
                var hv = Input.GetKey(KeyCode.Space) ? 1f : 0f;
                float uu = (airSpeedFactor + 1f) * 0.5f;
                float gravityMul = Mathf.Lerp(0.01f, 1f, uu * (1f - (t * t * t)));

                fallVel -= 10f * gravityMul * dt + hv;
                pos.y += fallVel * dt;

                if (pos.y <= 0f)
                {
                    pos.y = 0f;
                    jumpState = JumpState.None;
                    fallVel = 0f;
                    jumpTimer = 0f;

                    airVel = Vector3.zero;
                    airSpeedFactor = 0f;

                    if (!landingShakeFired)
                    {
                        landingShakeFired = true;

                        if (ctr1.bodyKey > 25f)
                        {
                            Vector3 cpos = playerCamera.transform.position;
                            bool inBox =
                                Mathf.Abs(pos.x - cpos.x) <= 20f &&
                                Mathf.Abs(pos.z - cpos.z) <= 20f;

                            if (inBox)
                            {
                                fatigue += currentBodyKey / 100 * 20;
                                ctr1.ApplyEvent("moving_walk2");
                                cameraController.TriggerShake(4 * (1 + Mathf.Max(0, (currentBodyKey - 25) / 75)));
                            }
                        }
                    }
                }
            }

            tr.position = ClampToBounds(pos);
        }

        if (jumpState != JumpState.None)
        {
            ctr1.ApplyEvent(ctr1.bodyKey > 25f ? "moving_fly2" : "moving_fly1");
        }
        else if (moveDir.sqrMagnitude <= 1e-8f)
        {
            ctr1.ApplyEvent(ctr1.bodyKey > 25f ? "moving_idol2" : "moving_idol1");
        }
        else if (dash)
        {
            ctr1.ApplyEvent(ctr1.bodyKey > 25f ? "moving_jogging2" : "moving_jogging1");
        }
        else
        {
            ctr1.ApplyEvent(ctr1.bodyKey > 25f ? "moving_walk2" : "moving_walk1");
        }
    }

    private void AddBodyKeyDelta(float delta)
    {
        float baseValue = bodyKeyAnimating ? bodyKeyTarget : currentBodyKey;
        float to = Mathf.Clamp(baseValue + delta, 0f, 100f);
        SetBodyKey(to, 0.35f);
    }

    private void SetBodyKey(float value, float seconds)
    {
        float to = Mathf.Clamp(value, 0f, 100f);

        bodyKeyStart = currentBodyKey;
        bodyKeyTarget = to;
        bodyKeyElapsed = 0f;
        bodyKeyDuration = Mathf.Max(0f, seconds);

        if (bodyKeyDuration > 1e-6f && Mathf.Abs(bodyKeyStart - bodyKeyTarget) > 1e-6f)
        {
            bodyKeyAnimating = true;
        }
        else
        {
            bodyKeyAnimating = false;

            isWritingCurrentBodyKeyFromAnim = true;
            currentBodyKey = bodyKeyTarget;
            isWritingCurrentBodyKeyFromAnim = false;

            lastObservedCurrentBodyKey = currentBodyKey;
            ApplyBodyKey(currentBodyKey);
        }
    }

    private void AdvanceBodyKeyAnimation(float dt)
    {
        if (!bodyKeyAnimating) return;

        bodyKeyElapsed += dt;
        float u = (bodyKeyDuration <= 1e-6f) ? 1f : Mathf.Clamp01(bodyKeyElapsed / bodyKeyDuration);

        isWritingCurrentBodyKeyFromAnim = true;
        currentBodyKey = Mathf.Lerp(bodyKeyStart, bodyKeyTarget, u);
        isWritingCurrentBodyKeyFromAnim = false;

        lastObservedCurrentBodyKey = currentBodyKey;
        ApplyBodyKey(currentBodyKey);

        if (u >= 1f) bodyKeyAnimating = false;
    }

    private void ApplyBodyKey(float bodyKey)
    {
        float v = Mathf.Clamp(bodyKey, 0f, 100f);
        ctr1.bodyKey = v;
        ctr1.lowKey = v * 0.3f;
        ctr1.bustKey = 20f + 80f * (v / 100f);
    }

    public bool CanTakeDamage => gameplayEnabled && !invincible && !isGameOver && CurrentHP > 0;

    public void ApplyDamage(DamageInfo info)
    {
        if (!CanTakeDamage) return;
        if (info.amount <= 0) { Debug.LogError("[PlayerController] damage <= 0 is invalid."); enabled = false; return; }

        CurrentHP = Mathf.Max(0, CurrentHP - (info.amount / (int)(1 + 0.5 * currentBodyKey / 100)));

        if (CurrentHP <= 0)
        {
            EnterGameOver();
            return;
        }

        controlLockTimer = Mathf.Max(controlLockTimer, hitStunSeconds);

        if (knockbackCo != null) StopCoroutine(knockbackCo);
        knockbackCo = StartCoroutine(CoKnockback(info.attackerWorldPos));

        if (invincibleCo != null) StopCoroutine(invincibleCo);
        invincibleCo = StartCoroutine(CoInvincibleBlink(invincibleSeconds));
    }

    private void EnterGameOver()
    {
        if (isGameOver) return;

        isGameOver = true;

        if (invincibleCo != null) StopCoroutine(invincibleCo);
        if (knockbackCo != null) StopCoroutine(knockbackCo);

        invincibleCo = null;
        knockbackCo = null;
        invincible = false;

        moveDir = Vector3.zero;
        dashRequested = false;
        jumpQueued = false;

        airVel = Vector3.zero;
        airSpeedFactor = 0f;

        forcedMoveState = ForcedMoveState.None;
        forcedMoveTimer = 0f;
        controlLockTimer = 0f;

        SetCharacterVisible(false);
        SetGameplayEnabled(false);

        moveGameSceneController.Instance.OnPlayerGameOver();
    }

    private IEnumerator CoKnockback(Vector3 attackerWorldPos)
    {
        Transform tr = vrmGameObject.transform;

        Vector3 dir = tr.position - attackerWorldPos;
        dir.y = 0f;

        if (dir.sqrMagnitude <= 1e-12f)
        {
            Debug.LogError("[PlayerController] knockback dir is zero (attackerWorldPos == playerPos).");
            yield break;
        }

        Vector3 push = dir.normalized;

        Vector3 start = tr.position;
        Vector3 target = start + push * 20f / Mathf.Max(1, 1 + 19 * (currentBodyKey / 100));

        float dur = Mathf.Max(1e-4f, knockbackDuration);
        float t = 0f;

        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);

            Vector3 p = Vector3.Lerp(start, target, u);

            p.y = tr.position.y;

            p = ClampToBounds(p);
            p.y = tr.position.y;

            tr.position = p;

            yield return null;
        }

        tr.position = ClampToBounds(tr.position);
    }

    private void SetVisualChildrenActive(bool active)
    {
        if (vrmGameObject == null) return;

        Transform parent = vrmGameObject.transform;
        int n = parent.childCount;
        int limit = (n < 2) ? n : 2;

        for (int i = 0; i < limit; i++)
        {
            Transform c = parent.GetChild(i);
            if (c == null) continue;
            c.gameObject.SetActive(active);
        }
    }

    private IEnumerator CoInvincibleBlink(float seconds)
    {
        invincible = true;

        float interval = Mathf.Max(0.02f, blinkInterval);
        float elapsed = 0f;
        bool visible = false;

        SetVisualChildrenActive(false);

        while (elapsed < seconds)
        {
            elapsed += interval;
            visible = !visible;
            SetVisualChildrenActive(visible);
            yield return new WaitForSeconds(interval);
        }

        SetVisualChildrenActive(true);
        invincible = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!initialized) return;
        if (!gameplayEnabled) return;
        if (other == null) return;

        Transform t = other.transform;
        while (t != null && !t.name.StartsWith("Dashboard_")) t = t.parent;

        if (t != null && t.name.StartsWith("Dashboard_"))
        {
            StartForcedZBurst();
            return;
        }

        if (groundStreamer.TryGetFoodDef(other.gameObject, out var info))
        {
            if (playerImage != null)
                playerImage.PlayPickupStretch();

            int heal = Mathf.Max(0, info.healAmount);

            switch (info.attribute)
            {
                case GroundStreamer.FoodAttribute.Thunder:
                    if (CurrentHP < maxHP)
                        CurrentHP = Mathf.Min(maxHP, CurrentHP + heal);

                    if (clickShootFromCenter != null)
                        clickShootFromCenter.enableThunder = true;
                    break;

                case GroundStreamer.FoodAttribute.Ice:
                    if (CurrentHP < maxHP)
                        CurrentHP = Mathf.Min(maxHP, CurrentHP + heal);

                    if (clickShootFromCenter != null)
                        clickShootFromCenter.enableFreezeOnHit = true;
                    break;

                case GroundStreamer.FoodAttribute.Gold:
                    CurrentHP = maxHP * 2;
                    break;

                case GroundStreamer.FoodAttribute.None:
                default:
                    if (CurrentHP < maxHP)
                        CurrentHP = Mathf.Min(maxHP, CurrentHP + heal);
                    break;
            }

            fatigue = 0;
            AddBodyKeyDelta(info.addWeight);

            AudioManager.Instance.PlaySE("tap1");
            AudioManager.Instance.PlaySE("heal");

            groundStreamer.ConsumeFood(other.gameObject);
        }
    }

    public void AddBodyKeyAnimated(float add, float seconds)
    {
        if (!initialized) return;

        float baseValue = bodyKeyAnimating ? bodyKeyTarget : currentBodyKey;
        float to = Mathf.Clamp(baseValue + add, 0f, 100f);
        SetBodyKey(to, seconds);
    }
}