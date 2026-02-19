using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal.Internal;

public class SweetGameSettingCanvas : MonoBehaviour
{

    private RectTransform pagesRt;
    private openSideBar configideBarOpen;

    private float targetX;
    private float velX;

    private const float Accel = 8000f;
    private const float MaxSpeed = 3000f;
    private const float StopEpsilon = 0.2f;

    private GameObject secretGameObject;

    void Start()
    {
        pagesRt = transform.GetChild(0).GetComponent<RectTransform>();
        configideBarOpen = transform.GetChild(1).GetComponent<openSideBar>();

        targetX = pagesRt.anchoredPosition.x;
        velX = 0f;

        secretGameObject = transform.GetChild(0).GetChild(0).GetChild(3).gameObject;
        secretGameObject.SetActive(false);
    }

    void Update()
    {
        AnimatePagesX();
    }

    private void AnimatePagesX()
    {
        if (pagesRt == null) return;

        var p = pagesRt.anchoredPosition;
        float dx = targetX - p.x;

        if (Mathf.Abs(dx) <= StopEpsilon)
        {
            p.x = targetX;
            pagesRt.anchoredPosition = p;
            velX = 0f;
            return;
        }

        float dir = Mathf.Sign(dx);

        velX += dir * Accel * Time.deltaTime;
        velX = Mathf.Clamp(velX, -MaxSpeed, MaxSpeed);

        float step = velX * Time.deltaTime;

        if (Mathf.Abs(step) >= Mathf.Abs(dx))
        {
            p.x = targetX;
            pagesRt.anchoredPosition = p;
            velX = 0f;
            return;
        }

        p.x += step;
        pagesRt.anchoredPosition = p;
    }

    public void onClickConfigButton()
    {
        if (configideBarOpen == null) return;

        if (configideBarOpen.isActive) configideBarOpen.close();
        else configideBarOpen.open();
    }

    public void onClickExitButton()
    {
        AudioManager.Instance.StopBGM();
        AudioManager.Instance.PlaySE("click");
        SceneManager.LoadScene("title");
    }


    public void OnTapSelectButton()
    {
        AudioManager.Instance.PlaySE("click");
        targetX = -800;
        velX = 0f;

        IEnumerator DelaySetActiveObject(bool active)
        {
            yield return new WaitForSeconds(0.5f);
            secretGameObject.SetActive(false);
            var inst = SweetGameSettingsSceneController.Instance;
            if (inst != null) inst.SetActiveObject(active);
        }


        StartCoroutine(DelaySetActiveObject(true));
    }

    public void OnTapSelectBackButton()
    {
        AudioManager.Instance.PlaySE("click");
        targetX = 0;
        velX = 0f;

        var rand = Random.Range(0, 50);
        if (rand < 1)
        {
            secretGameObject.SetActive(true);
        }

        var inst = SweetGameSettingsSceneController.Instance;
        if (inst != null) inst.SetActiveObject(false);
    }

    public void changeIsKgViewMode(bool isKgViewMode)
    {
        SettingStore.isKgViewMode = isKgViewMode;
    }
}
