using UnityEngine;

public class reColor : MonoBehaviour
{
    public Color color = new Color(1.0f, 0.1f, 1.0f, 1f);
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        var rends = GetComponentsInChildren<Renderer>(true);
        if (rends[0] != null) rends[0].material.color = color;
    }

    // Update is called once per frame
    void Update()
    {

    }
}
