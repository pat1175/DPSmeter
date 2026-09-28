using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DPSMeter;

public static class RunIdentity
{
    public static string Create(string seed, long startTime, string mode, string profile, IEnumerable<ulong> players)
    {
        string canonical = JsonSerializer.Serialize(new[] { "party-meter-v2", seed,
            startTime.ToString(CultureInfo.InvariantCulture), mode, profile,
            string.Join(",", players.OrderBy(id => id).Select(id => id.ToString(CultureInfo.InvariantCulture))) });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public sealed class RunArchiveStore(string directory)
{
    private string PathFor(string id)
    {
        // File names are always hashes, never game-supplied path components.
        return Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");
    }

    public void Save(SavedState state)
    {
        Directory.CreateDirectory(directory);
        string path = PathFor(state.StableRunId);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(state, SavedStateCtx.Default.SavedState));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public SavedState? Load(string id)
    {
        string path = PathFor(id);
        if (!File.Exists(path)) return null;
        var state = JsonSerializer.Deserialize(File.ReadAllText(path), SavedStateCtx.Default.SavedState);
        return state?.StableRunId == id && !state.Completed ? state : null;
    }

    public SavedState? FindResumedMultiplayer(string group, long started)
    {
        if (!Directory.Exists(directory)) return null;
        var candidates = new List<SavedState>();
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var state = JsonSerializer.Deserialize(File.ReadAllText(file), SavedStateCtx.Default.SavedState);
                // New multiplayer clients initialize their clocks locally; loading uses the host's saved clock.
                // Only the saved-run entry point may use this tightly bounded, unambiguous match.
                if (state != null && !state.Completed && state.SessionGroup == group &&
                    state.StartedAt > 0 && Math.Abs((decimal)state.StartedAt - started) <= 120)
                    candidates.Add(state);
            }
            catch (JsonException) { /* An unrelated damaged archive must not prevent other sessions loading. */ }
        }
        return candidates.Count == 1 ? candidates[0] : null;
    }
}
