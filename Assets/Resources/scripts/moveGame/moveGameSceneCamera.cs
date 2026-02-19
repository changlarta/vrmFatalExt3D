// moveGameSceneCamera.cs
using UnityEngine;

public class moveGameSceneCamera : MonoBehaviour
{
    [SerializeField] private Transform target;

    // target.xyz + (0, 1.5, -1.5) に居続ける（ワールドオフセット）
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.5f, -1.5f);

    // ※Inspector再設定回避のため残す（未使用）
    [SerializeField] private float zLerpSpeed = 1.5f;

    // ※Inspector再設定回避のため残す（未使用）
    [SerializeField] private float movingEpsilon = 0.0001f;

    private Vector3 _prevTargetPos;

    // ====== 追加：上下揺れ用（既存パラメータ名は変更しない） ======
    [Header("Shake (Vertical Only)")]
    [SerializeField] private float shakeDuration = 0.15f;   // 揺れる時間(秒)
    [SerializeField] private float shakeAmplitude = 0.05f;  // 上下揺れ幅(メートル)
    [SerializeField] private float shakeFrequency = 25f;    // 揺れの速さ

    private float _shakeTimeLeft = 0f;
    private float _shakePhase = 0f;
    // ===============================================================

    // ====== 追加：高さに応じた距離/角度制御 ======
    [Header("Height -> Distance & Pitch")]
    [SerializeField] private float heightAtMaxEffect = 50f;     // このYで効果最大（要件: 50）
    [SerializeField] private float extraBackAtMax = 3.0f;       // Y=50 のとき、Zをどれだけ追加で後ろへ（-方向）
    [SerializeField] private float basePitchDeg = 15f;          // 現状ピッチ
    [SerializeField] private float maxPitchDeg = 20f;           // Y=50 の最大ピッチ
    // ===============================================================

    public void SetTarget(GameObject go)
    {
        target = (go != null) ? go.transform : null;
        if (target != null) _prevTargetPos = target.position;
    }

    private void Start()
    {
        if (target != null) _prevTargetPos = target.position;
    }

    // 外部（moveGameSceneController 等）から呼ぶ
    public void TriggerShake()
    {
        _shakeTimeLeft = shakeDuration;
        // 位相は継続でOK。毎回変えたいなら _shakePhase += Random.value * 10f; など。
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // ベース位置：target.xyz + offset（ワールド）
        Vector3 basePos = target.position + worldOffset;

        // -----------------------------------------------------------
        // 高さに応じて「後ろに離す」と「ピッチ角を増やす」
        // ここで使うYは “このフレームで置きたいカメラのY” を基準にする
        // -----------------------------------------------------------
        float denom = Mathf.Max(1e-6f, heightAtMaxEffect);
        float t = Mathf.Clamp01(basePos.y / denom); // Y=0 => 0, Y=50 => 1

        // 後ろに離す（Zをさらにマイナスへ）
        Vector3 dynamicOffset = new Vector3(0f, extraBackAtMax * t, -extraBackAtMax * t);

        // ピッチ角（下向き）を 15 -> 25 に（Y=50で最大）
        float pitch = Mathf.Lerp(basePitchDeg, maxPitchDeg, t);

        // -----------------------------------------------------------
        // 上下揺れのみ（Y方向だけ）
        // -----------------------------------------------------------
        float shakeY = 0f;
        if (_shakeTimeLeft > 0f)
        {
            _shakeTimeLeft -= Time.deltaTime;
            _shakePhase += Time.deltaTime * shakeFrequency;

            // PerlinNoiseで滑らかに（-0.5〜0.5）
            float n = Mathf.PerlinNoise(_shakePhase, 0.123f) - 0.5f;

            // 終了に向けて減衰（自然に止まる）
            float tt = Mathf.Clamp01(_shakeTimeLeft / shakeDuration);
            shakeY = n * shakeAmplitude * tt;
        }

        // 最終位置
        transform.position = basePos + dynamicOffset + new Vector3(0f, shakeY, 0f);

        // 回転：Yaw/Rollは固定。Pitchのみ高さで変える
        transform.rotation = Quaternion.Euler(pitch, 0f, 0f);

        _prevTargetPos = target.position;
    }
}
