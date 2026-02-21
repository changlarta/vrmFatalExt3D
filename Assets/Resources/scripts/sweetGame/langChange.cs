using UnityEngine;
using Unity.VisualScripting;
using UnityEngine.UI;

public class langChange : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        var jpLangBtn = transform.GetChild(0).AddComponent<Button>();
        jpLangBtn.onClick.AddListener(() => LocalizationManager.Instance.ChangeLang(0));
        var engLangBtn = transform.GetChild(1).AddComponent<Button>();
        engLangBtn.onClick.AddListener(() => LocalizationManager.Instance.ChangeLang(1));
    }

    // Update is called once per frame
    void Update()
    {

    }
}
