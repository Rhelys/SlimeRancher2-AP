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
        };
    }

    public void SaveToConfig(ConfigFile cfg)
    {
        cfg.Bind(Section, "Host",     Uri,      "Archipelago server host").Value     = Uri;
        cfg.Bind(Section, "Port",     Port,     "Archipelago server port").Value     = Port;
        cfg.Bind(Section, "SlotName", SlotName, "Your slot/player name").Value       = SlotName;
        cfg.Bind(Section, "Password", Password, "Room password (leave blank if none)").Value = Password;
        cfg.Save();
    }
}
