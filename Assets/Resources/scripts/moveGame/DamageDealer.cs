using UnityEngine;

public sealed class DamageDealer : MonoBehaviour
{
    [Min(0)] public int damage = 10;

    [Tooltip("Trigger で判定するなら true。Collision なら false。")]
    public bool useTrigger = true;

    private void OnTriggerEnter(Collider other)
    {
        if (!useTrigger) return;
        Deal(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (useTrigger) return;
        Deal(collision.collider);
    }

    private void Deal(Collider other)
    {
        if (damage <= 0)
        {
            Debug.LogError("[DamageDealer] damage <= 0 is invalid.");
            enabled = false;
            return;
        }

        var dmg = other.GetComponentInParent<IDamageable>();
        if (dmg == null) return; // 対象外は対象外（黙って無視する以外の選択肢がない）

        if (!dmg.CanTakeDamage) return;

        Vector3 hitPoint = other.ClosestPoint(transform.position);

        dmg.ApplyDamage(new DamageInfo
        {
            amount = damage,
            hitPoint = hitPoint,
            attackerWorldPos = transform.position
        });
    }
}
