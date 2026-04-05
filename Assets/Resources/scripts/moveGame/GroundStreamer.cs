using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class GroundStreamer : MonoBehaviour
{
    public const float LANE_WIDTH = 20f;
    public const float EPS = 1e-4f;

    public static readonly Vector3 HIDDEN_POS = new Vector3(-100f, 100f, 0f);

    public int DIFFICULTY_MAX_TILE = 1000;

    [Header("Required")]
    public Material groundMaterial;

    [Header("Building materials (choose 1 of 3 per cube)")]
    public Material buildingMaterial1;
    public Material buildingMaterial2;
    public Material buildingMaterial3;

    [Header("Tile settings")]
    public float tileLength = 20f;
    public int tilesAhead = 8;
    public int tilesBehind = 2;

    [Header("Side buildings (generated cubes)")]
    public bool buildingsEnabled = true;
    public float buildingWidth = 10f;
    public float buildingMinHeight = 3f;
    public float buildingMaxHeight = 20f;
    public float segmentMinLength = 120f;
    public float segmentMaxLength = 120f;

    [Header("Enemy prefabs (Phase 1 / Phase 2)")]
    [SerializeField] private List<GameObject> enemyPrefabsPhase1 = new List<GameObject>();
    [SerializeField] private List<GameObject> enemyPrefabsPhase2 = new List<GameObject>();

    [Header("Enemy placement")]
    [Min(0f)] public float enemyXMargin = 0.5f;
    [SerializeField] private int enemyLayer = 11;

    [Header("Difficulty start tile offset (UI + difficulty)")]
    public int startTileIndexPublic = 0;

    [Header("Blockade (every N tiles)")]
    [Min(1)] public int blockadeEveryTiles = 100;

    [Header("Clear")]
    [Min(1)] public int clearTileIndex = 1000;

    [Header("Boss prefabs (Phase 1 / Phase 2)")]
    public List<GameObject> bossPrefabsPhase1 = new List<GameObject>();
    public List<GameObject> bossPrefabsPhase2 = new List<GameObject>();

    [Header("Blockade Wall (single thin quad)")]
    public Material blockadeWallMaterial;
    [Min(0.1f)] public float blockadeWallHeight = 8f;
    public float blockadeWallY = 0f;
    public float blockadeWallZOffset = -0.05f;

    private bool blockadeActive = false;
    private bool blockadeSpawned = false;
    private int nextBlockadeIndex = 100;
    private GameObject currentBoss = null;

    private bool blockadeMinFrozen = false;
    private int frozenMinKeepIdx = 0;

    private GameObject blockadeWallGO = null;

    private Vector3 lastBossPos = Vector3.zero;
    private bool isReloadingWorld = false;
    private bool suppressEnemySpawns = false;
    private bool titlePresentationMode = false;

    [Header("Heavy Enemy (Phase 2 only, template is loaded once, then duplicated)")]
    [SerializeField] private GameObject heavyEnemyPrefab;

    [Header("Heavy spawn key random (10..90)")]
    [Min(0f)] public float heavySpawnKeyMin = 10f;
    [Min(0f)] public float heavySpawnKeyMax = 90f;

    [HideInInspector]
    public GameObject heavyTemplate;
    private HeavyEnemyController heavyTemplateCtrl;
    private bool heavyTemplateLoaded = false;

    private readonly List<GameObject> aliveHeavies = new List<GameObject>();

    public HeavyEnemyController HeavyTemplateController => heavyTemplateCtrl;

    public enum FoodAttribute
    {
        None,
        Thunder,
        Ice,
        Gold
    }

    [Serializable]
    public struct FoodDef
    {
        public Sprite sprite;
        public int healAmount;
        public float addWeight;
        public float foodScale;
        public FoodAttribute attribute;
    }

    [Header("Food (spawned billboards)")]
    public List<FoodDef> foods = new List<FoodDef>();

    [Header("Rare Food (2% spawn chance)")]
    public List<FoodDef> rareFoods = new List<FoodDef>();

    public float foodY = 0.8f;
    public int foodLayer = 0;

    [SerializeField] private DashboardSpawner dashboardSpawner;

    private Transform followTarget;
    private readonly Dictionary<int, GameObject> tiles = new Dictionary<int, GameObject>();

    private float furthestZ = float.NegativeInfinity;
    private int lastBuiltFurthestIndex = int.MinValue;

    private float groundMinZ = 0f;
    private float groundMaxZ = 0f;

    public float GetGroundMinZ() => groundMinZ;
    public float GetGroundMaxZ() => groundMaxZ;

    private float enemyDespawnZLine = float.NegativeInfinity;
    public float GetEnemyDespawnZLine() => enemyDespawnZLine;

    private int totalCreatedTiles = 0;
    private float timerAccumulator = 0f;

    private readonly List<GameObject> aliveEnemies = new List<GameObject>();
    public List<GameObject> GetAliveEnemies()
    {
        for (int i = aliveEnemies.Count - 1; i >= 0; i--)
        {
            var e = aliveEnemies[i];
            if (e == null || !e.activeInHierarchy) aliveEnemies.RemoveAt(i);
        }
        return aliveEnemies;
    }

    private readonly List<GameObject> aliveFoods = new List<GameObject>();
    public List<GameObject> GetAliveFoods()
    {
        for (int i = aliveFoods.Count - 1; i >= 0; i--)
            if (aliveFoods[i] == null) aliveFoods.RemoveAt(i);
        return aliveFoods;
    }

    private readonly Dictionary<int, FoodDef> foodDefByInstanceId = new Dictionary<int, FoodDef>();

    public void SetTitlePresentationMode(bool value)
    {
        titlePresentationMode = value;
    }

    public bool TryGetFoodDef(GameObject foodGO, out FoodDef def)
    {
        def = default;
        return foodDefByInstanceId.TryGetValue(foodGO.GetInstanceID(), out def);
    }

    public void ConsumeFood(GameObject foodGO)
    {
        foodDefByInstanceId.Remove(foodGO.GetInstanceID());

        for (int i = aliveFoods.Count - 1; i >= 0; i--)
            if (aliveFoods[i] == null || aliveFoods[i] == foodGO) aliveFoods.RemoveAt(i);

        Destroy(foodGO);
    }

    public int GetCurrentTileIndex()
    {
        if (titlePresentationMode) return 0;
        return Mathf.FloorToInt(followTarget.position.z / tileLength);
    }

    public int GetLogicalTileIndex()
    {
        if (titlePresentationMode) return 0;

        long logical = (long)GetCurrentTileIndex() + (long)startTileIndexPublic;
        if (logical < 0) logical = 0;
        if (logical > int.MaxValue) logical = int.MaxValue;
        return (int)logical;
    }

    public int GetClearTileIndex()
    {
        return Mathf.Max(1, clearTileIndex);
    }

    public int GetLastPlayableLogicalTileIndex()
    {
        return GetClearTileIndex() - 1;
    }

    private int GetLastPlayablePhysicalTileIndex()
    {
        return GetLastPlayableLogicalTileIndex() - startTileIndexPublic;
    }

    public int GetContinueStartTileIndex()
    {
        int start = GetLogicalTileIndex() - 100;
        if (start < 0) start = 0;
        start = (start / 50) * 50;
        return start;
    }

    public void PrepareStartFromTitle()
    {
        titlePresentationMode = false;
        startTileIndexPublic = 0;
    }

    public void PrepareContinueFromCurrentPosition()
    {
        int continueStart = GetContinueStartTileIndex();
        titlePresentationMode = false;
        startTileIndexPublic = continueStart;
    }

    public void SetStartTileIndex(int logicalTileIndex)
    {
        startTileIndexPublic = Mathf.Max(0, logicalTileIndex);
    }

    private int GetLogicalTileIndexForPhysicalTile(int physicalTileIndex)
    {
        if (titlePresentationMode) return 0;

        long logical = (long)physicalTileIndex + (long)startTileIndexPublic;
        if (logical < 0) logical = 0;
        if (logical > int.MaxValue) logical = int.MaxValue;
        return (int)logical;
    }

    private float GetDifficultyT()
    {
        if (titlePresentationMode) return 0f;
        return Mathf.Clamp01(GetLogicalTileIndex() / (float)DIFFICULTY_MAX_TILE);
    }

    private static int SampleStochasticCount(float expected)
    {
        int baseCount = Mathf.FloorToInt(expected);
        float frac = expected - baseCount;
        return baseCount + ((frac > 0f && UnityEngine.Random.value < frac) ? 1 : 0);
    }

    private int GetAliveNormalEnemyCountClean()
    {
        GetAliveEnemies();
        return aliveEnemies.Count;
    }

    private bool IsEnemyPhase1ByLogicalTile(int logicalTile) => logicalTile <= 100;
    private bool IsBossPhase1ByLogicalTile(int logicalTile) => logicalTile <= 250;
    private bool IsEnemyPhase3ByLogicalTile(int logicalTile) => logicalTile <= 500;

    private List<GameObject> GetEnemyListByPhase()
    {
        return IsEnemyPhase1ByLogicalTile(GetLogicalTileIndex()) ? enemyPrefabsPhase1 : enemyPrefabsPhase2;
    }

    private void MarkAsRuntimeSpawnedEnemy(GameObject go)
    {
        if (go == null) return;
        if (go == heavyTemplate) return;

        if (go.GetComponent<GroundStreamerSpawnedEnemyMarker>() == null)
            go.AddComponent<GroundStreamerSpawnedEnemyMarker>();
    }

    public void SetFollowTarget(Transform t)
    {
        followTarget = t;

        furthestZ = float.NegativeInfinity;
        lastBuiltFurthestIndex = int.MinValue;
        enemyDespawnZLine = float.NegativeInfinity;

        totalCreatedTiles = 0;
        timerAccumulator = 0f;

        aliveEnemies.Clear();
        aliveFoods.Clear();
        foodDefByInstanceId.Clear();

        blockadeActive = false;
        blockadeSpawned = false;
        currentBoss = null;
        nextBlockadeIndex = Mathf.Max(1, blockadeEveryTiles);

        blockadeMinFrozen = false;
        frozenMinKeepIdx = 0;

        lastBossPos = Vector3.zero;
        suppressEnemySpawns = false;

        DestroyBlockadeWall();
    }

    void Start()
    {
        nextBlockadeIndex = blockadeEveryTiles;
    }

    void Update()
    {
        Tick(force: false);

        if (followTarget == null) return;
        if (titlePresentationMode) return;

        if (suppressEnemySpawns)
        {
            suppressEnemySpawns = false;
            return;
        }

        timerAccumulator += Time.deltaTime;
        while (true)
        {
            float t = GetDifficultyT();
            float interval = Mathf.Lerp(15f, 2f, t);
            if (interval < 0.01f) interval = 0.01f;

            if (timerAccumulator < interval) break;
            timerAccumulator -= interval;

            SpawnEnemiesAtFront(SampleStochasticCount(Mathf.Lerp(1f, 3f, t)));
        }

        if (aliveFoods.Count > 0)
        {
            float time = Time.time;
            for (int i = aliveFoods.Count - 1; i >= 0; i--)
            {
                GameObject f = aliveFoods[i];
                if (f == null) { aliveFoods.RemoveAt(i); continue; }

                float phase = (f.GetInstanceID() & 1023) * 0.01f;

                Vector3 p = f.transform.position;
                p.y = foodY + Mathf.Sin(time * 2f + phase) * 0.25f;
                f.transform.position = p;

                f.transform.Rotate(0f, 90 * Time.deltaTime, 0f, Space.World);
            }
        }
    }

    public void RebuildImmediate()
    {
        isReloadingWorld = true;
        suppressEnemySpawns = true;

        DestroyAllRuntimeEnemiesAndBoss();
        DestroyAllRuntimeFoods();

        foreach (var kv in tiles)
            if (kv.Value != null) Destroy(kv.Value);
        tiles.Clear();

        furthestZ = float.NegativeInfinity;
        lastBuiltFurthestIndex = int.MinValue;

        totalCreatedTiles = 0;
        enemyDespawnZLine = float.NegativeInfinity;
        timerAccumulator = 0f;

        aliveEnemies.Clear();
        aliveFoods.Clear();
        foodDefByInstanceId.Clear();

        blockadeActive = false;
        blockadeSpawned = false;
        currentBoss = null;
        nextBlockadeIndex = Mathf.Max(1, blockadeEveryTiles);

        blockadeMinFrozen = false;
        frozenMinKeepIdx = 0;

        lastBossPos = Vector3.zero;

        DestroyBlockadeWall();

        Tick(force: true);

        isReloadingWorld = false;
    }

    public void ReloadRebuildWorld()
    {
        RebuildImmediate();
    }

    public void Tick() => Tick(force: false);

    private bool CanSpawnBlockadeAtPhysicalTile(int physicalTileIndex)
    {
        return GetLogicalTileIndexForPhysicalTile(physicalTileIndex) < GetClearTileIndex();
    }

    private void Tick(bool force)
    {
        if (followTarget == null) return;

        if (titlePresentationMode)
        {
            TickTitlePresentation(force);
            return;
        }

        if (followTarget.position.z > furthestZ) furthestZ = followTarget.position.z;
        int furthestIdxRaw = Mathf.FloorToInt(furthestZ / tileLength);

        if (currentBoss != null)
            lastBossPos = currentBoss.transform.position;

        if (!isReloadingWorld && blockadeActive && blockadeSpawned && currentBoss == null)
        {
            if (foods != null && foods.Count > 0)
                SpawnFoodDropAt(lastBossPos, foods[0]);

            blockadeActive = false;
            blockadeSpawned = false;
            nextBlockadeIndex += Mathf.Max(1, blockadeEveryTiles);

            blockadeMinFrozen = false;
            frozenMinKeepIdx = 0;

            DestroyBlockadeWall();
        }

        if (!blockadeActive)
        {
            int wouldNeedMaxIdx = furthestIdxRaw + tilesAhead;
            if (wouldNeedMaxIdx >= nextBlockadeIndex && CanSpawnBlockadeAtPhysicalTile(nextBlockadeIndex))
            {
                blockadeActive = true;
                blockadeSpawned = false;

                blockadeMinFrozen = true;
                frozenMinKeepIdx = furthestIdxRaw - tilesBehind;

                EnsureBlockadeWall(nextBlockadeIndex);
            }
        }

        int furthestIdx = blockadeActive ? Mathf.Min(furthestIdxRaw, nextBlockadeIndex) : furthestIdxRaw;

        if (!force && furthestIdx == lastBuiltFurthestIndex) return;
        lastBuiltFurthestIndex = furthestIdx;

        int minKeepIdx = furthestIdx - tilesBehind;
        int maxKeepIdx = furthestIdx + tilesAhead;

        if (blockadeActive)
            maxKeepIdx = Mathf.Min(maxKeepIdx, nextBlockadeIndex);

        maxKeepIdx = Mathf.Min(maxKeepIdx, GetLastPlayablePhysicalTileIndex());

        if (blockadeActive && blockadeMinFrozen)
            minKeepIdx = Mathf.Min(minKeepIdx, frozenMinKeepIdx);

        groundMinZ = minKeepIdx * tileLength;
        groundMaxZ = (maxKeepIdx + 1) * tileLength;

        enemyDespawnZLine = groundMinZ;

        for (int i = minKeepIdx; i <= maxKeepIdx; i++)
        {
            if (!tiles.ContainsKey(i))
            {
                GameObject root = CreateTileRoot(i);
                tiles.Add(i, root);
                totalCreatedTiles++;

                if (dashboardSpawner != null)
                    dashboardSpawner.OnTileCreated(i, root.transform, LANE_WIDTH, tileLength, EPS);

                float t = GetDifficultyT();
                int logicalTile = GetLogicalTileIndexForPhysicalTile(i);
                if (logicalTile >= GetClearTileIndex())
                    continue;

                float tEvery = Mathf.Clamp01(t / 0.8f);
                int every = Mathf.Max(1, Mathf.FloorToInt(Mathf.Lerp(15f, 1f, tEvery)));

                if (!suppressEnemySpawns)
                {
                    if (logicalTile > 0 && (logicalTile % every) == 0)
                        SpawnEnemiesForTileIndex(i, SampleStochasticCount(Mathf.Lerp(1f, 4f, t)));
                }

                if (foods != null && foods.Count > 0)
                    SpawnFoodForTileIndex(i);
            }
        }

        if (!suppressEnemySpawns && blockadeActive && !blockadeSpawned)
        {
            SpawnBlockadeEncounter(nextBlockadeIndex);
            blockadeSpawned = true;
        }

        if (!blockadeActive && tiles.Count > 0)
        {
            var keys = new List<int>(tiles.Keys);
            for (int k = 0; k < keys.Count; k++)
            {
                int i = keys[k];
                if (i < minKeepIdx)
                {
                    GameObject tile = tiles[i];
                    tiles.Remove(i);
                    if (tile != null) Destroy(tile);
                }
            }
        }
    }

    private void TickTitlePresentation(bool force)
    {
        if (followTarget.position.z > furthestZ) furthestZ = followTarget.position.z;
        int furthestIdx = Mathf.FloorToInt(furthestZ / tileLength);

        if (!force && furthestIdx == lastBuiltFurthestIndex) return;
        lastBuiltFurthestIndex = furthestIdx;

        int minKeepIdx = furthestIdx - tilesBehind;
        int maxKeepIdx = furthestIdx + tilesAhead;

        groundMinZ = minKeepIdx * tileLength;
        groundMaxZ = (maxKeepIdx + 1) * tileLength;
        enemyDespawnZLine = groundMinZ;

        for (int i = minKeepIdx; i <= maxKeepIdx; i++)
        {
            if (!tiles.ContainsKey(i))
            {
                GameObject root = CreateTileRoot(i);
                tiles.Add(i, root);
                totalCreatedTiles++;
            }
        }

        if (tiles.Count > 0)
        {
            var keys = new List<int>(tiles.Keys);
            for (int k = 0; k < keys.Count; k++)
            {
                int i = keys[k];
                if (i < minKeepIdx)
                {
                    GameObject tile = tiles[i];
                    tiles.Remove(i);
                    if (tile != null) Destroy(tile);
                }
            }
        }
    }

    public bool EnsureHeavyTemplateShell()
    {
        if (heavyEnemyPrefab == null) return false;
        if (heavyTemplateLoaded) return true;
        if (heavyTemplate != null && heavyTemplateCtrl != null) return true;

        heavyTemplate = Instantiate(heavyEnemyPrefab, HIDDEN_POS, Quaternion.identity);
        heavyTemplate.name = $"HeavyEnemy_TEMPLATE_{Time.frameCount}";

        heavyTemplateCtrl = heavyTemplate.GetComponent<HeavyEnemyController>();
        if (heavyTemplateCtrl == null)
        {
            Debug.LogError("[GroundStreamer] HeavyEnemyController missing on heavyTemplate.");
            return false;
        }

        return true;
    }

    public void CompleteHeavyTemplateLoad()
    {
        if (heavyTemplate == null || heavyTemplateCtrl == null) return;

        var e = heavyTemplate.GetComponent<Enemy>();
        if (e != null) e.ResetHP();

        heavyTemplateLoaded = true;
    }

    private float SampleHeavySpawnKey()
    {
        float min = Mathf.Clamp(heavySpawnKeyMin, 0f, 100f);
        float max = Mathf.Clamp(heavySpawnKeyMax, 0f, 100f);
        if (max < min) { float tmp = min; min = max; max = tmp; }

        float v = UnityEngine.Random.Range(min, max);
        return Mathf.Round(Mathf.Clamp(v, 0f, 100f));
    }

    private void SpawnHeavyInRange(float zMin, float zMax, string namePrefix, int index)
    {
        if (!heavyTemplateLoaded) return;

        float half = LANE_WIDTH * 0.5f;
        float x = UnityEngine.Random.Range(-half + enemyXMargin, +half - enemyXMargin);
        float z = UnityEngine.Random.Range(zMin, zMax);

        var go = Instantiate(heavyTemplate, new Vector3(x, 0f, z), Quaternion.identity);
        go.name = $"{namePrefix}_Heavy_{index}";

        MarkAsRuntimeSpawnedEnemy(go);
        aliveHeavies.Add(go);

        var inst = moveGameSceneController.Instance;
        var p = inst.player;

        var ctrl = go.GetComponent<HeavyEnemyController>();
        ctrl.Bind(p, this);
        ctrl.SetBodyKeyImmediate(SampleHeavySpawnKey());

        var e = go.GetComponent<Enemy>();
        if (e != null) e.ResetHP();

        var d = go.GetComponent<EnemyStreamDespawn>();
        if (d == null) d = go.AddComponent<EnemyStreamDespawn>();
        d.Initialize(this);
    }

    private void EnsureBlockadeWall(int blockadeTileIndex)
    {
        if (blockadeWallMaterial == null) return;
        if (blockadeWallGO != null) return;

        blockadeWallGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        blockadeWallGO.name = $"BlockadeWall_{blockadeTileIndex}";

        var col = blockadeWallGO.GetComponent<Collider>();
        if (col != null) Destroy(col);

        blockadeWallGO.transform.position = new Vector3(
            0f,
            blockadeWallY + blockadeWallHeight * 0.5f,
            (blockadeTileIndex + 1) * tileLength + blockadeWallZOffset
        );

        blockadeWallGO.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        blockadeWallGO.transform.localScale = new Vector3(LANE_WIDTH * 2f, blockadeWallHeight, 1f);

        var mr = blockadeWallGO.GetComponent<MeshRenderer>();
        mr.sharedMaterial = blockadeWallMaterial;
    }

    private void DestroyBlockadeWall()
    {
        if (blockadeWallGO != null)
        {
            Destroy(blockadeWallGO);
            blockadeWallGO = null;
        }
    }

    private void SpawnBlockadeEncounter(int blockadeTileIndex)
    {
        float zMin = blockadeTileIndex * tileLength + EPS;
        float zMax = (blockadeTileIndex + 1) * tileLength - EPS;

        SpawnNormalEnemiesInRange(
            Mathf.FloorToInt(Mathf.Lerp(1f, 30f, GetDifficultyT())),
            zMin,
            zMax,
            $"Enemy_BossAdd_{blockadeTileIndex}"
        );

        int logicalTileIndex = GetLogicalTileIndexForPhysicalTile(blockadeTileIndex);

        List<GameObject> bossList = IsBossPhase1ByLogicalTile(logicalTileIndex)
            ? bossPrefabsPhase1
            : bossPrefabsPhase2;

        GameObject bossPrefab = bossList[UnityEngine.Random.Range(0, bossList.Count)];

        currentBoss = Instantiate(
            bossPrefab,
            new Vector3(0f, 0f, Mathf.Lerp(zMin, zMax, 0.5f)),
            Quaternion.identity
        );

        MarkAsRuntimeSpawnedEnemy(currentBoss);

        var bossEnemy = currentBoss.GetComponent<Enemy>();
        if (bossEnemy != null) bossEnemy.MarkAsSpawned();

        currentBoss.name = $"Boss_{blockadeTileIndex}_{Time.frameCount}";

        if (VrmChrSceneSpeechDirector.Instance != null)
        {
            VrmChrSceneSpeechDirector.Instance.setBossSpeech(logicalTileIndex);
        }
    }

    private void RegisterEnemyIfNeeded(GameObject e)
    {
        if (e == null) return;
        if (e == heavyTemplate) return;

        if (!aliveEnemies.Contains(e))
            aliveEnemies.Add(e);
    }

    private void SpawnEnemiesAtFront(int requestedCount)
    {
        if (requestedCount <= 0) return;

        float zMax = groundMaxZ;
        float zMin = Mathf.Max(groundMinZ, zMax - tileLength);

        if (blockadeActive)
        {
            float wallZ = (nextBlockadeIndex + 1) * tileLength + blockadeWallZOffset;
            zMax = Mathf.Min(zMax, wallZ - EPS);
            zMin = Mathf.Min(zMin, zMax - EPS);
        }

        if (zMax <= zMin + EPS) return;

        SpawnNormalEnemiesInRange(requestedCount, zMin + EPS, zMax - EPS, $"Enemy_Front_{Time.frameCount}");
    }

    private void SpawnEnemiesForTileIndex(int tileIndex, int requestedCount)
    {
        if (requestedCount <= 0) return;

        SpawnNormalEnemiesInRange(
            requestedCount,
            tileIndex * tileLength + EPS,
            (tileIndex + 1) * tileLength - EPS,
            $"Enemy_{tileIndex}"
        );
    }

    private void SpawnNormalEnemiesInRange(int requestedCount, float zMin, float zMax, string namePrefix)
    {
        if (requestedCount <= 0) return;
        if (zMax <= zMin + EPS) return;

        int logical = GetLogicalTileIndex();
        int alive = GetAliveNormalEnemyCountClean();

        int cap = Mathf.RoundToInt(Mathf.Lerp(10f, 100f, Mathf.Clamp01(logical / (float)DIFFICULTY_MAX_TILE)));
        int canSpawn = Mathf.Max(0, cap - alive);
        int spawnCount = Mathf.Min(requestedCount, canSpawn);
        if (spawnCount <= 0) return;

        List<GameObject> list = GetEnemyListByPhase();

        bool phase2 = !IsEnemyPhase1ByLogicalTile(GetLogicalTileIndex());
        bool phase3 = !IsEnemyPhase3ByLogicalTile(GetLogicalTileIndex());
        int listCount = list.Count;

        float heavyChance = phase3 ? (1f / (10f * (listCount + 1f))) : 0f;

        for (int i = 0; i < spawnCount; i++)
        {
            if (phase2 && phase3 && UnityEngine.Random.value < heavyChance)
            {
                SpawnHeavyInRange(zMin, zMax, namePrefix, i);
                continue;
            }

            int r = UnityEngine.Random.Range(0, listCount);
            GameObject prefab = list[r];

            GameObject e = Instantiate(
                prefab,
                new Vector3(
                    UnityEngine.Random.Range(-LANE_WIDTH * 0.5f + enemyXMargin, +LANE_WIDTH * 0.5f - enemyXMargin),
                    0f,
                    UnityEngine.Random.Range(zMin, zMax)
                ),
                Quaternion.identity
            );

            MarkAsRuntimeSpawnedEnemy(e);

            var enemy = e.GetComponent<Enemy>();
            if (enemy != null) enemy.MarkAsSpawned();

            e.name = $"{namePrefix}_{i}";

            var d = e.GetComponent<EnemyStreamDespawn>();
            if (d == null) d = e.AddComponent<EnemyStreamDespawn>();
            d.Initialize(this);

            RegisterEnemyIfNeeded(e);
        }
    }

    private void SpawnFoodDropAt(Vector3 worldPos, FoodDef def)
    {
        GameObject go = new GameObject($"Food_BossDrop_{Time.frameCount}", typeof(SpriteRenderer));
        float s = Mathf.Max(0.01f, def.foodScale);

        go.transform.position = new Vector3(worldPos.x, foodY, worldPos.z);
        go.transform.localScale = Vector3.one * s;

        if (foodLayer >= 0 && foodLayer <= 31) go.layer = foodLayer;

        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = def.sprite;
        sr.sortingOrder = 1000;

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 50f * s;

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        aliveFoods.Add(go);
        foodDefByInstanceId[go.GetInstanceID()] = def;
    }

    private FoodDef ChooseFoodDefForSpawn()
    {
        bool useRare = rareFoods != null
            && rareFoods.Count > 0
            && UnityEngine.Random.value < 0.01f;

        if (useRare)
        {
            return rareFoods[UnityEngine.Random.Range(0, rareFoods.Count)];
        }

        int pickMin = (foods.Count >= 2) ? 1 : 0;
        return foods[UnityEngine.Random.Range(pickMin, foods.Count)];
    }

    private void SpawnFoodForTileIndex(int tileIndex)
    {
        int count = UnityEngine.Random.Range(0, 4);

        for (int c = 0; c < count; c++)
        {
            FoodDef def = ChooseFoodDefForSpawn();

            GameObject go = new GameObject($"Food_{tileIndex}_{Time.frameCount}_{c}", typeof(SpriteRenderer));
            float s = Mathf.Max(0.01f, def.foodScale);

            go.transform.position = new Vector3(
                UnityEngine.Random.Range(-LANE_WIDTH * 0.5f + 0.6f, +LANE_WIDTH * 0.5f - 0.6f),
                foodY,
                UnityEngine.Random.Range(tileIndex * tileLength + EPS, (tileIndex + 1) * tileLength - EPS)
            );

            go.transform.localScale = Vector3.one * s;

            if (foodLayer >= 0 && foodLayer <= 31) go.layer = foodLayer;

            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = def.sprite;
            sr.sortingOrder = 1000;

            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 50f * s;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            if (tiles.TryGetValue(tileIndex, out var root) && root != null)
                go.transform.SetParent(root.transform, true);

            aliveFoods.Add(go);
            foodDefByInstanceId[go.GetInstanceID()] = def;
        }
    }

    private GameObject CreateTileRoot(int index)
    {
        GameObject root = new GameObject($"GroundTile_{index}");
        root.transform.position = new Vector3(0f, 0f, index * tileLength);

        {
            var groundGO = new GameObject("Ground");
            groundGO.transform.SetParent(root.transform, false);

            var mf = groundGO.AddComponent<MeshFilter>();
            var mr = groundGO.AddComponent<MeshRenderer>();

            mr.sharedMaterial = groundMaterial;
            mf.sharedMesh = BuildGroundMesh(LANE_WIDTH, tileLength);
        }

        if (buildingsEnabled)
        {
            var buildingsGO = new GameObject("Buildings");
            buildingsGO.transform.SetParent(root.transform, false);

            CreateBuildingsForSide(buildingsGO.transform, index, -1f);
            CreateBuildingsForSide(buildingsGO.transform, index, +1f);
        }

        return root;
    }

    private void CreateBuildingsForSide(Transform parent, int tileIndex, float sideSign)
    {
        float cx = sideSign * (LANE_WIDTH * 0.5f) + sideSign * (buildingWidth * 0.5f);

        float z0 = 0f;
        int segId = 0;

        while (z0 < tileLength - EPS)
        {
            float remaining = tileLength - z0;

            float segLen = (remaining <= segmentMaxLength + EPS)
                ? remaining
                : Mathf.Min(
                    Mathf.Clamp(UnityEngine.Random.Range(segmentMinLength, segmentMaxLength), segmentMinLength, segmentMaxLength),
                    remaining
                );

            float h = UnityEngine.Random.Range(buildingMinHeight, buildingMaxHeight);

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"B_{tileIndex}_{(sideSign < 0f ? "L" : "R")}_{segId}";
            cube.transform.SetParent(parent, false);
            cube.transform.localScale = new Vector3(buildingWidth, h, segLen);
            cube.transform.localPosition = new Vector3(cx, h * 0.5f, z0 + segLen * 0.5f);

            var mr = cube.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = ChooseBuildingMaterial();

            z0 += segLen;
            segId++;
        }
    }

    private Material ChooseBuildingMaterial()
    {
        int r = UnityEngine.Random.Range(0, 3);
        if (r == 0) return buildingMaterial1;
        if (r == 1) return buildingMaterial2;
        return buildingMaterial3;
    }

    private static Mesh BuildGroundMesh(float width, float length)
    {
        float hw = width * 0.5f;

        Vector3[] v = new Vector3[4]
        {
            new Vector3(-hw, 0f, 0f),
            new Vector3( hw, 0f, 0f),
            new Vector3(-hw, 0f, length),
            new Vector3( hw, 0f, length),
        };

        int[] t = new int[6] { 0, 2, 1, 2, 3, 1 };
        Vector3[] n = new Vector3[4] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };

        Vector2[] uv = new Vector2[4]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
        };

        Mesh m = new Mesh();
        m.name = "GroundTileMesh";
        m.vertices = v;
        m.triangles = t;
        m.normals = n;
        m.uv = uv;
        m.RecalculateBounds();
        return m;
    }

    public int GetTotalCreatedTiles()
    {
        return totalCreatedTiles - 3;
    }

    private void DestroyAllRuntimeEnemiesAndBoss()
    {
        var spawned = UnityEngine.Object.FindObjectsByType<GroundStreamerSpawnedEnemyMarker>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        for (int i = 0; i < spawned.Length; i++)
        {
            var marker = spawned[i];
            if (marker == null) continue;

            GameObject go = marker.gameObject;
            if (go == null) continue;
            if (go == heavyTemplate) continue;

            if (go.activeSelf) go.SetActive(false);
            Destroy(go);
        }

        aliveEnemies.Clear();
        aliveHeavies.Clear();
        currentBoss = null;
    }

    private void DestroyAllRuntimeFoods()
    {
        for (int i = aliveFoods.Count - 1; i >= 0; i--)
        {
            var f = aliveFoods[i];
            if (f != null) Destroy(f);
        }
        aliveFoods.Clear();
        foodDefByInstanceId.Clear();
    }
    public int GetRetryRestartDistance()
    {
        int current = GetTotalCreatedTiles() + startTileIndexPublic;
        if (current <= 0) return 0;

        return ((current - 1) / 50) * 50;
    }
}

