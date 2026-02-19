public static class SweetGameVrmStore
{
    // Selected VRM bytes (set in sweetGameSettings, consumed in sweetGame)
    public static byte[] VrmData;
    public static BodyVariant bodyVariant;

    public static float height;
    public static float body;
    public static int backgroundId;
    public static float weightChangeScale;
    public static SpeechCharacterType speechType;
}

public static class SettingStore
{
    public static bool isKgViewMode = true;
    public static bool useSteamMode = true;
}



public static class IsYjsStore
{
    public static bool isYjsMode = false;
}
