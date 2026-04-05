using UnityEngine;
using UnityEngine.UI;

public sealed class TitleBodyVariantSwitch : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private moveGameSceneController sceneController;
    [SerializeField] private Button button;
    [SerializeField] private Image switchImage;

    [Header("Sprites")]
    [SerializeField] private Sprite offSprite;
    [SerializeField] private Sprite onSprite;

    [Header("Body Variants")]
    [SerializeField] private BodyVariant offVariant = BodyVariant.Cooking;
    [SerializeField] private BodyVariant onVariant = BodyVariant.Normal_Bikini_Pink;

    [Header("Initial State")]
    [SerializeField] private bool isOn = false;

    private void Reset()
    {
        if (button == null) button = GetComponent<Button>();
        if (switchImage == null) switchImage = GetComponent<Image>();
    }

    private void Awake()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnClickSwitch);
            button.onClick.AddListener(OnClickSwitch);
        }

        RefreshVisual();
    }

    public void OnClickSwitch()
    {
        if (sceneController == null) return;

        bool nextIsOn = !isOn;
        BodyVariant nextVariant = nextIsOn ? onVariant : offVariant;

        bool accepted = sceneController.RequestTitleBodyVariantReload(nextVariant);
        if (!accepted) return;

        isOn = nextIsOn;
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        if (switchImage == null) return;
        switchImage.sprite = isOn ? onSprite : offSprite;
    }
}