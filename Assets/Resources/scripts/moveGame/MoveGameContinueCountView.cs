using UnityEngine;
using TMPro;

public sealed class MoveGameContinueCountView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI continueCountText;
    [SerializeField] private string localizationKey = "moveGameContinueCountFormat";

    private int currentContinueCount;

    private void Awake()
    {
        if (continueCountText == null)
        {
            continueCountText = GetComponent<TextMeshProUGUI>();
        }
    }

    private void OnEnable()
    {
        Refresh();
    }

    public void SetContinueCount(int count)
    {
        currentContinueCount = Mathf.Max(0, count);
        Refresh();
    }

    private void Refresh()
    {
        if (continueCountText == null) return;
        if (LocalizationManager.Instance == null) return;

        string format = LocalizationManager.Instance.Get(localizationKey);
        continueCountText.text = string.Format(format, currentContinueCount);
    }
}