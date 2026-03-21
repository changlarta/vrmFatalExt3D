using TMPro;
using UnityEngine;

public sealed class DistanceUI : MonoBehaviour
{
    [Header("Required (children)")]
    [SerializeField] private TMP_Text currentText;
    [SerializeField] private TMP_Text remainingText;

    [Header("Assign from Inspector")]
    public GroundStreamer groundStreamer;

    void Awake()
    {
    }

    void Update()
    {
        int current = groundStreamer.GetTotalCreatedTiles() + groundStreamer.startTileIndexPublic;
        int goal = groundStreamer.DIFFICULTY_MAX_TILE;

        int remaining = Mathf.Max(0, goal - current);

        string fmt = LocalizationManager.Instance.Get("moveGame_purpose_m"); // 例: "残り:{0}m"

        currentText.text = current.ToString() + "m";
        remainingText.text = string.Format(fmt, remaining);
    }
}