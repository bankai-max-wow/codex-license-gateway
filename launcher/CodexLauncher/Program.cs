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
    private const string DefaultModel = "gpt-5.4-mini";

    private static readonly Color AppBack = ColorTranslator.FromHtml("#0E0B08");
    private static readonly Color SidebarBack = ColorTranslator.FromHtml("#15110D");
    private static readonly Color PanelBack = ColorTranslator.FromHtml("#1C1611");
    private static readonly Color PanelRaised = ColorTranslator.FromHtml("#262019");
    private static readonly Color Border = ColorTranslator.FromHtml("#3B3127");
    private static readonly Color Ink = ColorTranslator.FromHtml("#F6EFE6");
    private static readonly Color Muted = ColorTranslator.FromHtml("#B7AA9A");
    private static readonly Color Gold = ColorTranslator.FromHtml("#F0A94B");
    private static readonly Color Olive = ColorTranslator.FromHtml("#C9D36F");
    private static readonly Color Danger = ColorTranslator.FromHtml("#F07D62");
    private static readonly Color Success = ColorTranslator.FromHtml("#83D4A4");
    private static readonly Color FocusTint = ColorTranslator.FromHtml("#302419");

    private readonly LocalStorage _storage = new();
    private readonly SemaphoreSlim _busyLock = new(1, 1);
    private readonly System.Windows.Forms.Timer _heartbeat = new() { Interval = 60_000 };

    private LauncherConfig _config;
    private SessionData? _session;
    private InstallationState? _installation;
    private LicenseView? _license;
    private ProviderKeyView? _providerKey;

    private readonly Label _banner = new();
    private readonly Label _planValue = CreateMetricValue();
    private readonly Label _expiresValue = CreateMetricValue();
    private readonly Label _statusValue = CreateMetricValue();
    private readonly Label _setupValue = CreateMetricValue();

    private readonly TextBox _accessCodeBox = CreateInput("Enter access code");
    private readonly TextBox _emailBox = CreateInput("Email required only for first redemption");
    private readonly TextBox _promptBox = CreateEditor(false);
    private readonly TextBox _responseBox = CreateEditor(true);
    private readonly TextBox _activityBox = CreateEditor(true);

    private readonly Button _activateButton = CreateButton("Activate Access", Gold, Color.Black);
    private readonly Button _executeButton = CreateButton("Execute Setup", Olive, Color.Black);
    private readonly Button _refreshButton = CreateButton("Refresh Access", FocusTint, Ink);
    private readonly Button _removeButton = CreateButton("Remove Access", ColorTranslator.FromHtml("#301612"), Danger);
    private readonly Button _sendButton = CreateButton("Send Prompt", Gold, Color.Black);

    public MainForm()
    {
        _config = _storage.LoadConfig() ?? new LauncherConfig
        {
            ServerUrl = "https://codex-license-gateway-image.onrender.com"
        };
        _session = _storage.LoadSession();
        _installation = _storage.LoadInstallation();

        Text = "Codex Gateway Client";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1380, 900);
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
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(18)
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        shell.Controls.Add(BuildSidebar(), 0, 0);
        shell.Controls.Add(BuildMainArea(), 1, 0);

        Controls.Add(shell);
    }

    private Control BuildSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SidebarBack,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0, 0, 16, 0),
            Padding = new Padding(18)
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 236));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        sidebar.Controls.Add(BuildBrandCard(), 0, 0);
        sidebar.Controls.Add(BuildAccessCard(), 0, 1);
        sidebar.Controls.Add(BuildActionsCard(), 0, 2);
        sidebar.Controls.Add(BuildNotesCard(), 0, 3);

        return sidebar;
    }

    private Control BuildBrandCard()
    {
        var card = CreateCard(SidebarBack);

        var title = new Label
        {
            Text = "CODEX GATEWAY",
            Dock = DockStyle.Top,
            Height = 28,
            ForeColor = Gold,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point)
        };

        var headline = new Label
        {
            Text = "Private customer access with seller-side control.",
            Dock = DockStyle.Top,
            Height = 58,
            ForeColor = Ink,
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold, GraphicsUnit.Point)
        };

        _banner.Dock = DockStyle.Bottom;
        _banner.Height = 36;
        _banner.ForeColor = Success;
        _banner.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point);

        card.Controls.Add(_banner);
        card.Controls.Add(headline);
        card.Controls.Add(title);
        return card;
    }

    private Control BuildAccessCard()
    {
        var card = CreateCard(PanelBack);
        card.Controls.Add(CreateSectionStack(
            "Redeem or restore",
            "Customers only need their code and, on first use, an email for redemption.",
            CreateFieldPanel("Access Code", _accessCodeBox),
            CreateFieldPanel("Customer Email", _emailBox),
            CreateAccentNote("If the code is already redeemed, Activate Access will restore the session instead of failing.")
        ));
        return card;
    }

    private Control BuildActionsCard()
    {
        var buttonStack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 4,
            AutoSize = true,
            BackColor = PanelBack
        };
        for (var i = 0; i < 4; i += 1)
        {
            buttonStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        }

        ConfigureButton(_activateButton);
        ConfigureButton(_executeButton);
        ConfigureButton(_refreshButton);
        ConfigureButton(_removeButton);

        buttonStack.Controls.Add(_activateButton, 0, 0);
        buttonStack.Controls.Add(_executeButton, 0, 1);
        buttonStack.Controls.Add(_refreshButton, 0, 2);
        buttonStack.Controls.Add(_removeButton, 0, 3);

        var card = CreateCard(PanelBack);
        card.Controls.Add(CreateSectionStack(
            "Device actions",
            "Execute Setup prepares the local runtime. Refresh pulls the latest backend state. Remove Access logs out this device and clears local files.",
            buttonStack
        ));
        return card;
    }

    private Control BuildNotesCard()
    {
        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            BackColor = PanelBack
        };
        list.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        list.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        list.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        list.Controls.Add(CreateBullet("No editable gateway URL is exposed to the customer."), 0, 0);
        list.Controls.Add(CreateBullet("Refresh picks up backend key rotation and access changes."), 0, 1);
        list.Controls.Add(CreateBullet("If the plan expires or is disabled, the app clears local access and requires a new code."), 0, 2);

        var card = CreateCard(PanelBack);
        card.Controls.Add(CreateSectionStack("How this works", "This app stays locked to your hosted gateway and your subscription controls.", list));
        return card;
    }

    private Control BuildMainArea()
    {
        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBack,
            ColumnCount = 1,
            RowCount = 4
        };
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        area.Controls.Add(BuildHeroCard(), 0, 0);
        area.Controls.Add(BuildMetricRow(), 0, 1);
        area.Controls.Add(BuildWorkspaceCard(), 0, 2);
        area.Controls.Add(BuildLowerRow(), 0, 3);

        return area;
    }

    private Control BuildHeroCard()
    {
        var card = CreateCard(PanelBack);

        var title = new Label
        {
            Text = "Premium dark client for your sold subscriptions.",
            Dock = DockStyle.Top,
            Height = 48,
            ForeColor = Ink,
            Font = new Font("Segoe UI Semibold", 24F, FontStyle.Bold, GraphicsUnit.Point)
        };

        var copy = new Label
        {
            Text = "Customers activate with a code, execute local setup, refresh when you rotate backend state, and get locked out automatically when access ends.",
            Dock = DockStyle.Top,
            Height = 36,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point)
        };

        card.Controls.Add(copy);
        card.Controls.Add(title);
        return card;
    }

    private Control BuildMetricRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBack,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 14, 0, 14)
        };
        for (var i = 0; i < 4; i += 1)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        row.Controls.Add(CreateMetricCard("Plan", _planValue), 0, 0);
        row.Controls.Add(CreateMetricCard("Expires", _expiresValue), 1, 0);
        row.Controls.Add(CreateMetricCard("Access Status", _statusValue), 2, 0);
        row.Controls.Add(CreateMetricCard("Setup State", _setupValue), 3, 0);
        return row;
    }

    private Control BuildWorkspaceCard()
    {
        var card = CreateCard(PanelBack);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBack,
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        var title = CreateSectionTitle("Workspace");
        var meta = new Label
        {
            Text = $"Requests route through your gateway using {DefaultModel}.",
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point)
        };

        ConfigureButton(_sendButton);
        _sendButton.Dock = DockStyle.Right;
        _sendButton.Width = 170;
        _sendButton.Margin = new Padding(0);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBack,
            ColumnCount = 2,
            RowCount = 1
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 182));
        footer.Controls.Add(new Label
        {
            Text = "Type the customer prompt here. Access rules stay server-side.",
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point)
        }, 0, 0);
        footer.Controls.Add(_sendButton, 1, 0);

        _promptBox.Text = "Describe the coding task here.";

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(meta, 0, 1);
        layout.Controls.Add(_promptBox, 0, 2);
        layout.Controls.Add(footer, 0, 3);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildLowerRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBack,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 14, 0, 0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        row.Controls.Add(BuildEditorCard("Response", _responseBox), 0, 0);
        row.Controls.Add(BuildEditorCard("Activity", _activityBox), 1, 0);
        return row;
    }

    private Control BuildEditorCard(string title, TextBox box)
    {
        var card = CreateCard(PanelBack);
        card.Margin = title == "Response" ? new Padding(0, 0, 14, 0) : new Padding(0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBack,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateSectionTitle(title), 0, 0);
        layout.Controls.Add(box, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private void BindEvents()
    {
        _activateButton.Click += async (_, _) => await ActivateAsync();
        _executeButton.Click += async (_, _) => await ExecuteSetupAsync();
        _refreshButton.Click += async (_, _) => await RefreshAccessAsync(manual: true);
        _removeButton.Click += async (_, _) => await RemoveAccessAsync(remoteLogout: true, clearCode: true, reason: "Access removed from this device.");
        _sendButton.Click += async (_, _) => await SendPromptAsync();
        _heartbeat.Tick += async (_, _) => await RefreshAccessAsync(manual: false);
    }

    private void LoadPersistedState()
    {
        if (_session is not null)
        {
            _accessCodeBox.Text = _session.LicenseCode;
            _emailBox.Text = _session.CustomerEmail ?? "";
        }

        AppendActivity("Launcher loaded.");
        if (_installation is not null)
        {
            AppendActivity($"Runtime already exists at {_installation.RuntimePath}.");
        }
    }

    private async Task InitializeAsync()
    {
        if (_session is null)
        {
            SetBanner("Redeem or restore a code to start.", Success);
            return;
        }

        _heartbeat.Start();
        await RefreshAccessAsync(manual: false);
    }

    private async Task ActivateAsync()
    {
        if (string.IsNullOrWhiteSpace(_accessCodeBox.Text))
        {
            SetBanner("Enter the customer access code first.", Danger);
            return;
        }

        await RunBusyAsync(async () =>
        {
            SetBanner("Activating access...", Gold);
            var code = _accessCodeBox.Text.Trim();
            var email = _emailBox.Text.Trim();
            HttpResponseMessage? response = null;

            if (!string.IsNullOrWhiteSpace(email))
            {
                response = await PostJsonAsync(
                    $"{_config.ServerUrl}/api/auth/redeem",
                    new
                    {
                        code,
                        email,
                        deviceId = DeviceFingerprint.Current
                    });

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    response.Dispose();
                    response = null;
                }
            }

            response ??= await PostJsonAsync(
                $"{_config.ServerUrl}/api/auth/login",
                new
                {
                    code,
                    deviceId = DeviceFingerprint.Current
                });

            await HandleAuthAsync(response, "Access activated.");
        });
    }

    private async Task ExecuteSetupAsync()
    {
        if (_session is null || _license is null)
        {
            SetBanner("Activate access before running setup.", Danger);
            return;
        }

        await RunBusyAsync(async () =>
        {
            await RefreshAccessCoreAsync(showSuccessBanner: false);
            if (_session is null || _license is null)
            {
                return;
            }

            _installation = _storage.SetupRuntime(_config.ServerUrl, _session, _license, _providerKey);
            AppendActivity($"Setup complete at {_installation.RuntimePath}.");
            SetBanner("Setup complete. The customer runtime is ready.", Success);
            UpdateUiState();
        });
    }

    private async Task RefreshAccessAsync(bool manual)
    {
        if (_session is null)
        {
            if (manual)
            {
                SetBanner("No active access to refresh.", Danger);
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

        var response = await SendAuthorizedAsync(HttpMethod.Post, $"{_config.ServerUrl}/api/client/refresh", "{}");
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var error = await ReadErrorAsync(response);
            await RemoveAccessAsync(remoteLogout: false, clearCode: true, reason: $"Access expired or disabled: {error}");
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            SetBanner(await ReadErrorAsync(response), Danger);
            return;
        }

        var body = JsonSerializer.Deserialize<RefreshResponse>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions.Default);

        _license = body?.License;
        _providerKey = body?.ProviderKey;

        if (_license is null || !string.Equals(_license.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            await RemoveAccessAsync(remoteLogout: false, clearCode: true, reason: "License is no longer active.");
            return;
        }

        if (_installation is not null)
        {
            _installation = _storage.SetupRuntime(_config.ServerUrl, _session!, _license, _providerKey);
        }

        AppendActivity("Access refreshed from gateway.");
        UpdateUiState();

        if (showSuccessBanner)
        {
            SetBanner("Access refreshed.", Success);
        }
    }

    private async Task RemoveAccessAsync(bool remoteLogout, bool clearCode, string reason)
    {
        if (remoteLogout && _session is not null)
        {
            try
            {
                await SendAuthorizedAsync(HttpMethod.Post, $"{_config.ServerUrl}/api/client/logout", "{}");
            }
            catch
            {
                // Cleanup should continue.
            }
        }

        _storage.ClearSession();
        _storage.ClearInstallation();

        _session = null;
        _license = null;
        _providerKey = null;
        _installation = null;
        _responseBox.Clear();

        if (clearCode)
        {
            _accessCodeBox.Clear();
            _emailBox.Clear();
        }

        AppendActivity(reason);
        SetBanner(reason, Danger);
        UpdateUiState();
    }

    private async Task SendPromptAsync()
    {
        if (_session is null)
        {
            SetBanner("Activate access before sending prompts.", Danger);
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
                model = DefaultModel,
                input = _promptBox.Text.Trim()
            }, JsonOptions.Default);

            SetBanner("Sending prompt...", Gold);
            var response = await SendAuthorizedAsync(HttpMethod.Post, $"{_config.ServerUrl}/api/client/responses", payload);
            var raw = await response.Content.ReadAsStringAsync();
            _responseBox.Text = Pretty(raw);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                await RemoveAccessAsync(remoteLogout: false, clearCode: true, reason: "Prompt rejected because access ended.");
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = ExtractErrorMessage(raw);
                AppendActivity($"Prompt failed: {error}");
                SetBanner(error, Danger);
                return;
            }

            AppendActivity("Prompt completed.");
            await RefreshAccessCoreAsync(showSuccessBanner: false);
            SetBanner("Prompt completed.", Success);
        });
    }

    private async Task HandleAuthAsync(HttpResponseMessage response, string successMessage)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var error = ExtractErrorMessage(raw);
            AppendActivity($"Activation failed: {error}");
            SetBanner(error, Danger);
            return;
        }

        var body = JsonSerializer.Deserialize<AuthResponse>(raw, JsonOptions.Default);
        if (body is null)
        {
            SetBanner("Invalid gateway response.", Danger);
            return;
        }

        _session = new SessionData
        {
            SessionToken = body.SessionToken,
            LicenseCode = body.License.Code,
            CustomerEmail = _emailBox.Text.Trim()
        };
        _license = body.License;
        _providerKey = null;

        _storage.SaveConfig(_config);
        _storage.SaveSession(_session);

        _heartbeat.Start();
        AppendActivity(successMessage);
        SetBanner(successMessage, Success);
        UpdateUiState();
        await RefreshAccessCoreAsync(showSuccessBanner: false);
    }

    private void UpdateUiState()
    {
        _planValue.Text = _license?.Plan?.ToUpperInvariant() ?? "LOCKED";
        _expiresValue.Text = FormatExpiry(_license?.ExpiresAt);
        _statusValue.Text = _license is null ? "NO ACCESS" : StatusText(_license);
        _setupValue.Text = _installation is null ? "NOT READY" : "READY";

        _activateButton.Enabled = true;
        _executeButton.Enabled = _session is not null && _license is not null;
        _refreshButton.Enabled = _session is not null;
        _removeButton.Enabled = _session is not null || _installation is not null;
        _sendButton.Enabled = _session is not null && _license is not null;
    }

    private async Task RunBusyAsync(Func<Task> work)
    {
        if (!await _busyLock.WaitAsync(0))
        {
            return;
        }

        ToggleBusy(true);
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            AppendActivity($"Unexpected error: {ex.Message}");
            SetBanner(ex.Message, Danger);
        }
        finally
        {
            ToggleBusy(false);
            _busyLock.Release();
        }
    }

    private void ToggleBusy(bool busy)
    {
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        _activateButton.Enabled = !busy;
        _executeButton.Enabled = !busy && _session is not null && _license is not null;
        _refreshButton.Enabled = !busy && _session is not null;
        _removeButton.Enabled = !busy && (_session is not null || _installation is not null);
        _sendButton.Enabled = !busy && _session is not null && _license is not null;
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string url, string payload)
    {
        if (_session is null)
        {
            throw new InvalidOperationException("Session is missing.");
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _session.SessionToken);

        using var request = new HttpRequestMessage(method, url);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(string url, object body)
    {
        using var client = new HttpClient();
        var payload = JsonSerializer.Serialize(body, JsonOptions.Default);
        return await client.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"));
    }

    private void AppendActivity(string message)
    {
        _activityBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void SetBanner(string text, Color color)
    {
        _banner.Text = text;
        _banner.ForeColor = color;
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
            var body = JsonSerializer.Deserialize<ApiErrorEnvelope>(raw, JsonOptions.Default);
            if (!string.IsNullOrWhiteSpace(body?.Error))
            {
                return body.Error;
            }
        }
        catch
        {
            // Ignore parse errors.
        }

        return raw;
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        return ExtractErrorMessage(await response.Content.ReadAsStringAsync());
    }

    private static string FormatExpiry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "NOT SET";
        }

        if (!DateTime.TryParse(value, out var parsed))
        {
            return value;
        }

        return parsed.ToLocalTime().ToString("dd MMM yyyy");
    }

    private static string StatusText(LicenseView license)
    {
        if (!string.IsNullOrWhiteSpace(license.DisabledAt))
        {
            return "DISABLED";
        }

        if (!string.IsNullOrWhiteSpace(license.Status))
        {
            return license.Status.ToUpperInvariant();
        }

        return "ACTIVE";
    }

    private static Panel CreateCard(Color backColor)
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = backColor,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(18),
            Margin = new Padding(0)
        };
    }

    private static Label CreateSectionTitle(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 28,
            ForeColor = Ink,
            Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold, GraphicsUnit.Point)
        };
    }

    private static Control CreateSectionStack(string title, string copy, params Control[] content)
    {
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent
        };

        stack.Controls.Add(CreateSectionTitle(title));
        stack.Controls.Add(new Label
        {
            Text = copy,
            Width = 260,
            Height = 44,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 8)
        });

        foreach (var control in content)
        {
            control.Margin = new Padding(0, 0, 0, 10);
            stack.Controls.Add(control);
        }

        return stack;
    }

    private static Panel CreateFieldPanel(string title, Control input)
    {
        var panel = new Panel
        {
            Width = 260,
            Height = 66,
            BackColor = Color.Transparent
        };

        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point)
        };

        input.Dock = DockStyle.Bottom;
        input.Height = 34;
        panel.Controls.Add(input);
        panel.Controls.Add(label);
        return panel;
    }

    private static Label CreateAccentNote(string text)
    {
        return new Label
        {
            Text = text,
            Width = 260,
            Height = 42,
            ForeColor = Gold,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
    }

    private static Label CreateBullet(string text)
    {
        return new Label
        {
            Text = $"- {text}",
            Width = 260,
            Height = 28,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point)
        };
    }

    private static Panel CreateMetricCard(string title, Label value)
    {
        var card = CreateCard(PanelBack);
        card.Margin = new Padding(0, 0, 14, 0);

        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point)
        };

        value.Dock = DockStyle.Bottom;
        card.Controls.Add(value);
        card.Controls.Add(label);
        return card;
    }

    private static Label CreateMetricValue()
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = Ink,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point)
        };
    }

    private static TextBox CreateInput(string placeholder)
    {
        return new TextBox
        {
            PlaceholderText = placeholder,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = PanelRaised,
            ForeColor = Ink
        };
    }

    private static TextBox CreateEditor(bool readOnly)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = readOnly,
            BackColor = PanelRaised,
            ForeColor = Ink,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point)
        };
    }

    private static Button CreateButton(string text, Color backColor, Color foreColor)
    {
        return new Button
        {
            Text = text,
            BackColor = backColor,
            ForeColor = foreColor,
            FlatStyle = FlatStyle.Flat,
            Height = 44,
            Width = 260,
            TabStop = false
        };
    }

    private static void ConfigureButton(Button button)
    {
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseDownBackColor = button.BackColor;
        button.FlatAppearance.MouseOverBackColor = button.BackColor;
        button.Margin = new Padding(0, 0, 0, 10);
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
                    "Created by the customer launcher.",
                    "No upstream OpenAI API key is stored here.",
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
    public string ServerUrl { get; set; } = "https://codex-license-gateway-image.onrender.com";
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
}

sealed class LicenseView
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string Plan { get; set; } = "";
    public string Status { get; set; } = "";
    public string? CustomerEmail { get; set; }
    public string? ExpiresAt { get; set; }
    public string? DisabledAt { get; set; }
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
