using UnityEngine;

public sealed class WobbleXY_Z : MonoBehaviour
{
    [Header("Rotation (deg)")]
    public float zRange = 8f;

    [Header("Position (units)")]
    public float xyRange = 5f;

    [Header("How fast it wobbles")]
    public float speed = 6f;

    Vector3 _baseLocalPos;
    float _seedX, _seedY, _seedZ;

    void OnEnable()
    {
        _baseLocalPos = transform.localPosition;

        // それぞれ別のノイズにするためのシード
        _seedX = Random.value * 1000f;
        _seedY = Random.value * 1000f;
        _seedZ = Random.value * 1000f;
    }

    void Update()
    {
        float t = Time.time * speed;

        // PerlinNoiseは0..1 → -1..1 に変換
        float nx = Mathf.PerlinNoise(_seedX, t) * 2f - 1f;
        float ny = Mathf.PerlinNoise(_seedY, t) * 2f - 1f;
        float nz = Mathf.PerlinNoise(_seedZ, t) * 2f - 1f;

        // 位置：x,y を -5..5 で揺らす（ローカル）
        transform.localPosition = _baseLocalPos + new Vector3(nx * xyRange, ny * xyRange, 0f);

        // 回転：z を -8..8 度で揺らす（ローカル）
        var e = transform.localEulerAngles;
        e.z = nz * zRange;
        transform.localEulerAngles = e;
    }
}
