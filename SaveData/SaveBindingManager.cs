using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlimeRancher2AP.SaveData;

/// <summary>
/// Per-save-slot JSON sidecar (<c>BepInEx/config/SlimeRancher2-AP/SaveSlot_{n}_binding.json</c>)
/// tying the slot to an AP session: connection details for auto-reconnect, and the seed. No
/// binding = vanilla save.
/// </summary>
public static class SaveBindingManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented       = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public record SaveBinding
    {
        [JsonPropertyName("host")]     public string Host     { get; init; } = "";
        [JsonPropertyName("port")]     public int    Port     { get; init; } = 38281;
        [JsonPropertyName("slot")]     public string Slot     { get; init; } = "";
        [JsonPropertyName("password")] public string Password { get; init; } = "";
        /// <summary>AP seed — validated at load time to detect slot mismatches.</summary>
        [JsonPropertyName("seed")]     public string Seed     { get; init; } = "";
    }

    // -------------------------------------------------------------------------

    private static string BindingPath(int slotIndex)
    {
        var dir = Path.Combine(BepInEx.Paths.ConfigPath, "SlimeRancher2-AP");
        return Path.Combine(dir, $"SaveSlot_{slotIndex}_binding.json");
    }

    /// <summary>The slot's binding, or null.</summary>
    public static SaveBinding? Load(int slotIndex)
    {
        var path = BindingPath(slotIndex);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<SaveBinding>(File.ReadAllText(path), JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.Warning(
                $"[AP] SaveBinding: could not read slot {slotIndex} binding — {ex.Message}");
            return null;
        }
    }

    public static void Save(int slotIndex, SaveBinding binding)
    {
        var dir = Path.Combine(BepInEx.Paths.ConfigPath, "SlimeRancher2-AP");
        Directory.CreateDirectory(dir);
        var path = BindingPath(slotIndex);
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(binding, JsonOpts));
            Logger.Info(
                $"[AP] SaveBinding: wrote slot {slotIndex} binding (seed={binding.Seed}, slot={binding.Slot})");
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"[AP] SaveBinding: could not write slot {slotIndex} binding — {ex.Message}");
        }
    }

    public static bool Exists(int slotIndex) => File.Exists(BindingPath(slotIndex));

    public static void Delete(int slotIndex)
    {
        var path = BindingPath(slotIndex);
        if (File.Exists(path))
        {
            File.Delete(path);
            Logger.Info($"[AP] SaveBinding: deleted slot {slotIndex} binding");
        }
    }
}
