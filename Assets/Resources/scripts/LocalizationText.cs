using UnityEngine;
using TMPro;
using System;

public class LocalizationText : MonoBehaviour
{
    public String key;
    private TextMeshProUGUI tmp;
    void Start()
    {
        tmp = transform.GetComponent<TextMeshProUGUI>();
    }

    void Update()
    {
        if (tmp != null)
        {
            tmp.text = LocalizationManager.Instance.Get(key);
        }
    }
}
