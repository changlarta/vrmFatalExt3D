using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public enum EatSE
{
    soft,
    snack,
    drink,
    drag,
    sugar,
}

[System.Serializable]
public struct ItemInfo
{
    public int id;
    public string itemName;
    public int cal;
    public int price;
    public int eatSteps;
    public float careStomach;
    public float addMaxStomach;
    public float addStomachSpeed;
    public float faceCal;
    public float bustCal;
    public bool isHose;
    public string description;
    public EatSE isDrinkSE;
    public int level;
    public Sprite icon;

    public ItemInfo(
        int id,
        string itemName,
        int cal,
        int price,
        int eatSteps,
        float careStomach,
        float addMaxStomach,
        float addStomachSpeed,
        float faceCal,
        float bustCal,
        bool isHose,
        string description,
        EatSE isDrinkSE,
        int level,
        Sprite icon)
    {
        this.id = id;
        this.itemName = itemName;
        this.cal = cal;
        this.price = price;
        this.eatSteps = eatSteps;
        this.careStomach = careStomach;
        this.addMaxStomach = addMaxStomach;
        this.addStomachSpeed = addStomachSpeed;
        this.faceCal = faceCal;
        this.bustCal = bustCal;
        this.isHose = isHose;
        this.description = description;
        this.isDrinkSE = isDrinkSE;
        this.level = level;
        this.icon = icon;
    }

    public static ItemInfo P(
        int cal = 1,
        int price = 1,
        int eatSteps = 3,
        float careStomach = 0f,
        float addMaxStomach = 0.05f,
        float addStomachSpeed = 0.005f,
        float faceCal = 0,
        float bustCal = 0,
        bool isHose = false,
        EatSE isDrinkSE = EatSE.soft,
        int level = 0)
    {
        return new ItemInfo(
            0,
            null,
            cal,
            price,
            eatSteps,
            careStomach,
            addMaxStomach,
            addStomachSpeed,
            faceCal,
            bustCal,
            isHose,
            null,
            isDrinkSE,
            level,
            null
        );
    }
}

public class mainSideBar : MonoBehaviour
{
    private const int ItemCount = 46;

    private static readonly float[] ColX = { -90f, -25f, 40f };
    private const float StartY = -250f;
    private const float RowStepY = -90f;
    private const int ColCount = 3;

    private const float BaseContentHeight = 450f;

    private bool[] unlockedByIndex;

    public bool IsItemUnlocked(int itemIndex)
    {
        if (unlockedByIndex == null) return false;
        if (itemIndex < 0 || itemIndex >= unlockedByIndex.Length) return false;
        return unlockedByIndex[itemIndex];
    }

    public void SetItemUnlocked(int itemIndex, bool unlocked)
    {
        if (unlockedByIndex == null) return;
        if (itemIndex < 0 || itemIndex >= unlockedByIndex.Length) return;
        unlockedByIndex[itemIndex] = unlocked;
    }

