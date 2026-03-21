using UnityEngine;

public sealed class DashboardSpawner : MonoBehaviour
{
    // 必須（このMaterialに後述Shaderを割り当てる）
    [SerializeField] private Material dashboardMaterial;

    // 10タイルに1個
    [SerializeField] private int everyTiles = 10;

    // 配置（タイルローカル）
    private float dashboardY = 0.1f;
    [SerializeField] private float dashboardXMargin = 0.6f;

    // 見た目サイズ（width=X, length=Z）
    [SerializeField] private Vector2 dashboardSize = new Vector2(4f, 2f);

    // 生成物のTrigger厚み（Y方向）
    [SerializeField] private float triggerThickness = 0.25f;

    // メッシュは使い回し（毎回 new Mesh しない）
    private Mesh cachedTriangleMesh;
    private Vector2 cachedSize;

    public void OnTileCreated(int tileIndex, Transform tileRoot, float laneWidth, float tileLength, float eps)
    {
        if (dashboardMaterial == null) return;
        if (everyTiles < 1) return;
        if (tileIndex <= 0) return;
        if ((tileIndex % everyTiles) != 0) return;
        if (tileRoot == null) return;

        float halfLane = laneWidth * 0.5f;

        float xLocal = Random.Range(-halfLane + dashboardXMargin, +halfLane - dashboardXMargin);
        float zLocal = Random.Range(eps, tileLength - eps);

        // 三角メッシュ（上向き、ローカルXZ平面）
        Mesh m = GetOrBuildTriangleMesh(dashboardSize);

        GameObject dash = new GameObject($"Dashboard_{tileIndex}");
        dash.transform.SetParent(tileRoot, worldPositionStays: false);
        dash.transform.localPosition = new Vector3(xLocal, dashboardY, zLocal);
        dash.transform.localRotation = Quaternion.identity; // ★床に平行（メッシュがXZ面なので回転不要）

        var mf = dash.AddComponent<MeshFilter>();
        mf.sharedMesh = m;

        var mr = dash.AddComponent<MeshRenderer>();
        mr.sharedMaterial = dashboardMaterial;

        // Trigger（板として拾いやすい）
        var box = dash.AddComponent<BoxCollider>();
        box.isTrigger = true;

        float w = Mathf.Max(0.01f, dashboardSize.x);
        float len = Mathf.Max(0.01f, dashboardSize.y);
        float thick = Mathf.Max(0.01f, triggerThickness);

        // 三角形の外接箱：中心を前寄りに（長さ方向の中心が len/2 になるように）
        box.size = new Vector3(w, thick, len);
        box.center = new Vector3(0f, thick * 0.5f, len * 0.5f);
    }

    private Mesh GetOrBuildTriangleMesh(Vector2 size)
    {
        if (cachedTriangleMesh != null && cachedSize == size) return cachedTriangleMesh;

        cachedSize = size;

        float w = Mathf.Max(0.01f, size.x);
        float len = Mathf.Max(0.01f, size.y);

        // 形：後ろが底辺、前が尖る（+Zが前）
        // v0: back-left, v1: back-right, v2: front-center
        Vector3 v0 = new Vector3(-w * 0.5f, 0f, 0f);
        Vector3 v1 = new Vector3(+w * 0.5f, 0f, 0f);
        Vector3 v2 = new Vector3(0f, 0f, len);

        // UV：V(uv.y) を「前方向」として 0→1
        Vector2 uv0 = new Vector2(0f, 0f);
        Vector2 uv1 = new Vector2(1f, 0f);
        Vector2 uv2 = new Vector2(0.5f, 1f);

        var mesh = new Mesh();
        mesh.name = "DashboardTriangle";

        mesh.vertices = new Vector3[] { v0, v1, v2 };
        mesh.uv = new Vector2[] { uv0, uv1, uv2 };

        // 上向きになるように winding を調整（Unity標準：時計回りが表の場合が多いのでここはこれで固定）
        // もし裏返るなら 0,2,1 ↔ 0,1,2 を入れ替えるだけで直る
        mesh.triangles = new int[] { 0, 2, 1 };

        mesh.normals = new Vector3[] { Vector3.up, Vector3.up, Vector3.up };
        mesh.RecalculateBounds();

        cachedTriangleMesh = mesh;
        return cachedTriangleMesh;
    }
}