using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ShopSideBar : MonoBehaviour
{
    private const int ItemLvMax = 4;
    private const int WorkoutLvMax = 5;

    private const string ClothTitleKey = "shopClothSectionTitle";
    private const string BackgroundTitleKey = "shopBackgroundSectionTitle";
    private const string BgmTitleKey = "shopBgmSectionTitle";
    private const string OpenDevTitleKey = "shopOpenDevButtonTitle";

    private const string BackgroundTexProp = "_MainTex";

    [SerializeField] private Material backgroundTargetMaterial;

    private class ShopEntry
    {
        public int price;
        public bool purchased;
        public TextMeshProUGUI priceText;
        public Image iconImage;
        public ShopEntry(int price) { this.price = price; }
    }

    private readonly ShopEntry[] cloth = new ShopEntry[9]
    {
        new ShopEntry(0),
        new ShopEntry(0),
        new ShopEntry(8000),
        new ShopEntry(16000),
        new ShopEntry(16000),
        new ShopEntry(200000),
        new ShopEntry(400000),
        new ShopEntry(400000),
        new ShopEntry(9999999),
    };

    private readonly ShopEntry[] backgrounds = new ShopEntry[6]
    {
        new ShopEntry(0),
        new ShopEntry(0),
        new ShopEntry(10000),
        new ShopEntry(10000),
        new ShopEntry(10000),
        new ShopEntry(500000),
    };

    private readonly ShopEntry[] bgms = new ShopEntry[3]
    {
        new ShopEntry(0),
        new ShopEntry(5000),
        new ShopEntry(5000),
    };

    private readonly ShopEntry openDev = new ShopEntry(999999);

    private TextMeshProUGUI coinText;

    private TextMeshProUGUI itemLvTitleText;
    private TextMeshProUGUI itemLvCostText;

    private TextMeshProUGUI workoutLvTitleText;
    private TextMeshProUGUI workoutLvCostText;

    private TextMeshProUGUI clothTitleText;
    private TextMeshProUGUI backgroundTitleText;
    private TextMeshProUGUI bgmTitleText;

    private TextMeshProUGUI openDevTitleText;
    private TextMeshProUGUI openDevCostText;

    private mainSideBar mainSB;
    private WorkoutSideBar workoutSB;

    private void Start()
    {
        var content = transform.GetChild(0).GetChild(0).GetChild(0);

        coinText = content.GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>();

        var itemLvSection = content.GetChild(1);
        itemLvTitleText = itemLvSection.GetChild(0).GetComponent<TextMeshProUGUI>();
        itemLvCostText = itemLvSection.GetChild(1).GetChild(2).GetComponent<TextMeshProUGUI>();
        itemLvSection.gameObject.AddComponent<Button>().onClick.AddListener(OnTapItemLevelUp);

        var workoutLvSection = content.GetChild(2);
        workoutLvTitleText = workoutLvSection.GetChild(0).GetComponent<TextMeshProUGUI>();
        workoutLvCostText = workoutLvSection.GetChild(1).GetChild(2).GetComponent<TextMeshProUGUI>();
        workoutLvSection.gameObject.AddComponent<Button>().onClick.AddListener(OnTapWorkoutLevelUp);

        var clothSection = content.GetChild(3);
        clothTitleText = clothSection.GetChild(0).GetComponent<TextMeshProUGUI>();
        SetupListItems(clothSection, cloth, OnTapClothItem, false);

        var backgroundSection = content.GetChild(4);
        backgroundTitleText = backgroundSection.GetChild(0).GetComponent<TextMeshProUGUI>();
        SetupListItems(backgroundSection, backgrounds, OnTapBackGroundItem, true);

        var bgmSection = content.GetChild(5);
        bgmTitleText = bgmSection.GetChild(0).GetComponent<TextMeshProUGUI>();
        SetupListItems(bgmSection, bgms, OnTapBgmItem, false);

        var openDevSection = content.GetChild(6);
        openDevTitleText = openDevSection.GetChild(0).GetComponent<TextMeshProUGUI>();
        openDevCostText = openDevSection.GetChild(1).GetChild(2).GetComponent<TextMeshProUGUI>();
        openDevSection.gameObject.AddComponent<Button>().onClick.AddListener(OnTapOpenDevButtonWrapper);

        var canvas = VrmChrSceneController.Instance.canvasUI;
        mainSB = canvas.transform.GetChild(9).GetComponent<mainSideBar>();
        workoutSB = canvas.transform.GetChild(11).GetComponent<WorkoutSideBar>();

        ApplyLocalizedTexts();
        ApplyOpenDevCostText();
        var inst = VrmChrSceneController.Instance;
        ApplyBackgroundByIndex(inst.defaultBackgroundIndex);
    }

    private void Update()
    {
        coinText.text = VrmChrSceneController.Instance.coin.ToString();
        ApplyLocalizedTexts();
        ApplyOpenDevCostText();
    }

    private void ApplyLocalizedTexts()
    {
        var lm = LocalizationManager.Instance;

        clothTitleText.text = lm.Get(ClothTitleKey);
        backgroundTitleText.text = lm.Get(BackgroundTitleKey);
        bgmTitleText.text = lm.Get(BgmTitleKey);
        openDevTitleText.text = lm.Get(OpenDevTitleKey);

        int itemLv = mainSB.GetItemLv();
        int workoutLv = workoutSB.GetWorkoutLv();

        string maxText = lm.Get("shopMax");

        itemLvTitleText.text = string.Format(lm.Get("shopItemLevelTitle"), itemLv, ItemLvMax);
        itemLvCostText.text = (itemLv >= ItemLvMax) ? maxText : CalcNextItemLvCost(itemLv).ToString();

        workoutLvTitleText.text = string.Format(lm.Get("shopWorkoutLevelTitle"), workoutLv, WorkoutLvMax);
        workoutLvCostText.text = (workoutLv >= WorkoutLvMax) ? maxText : CalcNextWorkoutLvCost(workoutLv).ToString();
    }

    private void ApplyOpenDevCostText()
    {
        openDevCostText.text = openDev.purchased ? "--" : openDev.price.ToString();
    }

    private void SetupListItems(Transform section, ShopEntry[] entries, System.Action<int> onTap, bool captureIconImage)
    {
        for (int id = 0; id < entries.Length; id++)
        {
            var entry = entries[id];
            var itemTr = section.GetChild(1 + id);

            entry.priceText = itemTr.GetChild(2).GetComponent<TextMeshProUGUI>();

            if (captureIconImage)
            {
                entry.iconImage = itemTr.GetComponent<Image>();
            }

            if (entry.price == 0)
            {
                entry.purchased = true;
                entry.priceText.text = "--";
            }
            else
            {
                entry.purchased = false;
                entry.priceText.text = entry.price.ToString();
            }

            int captured = id;
            itemTr.gameObject.AddComponent<Button>().onClick.AddListener(() => onTap(captured));
        }
    }

    private int CalcNextItemLvCost(int currentLv)
    {
        int exp = currentLv + 2;
        int v = 1;
        for (int i = 0; i < exp; i++) v *= 10;
        v = v * 5;
        return v;
    }

    private int CalcNextWorkoutLvCost(int currentLv)
    {
        return 5000 * (currentLv + 1);
    }

    private void ApplyBackgroundByIndex(int id)
    {
        if (backgroundTargetMaterial == null) return;
        if (id < 0 || id >= backgrounds.Length) return;

        var img = backgrounds[id].iconImage;
        if (img == null) return;

        var sp = img.sprite;
        if (sp == null) return;

        var tex = sp.texture;
        if (tex == null) return;

        backgroundTargetMaterial.SetTexture(BackgroundTexProp, tex);
    }

    private void OnTapItemLevelUp()
    {
        var inst = VrmChrSceneController.Instance;

        int cur = mainSB.GetItemLv();
        if (cur >= ItemLvMax)
        {
            AudioManager.Instance.PlaySE("beep");
            return;
        }

        int cost = CalcNextItemLvCost(cur);
        if (inst.coin < cost)
        {
            AudioManager.Instance.PlaySE("beep");
            return;
        }

        inst.coin -= cost;
        mainSB.SetItemLv(cur + 1);
        AudioManager.Instance.PlaySE("fanfare");
    }

    private void OnTapWorkoutLevelUp()
    {
        var inst = VrmChrSceneController.Instance;

        int cur = workoutSB.GetWorkoutLv();
        if (cur >= WorkoutLvMax)
        {
            AudioManager.Instance.PlaySE("beep");
            return;
        }

        int cost = CalcNextWorkoutLvCost(cur);
        if (inst.coin < cost)
        {
            AudioManager.Instance.PlaySE("beep");
            return;
        }

        inst.coin -= cost;
        workoutSB.SetWorkoutLv(cur + 1);
        AudioManager.Instance.PlaySE("fanfare");
    }

    private void OnTapClothItem(int id)
    {
        var inst = VrmChrSceneController.Instance;
        var ctr = inst.vrmToController;
        if (!ctr.IsReady) return;

        var entry = cloth[id];

        if (!entry.purchased)
        {
            if (inst.coin < entry.price)
            {
                AudioManager.Instance.PlaySE("beep");
                return;
            }

            inst.coin -= entry.price;
            entry.purchased = true;
            entry.priceText.text = "--";
            AudioManager.Instance.PlaySE("fanfare");
        }

        var x = id switch
        {
            0 => BodyVariant.Normal,
            1 => BodyVariant.School,
            2 => BodyVariant.Track,
            3 => BodyVariant.Sifuku,
            4 => BodyVariant.Cooking,
            5 => BodyVariant.Normal_Swim,
            6 => BodyVariant.Normal_Bikini_Blue,
            7 => BodyVariant.Normal_Bikini_Pink,
            8 => BodyVariant.Normal_Nude,
            _ => BodyVariant.Normal
        };

        var data = inst.CurrentVrmData;
        ctr.ReloadFromBytes(data, x, ctr.face3Key, ctr.bodyKey, ctr.bustKey, ctr.height);
    }

    private void OnTapBackGroundItem(int id)
    {
        var inst = VrmChrSceneController.Instance;
        var entry = backgrounds[id];

        if (!entry.purchased)
        {
            if (inst.coin < entry.price)
            {
                AudioManager.Instance.PlaySE("beep");
                return;
            }

            inst.coin -= entry.price;
            entry.purchased = true;
            entry.priceText.text = "--";
            AudioManager.Instance.PlaySE("fanfare");
        }

        ApplyBackgroundByIndex(id);
    }

    private void OnTapBgmItem(int id)
    {
        var inst = VrmChrSceneController.Instance;
        var entry = bgms[id];

        if (!entry.purchased)
        {
            if (inst.coin < entry.price)
            {
                AudioManager.Instance.PlaySE("beep");
                return;
            }

            inst.coin -= entry.price;
            entry.purchased = true;
            entry.priceText.text = "--";
            AudioManager.Instance.PlaySE("fanfare");
        }

        AudioManager.Instance.PlayBGM($"bgm{id + 1}");
    }

    private void OnTapOpenDevButtonWrapper()
    {
        var inst = VrmChrSceneController.Instance;

        if (!openDev.purchased)
        {
            if (inst.coin < openDev.price)
            {
                AudioManager.Instance.PlaySE("beep");
                return;
            }

            inst.coin -= openDev.price;
            openDev.purchased = true;
            AudioManager.Instance.PlaySE("fanfare");
        }

        inst.enableDevMood = true;
    }
}
