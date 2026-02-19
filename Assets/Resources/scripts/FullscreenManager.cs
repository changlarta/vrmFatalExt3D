using UnityEngine;

[DisallowMultipleComponent]
public class FullscreenManager : MonoBehaviour
{
    private static FullscreenManager _instance;
    public static FullscreenManager Instance
    {
        get { EnsureInstance(); return _instance; }
    }

    private const int WindowWidth = 960;
    private const int WindowHeight = 540;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap() => EnsureInstance();

    private static void EnsureInstance()
    {
        if (_instance != null) return;
        _instance = FindAnyObjectByType<FullscreenManager>();
        if (_instance != null) return;

        var go = new GameObject(nameof(FullscreenManager));
        _instance = go.AddComponent<FullscreenManager>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);

#if !UNITY_WEBGL || UNITY_EDITOR
        SetWindowed(); // Standaloneだけ起動時に小ウィンドウへ
#endif
    }

    public void SetWindowed()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return; // WebGLはブラウザ(canvas)がサイズを決める
#else
        Screen.fullScreenMode = FullScreenMode.Windowed;
        Screen.SetResolution(WindowWidth, WindowHeight, false);
#endif
    }

    public void SetFullscreen()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return;
#else
        Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
        var r = Screen.currentResolution;
        Screen.SetResolution(r.width, r.height, true);
#endif
    }
}
