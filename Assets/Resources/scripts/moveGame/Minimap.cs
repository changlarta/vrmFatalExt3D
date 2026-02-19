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

    [Header("Tracked targets")]
    public List<TargetEntry> targets = new List<TargetEntry>();

    [Header("Icon sprites (enum -> sprite)")]
    public List<IconSpriteEntry> iconSprites = new List<IconSpriteEntry>();

    [Header("Icon size (px)")]
    public float iconSize = 16f;

    private RectTransform minimapRect;
    private Dictionary<IconType, Sprite> spriteMap;
    private List<RectTransform> iconRects;

    private GameObject playerTarget;
    private float laneHalfWidth;

    private void Awake()
    {
        minimapRect = (RectTransform)transform;

        laneHalfWidth = GroundStreamer.LANE_WIDTH * 0.5f;

        spriteMap = new Dictionary<IconType, Sprite>();
        for (int i = 0; i < iconSprites.Count; i++)
        {
            spriteMap[iconSprites[i].iconType] = iconSprites[i].sprite;
        }

        iconRects = new List<RectTransform>(targets.Count);

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

            GameObject iconGO = new GameObject($"MinimapIcon_{i}_{targets[i].iconType}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

            iconGO.transform.SetParent(transform, false);

            RectTransform rt = (RectTransform)iconGO.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(iconSize, iconSize);

            Image img = iconGO.GetComponent<Image>();
            img.sprite = spriteMap[targets[i].iconType];
            img.raycastTarget = false;

            iconRects.Add(rt);
        }
    }

    private void LateUpdate()
    {
        Vector2 size = minimapRect.rect.size;

        float minX = -laneHalfWidth;
        float maxX = laneHalfWidth;

        float minZ = -viewBehind;
        float maxZ = viewAhead;

        Vector3 p = playerTarget.transform.position;

        for (int i = 0; i < targets.Count; i++)
        {
            Vector3 w = targets[i].target.transform.position;

            float x = Mathf.Clamp(w.x, minX, maxX);
            float nx = Mathf.InverseLerp(minX, maxX, x); // 0..1

            float relZ = w.z - p.z;
            relZ = Mathf.Clamp(relZ, minZ, maxZ);
            float nz = Mathf.InverseLerp(minZ, maxZ, relZ); // 0..1

            float px = (nx - 0.5f) * size.x;
            float py = (nz - 0.5f) * size.y;

            iconRects[i].anchoredPosition = new Vector2(px, py);
        }
    }
}
