using System.Collections.Generic;
using UnityEngine;

public enum SpeechCharacterType
{
    None,
    cat,
    hypnosis,
    haraibako,
}

public class VrmChrSceneTextController : MonoBehaviour
{
    public static VrmChrSceneTextController Instance { get; private set; }

    [SerializeField] public TMPPagedDialog dialog;

    private SpeechCharacterType speechCharacterType = SpeechCharacterType.None;

    // 「同じキーを二度出さない」＝同じIDを二度出さない（Food/Weightとも同じ方針に揃える）
    private readonly HashSet<int> spokenFoodIds = new HashSet<int>();
    private readonly HashSet<int> spokenWeightIds = new HashSet<int>();

    // 「跨いだ分を全部出す」ために、現在値へ追従するカーソルを持つ
    private bool cursorInitialized;
    private int cursorWeight;

    private bool isReady;

    private void Start()
    {
        Instance = this;
    }

    private void Update()
    {
        speechCharacterType = SweetGameVrmStore.speechType;

        var scene = VrmChrSceneController.Instance;
        if (scene == null || scene.vrmToController == null) return;

        if (!isReady)
        {
            isReady = scene.vrmToController.IsReady;
            if (!isReady) return;

            // 初期体重は通常運用：特別扱いしない（ただし過去分を遡って出すこともしない）
            cursorWeight = Mathf.RoundToInt(scene.vrmToController.bodyKey);
            cursorInitialized = true;

            if (speechCharacterType != SpeechCharacterType.None && dialog != null)
            {
                dialog.Begin($"{speechCharacterType}_Start");
            }
            return;
        }

        SetWeightSpeech(scene.vrmToController.bodyKey);
    }

    // --- public API（既存互換のため維持） ---

    public void UpdateSpeechCharacterType(SpeechCharacterType type)
    {
        // 仕様上「切替は起きない」前提だが、呼ばれても壊れないように更新だけする
        if (type == SpeechCharacterType.None) return;

        speechCharacterType = type;

        if (dialog != null)
            dialog.SetIconImage($"images/icons/icon_{type}");
    }

    public void SetWeightSpeech(float weight)
    {
        if (speechCharacterType == SpeechCharacterType.None) return;
        if (dialog == null) return;

        // 発話中は何もしない（保留リストもキューも持たない）
        if (dialog.gameObject.activeSelf) return;

        int current = Mathf.RoundToInt(weight);

        if (!cursorInitialized)
        {
            cursorWeight = current;
            cursorInitialized = true;
            return;
        }

        if (current == cursorWeight) return;

        // 「跨いだ分を全部」：一回の呼び出しでは一段だけ進める（次は次フレーム以降）
        cursorWeight += (current > cursorWeight) ? 1 : -1;

        // 1..100のみセリフ対象（weightSpeechListは不要という要件）
        if (cursorWeight < 0 || cursorWeight > 100) return;

        // 「同じキーを二度出さない」（A：キー定義）
        if (!spokenWeightIds.Add(cursorWeight)) return;

        dialog.Begin($"{speechCharacterType}_WeightSpeech_{cursorWeight}");
    }

    public void setFoodSpeech(int id)
    {
        if (speechCharacterType == SpeechCharacterType.None) return;
        if (dialog == null) return;

        if (!spokenFoodIds.Add(id)) return;

        dialog.Begin($"{speechCharacterType}_FoodSpeech_{id}");
    }
}
