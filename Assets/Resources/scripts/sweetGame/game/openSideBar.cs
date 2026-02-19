using UnityEngine;
using TMPro;

public class openSideBar : MonoBehaviour
{
    private RectTransform rt;
    public bool isActive;
    public bool firstOpen = true;
    public bool isRightSide = true;
    private float targetX;

    private float velX = 0f;

    private const float Accel = 8000f;
    private const float MaxSpeed = 3000f;
    private const float StopEpsilon = 0.2f;

    private TextMeshProUGUI coinText;

    private void Awake()
    {
        rt = GetComponent<RectTransform>();

        isActive = firstOpen;
        targetX = isActive ? 0f : (isRightSide ? 300f : -300f);

        var p = rt.anchoredPosition;
        p.x = targetX;
        rt.anchoredPosition = p;

        velX = 0f;
    }

    private void Update()
    {
        if (rt == null) return;

        var p = rt.anchoredPosition;
        float dx = targetX - p.x;

        if (Mathf.Abs(dx) <= StopEpsilon)
        {
            p.x = targetX;
            rt.anchoredPosition = p;
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
            rt.anchoredPosition = p;
            velX = 0f;
            return;
        }

        p.x += step;
        rt.anchoredPosition = p;
    }

    public void toggle()
    {
        if (isActive)
        {
            close();
        }
        else
        {
            open();
        }
    }


    public void open()
    {
        isActive = true;
        targetX = 0f;
        velX = 0f;

        AudioManager.Instance.PlaySE("card");
    }

    public void close()
    {
        isActive = false;
        targetX = isRightSide ? 300f : -300f;
        velX = 0f;
    }
}
