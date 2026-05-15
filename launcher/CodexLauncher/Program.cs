using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

ApplicationConfiguration.Initialize();
Application.Run(new MainForm());

sealed class MainForm : Form
{
    private static readonly Color AppBack = ColorTranslator.FromHtml("#11100D");
    private static readonly Color PanelBack = ColorTranslator.FromHtml("#1A1814");
    private static readonly Color PanelRaised = ColorTranslator.FromHtml("#24211B");
    private static readonly Color Border = ColorTranslator.FromHtml("#343027");
    private static readonly Color Ink = ColorTranslator.FromHtml("#F7F2E8");
    private static readonly Color Muted = ColorTranslator.FromHtml("#B7AEA0");
    private static readonly Color Accent = ColorTranslator.FromHtml("#E8A34B");
    private static readonly Color AccentSoft = ColorTranslator.FromHtml("#3A2A1C");
    private static readonly Color AccentAlt = ColorTranslator.FromHtml("#C7DA6B");
    private static readonly Color Danger = ColorTranslator.FromHtml("#FF7B62");
    private static readonly Color Success = ColorTranslator.FromHtml("#7ED6A6");

    private readonly LocalStorage _storage = new();
    private readonly SemaphoreSlim _busyLock = new(1, 1);
    private readonly System.Windows.Forms.Timer _heartbeat = new() { Interval = 60_000 };

    private LauncherConfig _config;
    private SessionData? _session;
    private InstallationState? _installation;
    private LicenseView? _license;
    private ProviderKeyView? _providerKey;

    private readonly TextBox _serverUrlBox = CreateInputBox("Gateway URL");
    private readonly TextBox _accessCodeBox = CreateInputBox("Redeemable access code");
    private readonly TextBox _emailBox = CreateInputBox("Customer email for first activation");
    private readonly ComboBox _modelBox = CreateModelBox();
    private readonly TextBox _promptBox = CreateEditorBox(readOnly: false);
    private readonly TextBox _responseBox = CreateEditorBox(readOnly: true);
    private readonly TextBox _activityBox = CreateEditorBox(readOnly: true);

    private readonly Button _activateButton = CreateButton("Redeem Code", Accent, Color.Black);
    private readonly Button _loginButton = CreateButton("Login With Code", AccentSoft, Ink);
    private readonly Button _executeButton = CreateButton("Execute Setup", AccentAlt, Color.Black);
    private readonly Button _refreshButton = CreateButton("Refresh Access", AccentSoft, Ink);
    private readonly Button _removeButton = CreateButton("Remove Access", ColorTranslator.FromHtml("#351B16"), Danger);
    private readonly Button _sendButton = CreateButton("Send Prompt", Accent, Color.Black);
    private readonly Button _openRuntimeButton = CreateButton("Open Runtime Folder", PanelRaised, Ink);

    private readonly Label _bannerLabel = CreateBannerLabel();
    private readonly Label _setupStateValue = CreateValueLabel();
    private readonly Label _planValue = CreateValueLabel();
    private readonly Label _expiresValue = CreateValueLabel();
    private readonly Label _statusValue = CreateValueLabel();
    private readonly Label _providerValue = CreateValueLabel();

    public MainForm()
    {
        _config = _storage.LoadConfig() ?? new LauncherConfig
        {
            ServerUrl = Environment.GetEnvironmentVariable("CODEX_GATEWAY_URL") ?? "https://codex-license-gateway-image.onrender.com"
        };
        _session = _storage.LoadSession();
        _installation = _storage.LoadInstallation();

        Text = "Codex Gateway Client";
        MinimumSize = new Size(1320, 860);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppBack;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        BuildLayout();
        BindEvents();
        LoadPersistedState();
        UpdateUiState();

        Shown += async (_, _) => await InitializeAsync();
        FormClosing += (_, _) => _heartbeat.Stop();
    }

