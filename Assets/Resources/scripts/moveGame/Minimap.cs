using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Minimap : MonoBehaviour
{
    public enum IconType
    {
        Player,
        Enemy,
        Food,      // ★追加
        NPC,
        Objective,
    }

    [Serializable]
    public class TargetEntry
    {
        public GameObject target;
        public IconType iconType;
    }

    [Serializable]
    public class IconSpriteEntry
    {
        public IconType iconType;
        public Sprite sprite;
    }

    [Header("Z view range relative to player")]
    public float viewBehind = 10f;
    public float viewAhead = 60f;

    [Header("Tracked targets (manual: Player/NPC/Objective)")]
    public List<TargetEntry> targets = new List<TargetEntry>();

    [Header("Enemy/Food source (from GroundStreamer)")]
    public GroundStreamer groundStreamer;

    [Header("Icon sprites (enum -> sprite)")]
    public List<IconSpriteEntry> iconSprites = new List<IconSpriteEntry>();

    [Header("Icon size (px)")]
    public float iconSize = 16f;

    private RectTransform minimapRect;
    private Dictionary<IconType, Sprite> spriteMap;

    private readonly List<RectTransform> manualIconRects = new List<RectTransform>();
    private readonly List<RectTransform> enemyIconRects = new List<RectTransform>();
    private readonly List<RectTransform> foodIconRects = new List<RectTransform>(); // ★追加

    private GameObject playerTarget;
    private float laneHalfWidth;

    private void Awake()
    {
        minimapRect = (RectTransform)transform;
        laneHalfWidth = GroundStreamer.LANE_WIDTH * 0.5f;

        spriteMap = new Dictionary<IconType, Sprite>();
        for (int i = 0; i < iconSprites.Count; i++)
            spriteMap[iconSprites[i].iconType] = iconSprites[i].sprite;

        playerTarget = null;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].iconType == IconType.Player)
            {
                playerTarget = targets[i].target;
                break;
            }
        }

        if (playerTarget == null)
        {
            Debug.LogError("Minimap: targets に Player が設定されていません。");
            enabled = false;
            return;
        }
        if (groundStreamer == null)
        {
            Debug.LogError("Minimap: groundStreamer が未設定です。");
            enabled = false;
            return;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].target == null)
            {
                Debug.LogError($"Minimap: targets[{i}].target が未設定です。");
                enabled = false;
                return;
            }
            if (!spriteMap.ContainsKey(targets[i].iconType) || spriteMap[targets[i].iconType] == null)
            {
                Debug.LogError($"Minimap: iconSprites に {targets[i].iconType} の sprite が設定されていません。");
                enabled = false;
                return;
            }

            manualIconRects.Add(CreateIconRect($"MinimapIcon_Manual_{i}_{targets[i].iconType}", targets[i].iconType));
        }

        if (!spriteMap.ContainsKey(IconType.Enemy) || spriteMap[IconType.Enemy] == null)
        {
            Debug.LogError("Minimap: iconSprites に Enemy の sprite が設定されていません。");
            enabled = false;
            return;
        }
        if (!spriteMap.ContainsKey(IconType.Food) || spriteMap[IconType.Food] == null)
        {
            Debug.LogError("Minimap: iconSprites に Food の sprite が設定されていません。");
            enabled = false;
            return;
        }

        SyncEnemyIconsToGroundStreamer();
        SyncFoodIconsToGroundStreamer();
    }

    private RectTransform CreateIconRect(string name, IconType iconType)
    {
        GameObject iconGO = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconGO.transform.SetParent(transform, false);

        RectTransform rt = (RectTransform)iconGO.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(iconSize, iconSize);

        Image img = iconGO.GetComponent<Image>();
        img.sprite = spriteMap[iconType];
        img.raycastTarget = false;

        return rt;
    }

    private void LateUpdate()
    {
        if (!enabled) return;
        if (playerTarget == null) return;

        // 量が少ない前提で毎フレーム同期
        SyncEnemyIconsToGroundStreamer();
        SyncFoodIconsToGroundStreamer();

        Vector2 size = minimapRect.rect.size;

        float minX = -laneHalfWidth;
        float maxX = laneHalfWidth;

        float minRelZ = -viewBehind * 2f;
        float maxRelZ = viewAhead * 0.5f;

        Vector3 p = playerTarget.transform.position;

        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i].target;
            if (t == null)
            {
                manualIconRects[i].gameObject.SetActive(false);
                continue;
            }
            UpdateIcon(manualIconRects[i], t.transform.position, p, minX, maxX, minRelZ, maxRelZ, size);
        }

        List<GameObject> enemies = groundStreamer.GetAliveEnemies();
        for (int i = 0; i < enemies.Count; i++)
        {
            var e = enemies[i];
            if (e == null)
            {
                enemyIconRects[i].gameObject.SetActive(false);
                continue;
            }
            UpdateIcon(enemyIconRects[i], e.transform.position, p, minX, maxX, minRelZ, maxRelZ, size);
        }

        List<GameObject> foods = groundStreamer.GetAliveFoods(); // ★追加
        for (int i = 0; i < foods.Count; i++)
        {
            var f = foods[i];
            if (f == null)
            {
                foodIconRects[i].gameObject.SetActive(false);
                continue;
            }
            UpdateIcon(foodIconRects[i], f.transform.position, p, minX, maxX, minRelZ, maxRelZ, size);
        }
    }

    private void UpdateIcon(RectTransform rt, Vector3 worldPos, Vector3 playerPos,
        float minX, float maxX, float minRelZ, float maxRelZ, Vector2 size)
    {
        float relZ = worldPos.z - playerPos.z;

        // 前後だけ圧縮（固定2倍）
        float relZc = relZ / 2f;

        bool inX = (worldPos.x >= minX) && (worldPos.x <= maxX);
        bool inZ = (relZc >= minRelZ) && (relZc <= maxRelZ);

        if (!(inX && inZ))
        {
            if (rt.gameObject.activeSelf) rt.gameObject.SetActive(false);
            return;
        }

        if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);

        float nx = Mathf.InverseLerp(minX, maxX, worldPos.x);
        float nz = Mathf.InverseLerp(minRelZ, maxRelZ, relZc);

        rt.anchoredPosition = new Vector2((nx - 0.5f) * size.x, (nz - 0.5f) * size.y);
    }

    private void SyncEnemyIconsToGroundStreamer()
    {
        List<GameObject> enemies = groundStreamer.GetAliveEnemies();

        for (int i = enemyIconRects.Count - 1; i >= enemies.Count; i--)
        {
            if (enemyIconRects[i] != null) Destroy(enemyIconRects[i].gameObject);
            enemyIconRects.RemoveAt(i);
        }

        while (enemyIconRects.Count < enemies.Count)
            enemyIconRects.Add(CreateIconRect($"MinimapIcon_Enemy_{enemyIconRects.Count}", IconType.Enemy));
    }

    private void SyncFoodIconsToGroundStreamer() // ★追加
    {
        List<GameObject> foods = groundStreamer.GetAliveFoods();

        for (int i = foodIconRects.Count - 1; i >= foods.Count; i--)
        {
            if (foodIconRects[i] != null) Destroy(foodIconRects[i].gameObject);
            foodIconRects.RemoveAt(i);
        }

        while (foodIconRects.Count < foods.Count)
            foodIconRects.Add(CreateIconRect($"MinimapIcon_Food_{foodIconRects.Count}", IconType.Food));
    }
}