using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WeightScale : MonoBehaviour
{
    public GameObject chr;
    private TextMeshProUGUI weightCell1;
    private TextMeshProUGUI weightCell2;
    private Image stringCellBox;
    private TextMeshProUGUI stringCell;
    private VrmChrSceneController vrmChrSceneController;

    void Start()
    {
        vrmChrSceneController = VrmChrSceneController.Instance;
        weightCell1 = transform.GetChild(0).GetComponent<TextMeshProUGUI>();
        weightCell2 = transform.GetChild(1).GetComponent<TextMeshProUGUI>();
        stringCellBox = transform.GetChild(2).GetComponent<Image>();
        stringCell = transform.GetChild(3).GetComponent<TextMeshProUGUI>();
    }

    void Update()
    {

        int kg100 = vrmChrSceneController.kg100;
        int kg100_2 = vrmChrSceneController.kg100_2;

        if (SettingStore.isKgViewMode)
        {
            weightCell1.text = (kg100_2 / 100).ToString("D3");
            weightCell2.text = "." + Mathf.Abs(kg100_2 % 100).ToString("D2") + "kg";
        }
        else
        {
            var lb100 = Mathf.RoundToInt(kg100_2 * 2.20462f);
            weightCell1.text = (lb100 / 100).ToString("D3");
            weightCell2.text = "." + Mathf.Abs(lb100 % 100).ToString("D2") + "lb";
        }



        float key = vrmChrSceneController.vrmToController.bodyKey / 100f;
        stringCellBox.color = new Color(1.0f, 1f - key * 0.8f, 1f - key * 0.7f);

        string scaleTextlv;
        if (kg100 < 4000) scaleTextlv = "weighScaleLv1";
        else if (kg100 < 4500) scaleTextlv = "weighScaleLv2";
        else if (kg100 < 5000) scaleTextlv = "weighScaleLv3";
        else if (kg100 < 6000) scaleTextlv = "weighScaleLv4";
        else if (kg100 < 8000) scaleTextlv = "weighScaleLv5";
        else if (kg100 < 10000) scaleTextlv = "weighScaleLv6";
        else if (kg100 < 15000) scaleTextlv = "weighScaleLv7";
        else if (kg100 < 20000) scaleTextlv = "weighScaleLv8";
        else if (kg100 < 29999) scaleTextlv = "weighScaleLv9";
        else scaleTextlv = "weighScaleLv10";
        stringCell.text = LocalizationManager.Instance.Get(scaleTextlv);
    }
}
