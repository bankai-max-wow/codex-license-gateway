using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

ApplicationConfiguration.Initialize();
Application.Run(new MainForm());

sealed class MainForm : Form
{
    private readonly LocalStorage _storage = new();
    private readonly LauncherConfig _config;
    private readonly TextBox _serverUrlBox = new() { Dock = DockStyle.Top };
    private readonly TextBox _apiKeyBox = new() { Dock = DockStyle.Top, PlaceholderText = "Customer access key" };
    private readonly TextBox _emailBox = new() { Dock = DockStyle.Top, PlaceholderText = "Customer email (first activation only)" };
    private readonly ComboBox _modelBox = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _promptBox = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _outputBox = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
    private readonly TextBox _statusBox = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
    private readonly Button _activateButton = new() { Text = "Activate", Dock = DockStyle.Top };
    private readonly Button _loginButton = new() { Text = "Login", Dock = DockStyle.Top };
    private readonly Button _refreshButton = new() { Text = "Refresh Access", Dock = DockStyle.Top };
    private readonly Button _sendButton = new() { Text = "Send Prompt", Dock = DockStyle.Top };

    public MainForm()
    {
        _config = _storage.LoadConfig() ?? new LauncherConfig
        {
            ServerUrl = Environment.GetEnvironmentVariable("CODEX_GATEWAY_URL") ?? "http://localhost:3000"
        };

        Text = "Codex Gateway";
        Width = 1080;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;

        _serverUrlBox.Text = _config.ServerUrl;
        _modelBox.Items.AddRange(["gpt-5.4-mini", "gpt-5.4", "gpt-5.3-codex"]);
        _modelBox.SelectedIndex = 0;

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 210, Padding = new Padding(12) };
        topPanel.Controls.AddRange([
            new Label { Text = "Server URL", Dock = DockStyle.Top, Height = 18 },
            _serverUrlBox,
            new Label { Text = "Access Key", Dock = DockStyle.Top, Height = 18 },
            _apiKeyBox,
            new Label { Text = "Email for first activation", Dock = DockStyle.Top, Height = 18 },
            _emailBox,
            _activateButton,
            _loginButton,
            _refreshButton
        ]);

