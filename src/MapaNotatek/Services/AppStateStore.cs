using System.Text.Json;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

public sealed class AppStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string DefaultDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MapaNotatek");

    public string StatePath => Path.Combine(DefaultDataFolder, "app-state.json");

    public AppState Load()
    {
        Directory.CreateDirectory(DefaultDataFolder);
        if (!File.Exists(StatePath))
        {
            var fresh = new AppState { DataFolder = DefaultDataFolder };
            Save(fresh);
            return fresh;
        }

        try
        {
            var json = File.ReadAllText(StatePath);
            var state = JsonSerializer.Deserialize<AppState>(json, JsonOptions) ?? new AppState();
            state.DataFolder = string.IsNullOrWhiteSpace(state.DataFolder) ? DefaultDataFolder : state.DataFolder;
            state.NodePositions = new Dictionary<string, GraphPosition>(
                state.NodePositions ?? new Dictionary<string, GraphPosition>(),
                StringComparer.OrdinalIgnoreCase);
            if (state.Zoom <= 0)
            {
                state.Zoom = 1;
            }

            return state;
        }
        catch
        {
            return new AppState { DataFolder = DefaultDataFolder };
        }
    }

    public void Save(AppState state)
    {
        var folder = string.IsNullOrWhiteSpace(state.DataFolder) ? DefaultDataFolder : state.DataFolder;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "app-state.json");
        // Keep a copy in the default folder when the data folder was changed,
        // so the next launch still finds the pointer.
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
        if (!string.Equals(path, StatePath, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(DefaultDataFolder);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOptions));
        }
    }
}
