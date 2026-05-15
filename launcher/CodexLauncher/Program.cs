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
    private static readonly Color AppBack = ColorTranslator.FromHtml("#0D0A08");
    private static readonly Color SidebarBack = ColorTranslator.FromHtml("#15100C");
    private static readonly Color CardBack = ColorTranslator.FromHtml("#1D1611");
    private static readonly Color CardAlt = ColorTranslator.FromHtml("#261D16");
    private static readonly Color Border = ColorTranslator.FromHtml("#3B3027");
    private static readonly Color Ink = ColorTranslator.FromHtml("#F7EFE4");
    private static readonly Color Muted = ColorTranslator.FromHtml("#B9AA98");
    private static readonly Color Gold = ColorTranslator.FromHtml("#F1A648");
    private static readonly Color Success = ColorTranslator.FromHtml("#8AD0A7");
    private static readonly Color Danger = ColorTranslator.FromHtml("#F07B61");
    private static readonly Color ButtonDark = ColorTranslator.FromHtml("#30241B");

    private readonly LocalStorage _storage = new();
    private readonly SemaphoreSlim _busyLock = new(1, 1);
    private readonly System.Windows.Forms.Timer _heartbeat = new() { Interval = 60_000 };

    private LauncherConfig _config;
    private SessionData? _session;
    private InstallationState? _installation;
    private LicenseView? _license;
    private ProviderKeyView? _providerKey;

    private readonly Label _banner = CreateBannerLabel();
    private readonly Label _heroSubtext = CreateMutedLabel(42);
    private readonly Label _statusPill = CreatePillLabel();
    private readonly Label _planValue = CreateValueLabel();
    private readonly Label _expiresValue = CreateValueLabel();
    private readonly Label _setupValue = CreateValueLabel();
    private readonly Label _runtimeValue = CreateValueLabel();
    private readonly TextBox _accessCodeBox = CreateInput("Enter your access code");
    private readonly TextBox _emailBox = CreateInput("Email required only for first activation");
    private readonly TextBox _detailsBox = CreateDetailsBox();
    private readonly Button _activateButton = CreateButton("Activate Access", Gold, Color.Black);
    private readonly Button _refreshButton = CreateButton("Refresh Access", ButtonDark, Ink);

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
        MinimumSize = new Size(1240, 760);
        Size = new Size(1360, 860);
        BackColor = AppBack;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;

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
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        shell.Controls.Add(BuildSidebar(), 0, 0);
        shell.Controls.Add(BuildMainPanel(), 1, 0);

        Controls.Add(shell);
    }

    private Control BuildSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SidebarBack,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18),
            Margin = new Padding(0, 0, 16, 0)
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        sidebar.Controls.Add(BuildBrandCard(), 0, 0);
        sidebar.Controls.Add(BuildMetricCard("Plan", _planValue), 0, 1);
        sidebar.Controls.Add(BuildMetricCard("Expires", _expiresValue), 0, 2);
        sidebar.Controls.Add(BuildMetricCard("Setup State", _setupValue), 0, 3);
        sidebar.Controls.Add(BuildMetricCard("Runtime", _runtimeValue), 0, 4);

        return sidebar;
    }

    private Control BuildBrandCard()
    {
        var card = CreateCard(SidebarBack);

        var eyebrow = new Label
        {
            Text = "CODEX GATEWAY",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = Gold,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point)
        };

        var title = new Label
        {
            Text = "Private customer access.",
            Dock = DockStyle.Top,
            Height = 46,
            ForeColor = Ink,
            Font = new Font("Segoe UI Semibold", 21F, FontStyle.Bold, GraphicsUnit.Point)
        };

        var copy = new Label
        {
            Text = "Redeem, refresh, and keep working until the subscription ends.",
            Dock = DockStyle.Top,
            Height = 36,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point)
        };

        card.Controls.Add(copy);
        card.Controls.Add(title);
        card.Controls.Add(eyebrow);
        return card;
    }

    private Control BuildMainPanel()
    {
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBack,
            ColumnCount = 1,
            RowCount = 4
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        main.Controls.Add(BuildHeroCard(), 0, 0);
        main.Controls.Add(BuildStatusCard(), 0, 1);
        main.Controls.Add(BuildActivationCard(), 0, 2);
        main.Controls.Add(BuildDetailsCard(), 0, 3);

        return main;
    }

    private Control BuildHeroCard()
    {
        var card = CreateCard(CardBack);

        var title = new Label
        {
            Text = "Clean dark client for sold subscriptions.",
            Dock = DockStyle.Top,
            Height = 50,
            ForeColor = Ink,
            Font = new Font("Segoe UI Semibold", 24F, FontStyle.Bold, GraphicsUnit.Point)
        };

        _heroSubtext.Text = "Enter the code, activate access, and use refresh whenever your seller updates access on the backend.";
        _banner.Dock = DockStyle.Bottom;

        card.Controls.Add(_banner);
        card.Controls.Add(_heroSubtext);
        card.Controls.Add(title);
        return card;
    }

    private Control BuildStatusCard()
    {
        var card = CreateCard(CardBack);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBack,
            ColumnCount = 2,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBack,
            ColumnCount = 1,
            RowCount = 2
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        left.Controls.Add(CreateSectionTitle("Access status"), 0, 0);
        left.Controls.Add(new Label
        {
            Text = "The app stays linked to your seller's hosted gateway. If the subscription expires or is disabled, local access is cleared automatically.",
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point)
        }, 0, 1);

        var pillHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBack,
            Padding = new Padding(20, 18, 0, 18)
        };
        _statusPill.Dock = DockStyle.Fill;
        pillHost.Controls.Add(_statusPill);

        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(pillHost, 1, 0);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildActivationCard()
    {
        var card = CreateCard(CardBack);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBack,
            ColumnCount = 2,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBack,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(0, 2, 12, 0)
        };
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        form.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        form.Controls.Add(CreateSectionTitle("Activation"), 0, 0);
        form.Controls.Add(_accessCodeBox, 0, 1);
        form.Controls.Add(CreateFieldNote("Access code"), 0, 2);
        form.Controls.Add(_emailBox, 0, 3);
        form.Controls.Add(CreateFieldNote("Email is only needed on first redemption. If the code was already redeemed, the app restores the session."), 0, 4);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = CardBack,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0),
            Padding = new Padding(0, 2, 0, 0)
        };

        ConfigureButton(_activateButton);
        ConfigureButton(_refreshButton);

        var actionsHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBack,
            Padding = new Padding(0, 0, 0, 0)
        };
        buttons.Controls.Add(CreateSectionTitle("Actions"));
        buttons.Controls.Add(_activateButton);
        buttons.Controls.Add(_refreshButton);
        actionsHost.Controls.Add(buttons);

        layout.Controls.Add(form, 0, 0);
        layout.Controls.Add(actionsHost, 1, 0);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildDetailsCard()
    {
        var card = CreateCard(CardAlt);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = CardAlt,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateSectionTitle("Subscription details"), 0, 0);
        layout.Controls.Add(_detailsBox, 0, 1);

        card.Controls.Add(layout);
        return card;
    }

    private void BindEvents()
    {
        _activateButton.Click += async (_, _) => await ActivateAsync();
        _refreshButton.Click += async (_, _) => await RefreshAccessAsync(manual: true);
        _heartbeat.Tick += async (_, _) => await RefreshAccessAsync(manual: false);
    }

    private void LoadPersistedState()
    {
        if (_session is not null)
        {
            _accessCodeBox.Text = _session.LicenseCode;
            _emailBox.Text = _session.CustomerEmail ?? "";
        }

        AppendDetails("Launcher ready.");
        if (_installation is not null)
        {
            AppendDetails($"Local runtime folder: {_installation.RuntimePath}");
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
            SetBanner("Enter the access code first.", Danger);
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

            await HandleAuthAsync(response);
        });
    }

    private async Task RefreshAccessAsync(bool manual)
    {
        if (_session is null)
        {
            if (manual)
            {
                SetBanner("No active subscription found on this device.", Danger);
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
            await ClearAccessAsync($"Access ended: {error}");
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
            await ClearAccessAsync("Subscription is no longer active.");
            return;
        }

        _installation = _storage.SetupRuntime(_config.ServerUrl, _session!, _license, _providerKey);
        UpdateUiState();
        AppendDetails($"Access refreshed at {DateTime.Now:G}.");

        if (showSuccessBanner)
        {
            SetBanner("Access refreshed.", Success);
        }
    }

    private async Task HandleAuthAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var error = ExtractErrorMessage(raw);
            SetBanner(error, Danger);
            AppendDetails($"Activation failed: {error}");
            return;
        }

        var body = JsonSerializer.Deserialize<AuthResponse>(raw, JsonOptions.Default);
        if (body is null)
        {
            SetBanner("Invalid server response.", Danger);
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
        _installation = _storage.SetupRuntime(_config.ServerUrl, _session, _license, _providerKey);

        _heartbeat.Start();
        UpdateUiState();
        AppendDetails("Access activated and local setup completed.");
        SetBanner("Access activated.", Success);
        await RefreshAccessCoreAsync(showSuccessBanner: false);
    }

    private async Task ClearAccessAsync(string reason)
    {
        if (_session is not null)
        {
            try
            {
                await SendAuthorizedAsync(HttpMethod.Post, $"{_config.ServerUrl}/api/client/logout", "{}");
            }
            catch
            {
                // Ignore cleanup errors during forced logout.
            }
        }

        _storage.ClearSession();
        _storage.ClearInstallation();

        _session = null;
        _license = null;
        _providerKey = null;
        _installation = null;
        _accessCodeBox.Clear();
        _emailBox.Clear();

        UpdateUiState();
        AppendDetails(reason);
        SetBanner(reason, Danger);
    }

    private void UpdateUiState()
    {
        _planValue.Text = _license?.Plan?.ToUpperInvariant() ?? "LOCKED";
        _expiresValue.Text = FormatExpiry(_license?.ExpiresAt);
        _setupValue.Text = _installation is null ? "NOT READY" : "READY";
        _runtimeValue.Text = _providerKey?.Name ?? (_installation is null ? "WAITING" : "SYNCED");

        var active = _license is not null && string.Equals(_license.Status, "active", StringComparison.OrdinalIgnoreCase);
        _statusPill.Text = active ? "ACTIVE" : "LOCKED";
        _statusPill.ForeColor = active ? Success : Danger;
        _statusPill.BackColor = active ? ColorTranslator.FromHtml("#1F2A21") : ColorTranslator.FromHtml("#2B1A17");

        _activateButton.Enabled = true;
        _refreshButton.Enabled = _session is not null;
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
            SetBanner(ex.Message, Danger);
            AppendDetails($"Unexpected error: {ex.Message}");
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
        _refreshButton.Enabled = !busy && _session is not null;
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string url, string payload)
    {
        if (_session is null)
        {
            throw new InvalidOperationException("No active session.");
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _session.SessionToken);

        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(string url, object body)
    {
        using var client = new HttpClient();
        var payload = JsonSerializer.Serialize(body, JsonOptions.Default);
        return await client.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"));
    }

    private void AppendDetails(string line)
    {
        if (_detailsBox.TextLength == 0)
        {
            _detailsBox.Text = line;
            return;
        }

        _detailsBox.AppendText($"{Environment.NewLine}{line}");
    }

    private void SetBanner(string text, Color color)
    {
        _banner.Text = text;
        _banner.ForeColor = color;
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

    private static Label CreateBannerLabel()
    {
        return new Label
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            ForeColor = Success,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point)
        };
    }

    private static Label CreateMutedLabel(int height)
    {
        return new Label
        {
            Dock = DockStyle.Top,
            Height = height,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point)
        };
    }

    private static Label CreatePillLabel()
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point),
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private static Label CreateValueLabel()
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
        var box = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = CardAlt,
            ForeColor = Ink,
            PlaceholderText = placeholder,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 0)
        };
        box.MinimumSize = new Size(0, 36);
        return box;
    }

    private static TextBox CreateDetailsBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = CardAlt,
            ForeColor = Ink,
            Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point)
        };
    }

    private static Button CreateButton(string text, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 46,
            Width = 214,
            BackColor = backColor,
            ForeColor = foreColor,
            FlatStyle = FlatStyle.Flat,
            TabStop = false
        };
        return button;
    }

    private static void ConfigureButton(Button button)
    {
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Lighten(button.BackColor, 10);
        button.FlatAppearance.MouseDownBackColor = Lighten(button.BackColor, -8);
        button.Margin = new Padding(0, 0, 0, 12);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        button.Cursor = Cursors.Hand;
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

    private static Label CreateFieldNote(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
    }

    private static Panel BuildMetricCard(string title, Label value)
    {
        var card = CreateCard(CardBack);
        card.Padding = new Padding(18, 16, 18, 16);

        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point)
        };

        value.Dock = DockStyle.Fill;
        card.Controls.Add(value);
        card.Controls.Add(label);
        return card;
    }

    private static Color Lighten(Color color, int amount)
    {
        var r = Math.Clamp(color.R + amount, 0, 255);
        var g = Math.Clamp(color.G + amount, 0, 255);
        var b = Math.Clamp(color.B + amount, 0, 255);
        return Color.FromArgb(r, g, b);
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
