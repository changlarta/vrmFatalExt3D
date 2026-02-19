using System.Collections.Generic;
using UnityEngine;

public sealed class GroundStreamer : MonoBehaviour
{
    // ★ 道幅はここ1か所だけ（変更はここだけでOK）
    public const float LANE_WIDTH = 20f;

    [Header("Required")]
    public Material groundMaterial; // 必須（PrefabではなくMaterialを指定）

    [Header("Building materials (choose 1 of 3 per cube)")]
    public Material buildingMaterial1; // 必須
    public Material buildingMaterial2; // 必須
    public Material buildingMaterial3; // 必須

    [Header("Tile settings")]
    public float tileLength = 20f;  // 1タイルのZ長さ
    public int tilesAhead = 8;      // 前方生成数
    public int tilesBehind = 2;     // 後方保持数（通過済みは破棄）

    [Header("Side buildings (generated cubes)")]
    public bool buildingsEnabled = true;

    // ===== public を上から順に指定値へ =====
    public float buildingWidth = 10f;        // 横幅10
    public float buildingMinHeight = 3f;     // 最小高さ3
    public float buildingMaxHeight = 20f;    // 最大高さ10
    public float segmentMinLength = 120f;    // 最小横幅120（Z方向の敷き詰め単位）
    public float segmentMaxLength = 120f;    // 最大横幅120（同じ）

    // -------------------------
    // Enemy spawn (integrated)
    // -------------------------
    [Header("Enemy spawn (integrated)")]
    [Tooltip("敵Prefab（必須）。フォールバック無し：未設定なら停止します。")]
    public GameObject enemyPrefab;

    [Tooltip("地面タイルを何個生成するたびに敵を生成するか")]
    [Min(1)] public int spawnEnemyEveryTiles = 10;

    [Tooltip("発生1回あたりの敵数")]
    [Min(1)] public int enemiesPerSpawn = 1;

    [Tooltip("タイル内の敵X配置マージン（端から内側に寄せる）")]
    [Min(0f)] public float enemyXMargin = 0.5f;

    [Tooltip("敵のYオフセット（地面=0基準）")]
    public float enemyYOffset = 0f;

    [Tooltip("敵が破棄ラインを越えてから削除するまでの遅延秒")]
    [Min(0f)] public float enemyDespawnDelaySeconds = 1.0f;

    [Tooltip("破棄ラインを越えた後、さらにこの距離ぶん後方へ抜けたら削除OKにする")]
    [Min(0f)] public float enemyExtraBehindDistance = 0.0f;

    // タイル境界にピッタリ合わせるための微小誤差吸収
    private const float EPS = 1e-4f;

    private Transform followTarget;

    // tile index -> root GameObject（地面 + 建物を子に持つ）
    private readonly Dictionary<int, GameObject> tiles = new Dictionary<int, GameObject>();

    // 「到達した最前方」を基準に生成/破棄する（後退では生成しない）
    private float furthestZ = float.NegativeInfinity;
    private int lastBuiltFurthestIndex = int.MinValue;

    // 生成済み地面のZ範囲（外へ出ないためのクランプ用）
    private float groundMinZ = 0f;
    private float groundMaxZ = 0f;

    public float GetGroundMinZ() => groundMinZ;
    public float GetGroundMaxZ() => groundMaxZ;

    // --- 敵削除ライン（GroundStreamerの保持範囲と同型） ---
    // tilesBehindに基づく「後方破棄ライン」をZで保持して、敵側が参照する
    private float enemyDespawnZLine = float.NegativeInfinity;

    /// <summary>
    /// GroundStreamerの現在の「後方破棄ライン」。
    /// このラインより十分後ろへ抜けた敵は、遅延の後に削除される。
    /// </summary>
    public float GetEnemyDespawnZLine() => enemyDespawnZLine;

    // 「新規生成したタイル数」（破棄されても減らさない）
    private int totalCreatedTiles = 0;

    public void SetFollowTarget(Transform t)
    {
        followTarget = t;
        furthestZ = float.NegativeInfinity;
        lastBuiltFurthestIndex = int.MinValue;
        enemyDespawnZLine = float.NegativeInfinity;
    }

