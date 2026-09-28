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

    private readonly string _defaultDataFolder;

    public AppStateStore(string? defaultDataFolder = null)
    {
        var developmentOverride = Environment.GetEnvironmentVariable("MAPANOTATEK_DATA_FOLDER");
        _defaultDataFolder = SafeFileStorage.NormalizeDirectory(
            defaultDataFolder ??
            (string.IsNullOrWhiteSpace(developmentOverride) ? null : developmentOverride) ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MapaNotatek"));
    }

    public string DefaultDataFolder => _defaultDataFolder;
    public string StatePath => SafeFileStorage.GetContainedFilePath(DefaultDataFolder, "app-state.json");
    public StorageReadIssue? LastLoadIssue { get; private set; }

    public event Action<StorageReadIssue>? ReadIssueDetected;

    public AppState Load()
    {
        LastLoadIssue = null;
        Directory.CreateDirectory(DefaultDataFolder);
        var statePath = StatePath;
        var backupPath = statePath + ".bak";

        if (!File.Exists(statePath))
        {
            if (File.Exists(backupPath))
            {
                return LoadBackup(
                    statePath,
                    backupPath,
                    new FileNotFoundException("Brakuje głównego pliku stanu aplikacji.", statePath));
            }

            var fresh = new AppState { DataFolder = DefaultDataFolder };
            Save(fresh);
            return fresh;
        }

        try
        {
            return ReadState(statePath);
        }
        catch (Exception primaryError)
        {
            if (File.Exists(backupPath))
            {
                try
                {
                    return LoadBackup(statePath, backupPath, primaryError);
                }
                catch (Exception backupError) when (backupError is not StorageReadException)
                {
                    throw ReportFatalReadFailure(statePath, primaryError, backupError);
                }
            }

            throw ReportFatalReadFailure(statePath, primaryError);
        }
    }

    public void Save(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var folder = string.IsNullOrWhiteSpace(state.DataFolder)
            ? DefaultDataFolder
            : SafeFileStorage.NormalizeDirectory(state.DataFolder);
        state.DataFolder = folder;

        Directory.CreateDirectory(folder);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        var path = SafeFileStorage.GetContainedFilePath(folder, "app-state.json");
        SafeFileStorage.AtomicWriteAllText(path, json);

        if (!SafeFileStorage.PathsEqual(path, StatePath))
        {
            Directory.CreateDirectory(DefaultDataFolder);
            SafeFileStorage.AtomicWriteAllText(StatePath, json);
        }
    }

    public AppState RecoverWithFreshState()
    {
        Directory.CreateDirectory(DefaultDataFolder);
        var recoveryFolder = SafeFileStorage.EnsureContainedDirectory(
            DefaultDataFolder,
            Path.Combine(DefaultDataFolder, "Recovery"));
        PreserveBrokenFile(StatePath, recoveryFolder);
        PreserveBrokenFile(StatePath + ".bak", recoveryFolder);

        var fresh = new AppState { DataFolder = DefaultDataFolder };
        Save(fresh);
        return fresh;
    }

    private static void PreserveBrokenFile(string path, string recoveryFolder)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
        var fileName = $"{Path.GetFileName(path)}.{timestamp}.corrupt";
        var destination = SafeFileStorage.GetContainedFilePath(recoveryFolder, fileName);
        File.Move(path, destination, overwrite: false);
    }

    private AppState LoadBackup(string statePath, string backupPath, Exception primaryError)
    {
        try
        {
            SafeFileStorage.ValidateContainedFilePath(DefaultDataFolder, backupPath);
            var state = ReadState(backupPath);
            ReportIssue(
                statePath,
                "Główny plik stanu jest uszkodzony lub niedostępny; wczytano kopię awaryjną.",
                primaryError,
                recoveredFromBackup: true);
            return state;
        }
        catch (Exception backupError)
        {
            throw ReportFatalReadFailure(statePath, primaryError, backupError);
        }
    }

    private AppState ReadState(string path)
    {
        SafeFileStorage.ValidateContainedFilePath(DefaultDataFolder, path);
        var json = File.ReadAllText(path);
        var state = JsonSerializer.Deserialize<AppState>(json, JsonOptions)
            ?? throw new JsonException("Plik stanu zawiera wartość null.");
        state.DataFolder = string.IsNullOrWhiteSpace(state.DataFolder)
            ? DefaultDataFolder
            : SafeFileStorage.NormalizeDirectory(state.DataFolder);
        state.NodePositions = new Dictionary<string, GraphPosition>(
            state.NodePositions ?? new Dictionary<string, GraphPosition>(),
            StringComparer.OrdinalIgnoreCase);
        state.PinnedIds ??= [];
        state.RecentIds ??= [];
        if (state.Zoom <= 0 || double.IsNaN(state.Zoom) || double.IsInfinity(state.Zoom))
        {
            state.Zoom = 1;
        }

        state.EditorFont = state.EditorFont is "Inter" or "Szeryfowa" or "Monospace"
            ? state.EditorFont
            : "Inter";
        if (state.EditorFontSize is < 14 or > 18 ||
            double.IsNaN(state.EditorFontSize) ||
            double.IsInfinity(state.EditorFontSize))
        {
            state.EditorFontSize = 16;
        }

        return state;
    }

    private StorageReadException ReportFatalReadFailure(
        string path,
        Exception primaryError,
        Exception? backupError = null)
    {
        var exception = backupError is null
            ? primaryError
            : new AggregateException(primaryError, backupError);
        var issue = ReportIssue(
            path,
            backupError is null
                ? "Nie można odczytać stanu aplikacji i nie ma poprawnej kopii awaryjnej."
                : "Nie można odczytać stanu aplikacji ani jego kopii awaryjnej.",
            exception,
            recoveredFromBackup: false);
        return new StorageReadException(issue);
    }

    private StorageReadIssue ReportIssue(
        string path,
        string message,
        Exception exception,
        bool recoveredFromBackup)
    {
        var issue = new StorageReadIssue(
            StorageArea.AppState,
            path,
            message,
            exception,
            recoveredFromBackup);
        LastLoadIssue = issue;
        ReadIssueDetected?.Invoke(issue);
        return issue;
    }
}
