using BepInEx.Configuration;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Holds the connection parameters for an Archipelago session.
/// Persisted to BepInEx config so the last-used values are remembered.
/// </summary>
public class ArchipelagoData
{
    private const string Section = "Connection";

    public string Uri      { get; set; } = "archipelago.gg";
    public int    Port     { get; set; } = 38281;
    public string SlotName { get; set; } = "";
    public string Password { get; set; } = "";

    // Populated after a successful login
    public string? Seed { get; set; }
    public int     Team { get; set; } = -1;
    public int     Slot { get; set; } = -1;

    public static ArchipelagoData LoadFromConfig(ConfigFile cfg)
    {
        return new ArchipelagoData
        {
            Uri      = cfg.Bind(Section, "Host",     "archipelago.gg", "Archipelago server host").Value,
            Port     = cfg.Bind(Section, "Port",     38281,            "Archipelago server port").Value,
            SlotName = cfg.Bind(Section, "SlotName", "",               "Your slot/player name").Value,
            Password = cfg.Bind(Section, "Password", "",               "Room password (leave blank if none)").Value,
            // Seed is persisted so PreloadLastItemIndex can run before login on reconnects.
            Seed     = cfg.Bind(Section, "LastSeed", "", "Seed from last successful login (auto-set)").Value
                           is string s && s.Length > 0 ? s : null,
        };
    }

    public void SaveToConfig(ConfigFile cfg)
    {
        cfg.Bind(Section, "Host",     Uri,      "Archipelago server host").Value     = Uri;
        cfg.Bind(Section, "Port",     Port,     "Archipelago server port").Value     = Port;
        cfg.Bind(Section, "SlotName", SlotName, "Your slot/player name").Value       = SlotName;
        cfg.Bind(Section, "Password", Password, "Room password (leave blank if none)").Value = Password;
        // Do NOT persist Seed here — it is saved separately after a successful login
        // (see ArchipelagoClient.Connect) so it only reflects a confirmed good seed.
        cfg.Save();
    }

    /// <summary>
    /// Persists the seed that was set after a successful login so that
    /// <see cref="ApSaveManager.PreloadLastItemIndex"/> can run correctly on the
    /// next reconnect attempt for the same slot.
    /// </summary>
    public void SaveSeedToConfig(ConfigFile cfg)
    {
        if (string.IsNullOrEmpty(Seed)) return;
        cfg.Bind(Section, "LastSeed", "", "Seed from last successful login (auto-set)").Value = Seed!;
        cfg.Save();
    }
}
