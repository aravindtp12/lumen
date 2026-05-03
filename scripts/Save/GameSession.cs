namespace PRISM.Save;

// In-memory pointer to the active save slot and current scene context.
// Set by the slot picker / level select before loading scenes that need to
// know what to play, read by LevelScene on _Ready.
public static class GameSession
{
    public static int?      ActiveSlot;
    public static SaveData? Active;
    public static string?   CurrentWorldId;
    public static int       CurrentLevelIndex;

    public static void Reset()
    {
        ActiveSlot        = null;
        Active            = null;
        CurrentWorldId    = null;
        CurrentLevelIndex = 0;
    }
}
