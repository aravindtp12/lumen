using System;
using System.Collections.Generic;

namespace PRISM.Save;

// JSON-serialisable save slot.
// Tracks the player's last position in the level catalogue plus per-world
// completion progress so the level select screen can show ticks.
public class SaveData
{
    public int    Slot           { get; set; }
    public string CreatedAt      { get; set; } = "";
    public string LastPlayedAt   { get; set; } = "";
    public string LastWorldId    { get; set; } = "";
    public int    LastLevelIndex { get; set; }
    public int    TotalMoves     { get; set; }

    // worldId -> sorted list of completed level indices
    public Dictionary<string, List<int>> Completed { get; set; } = new();

    public static SaveData NewSlot(int slot)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        return new SaveData
        {
            Slot         = slot,
            CreatedAt    = now,
            LastPlayedAt = now,
        };
    }

    public bool IsCompleted(string worldId, int levelIndex) =>
        Completed.TryGetValue(worldId, out var list) && list.Contains(levelIndex);

    public void MarkCompleted(string worldId, int levelIndex)
    {
        if (!Completed.TryGetValue(worldId, out var list))
        {
            list = new List<int>();
            Completed[worldId] = list;
        }
        if (!list.Contains(levelIndex))
        {
            list.Add(levelIndex);
            list.Sort();
        }
    }

    public int CompletedCount(string worldId) =>
        Completed.TryGetValue(worldId, out var list) ? list.Count : 0;
}
