using UnityEngine;

/// <summary>
/// 等速直線で飛んで、数秒後に消える弾。
/// さらに DamageDealer を付けてダメージ判定を行う（Trigger想定）。
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class SimpleStraightBullet : MonoBehaviour
{
    Vector3 dir;
    float speed;
    float life;
    float t;

    public void Initialize(Vector3 direction, float bulletSpeed, float lifeSeconds, int damage)
    {
        float sq = direction.sqrMagnitude;
        if (sq < 1e-8f)
        {
            Debug.LogError("SimpleStraightBullet: direction が不正です。");
            enabled = false;
            return;
        }

        dir = direction / Mathf.Sqrt(sq);
        speed = Mathf.Max(0.01f, bulletSpeed);
        life = Mathf.Max(0.01f, lifeSeconds);
        t = 0f;

        var dd = GetComponent<DamageDealer>();
        if (dd == null) dd = gameObject.AddComponent<DamageDealer>();
        dd.damage = Mathf.Max(1, damage);
        dd.useTrigger = true;

        var col = GetComponent<Collider>();
        col.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    void Update()
    {
        if (!enabled) return;

        transform.position += dir * speed * Time.deltaTime;

        t += Time.deltaTime;
        if (t >= life)
        {
            Destroy(gameObject);
        }
    }
}