    private static readonly ItemInfo[] ItemDefaults = new ItemInfo[ItemCount]
    {
        ItemInfo.P(level:0,price:1,cal:5,eatSteps:1,careStomach:1),
        ItemInfo.P(level:0,price:50,cal:15,careStomach:7,isDrinkSE: EatSE.snack),
        ItemInfo.P(level:0,price:100,cal:25,careStomach:7),
        ItemInfo.P(level:4,price:400,cal:60,careStomach:12),
        ItemInfo.P(level:2,price:300,cal:20,careStomach:5),
        ItemInfo.P(level:2,price:1500,cal:60,eatSteps:5,careStomach:10),
        ItemInfo.P(level:1,price:2000,cal:60,careStomach:7),
        ItemInfo.P(level:1,price:16000,cal:144,eatSteps:7,careStomach:36),
        ItemInfo.P(level:1,price:300,cal:20,careStomach:20,isDrinkSE: EatSE.drink),
        ItemInfo.P(level:1,price:500,cal:30,careStomach:7,isDrinkSE: EatSE.snack),

        ItemInfo.P(level:1,price:3000,cal:60,eatSteps:5,careStomach:7,addStomachSpeed:-0.01f),
        ItemInfo.P(level:1,price:2000,cal:100,careStomach:10),
        ItemInfo.P(level:2,price:200,cal:20,careStomach:7,isDrinkSE: EatSE.snack),
        ItemInfo.P(level:2,price:1000,cal:50,careStomach:10,isDrinkSE: EatSE.drink),
        ItemInfo.P(level:2,price:500,cal:40,careStomach:7),
        ItemInfo.P(level:4,price:750,cal:40,careStomach:10),
        ItemInfo.P(level:1,price:300,cal:30,careStomach:10,isDrinkSE: EatSE.snack),
        ItemInfo.P(level:1,price:800,cal:60,careStomach:5,isDrinkSE: EatSE.drink),
        ItemInfo.P(level:3,price:1500,cal:40,careStomach:7),
        ItemInfo.P(level:4,price:1500,cal:40,eatSteps:4,careStomach:10),

        ItemInfo.P(level:2,price:600,cal:30,eatSteps:4,careStomach:10),
        ItemInfo.P(level:3,price:400,cal:80,eatSteps:4,careStomach:5,faceCal:1f,isDrinkSE: EatSE.drink),
        ItemInfo.P(level:2,price:2000,cal:40,careStomach:10),
        ItemInfo.P(level:2,price:1500,cal:20,careStomach:15),
        ItemInfo.P(level:2,price:1500,cal:20,careStomach:15),
        ItemInfo.P(level:2,price:2000,cal:16,careStomach:20),
        ItemInfo.P(level:2,price:10000,cal:40,careStomach:1),
        ItemInfo.P(level:3,price:4000,cal:50,eatSteps:5,careStomach:10,addStomachSpeed:-0.01f),
        ItemInfo.P(level:3,price:8000,cal:40,eatSteps:5,careStomach:10,addStomachSpeed:-0.01f),
        ItemInfo.P(level:3,price:8000,cal:50,eatSteps:5,careStomach:10,addStomachSpeed:-0.01f,isDrinkSE: EatSE.snack),

        ItemInfo.P(level:3,price:6000,cal:50,eatSteps:5,careStomach:10,addStomachSpeed:-0.01f),
        ItemInfo.P(level:3,price:12000,cal:60,eatSteps:20,careStomach:10),
        ItemInfo.P(level:2,price:800,cal:30,careStomach:10,isDrinkSE: EatSE.drink),
        ItemInfo.P(level:1,price:400,cal:0,careStomach:30,addStomachSpeed:-0.01f,addMaxStomach:-0.05f,isDrinkSE: EatSE.drink),
        ItemInfo.P(level:3,price:100,cal:40,careStomach:10,bustCal:0.5f,isDrinkSE: EatSE.drink),
        ItemInfo.P(level:3,price:-100,cal:40,careStomach:10,bustCal:-0.5f,isDrinkSE: EatSE.drink),
        ItemInfo.P(level:3,price:800,cal:16,careStomach:5,addMaxStomach:-0.2f,isDrinkSE: EatSE.drink),
        ItemInfo.P(level:3,price:20000,cal:16,careStomach:5,addMaxStomach:-0.2f,isDrinkSE: EatSE.drink),
        ItemInfo.P(level:1,price:400,cal:2,careStomach:15,faceCal:-0.5f,addStomachSpeed:-0.01f,addMaxStomach:-0.05f,isDrinkSE: EatSE.snack),
        ItemInfo.P(level:0,price:400,cal:3,careStomach:15,addStomachSpeed:-0.01f,addMaxStomach:-0.05f,isDrinkSE: EatSE.snack),

        ItemInfo.P(level:1,price:50000,cal:3,careStomach:15,addStomachSpeed:-0.01f,addMaxStomach:-0.05f,isDrinkSE: EatSE.snack),
        ItemInfo.P(level:2,price:400,cal:1,careStomach:15,eatSteps:5,addStomachSpeed:-0.01f,addMaxStomach:-0.05f,isDrinkSE: EatSE.snack),
        ItemInfo.P(level:2,price:4000,cal:0,careStomach:-30),
        ItemInfo.P(level:3,price:10000,cal:-3000,careStomach:-30,addStomachSpeed:2f,addMaxStomach:30f,eatSteps: 1,isDrinkSE: EatSE.drag),
        ItemInfo.P(level:4,price:90000,cal:10000,eatSteps: 1,isDrinkSE: EatSE.sugar),
        ItemInfo.P(level:4,price:90000,cal:100,eatSteps: 1,careStomach:10,isDrinkSE: EatSE.drink,isHose:true)
    };

    private static ItemInfo[] itemsFixed;

    private TextMeshProUGUI coinLvText;
    private TextMeshProUGUI nextCoinLvValue;
    private TextMeshProUGUI coinText;
    private TextMeshProUGUI workButtonText;

    [Header("Item")]
    [SerializeField] private GameObject itemPrefab;

    private Transform content;
    private RectTransform contentRect;

    private readonly List<GameObject> spawnedItems = new List<GameObject>();

    private int itemLv = 0;


    private void Start()
    {
        content = transform.GetChild(0).GetChild(0).GetChild(0);
        contentRect = content.GetComponent<RectTransform>();

        workButtonText = content.GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>();

        coinLvText = content.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>();
        nextCoinLvValue = content.GetChild(1).GetChild(3).GetComponent<TextMeshProUGUI>();

        coinText = content.GetChild(2).GetChild(0).GetComponent<TextMeshProUGUI>();

        if (unlockedByIndex == null || unlockedByIndex.Length != ItemCount)
        {
            unlockedByIndex = new bool[ItemCount];
        }

        if (itemsFixed == null || itemsFixed.Length != ItemCount)
        {
            itemsFixed = BuildFixedItems();
        }

        RegenerateItemList();
    }

