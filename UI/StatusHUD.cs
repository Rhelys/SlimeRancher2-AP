using UnityEngine;

namespace SlimeRancher2AP.UI;

/// <summary>IMGUI overlay: connection state, timed notifications, and a modal.</summary>
public class StatusHUD : MonoBehaviour
{
    public StatusHUD(IntPtr handle) : base(handle) { }

    private string _statusText  = "AP: Disconnected";
    private Color  _statusColor = Color.red;

    private readonly Queue<(string message, float expiry)> _notifications = new();
    private const float NotificationDuration = 4f;
    private const int   MaxNotifications     = 5;

    private bool            _showModal           = false;
    private string          _modalMessage        = "";
    private System.Action?  _modalConfirmAction  = null;
    private string          _modalConfirmLabel   = "Create Anyways";

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
    /// A modal warning: "Go Back" plus <paramref name="confirmLabel"/> when there is a
    /// <paramref name="confirmAction"/>, else a single "Dismiss".
    /// </summary>
    public void ShowWarningModal(string message, System.Action? confirmAction = null,
                                 string confirmLabel = "Create Anyways")
    {
        _showModal           = true;
        _modalMessage        = message;
        _modalConfirmAction  = confirmAction;
        _modalConfirmLabel   = string.IsNullOrEmpty(confirmLabel) ? "Create Anyways" : confirmLabel;
    }

    /// <summary>Shows a message for a few seconds. Main thread.</summary>
    public void ShowNotification(string message)
    {
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
        while (_notifications.Count > 0 && _notifications.Peek().expiry <= Time.unscaledTime)
            _notifications.Dequeue();
    }

    // Profiled separately — OnGUI runs at least twice per frame (Layout + Repaint).
    private static readonly System.Action _drawBody = () => Instance?.DrawGui();

    private void OnGUI() => SlimeRancher2AP.Utils.ModProfiler.Time("StatusHUD.OnGUI", _drawBody);

    private void DrawGui()
    {
        var prev = GUI.color;
        try
        {
        GUI.color = _statusColor;
        GUI.Label(new Rect(8, 8, 300, 24), _statusText);

        // Notifications below the status. Skip ToArray() when empty — OnGUI runs twice a frame.
        GUI.color = Color.white;
        var notices = _notifications.Count > 0 ? _notifications.ToArray() : System.Array.Empty<(string message, float expiry)>();
        for (int i = notices.Length - 1; i >= 0; i--)
        {
            float row      = 36f + (notices.Length - 1 - i) * 26f;
            float timeLeft = notices[i].expiry - Time.unscaledTime;
            float alpha    = Mathf.Clamp01(timeLeft);   // fade out in last second

            GUI.color = new Color(0f, 0f, 0f, 0.55f * alpha);
            GUI.DrawTexture(new Rect(6, row - 2, 354, 24), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 1f, 0.6f, alpha); // soft yellow
            GUI.Label(new Rect(10, row, 350, 24), notices[i].message);
        }

        // Modal last, so it's on top. Drawn by hand: GUI.Window's delegate fails in IL2CPP.
        if (_showModal)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

            const float W = 500f, H = 230f;
            float bx = (Screen.width  - W) / 2f;
            float by = (Screen.height - H) / 2f;

            GUI.color = new Color(0.12f, 0.12f, 0.20f, 1f);
            GUI.DrawTexture(new Rect(bx, by, W, H), Texture2D.whiteTexture);

            GUI.color = new Color(0.4f, 0.7f, 1f, 0.8f);
            GUI.DrawTexture(new Rect(bx,         by,         W,  2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(bx,         by + H - 2, W,  2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(bx,         by,         2,  H), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(bx + W - 2, by,         2,  H), Texture2D.whiteTexture);

            GUI.color = Color.white;
            GUI.Label(new Rect(bx + 20, by + 20, W - 40, 140), _modalMessage);

            const float BtnW = 140f, BtnH = 36f;
            float btnY = by + H - 52;

            if (_modalConfirmAction != null)
            {
                GUI.color = new Color(0.3f, 0.6f, 1f, 1f);
                GUI.DrawTexture(new Rect(bx + 40, btnY, BtnW, BtnH), Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (GUI.Button(new Rect(bx + 40, btnY, BtnW, BtnH), "Go Back"))
                {
                    _showModal          = false;
                    _modalConfirmAction = null;
                }

                // Orange: the risky choice
                GUI.color = new Color(0.85f, 0.5f, 0.1f, 1f);
                GUI.DrawTexture(new Rect(bx + W - 40 - BtnW, btnY, BtnW, BtnH), Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (GUI.Button(new Rect(bx + W - 40 - BtnW, btnY, BtnW, BtnH), _modalConfirmLabel))
                {
                    _showModal = false;
                    var action = _modalConfirmAction;
                    _modalConfirmAction = null;
                    action?.Invoke();
                }
            }
            else
            {
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