        var promptPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        promptPanel.Controls.Add(_promptBox);
        promptPanel.Controls.Add(_sendButton);
        promptPanel.Controls.Add(_modelBox);
        promptPanel.Controls.Add(new Label { Text = "Model", Dock = DockStyle.Top, Height = 18 });
        promptPanel.Controls.Add(new Label { Text = "Prompt", Dock = DockStyle.Top, Height = 18 });

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("Prompt") { Controls = { promptPanel } });
        tabs.TabPages.Add(new TabPage("Response") { Controls = { _outputBox } });
        tabs.TabPages.Add(new TabPage("Status") { Controls = { _statusBox } });

        Controls.Add(tabs);
        Controls.Add(topPanel);

        _activateButton.Click += async (_, _) => await RedeemAsync();
        _loginButton.Click += async (_, _) => await LoginAsync();
        _refreshButton.Click += async (_, _) => await RefreshAsync();
        _sendButton.Click += async (_, _) => await SendPromptAsync();

        LoadStoredState();
    }

    private async Task RedeemAsync()
    {
        var result = await PostJsonAsync(
            $"{GetServerUrl()}/api/auth/redeem",
            new
            {
                code = _apiKeyBox.Text.Trim(),
                email = _emailBox.Text.Trim(),
                deviceId = DeviceFingerprint.Current
            });

        await HandleAuthAsync(result);
    }

    private async Task LoginAsync()
    {
        var result = await PostJsonAsync(
            $"{GetServerUrl()}/api/auth/login",
            new
            {
                code = _apiKeyBox.Text.Trim(),
                deviceId = DeviceFingerprint.Current
            });

        await HandleAuthAsync(result);
    }

    private async Task RefreshAsync()
    {
        var session = _storage.LoadSession();
        if (session is null)
        {
            _statusBox.Text = "No stored session.";
            return;
        }

        using var client = NewClient(session.SessionToken);
        var response = await client.PostAsync($"{GetServerUrl()}/api/client/refresh", new StringContent(""));
        var text = await response.Content.ReadAsStringAsync();
        _statusBox.Text = Pretty(text);
    }

    private async Task SendPromptAsync()
    {
        var session = _storage.LoadSession();
        if (session is null)
        {
            _statusBox.Text = "No stored session. Activate or login first.";
            return;
        }

        using var client = NewClient(session.SessionToken);
        var payload = JsonSerializer.Serialize(new
        {
            model = _modelBox.SelectedItem?.ToString() ?? "gpt-5.4-mini",
            input = _promptBox.Text
        }, JsonOptions.Default);

        var response = await client.PostAsync(
            $"{GetServerUrl()}/api/client/responses",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        var text = await response.Content.ReadAsStringAsync();
        _outputBox.Text = Pretty(text);
        if (!response.IsSuccessStatusCode)
        {
            _statusBox.Text = Pretty(text);
        }
        else
        {
            await LoadMeAsync(session.SessionToken);
        }
    }

    private async Task HandleAuthAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _statusBox.Text = Pretty(text);
            return;
        }

        var body = JsonSerializer.Deserialize<AuthResponse>(text, JsonOptions.Default);
        if (body is null)
        {
            _statusBox.Text = "Invalid server response.";
            return;
        }

        _storage.SaveConfig(new LauncherConfig { ServerUrl = GetServerUrl() });
        _storage.SaveSession(new SessionData
        {
            SessionToken = body.SessionToken,
            LicenseCode = body.License.Code
        });

        _outputBox.Text = "";
        _statusBox.Text = RenderLicense(body.License);
    }

    private async Task LoadMeAsync(string sessionToken)
    {
        using var client = NewClient(sessionToken);
        var response = await client.GetAsync($"{GetServerUrl()}/api/client/me");
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _statusBox.Text = Pretty(text);
            return;
        }

        var body = JsonSerializer.Deserialize<LicenseView>(text, JsonOptions.Default);
        _statusBox.Text = body is null ? Pretty(text) : RenderLicense(body);
    }

    private void LoadStoredState()
    {
        var session = _storage.LoadSession();
        if (session is not null)
        {
            _apiKeyBox.Text = session.LicenseCode;
            _ = LoadMeAsync(session.SessionToken);
        }
    }

    private string GetServerUrl() => _serverUrlBox.Text.Trim().TrimEnd('/');

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

    private static HttpClient NewClient(string bearerToken)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(string url, object body)
    {
        using var client = new HttpClient();
        var json = JsonSerializer.Serialize(body, JsonOptions.Default);
        return await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
    }

    private static string RenderLicense(LicenseView license)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Plan: {license.Plan}");
        sb.AppendLine($"Code: {license.Code}");
        sb.AppendLine($"Redeemed: {license.RedeemedAt ?? "-"}");
        sb.AppendLine($"Expires: {license.ExpiresAt ?? "-"}");
        sb.AppendLine($"Status: {(license.DisabledAt is null ? "Active" : "Disabled")}");

        if (license.RateLimits?.Categories is not null &&
            license.RateLimits.Categories.TryGetValue("local_messages", out var localMessages))
        {
            sb.AppendLine();
            sb.AppendLine("5-hour and weekly limits:");
            foreach (var item in localMessages)
            {
                sb.AppendLine($"{item.Key}: {item.Value.Remaining}/{item.Value.Limit} left in 5h, weekly {item.Value.WeeklyRemaining}/{item.Value.WeeklyLimit}");
            }
        }

        return sb.ToString();
    }
}

sealed class LocalStorage
{
    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexGateway");

    private string ConfigPath => Path.Combine(_root, "config.json");
    private string SessionPath => Path.Combine(_root, "session.json");

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
}

sealed class AuthResponse
{
    public string SessionToken { get; set; } = "";
    public LicenseView License { get; set; } = new();
}

sealed class LicenseView
{
    public string Code { get; set; } = "";
    public string Plan { get; set; } = "";
    public string? RedeemedAt { get; set; }
    public string? ExpiresAt { get; set; }
    public string? DisabledAt { get; set; }
    public JsonElement? QuotaRemaining { get; set; }
    public RateLimitSummary? RateLimits { get; set; }
}

sealed class RateLimitSummary
{
    public Dictionary<string, Dictionary<string, RateWindow>>? Categories { get; set; }
}

sealed class RateWindow
{
    public int Limit { get; set; }
    public int Remaining { get; set; }
    public int WeeklyLimit { get; set; }
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