    void Start()
    {
        if (groundMaterial == null)
        {
            Debug.LogError("GroundStreamer: groundMaterial が未設定です。");
            enabled = false;
            return;
        }
        if (buildingMaterial1 == null || buildingMaterial2 == null || buildingMaterial3 == null)
        {
            Debug.LogError("GroundStreamer: buildingMaterial1/2/3 が全て未設定です。");
            enabled = false;
            return;
        }
        if (tileLength <= 0f)
        {
            Debug.LogError("GroundStreamer: tileLength は 0 より大きい必要があります。");
            enabled = false;
            return;
        }
        if (tilesAhead < 1)
        {
            Debug.LogError("GroundStreamer: tilesAhead は 1 以上が必要です。");
            enabled = false;
            return;
        }
        if (tilesBehind < 0)
        {
            Debug.LogError("GroundStreamer: tilesBehind は 0 以上が必要です。");
            enabled = false;
            return;
        }

        if (buildingWidth <= 0f)
        {
            Debug.LogError("GroundStreamer: buildingWidth は 0 より大きい必要があります。");
            enabled = false;
            return;
        }
        if (buildingMinHeight <= 0f || buildingMaxHeight <= 0f || buildingMaxHeight < buildingMinHeight)
        {
            Debug.LogError("GroundStreamer: buildingMinHeight / buildingMaxHeight の指定が不正です。");
            enabled = false;
            return;
        }
        if (segmentMinLength <= 0f || segmentMaxLength <= 0f || segmentMaxLength < segmentMinLength)
        {
            Debug.LogError("GroundStreamer: segmentMinLength / segmentMaxLength の指定が不正です。");
            enabled = false;
            return;
        }

        // 敵は統合仕様上必須（フォールバック禁止）
        if (enemyPrefab == null)
        {
            Debug.LogError("GroundStreamer: enemyPrefab が未設定です。");
            enabled = false;
            return;
        }
        if (spawnEnemyEveryTiles < 1)
        {
            Debug.LogError("GroundStreamer: spawnEnemyEveryTiles は 1 以上が必要です。");
            enabled = false;
            return;
        }
    }

    public void RebuildImmediate()
    {
        foreach (var kv in tiles)
        {
            if (kv.Value != null) Destroy(kv.Value);
        }
        tiles.Clear();

        furthestZ = float.NegativeInfinity;
        lastBuiltFurthestIndex = int.MinValue;

        totalCreatedTiles = 0;
        enemyDespawnZLine = float.NegativeInfinity;

        Tick(force: true);
    }

    public void Tick() => Tick(force: false);

