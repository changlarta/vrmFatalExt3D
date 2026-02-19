using Unity.VisualScripting;
using UnityEngine;

using UnityEngine.SceneManagement;
using TMPro;

public class CanvasUIController : MonoBehaviour
{
    [HideInInspector] public calToastGen calToastGenController;
    [HideInInspector] public foodInfo foodInfo;

    private openSideBar mainSideBarOpen;
    private openSideBar workOutSideBarOpen;
    private openSideBar shopSideBarOpen;
    private openSideBar configideBarOpen;

    private GameObject loadingGameObject;
    private GameObject devButton;
    private GameObject closeDialogObject;
    private GameObject yjsDialogObject;

    [HideInInspector] public mainSideBar mainSideBar;
    [HideInInspector] public WorkoutSideBar workoutSideBar;

    void Start()
    {
        loadingGameObject = transform.GetChild(5).gameObject;
        loadingGameObject.SetActive(false);

        foodInfo = transform.GetChild(8).GetComponent<foodInfo>();

        mainSideBarOpen = transform.GetChild(9).GetComponent<openSideBar>();
        mainSideBar = transform.GetChild(9).GetComponent<mainSideBar>();

        shopSideBarOpen = transform.GetChild(10).GetComponent<openSideBar>();

        workOutSideBarOpen = transform.GetChild(11).GetComponent<openSideBar>();
        workoutSideBar = transform.GetChild(11).GetComponent<WorkoutSideBar>();

        configideBarOpen = transform.GetChild(12).GetComponent<openSideBar>();

        var rSideBar = transform.GetChild(14).gameObject;
        devButton = rSideBar.transform.GetChild(3).gameObject;
        devButton.SetActive(false);

        calToastGenController = transform.GetChild(16).GetComponent<calToastGen>();

        closeDialogObject = transform.GetChild(18).gameObject;
        closeDialogObject.SetActive(false);

        yjsDialogObject = transform.GetChild(19).gameObject;
        yjsDialogObject.SetActive(false);
    }

    void Update()
    {
        var inst = VrmChrSceneController.Instance;
        var ctr = inst.vrmToController;
        loadingGameObject.SetActive(!ctr.IsReady);

        if (loadingGameObject.activeSelf)
        {
            loadingGameObject.transform.Rotate(0, 0, -180f * Time.deltaTime);
        }

        devButton.SetActive(inst.enableDevMood);
    }

    public void onClickMainButton()
    {
        if (mainSideBarOpen.isActive)
        {
            mainSideBarOpen.close();
        }
        else
        {
            mainSideBarOpen.open();
            shopSideBarOpen.close();
            workOutSideBarOpen.close();
        }
    }

    public void onClickShopButton()
    {
        if (shopSideBarOpen.isActive)
        {
            shopSideBarOpen.close();
        }
        else
        {
            shopSideBarOpen.open();
            mainSideBarOpen.close();
            workOutSideBarOpen.close();
        }
    }

    public void onClickWorkOutButton()
    {
        if (workOutSideBarOpen.isActive)
        {
            workOutSideBarOpen.close();
        }
        else
        {
            workOutSideBarOpen.open();
            shopSideBarOpen.close();
            mainSideBarOpen.close();
        }
    }

    public void onClickConfigButton()
    {
        if (configideBarOpen.isActive)
        {
            configideBarOpen.close();
        }
        else
        {
            configideBarOpen.open();
        }
    }

    public void onClickExitButton()
    {
        AudioManager.Instance.PlaySE("card");
        var isActive = closeDialogObject.activeSelf;
        yjsDialogObject.SetActive(false);
        closeDialogObject.SetActive(!isActive);
    }
    public void onClickCloseDialogButtonCancel()
    {
        AudioManager.Instance.PlaySE("click");
        closeDialogObject.SetActive(false);
    }
    public void onClickCloseDialogYes()
    {
        AudioManager.Instance.PlaySE("click");
        SceneManager.LoadScene("sweetGameSettings");
    }


    public void OnClickYjsButton()
    {
        if (IsYjsStore.isYjsMode)
        {
            IsYjsStore.isYjsMode = false;
            AudioManager.Instance.PlayBGM("bgm1");
            return;
        }
        AudioManager.Instance.PlaySE(yjsDialogObject.activeSelf ? "click" : "warn");
        var isActive = yjsDialogObject.activeSelf;
        closeDialogObject.SetActive(false);
        yjsDialogObject.SetActive(!isActive);
    }

    public void OnClickYjsDialogCancel()
    {
        AudioManager.Instance.PlaySE("click");
        yjsDialogObject.SetActive(false);
    }

    public void OnClickYjsDialogYes()
    {
        IsYjsStore.isYjsMode = !IsYjsStore.isYjsMode;
        AudioManager.Instance.PlayBGM("bigyajue");
        yjsDialogObject.SetActive(false);
    }

    public void changeIsKgViewMode(bool isKgViewMode)
    {
        SettingStore.isKgViewMode = isKgViewMode;
    }
}
