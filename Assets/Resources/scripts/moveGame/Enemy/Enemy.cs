using System;
using UnityEngine;

public class Enemy : MonoBehaviour
{
    [Header("Stats")]
    public int maxHP = 30;

    [Header("FX")]
    public bool playRuntimeHitFx = true;
    public bool playRuntimeDeathFx = true;

    [Header("Control")]
    [SerializeField] private bool isFrozen = false;
    public bool IsFrozen => isFrozen;

    public int CurrentHP { get; private set; }
    public event Action<Enemy> Died;

    void Awake()
    {
        CurrentHP = maxHP;
    }

    // 互換用：呼ばれても何もしない（Destroy-onlyにするためフラグ不要）
    public void MarkAsSpawned()
    {
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = true;

        var rbs = GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rbs.Length; i++) rbs[i].detectCollisions = true;
    }

    public void ResetHP() => CurrentHP = maxHP;

    public void FreezeForSeconds(float seconds)
    {
        if (!isFrozen) AudioManager.Instance.PlaySE("freeze");
        seconds = Mathf.Max(0f, seconds);
        isFrozen = true;
        if (seconds <= 0f) return;
        CancelInvoke(nameof(Unfreeze));
        Invoke(nameof(Unfreeze), seconds);
    }

    private void Unfreeze() => isFrozen = false;

    public void TakeDamage(int amount, Vector3 hitPoint)
    {
        if (amount <= 0) return;
        if (CurrentHP <= 0) return;

        if (playRuntimeHitFx) RuntimeFx.SpawnHitFx(hitPoint);
        AudioManager.Instance.PlaySE("eat_soft");

        CurrentHP = Mathf.Max(0, CurrentHP - amount);

        if (CurrentHP == 0) Die(hitPoint);
    }

    private void Die(Vector3 hitPoint)
    {
        if (playRuntimeDeathFx) RuntimeFx.SpawnDeathFx(hitPoint);
        AudioManager.Instance.PlaySE("exp");

        Died?.Invoke(this);

        // ★Destroy-only：ここ以外の道は無い
        Destroy(gameObject);
    }
}

public static class RuntimeFx
{
    private static ParticleSystem CreateParticleSystemGO(string name, Vector3 position, out GameObject go)
    {
        go = new GameObject(name);
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;

        return ps;
    }

    public static void SpawnHitFx(Vector3 position)
    {
        var ps = CreateParticleSystemGO("EnemyHitFx", position, out var go);

        var main = ps.main;
        main.duration = 0.25f;
        main.startLifetime = 0.20f;
        main.startSpeed = 3.5f;
        main.startSize = 0.11f;
        main.gravityModifier = 0.0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.loop = false;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 18)
        });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.14f;

        ps.Play();
        UnityEngine.Object.Destroy(go, 1.0f);
    }

    public static void SpawnDeathFx(Vector3 position)
    {
        var ps = CreateParticleSystemGO("EnemyDeathFx", position, out var go);

        var main = ps.main;
        main.duration = 0.8f;
        main.startLifetime = 0.6f;
        main.startSpeed = 7.0f;
        main.startSize = 0.20f;
        main.gravityModifier = 0.35f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.loop = false;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 65),
            new ParticleSystem.Burst(0.08f, 35),
            new ParticleSystem.Burst(0.16f, 15),
        });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.22f;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.radial = 1.5f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        var curve = new AnimationCurve();
        curve.AddKey(0f, 1f);
        curve.AddKey(1f, 0f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, curve);

        ps.Play();
        UnityEngine.Object.Destroy(go, 2.2f);
    }
}