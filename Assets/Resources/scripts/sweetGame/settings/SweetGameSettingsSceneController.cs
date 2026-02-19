using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Runtime.InteropServices; // 使ってないみたいに表示が出るが、必要


#if !UNITY_WEBGL || UNITY_EDITOR
using SFB;
#endif

public sealed class SweetGameSettingsSceneController : MonoBehaviour
{
    public static SweetGameSettingsSceneController Instance { get; private set; }

    [Header("Scene Names")]
    [SerializeField] private string sweetGameSceneName = "sweetGame";

    [Header("UI (Optional)")]
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Preview (Optional)")]
    [SerializeField] private GameObject previewVrmGameObject;
    private float height = 0.8f;

    // Holds current selection in this scene (also copied into SweetGameVrmStore.VrmData)
    public byte[] SelectedVrmData { get; private set; }
    public TextAsset character1;
    public TextAsset character2;
    public TextAsset character3;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void WebGLVrmPicker_Open(string gameObjectName, string callbackMethodName);
    [DllImport("__Internal")] private static extern void WebGLVrmPicker_CopyTo(IntPtr dstPtr);
    [DllImport("__Internal")] private static extern void WebGLVrmPicker_Clear();
#endif

    private void Awake()
    {
        Instance = this;
        AudioManager.Instance.PlayBGM("setting");
        SweetGameVrmStore.VrmData = null;
        previewVrmGameObject.SetActive(false);
        UpdateStatus();
    }

    public void OnTapStartMode1()
    {
        try
        {
            byte[] data = character1.bytes;
            SelectedVrmData = data;

            SetStatus($"Selected: {"character1"} ({data.Length} bytes)");

            SweetGameVrmStore.VrmData = data;
            SweetGameVrmStore.bodyVariant = BodyVariant.School;
            SweetGameVrmStore.height = 0f;
            SweetGameVrmStore.body = 0;
            SweetGameVrmStore.backgroundId = 0;
            SweetGameVrmStore.speechType = SpeechCharacterType.hypnosis;
            SweetGameVrmStore.weightChangeScale = 1.0f;

            AudioManager.Instance.PlaySE("startGame");
            SceneManager.LoadScene(sweetGameSceneName);
        }
        catch (Exception e)
        {
            SetStatus($"Error: {e.Message}");
        }
    }

    public void OnTapStartMode2()
    {
        try
        {
            byte[] data = character2.bytes;
            SelectedVrmData = data;

            SetStatus($"Selected: {"character2"} ({data.Length} bytes)");

            SweetGameVrmStore.VrmData = data;
            SweetGameVrmStore.bodyVariant = BodyVariant.Normal;
            SweetGameVrmStore.height = 0.1f;
            SweetGameVrmStore.body = 10;
            SweetGameVrmStore.backgroundId = 1;
            SweetGameVrmStore.speechType = SpeechCharacterType.cat;
            SweetGameVrmStore.weightChangeScale = 1.0f;

            AudioManager.Instance.PlaySE("startGame");
            SceneManager.LoadScene(sweetGameSceneName);
        }
        catch (Exception e)
        {
            SetStatus($"Error: {e.Message}");
        }
    }

    public void OnTapStartMode3()
    {
        try
        {
            byte[] data = character3.bytes;
            SelectedVrmData = data;

            SetStatus($"Selected: {"character3"} ({data.Length} bytes)");

            SweetGameVrmStore.VrmData = data;
            SweetGameVrmStore.bodyVariant = BodyVariant.School;
            SweetGameVrmStore.height = 0.1f;
            SweetGameVrmStore.body = 45;
            SweetGameVrmStore.backgroundId = 1;
            SweetGameVrmStore.speechType = SpeechCharacterType.haraibako;
            SweetGameVrmStore.weightChangeScale = 0.1f;

            AudioManager.Instance.PlaySE("startGame");
            SceneManager.LoadScene(sweetGameSceneName);
        }
        catch (Exception e)
        {
            SetStatus($"Error: {e.Message}");
        }
    }