    private void BuildLayout()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBack,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 3
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 138));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        shell.Controls.Add(BuildHeader(), 0, 0);
        shell.Controls.Add(BuildStatusRow(), 0, 1);
        shell.Controls.Add(BuildWorkspace(), 0, 2);

        Controls.Add(shell);
    }

    private Control BuildHeader()
    {
        var header = CreatePanel();
        header.Padding = new Padding(24, 18, 24, 18);

        var title = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 42,
            Text = "Customer Codex access, controlled by your gateway.",
            Font = new Font("Segoe UI Semibold", 24F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Ink
        };

        var subtitle = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 36,
            Text = "Redeem a code, execute setup on this device, refresh access when the backend changes, and get logged out automatically when the plan expires or is disabled.",
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Muted
        };

        _bannerLabel.Dock = DockStyle.Bottom;
        _bannerLabel.Height = 24;

        header.Controls.Add(_bannerLabel);
        header.Controls.Add(subtitle);
        header.Controls.Add(title);
        return header;
    }

    private Control BuildStatusRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBack,
            ColumnCount = 5,
            RowCount = 1,
            Margin = new Padding(0, 12, 0, 12)
        };

        for (var i = 0; i < 5; i += 1)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        }

        row.Controls.Add(BuildMetricCard("Setup State", _setupStateValue), 0, 0);
        row.Controls.Add(BuildMetricCard("Plan", _planValue), 1, 0);
        row.Controls.Add(BuildMetricCard("Expires", _expiresValue), 2, 0);
        row.Controls.Add(BuildMetricCard("Access Status", _statusValue), 3, 0);
        row.Controls.Add(BuildMetricCard("Provider Key", _providerValue), 4, 0);

        return row;
    }

    private Control BuildWorkspace()
    {
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBack,
            ColumnCount = 2,
            RowCount = 1
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 408));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        workspace.Controls.Add(BuildLeftRail(), 0, 0);
        workspace.Controls.Add(BuildPromptArea(), 1, 0);

        return workspace;
    }

    private Control BuildLeftRail()
    {
        var rail = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBack,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 16, 0)
        };
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 272));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        rail.Controls.Add(BuildConnectionPanel(), 0, 0);
        rail.Controls.Add(BuildSetupPanel(), 0, 1);
        rail.Controls.Add(BuildNotesPanel(), 0, 2);
        return rail;
    }

    private Control BuildConnectionPanel()
    {
        var panel = CreatePanel();
        panel.Padding = new Padding(18);

        var layout = CreateSectionLayout();
        layout.Controls.Add(CreateSectionTitle("Access"), 0, 0);
        layout.Controls.Add(CreateSectionText("Customers redeem their one-time code here. The first redemption locks the device and starts the subscription timeline."), 0, 1);
        layout.Controls.Add(CreateField("Gateway URL", _serverUrlBox), 0, 2);
        layout.Controls.Add(CreateField("Access Code", _accessCodeBox), 0, 3);
        layout.Controls.Add(CreateField("Customer Email", _emailBox), 0, 4);

        var actions = CreateActionRow();
        actions.Controls.Add(_activateButton);
        actions.Controls.Add(_loginButton);
        layout.Controls.Add(actions, 0, 5);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildSetupPanel()
    {
        var panel = CreatePanel();
        panel.Padding = new Padding(18);

        var layout = CreateSectionLayout();
        layout.Controls.Add(CreateSectionTitle("Device Setup"), 0, 0);
        layout.Controls.Add(CreateSectionText("Execute setup writes the local runtime files for this customer device. Refresh re-checks backend access. Remove access wipes local state and revokes the current session."), 0, 1);

        var actions = CreateActionRow();
        actions.Controls.Add(_executeButton);
        actions.Controls.Add(_refreshButton);
        layout.Controls.Add(actions, 0, 2);

        var lowerActions = CreateActionRow();
        lowerActions.Controls.Add(_removeButton);
        lowerActions.Controls.Add(_openRuntimeButton);
        layout.Controls.Add(lowerActions, 0, 3);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildNotesPanel()
    {
        var panel = CreatePanel();
        panel.Padding = new Padding(18);

        var layout = CreateSectionLayout();
        layout.Controls.Add(CreateSectionTitle("Runtime Notes"), 0, 0);
        layout.Controls.Add(CreateSectionText("The customer app never stores or receives your upstream OpenAI key. It only keeps a session token and talks to your gateway. If the admin disables the license or the expiry hits, the next refresh clears local access."), 0, 1);

        var notes = CreateInfoList([
            "Refresh Access picks up backend changes, including key rotation.",
            "Remove Access clears the local session and runtime folder.",
            "After expiry, the customer must redeem a new code to continue."
        ]);
        layout.Controls.Add(notes, 0, 2);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildPromptArea()
    {
        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBack,
            ColumnCount = 1,
            RowCount = 2
        };
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 44));

        area.Controls.Add(BuildPromptPanel(), 0, 0);
        area.Controls.Add(BuildOutputPanel(), 0, 1);
        return area;
    }

    private Control BuildPromptPanel()
    {
        var panel = CreatePanel();
        panel.Padding = new Padding(18);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBack,
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        layout.Controls.Add(CreateSectionTitle("Prompt Runner"), 0, 0);

        var topBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBack,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 8)
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        topBar.Controls.Add(CreateField("Prompt Model", _modelBox), 1, 0);
        topBar.Controls.Add(_sendButton, 2, 0);

        layout.Controls.Add(topBar, 0, 1);

        _promptBox.Text = "Describe the coding task here and route it through your gateway.";
        layout.Controls.Add(_promptBox, 0, 2);
        layout.Controls.Add(CreateSectionText("Use this as the customer-side workspace runner. Requests go to your gateway and inherit the license limits and expiry state."), 0, 3);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildOutputPanel()
    {
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBack,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 16, 0, 0)
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        split.Controls.Add(BuildEditorPanel("Response", _responseBox), 0, 0);
        split.Controls.Add(BuildEditorPanel("Activity", _activityBox), 1, 0);

        return split;
    }

    private Control BuildEditorPanel(string title, TextBox box)
    {
        var panel = CreatePanel();
        panel.Padding = new Padding(18);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBack,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateSectionTitle(title), 0, 0);
        layout.Controls.Add(box, 0, 1);

        panel.Controls.Add(layout);
        return panel;
    }

    private void BindEvents()
    {
        _activateButton.Click += async (_, _) => await RedeemAsync();
        _loginButton.Click += async (_, _) => await LoginAsync();
        _executeButton.Click += async (_, _) => await ExecuteSetupAsync();
        _refreshButton.Click += async (_, _) => await RefreshAccessAsync(manual: true);
        _removeButton.Click += async (_, _) => await RemoveAccessAsync(remoteLogout: true, clearCode: true, reason: "Access removed on this device.");
        _sendButton.Click += async (_, _) => await SendPromptAsync();
        _openRuntimeButton.Click += (_, _) => OpenRuntimeFolder();
        _heartbeat.Tick += async (_, _) => await RefreshAccessAsync(manual: false);
    }

    private void LoadPersistedState()
    {
        _serverUrlBox.Text = _config.ServerUrl;
        if (_session is not null)
        {
            _accessCodeBox.Text = _session.LicenseCode;
        }

        AppendActivity("Launcher loaded.");
        if (_installation is not null)
        {
            AppendActivity($"Runtime folder detected at {_installation.RuntimePath}.");
        }
    }

    private async Task InitializeAsync()
    {
        if (_session is null)
        {
            SetBanner("Redeem or login with a customer code to start.", Success);
            return;
        }

        await RefreshAccessAsync(manual: false);
        _heartbeat.Start();
    }

    private async Task RedeemAsync()
    {
        if (string.IsNullOrWhiteSpace(_accessCodeBox.Text) || string.IsNullOrWhiteSpace(_emailBox.Text))
        {
            SetBanner("Access code and email are required for first activation.", Danger);
            return;
        }

        await RunBusyAsync(async () =>
        {
            SetBanner("Redeeming code...", Accent);
            var result = await PostJsonAsync(
                $"{GetServerUrl()}/api/auth/redeem",
                new
                {
                    code = _accessCodeBox.Text.Trim(),
                    email = _emailBox.Text.Trim(),
                    deviceId = DeviceFingerprint.Current
                });

            await HandleAuthAsync(result, "Code redeemed.");
        });
    }

    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(_accessCodeBox.Text))
        {
            SetBanner("Enter the customer access code first.", Danger);
            return;
        }

        await RunBusyAsync(async () =>
        {
            SetBanner("Logging in...", Accent);
            var result = await PostJsonAsync(
                $"{GetServerUrl()}/api/auth/login",
                new
                {
                    code = _accessCodeBox.Text.Trim(),
                    deviceId = DeviceFingerprint.Current
                });

            await HandleAuthAsync(result, "Session restored.");
        });
    }

    private async Task ExecuteSetupAsync()
    {
        if (_session is null || _license is null)
        {
            SetBanner("Activate or login before executing setup.", Danger);
            return;
        }

        await RunBusyAsync(async () =>
        {
            await RefreshAccessCoreAsync(showSuccessBanner: false);
            if (_license is null || _session is null)
            {
                return;
            }

            _installation = _storage.SetupRuntime(GetServerUrl(), _session, _license, _providerKey);
            SetBanner("Setup complete. This device now has a local Codex gateway runtime folder.", Success);
            AppendActivity($"Setup executed. Runtime ready at {_installation.RuntimePath}.");
            UpdateUiState();
        });
    }

    private async Task RefreshAccessAsync(bool manual)
    {
        if (_session is null)
        {
            if (manual)
            {
                SetBanner("No stored session to refresh.", Danger);
            }
            return;
        }

        await RunBusyAsync(async () => await RefreshAccessCoreAsync(showSuccessBanner: manual));
    }

    private async Task RefreshAccessCoreAsync(bool showSuccessBanner)
    {
        if (_session is null)
        {
            return;
        }

        var refreshResponse = await SendAuthorizedAsync(HttpMethod.Post, $"{GetServerUrl()}/api/client/refresh", "");
        if (refreshResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var error = await ReadErrorAsync(refreshResponse);
            await RemoveAccessAsync(remoteLogout: false, clearCode: true, reason: $"Access ended: {error}");
            return;
        }

        if (!refreshResponse.IsSuccessStatusCode)
        {
            SetBanner(await ReadErrorAsync(refreshResponse), Danger);
            return;
        }

        var refreshBody = JsonSerializer.Deserialize<RefreshResponse>(
            await refreshResponse.Content.ReadAsStringAsync(),
            JsonOptions.Default);

        _license = refreshBody?.License;
        _providerKey = refreshBody?.ProviderKey;

        if (_license is null || !string.Equals(_license.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            await RemoveAccessAsync(remoteLogout: false, clearCode: true, reason: "License is no longer active.");
            return;
        }

        if (_installation is not null)
        {
            _installation = _storage.SetupRuntime(GetServerUrl(), _session!, _license, _providerKey);
        }

        UpdateUiState();
        AppendActivity($"Access refreshed at {DateTime.Now:G}.");
        if (showSuccessBanner)
        {
            SetBanner("Access refreshed from the gateway.", Success);
        }
    }

    private async Task RemoveAccessAsync(bool remoteLogout, bool clearCode, string reason)
    {
        if (remoteLogout && _session is not null)
        {
            try
            {
                await SendAuthorizedAsync(HttpMethod.Post, $"{GetServerUrl()}/api/client/logout", "");
            }
            catch
            {
                // Ignore remote logout failures during cleanup.
            }
        }

        _storage.ClearSession();
        _storage.ClearInstallation();
        _session = null;
        _license = null;
        _providerKey = null;
        _installation = null;

        if (clearCode)
        {
            _accessCodeBox.Text = "";
        }

        _responseBox.Clear();
        AppendActivity(reason);
        SetBanner(reason, Danger);
        UpdateUiState();
    }

    private async Task SendPromptAsync()
    {
        if (_session is null)
        {
            SetBanner("No active session. Redeem or login first.", Danger);
            return;
        }

        if (string.IsNullOrWhiteSpace(_promptBox.Text))
        {
            SetBanner("Enter a prompt first.", Danger);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var payload = JsonSerializer.Serialize(new
            {
                model = _modelBox.SelectedItem?.ToString() ?? "gpt-5.4-mini",
                input = _promptBox.Text
            }, JsonOptions.Default);

            SetBanner("Sending prompt through the gateway...", Accent);
            var response = await SendAuthorizedAsync(
                HttpMethod.Post,
                $"{GetServerUrl()}/api/client/responses",
                payload);

            var raw = await response.Content.ReadAsStringAsync();
            _responseBox.Text = Pretty(raw);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                await RemoveAccessAsync(remoteLogout: false, clearCode: true, reason: "Prompt blocked because access is no longer valid.");
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                SetBanner(await ReadErrorAsync(response, raw), Danger);
                AppendActivity($"Prompt failed: {response.StatusCode}");
                return;
            }

            AppendActivity("Prompt completed successfully.");
            await RefreshAccessCoreAsync(showSuccessBanner: false);
            SetBanner("Prompt completed.", Success);
        });
    }

    private async Task HandleAuthAsync(HttpResponseMessage response, string successMessage)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            SetBanner(ExtractErrorMessage(raw), Danger);
            AppendActivity($"Auth request failed: {response.StatusCode}");
            return;
        }

        var body = JsonSerializer.Deserialize<AuthResponse>(raw, JsonOptions.Default);
        if (body is null)
        {
            SetBanner("Invalid response from the gateway.", Danger);
            return;
        }

        _config = new LauncherConfig { ServerUrl = GetServerUrl() };
        _storage.SaveConfig(_config);

        _session = new SessionData
        {
            SessionToken = body.SessionToken,
            LicenseCode = body.License.Code,
            CustomerEmail = body.License.CustomerEmail
        };
        _storage.SaveSession(_session);

        _license = body.License;
        _providerKey = null;
        _heartbeat.Start();

        UpdateUiState();
        AppendActivity(successMessage);
        SetBanner(successMessage, Success);
        await RefreshAccessCoreAsync(showSuccessBanner: false);
    }

    private void UpdateUiState()
    {
        _setupStateValue.Text = _installation is null ? "Not Ready" : "Runtime Ready";
        _planValue.Text = _license?.Plan?.ToUpperInvariant() ?? "No Plan";
        _expiresValue.Text = FormatExpiry(_license?.ExpiresAt);
        _statusValue.Text = _license is null ? "Locked" : StatusText(_license);
        _providerValue.Text = _providerKey?.Name ?? "Waiting";

        _executeButton.Enabled = _session is not null && _license is not null;
        _refreshButton.Enabled = _session is not null;
        _removeButton.Enabled = _session is not null || _installation is not null;
        _sendButton.Enabled = _session is not null && _license is not null;
        _openRuntimeButton.Enabled = _installation is not null && Directory.Exists(_installation.RuntimePath);

        if (_installation is not null && Directory.Exists(_installation.RuntimePath))
        {
            _activityBox.Text = _activityBox.Text;
        }
    }

    private void SetBanner(string message, Color color)
    {
        _bannerLabel.Text = message;
        _bannerLabel.ForeColor = color;
    }

    private void AppendActivity(string message)
    {
        var prefix = $"[{DateTime.Now:HH:mm:ss}] ";
        _activityBox.AppendText($"{prefix}{message}{Environment.NewLine}");
    }

    private void OpenRuntimeFolder()
    {
        if (_installation is null || !Directory.Exists(_installation.RuntimePath))
        {
            SetBanner("No runtime folder has been set up on this device.", Danger);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _installation.RuntimePath,
            UseShellExecute = true
        });
    }

    private string GetServerUrl() => _serverUrlBox.Text.Trim().TrimEnd('/');

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string url, string payload)
    {
        if (_session is null)
        {
            throw new InvalidOperationException("Session is not available.");
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _session.SessionToken);
        using var request = new HttpRequestMessage(method, url);
        if (method != HttpMethod.Get)
        {
            request.Content = new StringContent(payload ?? "", Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(string url, object body)
    {
        using var client = new HttpClient();
        var json = JsonSerializer.Serialize(body, JsonOptions.Default);
        return await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
    }

    private async Task RunBusyAsync(Func<Task> work)
    {
        if (!await _busyLock.WaitAsync(0))
        {
            return;
        }

        ToggleBusyState(true);
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            SetBanner(ex.Message, Danger);
            AppendActivity($"Unexpected error: {ex.Message}");
        }
        finally
        {
            ToggleBusyState(false);
            _busyLock.Release();
        }
    }

    private void ToggleBusyState(bool busy)
    {
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        _activateButton.Enabled = !busy;
        _loginButton.Enabled = !busy;
        _executeButton.Enabled = !busy && _session is not null && _license is not null;
        _refreshButton.Enabled = !busy && _session is not null;
        _removeButton.Enabled = !busy && (_session is not null || _installation is not null);
        _sendButton.Enabled = !busy && _session is not null && _license is not null;
        _openRuntimeButton.Enabled = !busy && _installation is not null && Directory.Exists(_installation.RuntimePath);
    }

    private static string Pretty(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc, JsonOptions.Default);
        }
        catch
        {
            return raw;
        }
    }

    private static string ExtractErrorMessage(string raw)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ApiErrorEnvelope>(raw, JsonOptions.Default);
            if (!string.IsNullOrWhiteSpace(error?.Error))
            {
                return error.Error;
            }
        }
        catch
        {
            // Fall back to raw message.
        }

        return raw;
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, string? raw = null)
    {
        raw ??= await response.Content.ReadAsStringAsync();
        return ExtractErrorMessage(raw);
    }

    private static string StatusText(LicenseView license)
    {
        if (!string.IsNullOrWhiteSpace(license.DisabledAt))
        {
            return "Disabled";
        }

        return string.IsNullOrWhiteSpace(license.Status) ? "Active" : license.Status;
    }

    private static string FormatExpiry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Not Set";
        }

        if (!DateTime.TryParse(value, out var parsed))
        {
            return value;
        }

        return parsed.ToLocalTime().ToString("dd MMM yyyy, HH:mm");
    }

    private static Label CreateBannerLabel()
    {
        return new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Success
        };
    }

    private static TextBox CreateInputBox(string placeholder)
    {
        return new TextBox
        {
            PlaceholderText = placeholder,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = PanelRaised,
            ForeColor = Ink,
            Height = 34,
            Margin = new Padding(0, 6, 0, 0)
        };
    }

    private static TextBox CreateEditorBox(bool readOnly)
    {
        return new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = readOnly,
            Dock = DockStyle.Fill,
            BackColor = PanelRaised,
            ForeColor = Ink,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point)
        };
    }

    private static ComboBox CreateModelBox()
    {
        var box = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = PanelRaised,
            ForeColor = Ink,
            FlatStyle = FlatStyle.Flat
        };
        box.Items.AddRange(["gpt-5.4-mini", "gpt-5.4", "gpt-5.3-codex"]);
        box.SelectedIndex = 0;
        return box;
    }

    private static Button CreateButton(string text, Color back, Color fore)
    {
        return new Button
        {
            Text = text,
            BackColor = back,
            ForeColor = fore,
            FlatStyle = FlatStyle.Flat,
            Height = 38,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0)
        };
    }

    private static Panel CreatePanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBack,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
    }

    private static TableLayoutPanel CreateSectionLayout()
    {
        return new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBack,
            ColumnCount = 1,
            RowCount = 8
        };
    }

    private static FlowLayoutPanel CreateActionRow()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 10, 0, 0),
            BackColor = PanelBack
        };
    }

    private static Label CreateSectionTitle(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Height = 26,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Ink
        };
    }

    private static Label CreateSectionText(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Height = 46,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Muted,
            Margin = new Padding(0, 4, 0, 0)
        };
    }

    private static Control CreateField(string title, Control input)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = PanelBack,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 10, 0, 0),
            AutoSize = true
        };

        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 18,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Muted
        }, 0, 0);
        panel.Controls.Add(input, 0, 1);
        return panel;
    }

    private static Control BuildMetricCard(string title, Label valueLabel)
    {
        var panel = CreatePanel();
        panel.Padding = new Padding(18, 16, 18, 16);
        panel.Margin = new Padding(0, 0, 12, 0);

        var titleLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 18,
            Text = title,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Muted
        };

        valueLabel.Dock = DockStyle.Fill;
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(titleLabel);
        return panel;
    }

    private static Label CreateValueLabel()
    {
        return new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Ink
        };
    }

    private static Control CreateInfoList(IReadOnlyList<string> items)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBack,
            ColumnCount = 1,
            RowCount = items.Count
        };

        for (var i = 0; i < items.Count; i += 1)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            panel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = $"• {items[i]}",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Muted
            }, 0, i);
        }

        return panel;
    }
}

