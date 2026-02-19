using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class devToul : MonoBehaviour
{
    public GameObject chr;
    public GameObject weightScale;
    private VrmToController vrmToController;
    private SliderController weightSlider;
    private SliderController heightSlider;
    private SliderController cupSlider;
    private SliderController faceSlider;
    private TextMeshProUGUI weightCell;
    private TextMeshProUGUI heightCell;
    private TextMeshProUGUI cupCell;
    private TextMeshProUGUI faceCell;

    void Start()
    {
        gameObject.SetActive(false);
        vrmToController = chr.GetComponent<VrmToController>();

        weightSlider = transform.GetChild(0).GetComponent<SliderController>();
        weightCell = transform.GetChild(1).GetComponent<TextMeshProUGUI>();
        heightSlider = transform.GetChild(2).GetComponent<SliderController>();
        heightCell = transform.GetChild(3).GetComponent<TextMeshProUGUI>();
        cupSlider = transform.GetChild(4).GetComponent<SliderController>();
        cupCell = transform.GetChild(5).GetComponent<TextMeshProUGUI>();
        faceSlider = transform.GetChild(6).GetComponent<SliderController>();
        faceCell = transform.GetChild(7).GetComponent<TextMeshProUGUI>();
    }

    void Update()
    {
        if (!gameObject.activeInHierarchy) return;
        if (vrmToController == null) return;

        if (!weightSlider.isAnimating && !heightSlider.isAnimating && !cupSlider.isAnimating && !faceSlider.isAnimating)
        {
            weightSlider.SetValueFromController(vrmToController.bodyKey / 100f);
            heightSlider.SetValueFromController(0.8f - vrmToController.height);
            cupSlider.SetValueFromController(vrmToController.bustKey / 100f);
            faceSlider.SetValueFromController(vrmToController.face3Key / 100f);
        }

        bool anyDown =
            weightSlider.isPointerDown ||
            heightSlider.isPointerDown ||
            cupSlider.isPointerDown ||
            faceSlider.isPointerDown;

        var inst = VrmChrSceneController.Instance;
        if (inst != null) inst.isShockState = anyDown;

        ChngeHieghtText(vrmToController.height);
        ChngeWeightText(vrmToController.bodyKey / 100);
        ChngeCupText(vrmToController.bustKey / 100);
        ChngeFaceText(vrmToController.face3Key / 100);
    }

    public void SlideWeight(float value)
    {
        var inst = VrmChrSceneController.Instance;
        if (inst == null) return;

        inst.SetBodyKeyImmediate(value * 100f);
        ChngeWeightText(value);
    }

    public void SlideHeight(float value)
    {
        var xValue = 0.8f - value;
        var inst = VrmChrSceneController.Instance;
        if (inst == null) return;

        inst.ChangeHeight(xValue);
        if (vrmToController != null) weightSlider.SetValueFromController(vrmToController.bodyKey / 100f);
        ChngeHieghtText(xValue);
    }

    public void SlideCup(float value)
    {
        var inst = VrmChrSceneController.Instance;
        if (inst == null) return;

        inst.ChangeCup(value * 100f);
        ChngeCupText(value);
    }

    public void SlideFace(float value)
    {
        var inst = VrmChrSceneController.Instance;
        if (inst == null) return;

        inst.SetFace3KeyImmediate(value * 100f);
        ChngeFaceText(value);
    }


    private void ChngeWeightText(float value)
    {
        var text = ((int)(value * 100f)).ToString("D3") + "%";
        weightCell.text = text;
    }

    private void ChngeHieghtText(float value)
    {
        var cm = 162 - (int)(value * 62f);
        var text = cm.ToString("D3") + "cm";
        heightCell.text = text;
    }

    private void ChngeCupText(float value)
    {
        int idx = Mathf.RoundToInt(Mathf.Pow(value, 4.0f / 5.0f) * 15f);
        char c = (char)('A' + idx);
        cupCell.text = c.ToString();
    }

    private void ChngeFaceText(float value)
    {
        var text = ((int)(value * 100f)).ToString("D3") + "%";
        faceCell.text = text;
    }

    public void onClick()
    {
        gameObject.SetActive(!gameObject.activeSelf);
    }
}