    public void SetActiveObject(bool active)
    {
        previewVrmGameObject.SetActive(active);
    }

    public void OnTapLoadVrm()
    {
        var ctr = previewVrmGameObject.GetComponent<VrmToController>();

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: open browser file picker and callback into OnWebGLVrmPicked(lengthString)
        WebGLVrmPicker_Open(gameObject.name, nameof(OnWebGLVrmPicked));
#else
        try
        {
            var extensions = new[]
            {
                new SFB.ExtensionFilter("VRM", "vrm"),
                new SFB.ExtensionFilter("All Files", "*"),
            };

            string[] paths = SFB.StandaloneFileBrowser.OpenFilePanel("Select VRM", "", extensions, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0]))
            {
                SetStatus("No file selected.");
                return;
            }

            string path = paths[0];

            if (!path.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Selected file is not .vrm");
                return;
            }

            byte[] data = File.ReadAllBytes(path);
            if (data == null || data.Length == 0)
            {
                SetStatus("Failed to read VRM file.");
                return;
            }

            ApplyPreview(data);

            SelectedVrmData = data;
            SweetGameVrmStore.bodyVariant = BodyVariant.Normal;
            SweetGameVrmStore.VrmData = data;

            SetStatus($"Selected: {Path.GetFileName(path)} ({data.Length} bytes)");
        }
        catch (Exception e)
        {
            SetStatus($"Error: {e.Message}");
        }
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    // Called from JS via SendMessage(gameObjectName, methodName, lengthString)
    public void OnWebGLVrmPicked(string lengthString)
    {
        if (!int.TryParse(lengthString, out int len) || len <= 0)
        {
            WebGLVrmPicker_Clear();
            SetStatus("No file selected.");
            return;
        }

        try
        {
            byte[] data = new byte[len];

            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                WebGLVrmPicker_CopyTo(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
                WebGLVrmPicker_Clear();
            }

            SelectedVrmData = data;
            SweetGameVrmStore.VrmData = data;

            ApplyPreview(data);

            SetStatus($"Selected: (WebGL) ({data.Length} bytes)");
        }
        catch (Exception e)
        {
            WebGLVrmPicker_Clear();
            SetStatus($"Error: {e.Message}");
        }
    }
#endif

    public void OnTapStartGame()
    {
        if (SweetGameVrmStore.VrmData == null || SweetGameVrmStore.VrmData.Length == 0)
        {
            SetStatus("No VRM selected.");
            return;
        }

        AudioManager.Instance.PlaySE("startGame");
        SweetGameVrmStore.height = 0.8f - height;
        SweetGameVrmStore.backgroundId = 0;
        SweetGameVrmStore.body = 0;
        SweetGameVrmStore.speechType = SpeechCharacterType.None;
        SweetGameVrmStore.weightChangeScale = 1.5f;
        SceneManager.LoadScene(sweetGameSceneName);
    }

    public void ChangeHeight(float newHeight)
    {
        height = newHeight;
        if (previewVrmGameObject != null)
        {
            var ctr = previewVrmGameObject.GetComponent<VrmToController>();
            ctr.height = 0.8f - height;
        }
    }

    private async void ApplyPreview(byte[] vrmData)
    {
        if (vrmData == null || vrmData.Length == 0) return;
        if (previewVrmGameObject == null) return;

        var ctr = previewVrmGameObject.GetComponent<VrmToController>();
        if (ctr == null)
        {
            SetStatus("Preview VRM GameObject has no VrmToController.");
            return;
        }

        ctr.ReloadFromBytes(
            vrmData,
            SweetGameVrmStore.bodyVariant,
            ctr.face3Key,
            ctr.bodyKey,
            ctr.bustKey,
            0.8f - height
        );
    }
    private void UpdateStatus()
    {
        if (statusText == null) return;

        if (SweetGameVrmStore.VrmData != null && SweetGameVrmStore.VrmData.Length > 0)
        {
            statusText.text = "VRM: selected";
        }
        else
        {
            statusText.text = "VRM: not selected";
        }
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

}
