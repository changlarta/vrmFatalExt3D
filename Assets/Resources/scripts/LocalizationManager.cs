using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class LocalizationEntry
{
    public string key;
    public string value;
}

[System.Serializable]
public class LanguageFile
{
    public string id;
    public string name;
    public LocalizationEntry[] entries;
}

[DisallowMultipleComponent]
public class LocalizationManager : MonoBehaviour
{
    private static LocalizationManager _instance;
    public static LocalizationManager Instance
    {
        get
        {
            EnsureInstance();
            return _instance;
        }
    }

    // 固定順: ja=0, en=1
    private static readonly string[] LanguageOrder = { "en", "ja" };
    private const string LANG_ROOT = "lang/";

    private readonly List<LanguageFile> _languages = new List<LanguageFile>();
    private readonly Dictionary<string, string> _currentTable = new Dictionary<string, string>();

    public int CurrentLanguageIndex { get; private set; } = -1;

    public string CurrentLanguageId
    {
        get
        {
            if (CurrentLanguageIndex < 0 || CurrentLanguageIndex >= _languages.Count) return null;
            return _languages[CurrentLanguageIndex].id;
        }
    }

    public int LanguageCount => _languages.Count;

    public event System.Action OnLanguageChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;

        _instance = FindAnyObjectByType<LocalizationManager>();
        if (_instance != null) return;

        var go = new GameObject(nameof(LocalizationManager));
        _instance = go.AddComponent<LocalizationManager>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        LoadAllLanguageFilesFromResources();

        if (_languages.Count > 0)
        {
            // 原則 ja(0) を選ぶ。欠けている場合は先頭。
            int defaultIndex = Mathf.Clamp(0, 0, _languages.Count - 1);
            ChangeLang(defaultIndex);
        }
        else
        {
            Debug.LogWarning("LocalizationManager: no language files found in Resources/lang (expected: ja, en).");
        }
    }

    private void LoadAllLanguageFilesFromResources()
    {
        _languages.Clear();

        for (int i = 0; i < LanguageOrder.Length; i++)
        {
            string code = LanguageOrder[i];
            string path = LANG_ROOT + code;

            var ta = Resources.Load<TextAsset>(path);
            if (ta == null)
            {
                Debug.LogWarning($"LocalizationManager: missing TextAsset at Resources/{path} (expected file like Assets/Resources/{path}.json)");
                continue;
            }

            if (string.IsNullOrEmpty(ta.text))
            {
                Debug.LogError($"LocalizationManager: empty json in Resources/{path}");
                continue;
            }

            var lang = JsonUtility.FromJson<LanguageFile>(ta.text);
            if (lang == null || lang.entries == null)
            {
                Debug.LogError($"LocalizationManager: invalid json format in Resources/{path}");
                continue;
            }

            // JSON内の id/name が空でも最低限動くように補完
            if (string.IsNullOrEmpty(lang.id)) lang.id = code;
            if (string.IsNullOrEmpty(lang.name)) lang.name = code;

            _languages.Add(lang);
        }
    }

    public string GetLanguageName(int index)
    {
        if (index < 0 || index >= _languages.Count) return null;
        return string.IsNullOrEmpty(_languages[index].name)
            ? _languages[index].id
            : _languages[index].name;
    }

    public void ChangeLang(int index)
    {
        if (index < 0 || index >= _languages.Count)
        {
            Debug.LogWarning($"ChangeLang: index out of range ({index})");
            return;
        }

        var lang = _languages[index];

        _currentTable.Clear();
        foreach (var e in lang.entries)
        {
            if (!string.IsNullOrEmpty(e.key))
            {
                _currentTable[e.key] = e.value ?? "";
            }
        }

        CurrentLanguageIndex = index;
        OnLanguageChanged?.Invoke();
    }

    // 便利：id("ja"/"en")でも切り替えたい場合
    public void ChangeLangById(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        for (int i = 0; i < _languages.Count; i++)
        {
            if (_languages[i].id == id)
            {
                ChangeLang(i);
                return;
            }
        }

        Debug.LogWarning($"ChangeLangById: unknown id ({id})");
    }

    public string Get(string key)
    {
        if (IsYjsStore.isYjsMode)
        {
            return "野獣先輩";
        }
        if (!string.IsNullOrEmpty(key) && _currentTable.TryGetValue(key, out var v))
            return v;

        return null;
    }
}