    private void Tick(bool force)
    {
        if (!enabled) return;
        if (followTarget == null) return;

        float z = followTarget.position.z;
        if (z > furthestZ) furthestZ = z;

        int furthestIdx = Mathf.FloorToInt(furthestZ / tileLength);

        if (!force && furthestIdx == lastBuiltFurthestIndex) return;
        lastBuiltFurthestIndex = furthestIdx;

        int minKeepIdx = furthestIdx - tilesBehind;
        int maxKeepIdx = furthestIdx + tilesAhead;

        // タイル i は [i*L, (i+1)*L] を覆う前提
        groundMinZ = minKeepIdx * tileLength;
        groundMaxZ = (maxKeepIdx + 1) * tileLength;

        // 敵の破棄ラインは「タイルの後方保持範囲」と同型で更新
        // ※ここが“プレイヤーZ判定”ではなく“GroundStreamerの保持窓”に基づくライン
        enemyDespawnZLine = groundMinZ;

        // 生成：最前方基準（後退では増えない）
        for (int i = minKeepIdx; i <= maxKeepIdx; i++)
        {
            if (!tiles.ContainsKey(i))
            {
                GameObject tileRoot = CreateTileRoot(i);
                tiles.Add(i, tileRoot);

                // --- タイル新規生成カウント ---
                totalCreatedTiles++;

                // --- 10個ごとに敵生成（敵はタイルの子にしない） ---
                if (totalCreatedTiles % spawnEnemyEveryTiles == 0)
                {
                    SpawnEnemiesForTileIndex(i);
                }
            }
        }

        // 破棄：後方は消す（最前方基準）
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

    private void SpawnEnemiesForTileIndex(int tileIndex)
    {
        // タイルのZ範囲：[tileIndex*L, (tileIndex+1)*L]
        float zMin = tileIndex * tileLength;
        float zMax = (tileIndex + 1) * tileLength;

        float halfLane = LANE_WIDTH * 0.5f;
        float xMin = -halfLane + enemyXMargin;
        float xMax = +halfLane - enemyXMargin;

        if (xMax < xMin)
        {
            Debug.LogError("GroundStreamer: enemyXMargin が大きすぎて配置範囲がありません。");
            enabled = false;
            return;
        }

        for (int n = 0; n < enemiesPerSpawn; n++)
        {
            float x = Random.Range(xMin, xMax);
            float zz = Random.Range(zMin + EPS, zMax - EPS);

            Vector3 pos = new Vector3(x, enemyYOffset, zz);

            // ★親にしない：地面と寿命を共通化しない
            GameObject e = Instantiate(enemyPrefab, pos, Quaternion.identity);
            e.name = $"Enemy_{tileIndex}_{n}";

            // 破棄ライン追従コンポーネント（同ファイル末尾）
            var d = e.GetComponent<EnemyStreamDespawn>();
            if (d == null) d = e.AddComponent<EnemyStreamDespawn>();
            d.Initialize(this, enemyDespawnDelaySeconds, enemyExtraBehindDistance);
        }
    }

    private GameObject CreateTileRoot(int index)
    {
        GameObject root = new GameObject($"GroundTile_{index}");
        root.transform.position = new Vector3(0f, 0f, index * tileLength);

        // ---- ground mesh ----
        {
            var groundGO = new GameObject("Ground");
            groundGO.transform.SetParent(root.transform, false);
            groundGO.transform.localPosition = Vector3.zero;

            var mf = groundGO.AddComponent<MeshFilter>();
            var mr = groundGO.AddComponent<MeshRenderer>();

            mr.sharedMaterial = groundMaterial;
            mf.sharedMesh = BuildGroundMesh(LANE_WIDTH, tileLength);
        }

        // ---- buildings ----
        if (buildingsEnabled)
        {
            var buildingsGO = new GameObject("Buildings");
            buildingsGO.transform.SetParent(root.transform, false);
            buildingsGO.transform.localPosition = Vector3.zero;

            CreateBuildingsForSide(buildingsGO.transform, index, sideSign: -1f); // left
            CreateBuildingsForSide(buildingsGO.transform, index, sideSign: +1f); // right
        }

        return root;
    }

    private void CreateBuildingsForSide(Transform parent, int tileIndex, float sideSign)
    {
        // 道の端（内側面）を x = ±LANE_WIDTH/2 に合わせる
        float innerEdgeX = sideSign * (LANE_WIDTH * 0.5f);
        float cx = innerEdgeX + sideSign * (buildingWidth * 0.5f);

        // タイル内Z範囲 [0, tileLength] を「重ならず」「隙間なし」で分割してキューブ配置
        float z0 = 0f;
        int segId = 0;

        while (z0 < tileLength - EPS)
        {
            float remaining = tileLength - z0;

            float segLen;
            if (remaining <= segmentMaxLength + EPS)
            {
                segLen = remaining;
            }
            else
            {
                float candidate = Random.Range(segmentMinLength, segmentMaxLength);
                segLen = Mathf.Clamp(candidate, segmentMinLength, segmentMaxLength);

                if (segLen > remaining) segLen = remaining;
            }

            float cz = z0 + segLen * 0.5f;

            float h = Random.Range(buildingMinHeight, buildingMaxHeight);

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"B_{tileIndex}_{(sideSign < 0f ? "L" : "R")}_{segId}";
            cube.transform.SetParent(parent, false);

            cube.transform.localScale = new Vector3(buildingWidth, h, segLen);
            cube.transform.localPosition = new Vector3(cx, h * 0.5f, cz);

            Material chosen = ChooseBuildingMaterial();
            var mr = cube.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = chosen;

            z0 += segLen;
            segId++;
        }
    }

    private Material ChooseBuildingMaterial()
    {
        int r = Random.Range(0, 3);
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

        int[] t = new int[6]
        {
            0, 2, 1,
            2, 3, 1
        };

        Vector3[] n = new Vector3[4]
        {
            Vector3.up, Vector3.up, Vector3.up, Vector3.up
        };

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
}

/// <summary>
/// 地面タイルとは独立して敵を削除するためのコンポーネント。
/// - GroundStreamer が算出する「後方破棄ライン」を参照する
/// - ラインを越えて十分後方に抜け、指定秒数連続で後方に居たら Destroy
/// </summary>
public sealed class EnemyStreamDespawn : MonoBehaviour
{
    private GroundStreamer streamer;
    private float delaySeconds;
    private float extraBehind;

    private bool behindStarted;
    private float behindTimer;

    public void Initialize(GroundStreamer streamer, float delaySeconds, float extraBehindDistance)
    {
        if (streamer == null)
        {
            Debug.LogError("EnemyStreamDespawn: streamer が null です。");
            enabled = false;
            return;
        }

        this.streamer = streamer;
        this.delaySeconds = Mathf.Max(0f, delaySeconds);
        this.extraBehind = Mathf.Max(0f, extraBehindDistance);

        behindStarted = false;
        behindTimer = 0f;
    }

    void Update()
    {
        if (!enabled) return;
        if (streamer == null)
        {
            Debug.LogError("EnemyStreamDespawn: streamer 参照が失われました。");
            enabled = false;
            return;
        }

        float lineZ = streamer.GetEnemyDespawnZLine() - extraBehind;

        // 「GroundStreamerの保持窓」から十分外へ抜けたかどうか
        bool isBehind = transform.position.z < lineZ;

        if (isBehind)
        {
            if (!behindStarted)
            {
                behindStarted = true;
                behindTimer = 0f;
            }

            behindTimer += Time.deltaTime;
            if (behindTimer >= delaySeconds)
            {
                Destroy(gameObject);
            }
        }
        else
        {
            // 一度戻ってきたらリセット（瞬間的な越えで消えない）
            behindStarted = false;
            behindTimer = 0f;
        }
    }
}
