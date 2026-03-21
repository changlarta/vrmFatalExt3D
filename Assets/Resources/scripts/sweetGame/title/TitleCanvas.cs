using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class TitleCanvas : MonoBehaviour
{

    private bool _isTransitioning = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
    }

    // Update is called once per frame
    void Update()
    {

    }



    public void OnTapGithubLink()
    {
        var url = "https://github.com/syfty0/vrmFatalExt3D";
        if (string.IsNullOrWhiteSpace(url)) return;
        Application.OpenURL(url);
    }

    public IEnumerator LoadSceneMoveGame()
    {
        yield return new WaitForSeconds(0.5f);
        SceneManager.LoadScene("moveGame");
    }

    public void OnTapStartGameMoveGame()
    {
        if (_isTransitioning) return;
        _isTransitioning = true;

        AudioManager.Instance.PlaySE("titleButton");
        StartCoroutine(LoadSceneMoveGame());
    }

    private IEnumerator LoadSceneAfterDelay()
    {
        yield return new WaitForSeconds(0.5f);
        SceneManager.LoadScene("SweetGameSettings");
    }
    public void OnTapStartGame()
    {
        if (_isTransitioning) return;
        _isTransitioning = true;

        AudioManager.Instance.PlaySE("titleButton");
        StartCoroutine(LoadSceneAfterDelay());
    }
}
