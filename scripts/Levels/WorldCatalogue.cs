using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace PRISM.Levels;

// Scans res://levels/ for world directories and JSON levels, then exposes
// them as ordered (worldId -> levels) lookups. Used by LevelSelectScene
// and LevelScene's next/prev navigation.
public static class WorldCatalogue
{
    public sealed record LevelEntry(string ResPath, int Index, string DisplayName);
    public sealed record World(string Id, string DisplayName, IReadOnlyList<LevelEntry> Levels);

    private static List<World>? _cached;

    public static IReadOnlyList<World> Worlds => _cached ??= Build();

    public static World? FindWorld(string id) =>
        Worlds.FirstOrDefault(w => w.Id == id);

    public static LevelEntry? GetLevel(string worldId, int index)
    {
        var w = FindWorld(worldId);
        if (w == null || index < 0 || index >= w.Levels.Count) return null;
        return w.Levels[index];
    }

    public static int IndexOfWorld(string worldId)
    {
        for (int i = 0; i < Worlds.Count; i++)
            if (Worlds[i].Id == worldId) return i;
        return -1;
    }

    public static void Refresh() => _cached = null;

    private static List<World> Build()
    {
        var result = new List<World>();
        var rootAbs = ProjectSettings.GlobalizePath("res://levels/");
        if (!Directory.Exists(rootAbs)) return result;

        var jsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling         = JsonCommentHandling.Skip,
            AllowTrailingCommas         = true,
        };

        foreach (var worldDir in Directory.EnumerateDirectories(rootAbs).OrderBy(p => p))
        {
            string worldId      = Path.GetFileName(worldDir);
            string worldDisplay = worldId;
            var    levels       = new List<LevelEntry>();
            int    idx          = 0;

            foreach (var file in Directory.EnumerateFiles(worldDir, "*.json").OrderBy(p => p))
            {
                string fileName = Path.GetFileName(file);
                // Skip the editor's sandbox scratch file
                if (fileName.StartsWith("sandbox", StringComparison.OrdinalIgnoreCase)) continue;

                string display = Path.GetFileNameWithoutExtension(file);
                try
                {
                    var meta = JsonSerializer.Deserialize<LevelMetadata>(File.ReadAllText(file), jsonOpts);
                    if (meta != null)
                    {
                        if (!string.IsNullOrWhiteSpace(meta.Name))  display      = meta.Name;
                        if (!string.IsNullOrWhiteSpace(meta.World)) worldDisplay = meta.World;
                    }
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[WorldCatalogue] {fileName}: {e.Message}");
                }

                string resPath = $"res://levels/{worldId}/{fileName}";
                levels.Add(new LevelEntry(resPath, idx++, display));
            }

            if (levels.Count > 0)
                result.Add(new World(worldId, worldDisplay, levels));
        }

        return result;
    }

    private sealed class LevelMetadata
    {
        public string? Name  { get; set; }
        public string? World { get; set; }
    }
}
