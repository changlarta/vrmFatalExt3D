using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class foodInfo : MonoBehaviour
{
    private GameObject imageObj;

    private TextMeshProUGUI nameText;
    private TextMeshProUGUI calText;
    private TextMeshProUGUI descriptionText;

    void Start()
    {
        gameObject.SetActive(false);
        imageObj = transform.GetChild(0).gameObject;
        nameText = transform.GetChild(1).GetComponent<TextMeshProUGUI>();
        calText = transform.GetChild(2).GetComponent<TextMeshProUGUI>();
        descriptionText = transform.GetChild(3).GetComponent<TextMeshProUGUI>();
    }

    public void setInfo(ItemInfo info, Sprite itemSprite, bool isLocked)
    {
        var img = imageObj.GetComponent<Image>();
        img.sprite = itemSprite;

        if (isLocked)
        {
            img.color = Color.black;
            nameText.text = "???";
            calText.text = "???kcal";
            descriptionText.text = LocalizationManager.Instance.Get("unpurchasedText");
        }
        else
        {
            img.color = Color.white;
            nameText.text = LocalizationManager.Instance.Get(info.itemName);
            calText.text = (info.cal * info.eatSteps).ToString() + "0kcal";
            descriptionText.text = LocalizationManager.Instance.Get(info.description);
        }

        gameObject.SetActive(true);
    }

    public void hide()
    {
        gameObject.SetActive(false);
    }
}
