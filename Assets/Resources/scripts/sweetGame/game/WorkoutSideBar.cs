using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

[System.Serializable]
public struct WorkOutInfo
{
    public string name;
    public string id;
    public int level;
    public int consumeCal;

    public WorkOutInfo(string name, string id, int level, int consumeCal)
    {
        this.name = name;
        this.id = id;
        this.level = level;
        this.consumeCal = consumeCal;
    }
}

public class WorkoutSideBar : MonoBehaviour
{
    [Header("WorkOut")]
    [SerializeField] private GameObject workOutPrefab;

    private Transform content;
    private RectTransform contentRect;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private int workoutLv = 0;

    private const float X = -25f;
    private const float StartY = -50f;
    private const float StepY = -80f;

    private const float BaseContentHeight = 450f;

    private static readonly WorkOutInfo[] WorkoutsFixed = new WorkOutInfo[]
    {
        new WorkOutInfo("workout1name", "workOut_walk", 0, 3),
        new WorkOutInfo("workout2name", "workOut_jogging", 1, 10),
        new WorkOutInfo("workout3name", "workOut_situps", 2, 5),
        new WorkOutInfo("workout4name", "workOut_squat", 3, 5),
        new WorkOutInfo("workout5name", "workOut_jumping_jacks", 4, 12),
        new WorkOutInfo("workout7name", "workOut_burpee", 5, 20),
    };

    private void Start()
    {
        content = transform.GetChild(0).GetChild(0).GetChild(0);
        contentRect = content.GetComponent<RectTransform>();

        Regenerate();
    }

    public void SetWorkoutLv(int newLv)
    {
        if (newLv < 0) newLv = 0;
        if (workoutLv == newLv) return;

        workoutLv = newLv;
        Regenerate();
    }

    public int GetWorkoutLv()
    {
        return workoutLv;
    }

    private void Regenerate()
    {
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null) Destroy(spawned[i]);
        }
        spawned.Clear();

        if (content == null || contentRect == null || workOutPrefab == null)
        {
            return;
        }

        int visibleIndex = 0;

        for (int i = 0; i < WorkoutsFixed.Length; i++)
        {
            var info = WorkoutsFixed[i];
            if (workoutLv < info.level) continue;

            var go = Instantiate(workOutPrefab, content, false);
            spawned.Add(go);

            float y = StartY + StepY * visibleIndex;

            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchoredPosition = new Vector2(X, y);
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
            }
            else
            {
                go.transform.localPosition = new Vector3(X, y, 0f);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }

            if (go.transform.childCount >= 2)
            {
                var t0 = go.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
                var t1 = go.transform.GetChild(1).GetComponent<TextMeshProUGUI>();

                if (t0 != null)
                {
                    var loc = t0.GetComponent<LocalizationText>();
                    if (loc == null) loc = t0.gameObject.AddComponent<LocalizationText>();
                    loc.key = info.name;
                }

                if (t1 != null) t1.text = info.consumeCal.ToString() + "kcal/s";
            }

            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.GetComponentInChildren<Button>(true);
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                var captured = info;
                btn.onClick.AddListener(() => OnTap(captured));
            }

            visibleIndex++;
        }

        ResizeContentHeight(visibleIndex);
    }

    private void OnTap(WorkOutInfo info)
    {
        var inst = VrmChrSceneController.Instance;
        if (inst == null) return;

        if (inst == null || (info.level == 5 && inst.vrmToController.bodyKey > 50))
        {
            AudioManager.Instance.PlaySE("beep");
            return;
        }
        inst.ToggleWorkout(info);
    }

    private void ResizeContentHeight(int visibleCount)
    {
        if (contentRect == null) return;

        float height = BaseContentHeight;
        if (visibleCount >= 6)
        {
            height = BaseContentHeight + 80f * (visibleCount - 5);
        }

        var size = contentRect.sizeDelta;
        size.y = height;
        contentRect.sizeDelta = size;
    }
}