sealed class LocalStorage
{
    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexGatewayCustomer");

    private string ConfigPath => Path.Combine(_root, "config.json");
    private string SessionPath => Path.Combine(_root, "session.json");
    private string InstallationPath => Path.Combine(_root, "installation.json");
    private string RuntimePath => Path.Combine(_root, "runtime");

    public LauncherConfig? LoadConfig()
    {
        var persisted = LoadJson<LauncherConfig>(ConfigPath);
        if (persisted is not null)
        {
            return persisted;
        }

        var bundledConfig = Path.Combine(AppContext.BaseDirectory, "config.json");
        return LoadJson<LauncherConfig>(bundledConfig);
    }

    public SessionData? LoadSession() => LoadJson<SessionData>(SessionPath);

    public InstallationState? LoadInstallation() => LoadJson<InstallationState>(InstallationPath);

    public void SaveConfig(LauncherConfig config)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions.Default));
    }

    public void SaveSession(SessionData session)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(SessionPath, JsonSerializer.Serialize(session, JsonOptions.Default));
    }

    public InstallationState SetupRuntime(string serverUrl, SessionData session, LicenseView license, ProviderKeyView? providerKey)
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(RuntimePath);

        var install = new InstallationState
        {
            RuntimePath = RuntimePath,
            InstalledAt = DateTime.UtcNow.ToString("O"),
            LastRefreshAt = DateTime.UtcNow.ToString("O"),
            ServerUrl = serverUrl,
            LicenseCode = license.Code,
            Plan = license.Plan,
            ExpiresAt = license.ExpiresAt,
            ProviderKeyName = providerKey?.Name
        };

        File.WriteAllText(
            Path.Combine(RuntimePath, "gateway-profile.json"),
            JsonSerializer.Serialize(new RuntimeProfile
            {
                ServerUrl = serverUrl,
                LicenseCode = license.Code,
                Plan = license.Plan,
                ExpiresAt = license.ExpiresAt,
                Status = license.Status,
                CustomerEmail = license.CustomerEmail,
                ProviderKeyName = providerKey?.Name
            }, JsonOptions.Default));

        File.WriteAllText(
            Path.Combine(RuntimePath, "README.txt"),
            string.Join(
                Environment.NewLine,
                [
                    "Codex Gateway Runtime",
                    "",
                    "This folder is created by the customer EXE.",
                    "It does not contain the upstream OpenAI API key.",
                    $"Server: {serverUrl}",
                    $"License: {license.Code}",
                    $"Plan: {license.Plan}",
                    $"Expires: {license.ExpiresAt ?? "-"}",
                    $"Installed: {install.InstalledAt}"
                ]));

        SaveInstallation(install);
        SaveSession(session);
        return install;
    }

    public void ClearSession()
    {
        if (File.Exists(SessionPath))
        {
            File.Delete(SessionPath);
        }
    }

    public void ClearInstallation()
    {
        if (File.Exists(InstallationPath))
        {
            File.Delete(InstallationPath);
        }

        if (Directory.Exists(RuntimePath))
        {
            Directory.Delete(RuntimePath, true);
        }
    }

    private void SaveInstallation(InstallationState installation)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(InstallationPath, JsonSerializer.Serialize(installation, JsonOptions.Default));
    }

    private static T? LoadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions.Default);
    }
}

