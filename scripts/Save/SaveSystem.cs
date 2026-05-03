using System.IO;
using System.Text.Json;
using Godot;

namespace PRISM.Save;

// Reads/writes per-slot JSON files under user:// (Godot's user data dir).
// Three fixed slots: 1, 2, 3.
public static class SaveSystem
{
    public const int SlotCount = 3;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented              = true,
        PropertyNameCaseInsensitive = true,
    };

    public static bool Exists(int slot) => File.Exists(PathFor(slot));

    public static SaveData? Load(int slot)
    {
        var path = PathFor(slot);
        if (!File.Exists(path)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(path), JsonOpts);
            if (data != null) data.Slot = slot;
            return data;
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[SaveSystem] Failed to read slot {slot}: {e.Message}");
            return null;
        }
    }

    public static void Save(SaveData data)
    {
        EnsureDir();
        File.WriteAllText(PathFor(data.Slot), JsonSerializer.Serialize(data, JsonOpts));
    }

    public static void Delete(int slot)
    {
        var path = PathFor(slot);
        if (File.Exists(path)) File.Delete(path);
    }

    private static string PathFor(int slot) =>
        ProjectSettings.GlobalizePath($"user://saves/slot_{slot}.json");

    private static void EnsureDir()
    {
        var dir = ProjectSettings.GlobalizePath("user://saves/");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }
}
