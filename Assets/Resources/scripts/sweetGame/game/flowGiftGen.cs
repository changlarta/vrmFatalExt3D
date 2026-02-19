using System.Collections;
using UnityEngine;
using TMPro;
using Unity.VisualScripting;

public class flowGiftGen : MonoBehaviour
{
    public GameObject prefab;

    [SerializeField] private TMP_FontAsset fontAsset;

    float timer;

    float yjsTrueElapsed;
    bool isAccelerated;

    void Update()
    {
        float dt = Time.deltaTime;

        if (IsYjsStore.isYjsMode)
        {
            yjsTrueElapsed += dt;

            if (yjsTrueElapsed >= 17)
            {
                isAccelerated = true;
            }
            if (yjsTrueElapsed >= 60)
            {
                IsYjsStore.isYjsMode = false;
                AudioManager.Instance.PlayBGM("bgm1");
            }
        }
        else
        {
            yjsTrueElapsed = 0f;
            isAccelerated = false;
        }

        float spawnInterval = isAccelerated ? 0.0114514f : (IsYjsStore.isYjsMode ? 0.514f : 15f);

        // --- 生成ロジック（intervalだけ差し替え） ---
        timer += dt;

        if (timer >= spawnInterval)
        {
            timer = 0f;

            float y = Random.Range(-160f, 160f);
            var go = Instantiate(prefab, transform);
            go.transform.localPosition = new Vector3(540f, y, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }
    }

    public void ShowToast(Vector3 localPos, bool isOnlyToast1)
    {
        string text;

        var inst = VrmChrSceneController.Instance;

        var i = Random.Range(0, 100);
        if (i > 90 || isOnlyToast1)
        {
            var x = inst.workLv * 1;
            inst.coin += x;
            text = string.Format(LocalizationManager.Instance.Get("giftToast1"), x);
        }
        else if (i > 60)
        {
            var x = inst.workLv * 20;
            inst.coin += x;
            text = string.Format(LocalizationManager.Instance.Get("giftToast2"), x);
        }
        else if (i > 20)
        {
            var x = inst.workLv * 50;
            inst.coin += x;
            text = string.Format(LocalizationManager.Instance.Get("giftToast3"), x);
        }
        else
        {
            var x = inst.workLv * 300;
            inst.coin += x;
            text = string.Format(LocalizationManager.Instance.Get("giftToast4"), x);
        }

        GameObject go = new GameObject("Toast", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rt = go.GetComponent<RectTransform>();
        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();

        rt.SetParent(transform, false);
        rt.anchoredPosition3D = localPos - new Vector3(-100f, 0f, 0f);
        rt.sizeDelta = new Vector2(250f, 30f);

        tmp.text = text;
        tmp.fontSize = 20f;
        tmp.color = Color.black;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;

        if (fontAsset != null) tmp.font = fontAsset;

        StartCoroutine(ToastRoutine(rt, go));

        AudioManager.Instance.PlaySE("gift");
    }

    IEnumerator ToastRoutine(RectTransform rt, GameObject go)
    {
        float t = 0f;
        Vector3 start = rt.localPosition;

        while (t < 3f)
        {
            if (rt == null) yield break;
            float dt = Time.deltaTime;
            t += dt;
            rt.localPosition = start + Vector3.up * (40f * t);
            yield return null;
        }

        if (go != null) Destroy(go);
    }
}