public sealed class GroundStreamerSpawnedEnemyMarker : MonoBehaviour
{
}

public sealed class EnemyStreamDespawn : MonoBehaviour
{
    private GroundStreamer streamer;
    private bool behindStarted;
    private float behindTimer;

    public void Initialize(GroundStreamer streamer)
    {
        this.streamer = streamer;
        behindStarted = false;
        behindTimer = 0f;
    }

    void Update()
    {
        if (streamer == null) return;

        if (transform.position.z < streamer.GetEnemyDespawnZLine())
        {
            if (!behindStarted)
            {
                behindStarted = true;
                behindTimer = 0f;
            }

            behindTimer += Time.deltaTime;

            if (behindTimer >= 1.0f)
            {
                float zMax = streamer.GetGroundMaxZ();
                float zMin = Mathf.Max(streamer.GetGroundMinZ(), zMax - streamer.tileLength);

                float x = UnityEngine.Random.Range(
                    -GroundStreamer.LANE_WIDTH * 0.5f + streamer.enemyXMargin,
                    +GroundStreamer.LANE_WIDTH * 0.5f - streamer.enemyXMargin
                );

                float z = UnityEngine.Random.Range(zMin + GroundStreamer.EPS, zMax - GroundStreamer.EPS);

                bool isHeavy = (GetComponent<HeavyEnemyController>() != null);
                float y = isHeavy ? transform.position.y : 0f;

                transform.position = new Vector3(x, y, z);

                behindStarted = false;
                behindTimer = 0f;
            }
        }
        else
        {
            behindStarted = false;
            behindTimer = 0f;
        }
    }
}
