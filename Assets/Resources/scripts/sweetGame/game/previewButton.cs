using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class previewButton : MonoBehaviour
{
    TextMeshProUGUI tmp;

    void Awake()
    {
        tmp = transform.GetChild(0).GetComponent<TextMeshProUGUI>();

        var trigger = gameObject.GetComponent<EventTrigger>();
        if (trigger == null) trigger = gameObject.AddComponent<EventTrigger>();
        trigger.triggers = new System.Collections.Generic.List<EventTrigger.Entry>();

        AddTrigger(trigger, EventTriggerType.PointerDown, _ =>
        {
            var inst = VrmChrSceneController.Instance;
            if (inst == null) return;
            inst.OnPreviewPointerDown();
        });

        AddTrigger(trigger, EventTriggerType.PointerUp, _ =>
        {
            var inst = VrmChrSceneController.Instance;
            if (inst == null) return;
            inst.OnPreviewPointerUp();
        });
    }

    void Update()
    {
        if (tmp != null)
        {
            tmp.text = LocalizationManager.Instance.Get("previewButtonText");
        }
    }

    static void AddTrigger(EventTrigger trigger, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(action);
        trigger.triggers.Add(entry);
    }
}
