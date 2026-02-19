using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class NormalizedWheelScrollRect : MonoBehaviour, IScrollHandler
{
    [SerializeField] private ScrollRect target;          // 既存のScrollRect
    private void Reset()
    {
        target = GetComponentInParent<ScrollRect>();
    }

    private void Awake()
    {
        if (target == null) target = GetComponentInParent<ScrollRect>();
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (target == null) return;

        float y = eventData.scrollDelta.y;

        if (y != 0f)
            y = Mathf.Clamp(y, -12, 12);

        eventData.scrollDelta = new Vector2(0f, y);

        target.OnScroll(eventData);
        eventData.Use();
    }
}