    private void Update()
    {
        var inst = VrmChrSceneController.Instance;
        coinText.text = inst.coin.ToString();

        var coinLvText0 = LocalizationManager.Instance.Get("coinLvText0");
        coinLvText.text = coinLvText0 + inst.workLv.ToString("D3");

        nextCoinLvValue.text = CalculateNextLvCost(inst.workLv).ToString();

        workButtonText.text = LocalizationManager.Instance.Get("workButtonText");
    }

    public void onClickWorkButton()
    {
        var inst = VrmChrSceneController.Instance;
        inst.coin += inst.workLv;
        AudioManager.Instance.PlaySE("click");
    }

    public void onClickLvButton()
    {
        var inst = VrmChrSceneController.Instance;
        int cost = CalculateNextLvCost(inst.workLv);
        if (inst.coin >= cost)
        {
            inst.coin -= cost;
            inst.workLv += 1;
            AudioManager.Instance.PlaySE("fanfare");
        }
        else
        {
            AudioManager.Instance.PlaySE("beep");
        }
    }

    private int CalculateNextLvCost(int lv)
    {
        return (1 + lv) * (1 + lv) / 2;
    }

    public void SetItemLv(int newItemLv)
    {
        if (newItemLv < 0) newItemLv = 0;
        if (itemLv == newItemLv) return;

        itemLv = newItemLv;
        RegenerateItemList();
    }

    public int GetItemLv()
    {
        return itemLv;
    }

    private static ItemInfo[] BuildFixedItems()
    {
        var arr = new ItemInfo[ItemCount];

        for (int i = 1; i <= ItemCount; i++)
        {
            string name = "item" + i + "name";
            string desc = "item" + i + "description";
            Sprite icon = Resources.Load<Sprite>("images/foods/" + i);

            var baseInfo = ItemDefaults[i - 1];

            arr[i - 1] = new ItemInfo(
                i,
                name,
                baseInfo.cal,
                baseInfo.price,
                baseInfo.eatSteps,
                baseInfo.careStomach,
                baseInfo.addMaxStomach,
                baseInfo.addStomachSpeed,
                baseInfo.faceCal,
                baseInfo.bustCal,
                baseInfo.isHose,
                desc,
                baseInfo.isDrinkSE,
                baseInfo.level,
                icon
            );
        }

        return arr;
    }

    private void RegenerateItemList()
    {
        for (int i = 0; i < spawnedItems.Count; i++)
        {
            if (spawnedItems[i] != null) Destroy(spawnedItems[i]);
        }
        spawnedItems.Clear();

        if (content == null || itemPrefab == null || itemsFixed == null)
        {
            ResizeContentHeight(0);
            return;
        }

        int visibleIndex = 0;

        for (int i = 0; i < itemsFixed.Length; i++)
        {
            var info = itemsFixed[i];
            if (itemLv < info.level) continue;

            var go = Instantiate(itemPrefab, content, false);
            spawnedItems.Add(go);

            int col = visibleIndex % ColCount;
            int row = visibleIndex / ColCount;

            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchoredPosition = new Vector2(ColX[col], StartY + RowStepY * row);
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
            }
            else
            {
                go.transform.localPosition = new Vector3(ColX[col], StartY + RowStepY * row, 0f);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }

            var img = go.GetComponentInChildren<Image>(true);
            if (img != null) img.sprite = info.icon;

            if (info.isHose)
            {
                var old = go.GetComponent<LongPressDragSpawner>();
                if (old != null) Destroy(old);

                var hose = go.GetComponent<HoseLongPressDragSpawner>();
                if (hose == null) hose = go.AddComponent<HoseLongPressDragSpawner>();
                hose.Initialize(info, i, this);
            }
            else
            {
                var spawner = go.GetComponent<LongPressDragSpawner>();
                if (spawner != null) spawner.Initialize(info, i, this);
            }

            visibleIndex++;
        }

        ResizeContentHeight(visibleIndex);
    }

    private void ResizeContentHeight(int visibleCount)
    {
        if (contentRect == null) return;

        int rowCount = (visibleCount <= 0) ? 0 : ((visibleCount - 1) / ColCount + 1);

        float height = BaseContentHeight;
        if (rowCount >= 3)
        {
            height += (rowCount - 2) * Mathf.Abs(RowStepY);
        }

        var size = contentRect.sizeDelta;
        size.y = height;
        contentRect.sizeDelta = size;
    }
}
