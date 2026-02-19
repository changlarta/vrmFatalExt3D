using UnityEngine;
using UnityEngine.UI;

public class foodGauge : MonoBehaviour
{
    private Slider slider;
    private RectTransform line1Transform;

    private bool hasPrev;
    private float prevGaugeX;

    private bool hungryArmed;
    private bool warnArmed;

    private const float WarnLineX = -90f;

    void Start()
    {
        slider = GetComponent<Slider>();
        line1Transform = transform.GetChild(3).gameObject.GetComponent<RectTransform>();
    }

    void Update()
    {
        var inst = VrmChrSceneController.Instance;
        if (inst == null) return;

        slider.value = Mathf.Clamp01(inst.foodGauge / 100f);
        line1Transform.anchoredPosition = new Vector2(inst.foodGaugeLine1X, line1Transform.anchoredPosition.y);

        float gaugeX = (Mathf.Clamp(inst.foodGauge, 0f, 100f) / 100f) * 200f - 100f;
        float line1X = inst.foodGaugeLine1X;

        if (!hasPrev)
        {
            hasPrev = true;
            prevGaugeX = gaugeX;
            hungryArmed = gaugeX > line1X;
            warnArmed = gaugeX > WarnLineX;
            return;
        }

        bool isDecreasing = gaugeX < prevGaugeX;

        if (hungryArmed)
        {
            if (isDecreasing && prevGaugeX > line1X && gaugeX <= line1X)
            {
                AudioManager.Instance.PlaySE("hungry");
                hungryArmed = false;
            }
        }
        else
        {
            if (gaugeX > line1X) hungryArmed = true;
        }

        if (warnArmed)
        {
            if (isDecreasing && prevGaugeX > WarnLineX && gaugeX <= WarnLineX)
            {
                AudioManager.Instance.PlaySE("warn");
                warnArmed = false;
            }
        }
        else
        {
            if (gaugeX > WarnLineX) warnArmed = true;
        }

        prevGaugeX = gaugeX;
    }
}
