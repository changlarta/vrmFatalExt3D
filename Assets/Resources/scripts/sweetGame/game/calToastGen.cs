using System.Collections;
using TMPro;
using UnityEngine;

public class calToastGen : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset fontAsset;

    private const float LifeSeconds = 3f;
    private const float RisePerSecond = 40f;

    public void GenToast(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        GameObject go = new GameObject("Toast", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rt = go.GetComponent<RectTransform>();
        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();

        rt.SetParent(transform, false);
        rt.anchoredPosition3D = Vector3.zero;
        rt.sizeDelta = new Vector2(125f, 30f);

        tmp.text = text;
        tmp.fontSize = 20f;
        tmp.color = Color.black;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;

        if (fontAsset != null) tmp.font = fontAsset;

        StartCoroutine(ToastRoutine(rt, go));
    }

    private IEnumerator ToastRoutine(RectTransform rt, GameObject go)
    {
        float t = 0f;
        Vector3 start = rt.localPosition;

        while (t < LifeSeconds)
        {
            if (rt == null) yield break;
            float dt = Time.deltaTime;
            t += dt;
            rt.localPosition = start + Vector3.up * (RisePerSecond * t);
            yield return null;
        }

        if (go != null) Destroy(go);
    }
}