static class DeviceFingerprint
{
    public static string Current
    {
        get
        {
            var input = $"{Environment.MachineName}|{Environment.UserName}|{Environment.OSVersion}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }
    }
}

sealed class LauncherConfig
{
    public string ServerUrl { get; set; } = "";
}

sealed class SessionData
{
    public string SessionToken { get; set; } = "";
    public string LicenseCode { get; set; } = "";
    public string? CustomerEmail { get; set; }
}

sealed class InstallationState
{
    public string RuntimePath { get; set; } = "";
    public string InstalledAt { get; set; } = "";
    public string LastRefreshAt { get; set; } = "";
    public string ServerUrl { get; set; } = "";
    public string LicenseCode { get; set; } = "";
    public string Plan { get; set; } = "";
    public string? ExpiresAt { get; set; }
    public string? ProviderKeyName { get; set; }
}

sealed class RuntimeProfile
{
    public string ServerUrl { get; set; } = "";
    public string LicenseCode { get; set; } = "";
    public string Plan { get; set; } = "";
    public string? ExpiresAt { get; set; }
    public string? Status { get; set; }
    public string? CustomerEmail { get; set; }
    public string? ProviderKeyName { get; set; }
}

sealed class AuthResponse
{
    public string SessionToken { get; set; } = "";
    public LicenseView License { get; set; } = new();
}

sealed class RefreshResponse
{
    public LicenseView License { get; set; } = new();
    public ProviderKeyView? ProviderKey { get; set; }
}

sealed class ProviderKeyView
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Name { get; set; } = "";
    public string? UpdatedAt { get; set; }
}

sealed class ApiErrorEnvelope
{
    public string? Error { get; set; }
    public LicenseView? License { get; set; }
}

sealed class LicenseView
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string Plan { get; set; } = "";
    public string Status { get; set; } = "";
    public string? CustomerEmail { get; set; }
    public string? DeviceId { get; set; }
    public int DurationMonths { get; set; }
    public string? RedeemedAt { get; set; }
    public string? ExpiresAt { get; set; }
    public string? DisabledAt { get; set; }
    public string? DisabledReason { get; set; }
    public string? ResetAt { get; set; }
    public RateLimitSummary? RateLimits { get; set; }
}

sealed class RateLimitSummary
{
    public Dictionary<string, Dictionary<string, RateWindow>>? Categories { get; set; }
}

sealed class RateWindow
{
    public bool Ok { get; set; }
    public int? Limit { get; set; }
    public int Remaining { get; set; }
    public int? WeeklyLimit { get; set; }
    public int WeeklyRemaining { get; set; }
}

static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
