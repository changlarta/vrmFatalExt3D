using UnityEngine;

public sealed class SpinYaw : MonoBehaviour
{
    [Tooltip("回転速度（度/秒）")]
    public float degreesPerSecond = 180f;

    private Enemy enemy;

    void Awake()
    {
        // 同一オブジェクトに Enemy がいる前提。子に付く構造なら GetComponentInParent に変更。
        enemy = GetComponent<Enemy>();
        if (enemy == null) enemy = GetComponentInParent<Enemy>();
    }

    void Update()
    {
        if (enemy != null && enemy.IsFrozen) return;

        // Y軸まわりに回転（横にくるくる）
        transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
    }
}