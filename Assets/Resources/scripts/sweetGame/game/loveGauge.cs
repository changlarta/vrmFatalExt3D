using UnityEngine;
using UnityEngine.UI;

public class loveGauge : MonoBehaviour
{
    private Slider slider;

    private bool hasPrev;



    void Start()
    {
        slider = GetComponent<Slider>();
    }

    void Update()
    {
        var inst = VrmChrSceneController.Instance;
        if (inst == null) return;

        slider.value = Mathf.Clamp01(inst.loveGauge / 100f);

        // float gaugeX = (Mathf.Clamp(inst.loveGauge, 0f, 100f) / 100f) * 200f - 100f;
    }
}