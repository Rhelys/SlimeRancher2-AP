using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlimeRancher2AP.SaveData;

/// <summary>
/// Reads and writes a small JSON sidecar file per SR2 save slot that ties that slot
/// to a specific Archipelago session.  The file lives at:
///   BepInEx/config/SlimeRancher2-AP/SaveSlot_{n}_binding.json
///
/// The binding contains everything needed to auto-reconnect when the slot is loaded,
/// plus the AP seed for cross-validation.  A slot without a binding is treated as a
/// vanilla save — no AP logic is applied.
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

    /// <summary>Loads the binding for <paramref name="slotIndex"/>, or null if none exists.</summary>
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

    /// <summary>Persists a binding for <paramref name="slotIndex"/>.</summary>
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

    /// <summary>Returns true if a binding file exists for <paramref name="slotIndex"/>.</summary>
    public static bool Exists(int slotIndex) => File.Exists(BindingPath(slotIndex));

    /// <summary>Removes the binding for <paramref name="slotIndex"/> (e.g. on save-delete).</summary>
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
