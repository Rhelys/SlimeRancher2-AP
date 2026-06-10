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

    // Modal warning dialog
    private bool            _showModal           = false;
    private string          _modalMessage        = "";
    private System.Action?  _modalConfirmAction  = null;

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
    /// Show a modal warning dialog over the screen.
    /// When <paramref name="confirmAction"/> is provided the dialog shows two buttons:
    /// "Go Back" (dismiss) and "Create Anyways" (invokes the action).
    /// When null a single centred "Dismiss" button is shown.
    /// </summary>
    public void ShowWarningModal(string message, System.Action? confirmAction = null)
    {
        _showModal           = true;
        _modalMessage        = message;
        _modalConfirmAction  = confirmAction;
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

        // Modal warning — drawn last so it renders on top of everything else.
        // Uses GUI.Window equivalent drawn manually since GUI.Window delegate
        // creation fails in IL2CPP builds.
        if (_showModal)
        {
            // Semi-transparent darkened overlay
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

            // Dialog background
            const float W = 500f, H = 230f;
            float bx = (Screen.width  - W) / 2f;
            float by = (Screen.height - H) / 2f;

            GUI.color = new Color(0.12f, 0.12f, 0.20f, 1f);
            GUI.DrawTexture(new Rect(bx, by, W, H), Texture2D.whiteTexture);

            // Thin border
            GUI.color = new Color(0.4f, 0.7f, 1f, 0.8f);
            GUI.DrawTexture(new Rect(bx,         by,         W,  2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(bx,         by + H - 2, W,  2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(bx,         by,         2,  H), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(bx + W - 2, by,         2,  H), Texture2D.whiteTexture);

            // Message
            GUI.color = Color.white;
            GUI.Label(new Rect(bx + 20, by + 20, W - 40, 140), _modalMessage);

            // Buttons
            const float BtnW = 140f, BtnH = 36f;
            float btnY = by + H - 52;

            if (_modalConfirmAction != null)
            {
                // "Go Back" on the left
                GUI.color = new Color(0.3f, 0.6f, 1f, 1f);
                GUI.DrawTexture(new Rect(bx + 40, btnY, BtnW, BtnH), Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (GUI.Button(new Rect(bx + 40, btnY, BtnW, BtnH), "Go Back"))
                {
                    _showModal          = false;
                    _modalConfirmAction = null;
                }

                // "Create Anyways" on the right (orange — signals a risky action)
                GUI.color = new Color(0.85f, 0.5f, 0.1f, 1f);
                GUI.DrawTexture(new Rect(bx + W - 40 - BtnW, btnY, BtnW, BtnH), Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (GUI.Button(new Rect(bx + W - 40 - BtnW, btnY, BtnW, BtnH), "Create Anyways"))
                {
                    _showModal = false;
                    var action = _modalConfirmAction;
                    _modalConfirmAction = null;
                    action?.Invoke();
                }
            }
            else
            {
                // Single centred dismiss button
                GUI.color = new Color(0.3f, 0.6f, 1f, 1f);
                GUI.DrawTexture(new Rect(bx + W / 2 - BtnW / 2, btnY, BtnW, BtnH), Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (GUI.Button(new Rect(bx + W / 2 - BtnW / 2, btnY, BtnW, BtnH), "Dismiss"))
                    _showModal = false;
            }
        }

        }
        catch (System.Exception ex)
        {
            Logger.Error($"[AP] StatusHUD.OnGUI exception: {ex}");
        }
        finally
        {
            GUI.color = prev;
        }
    }
}
