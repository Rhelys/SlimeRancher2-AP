using UnityEngine;

namespace SlimeRancher2AP.UI;

/// <summary>
/// Always-visible IMGUI overlay showing AP connection state, an Archipelago button,
/// and a timed notification queue for received items.
/// </summary>
public class StatusHUD : MonoBehaviour
{
    public StatusHUD(IntPtr handle) : base(handle) { }

    // Connection status
    private string _statusText  = "AP: Disconnected";
    private Color  _statusColor = Color.red;

    // Notification queue — each entry is (message, expiry time)
    private readonly Queue<(string message, float expiry)> _notifications = new();
    private const float NotificationDuration = 4f;
    private const int   MaxNotifications     = 5;

    // -------------------------------------------------------------------------

    public static StatusHUD? Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        Plugin.Instance.ApClient.OnConnected        += UpdateStatus;
        Plugin.Instance.ApClient.OnDisconnected     += UpdateStatus;
        Plugin.Instance.ApClient.OnConnectionFailed += _ => UpdateStatus();
        UpdateStatus();
    }

    /// <summary>
    /// Enqueue a notification message to display on-screen for a few seconds.
    /// Call from the main thread (e.g. from ItemHandler after applying an item).
    /// </summary>
    public void ShowNotification(string message)
    {
        // Drop oldest if queue is full
        while (_notifications.Count >= MaxNotifications)
            _notifications.Dequeue();

        _notifications.Enqueue((message, Time.unscaledTime + NotificationDuration));
    }

    // -------------------------------------------------------------------------

    private void UpdateStatus()
    {
        if (Plugin.Instance.ApClient.IsConnected)
        {
            var slot = Plugin.Instance.ApClient.Session?.ConnectionInfo.Slot ?? -1;
            _statusText  = $"AP: Connected (slot {slot})";
            _statusColor = Color.green;
        }
        else
        {
            _statusText  = "AP: Disconnected";
            _statusColor = Color.red;
        }
    }

    private void Update()
    {
        // Expire old notifications (Update runs on main thread, safe to mutate queue)
        while (_notifications.Count > 0 && _notifications.Peek().expiry <= Time.unscaledTime)
            _notifications.Dequeue();
    }

    private void OnGUI()
    {
        var prev = GUI.color;
        try
        {
        // Connection status line
        GUI.color = _statusColor;
        GUI.Label(new Rect(8, 8, 300, 24), _statusText);

        // Mod-enabled toggle — lets the player switch to vanilla mode without uninstalling
        // (Connection settings: Options > Archipelago)
        bool modOn = Plugin.Instance.ModEnabled;
        GUI.color = modOn ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f);
        if (GUI.Button(new Rect(8, 36, 130, 26), modOn ? "AP: Enabled" : "AP: Disabled"))
            Plugin.Instance.SetModEnabled(!modOn);

        // Notifications — stacked below the mod toggle button (start at y=70)
        GUI.color = Color.white;
        var notices = _notifications.ToArray();
        for (int i = notices.Length - 1; i >= 0; i--)
        {
            float row      = 70f + (notices.Length - 1 - i) * 26f;
            float timeLeft = notices[i].expiry - Time.unscaledTime;
            float alpha    = Mathf.Clamp01(timeLeft);   // fade out in last second

            GUI.color = new Color(0f, 0f, 0f, 0.55f * alpha);
            GUI.DrawTexture(new Rect(6, row - 2, 354, 24), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 1f, 0.6f, alpha); // soft yellow
            GUI.Label(new Rect(10, row, 350, 24), notices[i].message);
        }

        }
        catch (System.Exception ex)
        {
            Plugin.Instance.Log.LogError($"[AP] StatusHUD.OnGUI exception: {ex}");
        }
        finally
        {
            GUI.color = prev;
        }
    }
}
