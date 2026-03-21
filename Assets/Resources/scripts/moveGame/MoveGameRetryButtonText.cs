using TMPro;
using UnityEngine;

public sealed class MoveGameRetryButtonText : MonoBehaviour
{
    [SerializeField] private TMP_Text targetText;
    [SerializeField] private GroundStreamer groundStreamer;

    private void Update()
    {
        int restart = groundStreamer.GetRetryRestartDistance();
        string fmt = LocalizationManager.Instance.Get("moveGameRetryButton");
        targetText.text = string.Format(fmt, restart);
    }
}