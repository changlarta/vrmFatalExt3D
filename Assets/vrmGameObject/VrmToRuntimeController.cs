// VrmToRuntimeController.cs
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// JSON で定義した「イベント」を元に、VrmToController の
/// currentAnimationKey / expressionPreset / eyeContact を操作するコントローラ。
/// さらに VrmExpressionController の enableBlink / enableMouthLoop も制御する。
/// </summary>
[DisallowMultipleComponent]
public sealed class VrmToRuntimeController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private VrmToController vrmTo;

    [Header("Config (JSON)")]
    [Tooltip("イベント定義 JSON。構造はサンプル参照。")]
    [SerializeField] private TextAsset eventsJson;

    [Header("Event Key (Inspector / Script)")]
    [Tooltip("このキーに対応するイベントが自動的に適用される。値を変えると即座に反映。")]
    [SerializeField] private string debugEventKey;

    // JSON の 1 イベント分
    [Serializable]
    private class EventConfig
    {
        public string eventKey;            // イベントID（必須・一意）
        public string currentAnimationKey; // 例: "idle", "walk"
        public string expressionPreset;    // 例: "Neutral", "JoySoft"（ SavedExpression の enum 名）
        public bool eyeContact;           // true / false
        public bool enableBlink;          // true / false
        public bool showBlush;
        public bool enableMouthLoop;      // true / false
    }

    // JSON 全体
    [Serializable]
    private class EventConfigRoot
    {
        public EventConfig[] events;
    }

    // パース済みデータ
    private readonly Dictionary<string, EventConfig> _eventsByKey = new();
    private bool _loaded;
    private string _lastAppliedDebugEventKey;

    private void Reset()
    {
        // 同じ GameObject に VrmToController が付いているケースを想定
        if (vrmTo == null) vrmTo = GetComponent<VrmToController>();
    }

    private void Awake()
    {
        if (vrmTo == null) vrmTo = GetComponent<VrmToController>();
        ReloadEventsFromJson();
        AutoApplyDebugEventIfChanged(force: true); // 起動時に debugEventKey がセットされていれば適用
    }

#if UNITY_EDITOR
    // Editor で JSON や debugEventKey を変えたときにもすぐ反映させる
    private void OnValidate()
    {
        if (vrmTo == null) vrmTo = GetComponent<VrmToController>();
        ReloadEventsFromJson();
        AutoApplyDebugEventIfChanged(force: true);
    }
#endif

    private void Update()
    {
        // ランタイム中にスクリプトから debugEventKey を変更された場合も自動で反映
        AutoApplyDebugEventIfChanged(force: false);
    }

    /// <summary>
    /// eventsJson を再パースする。
    /// </summary>
    public void ReloadEventsFromJson()
    {
        _eventsByKey.Clear();
        _loaded = false;

        if (eventsJson == null || string.IsNullOrWhiteSpace(eventsJson.text))
        {
            Debug.LogWarning("[VrmToRuntimeController] eventsJson is null or empty.");
            return;
        }

        EventConfigRoot root = null;
        try
        {
            root = JsonUtility.FromJson<EventConfigRoot>(eventsJson.text);
        }
        catch (Exception e)
        {
            Debug.LogError($"[VrmToRuntimeController] JSON parse failed: {e.Message}");
            return;
        }

        if (root == null || root.events == null || root.events.Length == 0)
        {
            Debug.LogWarning("[VrmToRuntimeController] JSON has no events.");
            return;
        }

        foreach (var ev in root.events)
        {
            if (ev == null) continue;
            if (string.IsNullOrEmpty(ev.eventKey))
            {
                Debug.LogWarning("[VrmToRuntimeController] Found event with empty eventKey. Skipped.");
                continue;
            }

            if (_eventsByKey.ContainsKey(ev.eventKey))
            {
                Debug.LogWarning($"[VrmToRuntimeController] Duplicate eventKey '{ev.eventKey}' found. Overwrite.");
            }
            _eventsByKey[ev.eventKey] = ev;
        }

        _loaded = true;
    }

    /// <summary>
    /// 指定した eventKey のイベントを適用する。
    /// </summary>
    /// <param name="eventKey">JSON 内で定義された eventKey</param>
    /// <returns>適用に成功したかどうか</returns>
    public bool ApplyEvent(string eventKey)
    {
        if (vrmTo == null)
        {
            Debug.LogError("[VrmToRuntimeController] vrmTo is not assigned.");
            return false;
        }

        if (!_loaded)
        {
            Debug.LogWarning("[VrmToRuntimeController] Events not loaded yet. Try ReloadEventsFromJson().");
            return false;
        }

        if (string.IsNullOrEmpty(eventKey))
        {
            Debug.LogWarning("[VrmToRuntimeController] eventKey is null or empty.");
            return false;
        }

        if (!_eventsByKey.TryGetValue(eventKey, out var config) || config == null)
        {
            Debug.LogWarning($"[VrmToRuntimeController] Event not found: '{eventKey}'.");
            return false;
        }

        var ok = ApplyEvent(config);
        if (ok)
        {
            _lastAppliedDebugEventKey = eventKey;
        }
        return ok;
    }


    // debugEventKey の変更を検出して自動適用
    private void AutoApplyDebugEventIfChanged(bool force)
    {
        if (vrmTo == null || !_loaded) return;

        if (string.IsNullOrEmpty(debugEventKey))
        {
            if (force) _lastAppliedDebugEventKey = null;
            return;
        }

        if (!force && debugEventKey == _lastAppliedDebugEventKey) return;

        var ok = ApplyEvent(debugEventKey);
        if (ok)
        {
            _lastAppliedDebugEventKey = debugEventKey;
        }
    }

    // 実際に VrmToController / VrmExpressionController に値を流し込む実装
    private bool ApplyEvent(EventConfig ev)
    {
        if (vrmTo == null || ev == null) return false;

        // currentAnimationKey
        if (!string.IsNullOrEmpty(ev.currentAnimationKey))
        {
            vrmTo.currentAnimationKey = ev.currentAnimationKey;
        }

        // expressionPreset (enum 文字列 → enum 値)
        if (!string.IsNullOrEmpty(ev.expressionPreset))
        {
            if (Enum.TryParse<VrmExpressionController.SavedExpression>(
                    ev.expressionPreset, ignoreCase: true, out var preset))
            {
                vrmTo.expressionPreset = preset;
            }
            else
            {
                Debug.LogWarning(
                    $"[VrmToRuntimeController] Unknown expressionPreset '{ev.expressionPreset}' in event '{ev.eventKey}'.");
            }
        }

        // eyeContact
        vrmTo.eyeContact = ev.eyeContact;

        vrmTo.visibleBlush = ev.showBlush;

        // 表情系コンポーネントへの橋渡し
        var expr = vrmTo.GetComponent<VrmExpressionController>();
        if (expr != null)
        {
            expr.enableBlink = ev.enableBlink;
            expr.enableMouthLoop = ev.enableMouthLoop;
        }

        return true;
    }
}
