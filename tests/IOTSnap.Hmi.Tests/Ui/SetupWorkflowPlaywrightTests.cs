using Microsoft.Data.Sqlite;
using Microsoft.Playwright;
using Xunit;

namespace IOTSnap.Hmi.Tests.Ui;

[Collection(PlaywrightCollection.Name)]
public sealed class SetupWorkflowPlaywrightTests(PlaywrightBrowserFixture browserFixture) : IAsyncLifetime
{
    private readonly PlaywrightUiHost _host = new();
    private IBrowserContext _browserContext = null!;
    private IPage _page = null!;

    public async Task InitializeAsync()
    {
        await EnsureOpcEndpointReachableAsync(_host.OpcEndpoint);
        await _host.InitializeAsync();
        _browserContext = await (browserFixture.Browser
            ?? throw new InvalidOperationException("Playwright did not launch the browser fixture."))
            .NewContextAsync();
        _page = await _browserContext.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await _browserContext.DisposeAsync();
        await _host.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "UiPlaywright")]
    public async Task FirstRunSetup_ManualOpcFlow_CreatesAdminImportsTagsAndLandsHome()
    {
        await _page.GotoAsync(new Uri(_host.BaseUri, "/").ToString());
        await WaitForBlazorReadyAsync();

        await ExpectVisibleAsync("Bring the HMI online");
        await _page.GetByLabel("Username").FillAsync("admin");
        await _page.GetByLabel("Display Name").FillAsync("Automation Admin");
        await _page.GetByLabel("Password", new() { Exact = true }).FillAsync("admin123!");
        await _page.GetByLabel("Confirm Password", new() { Exact = true }).FillAsync("admin123!");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Continue to OPC Setup" }).ClickAsync();

        await ExpectVisibleWithDebugAsync("Connection Profile");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Enter Manually" }).ClickAsync();
        await _page.GetByLabel("Endpoint URL").FillAsync(_host.OpcEndpoint);
        await _page.GetByLabel("Profile Name").FillAsync("Local Docker PLC");
        await _page.GetByLabel("Browse Root").FillAsync("Objects/OpcPlc");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Browse and Import Tags" }).ClickAsync();

        await ExpectExactVisibleWithDebugAsync("Tag Import");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Review and Finish" }).ClickAsync();

        await ExpectExactVisibleWithDebugAsync("Ready to Finish");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Finish Setup" }).ClickAsync();
        await _page.WaitForURLAsync("**/");
        await ExpectVisibleWithDebugAsync("Operator-ready HMI starter shell");

        Assert.Equal(1, await QueryScalarAsync("select count(*) from LocalUsers where Username = 'admin'"));
        Assert.Equal(1, await QueryScalarAsync("select count(*) from OpcUaConnectionProfiles"));
        Assert.True(await QueryScalarAsync("select count(*) from OpcUaNodeMappings") >= 1);
    }

    [Fact]
    [Trait("Category", "UiPlaywright")]
    public async Task FirstRunSetup_ScanFlow_FindsLiveEndpointAndCompletes()
    {
        await _page.GotoAsync(new Uri(_host.BaseUri, "/setup").ToString());
        await WaitForBlazorReadyAsync();

        await _page.GetByLabel("Username").FillAsync("scanner");
        await _page.GetByLabel("Display Name").FillAsync("Scanner Admin");
        await _page.GetByLabel("Password", new() { Exact = true }).FillAsync("admin123!");
        await _page.GetByLabel("Confirm Password", new() { Exact = true }).FillAsync("admin123!");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Continue to OPC Setup" }).ClickAsync();

        await _page.GetByRole(AriaRole.Button, new() { Name = "Scan Network" }).First.ClickAsync();
        await _page.GetByLabel("Subnet Prefix").FillAsync("127.0.0");
        await _page.GetByLabel("Start Host").FillAsync("1");
        await _page.GetByLabel("End Host").FillAsync("1");
        await _page.GetByLabel("Port").FillAsync("50000");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Scan Network" }).Nth(1).ClickAsync();

        await ExpectVisibleWithDebugAsync("Scan complete.");
        await _page.GetByRole(AriaRole.Row).Filter(new() { HasText = "127.0.0.1:50000" }).GetByRole(AriaRole.Button, new() { Name = "Use Endpoint" }).ClickAsync();
        await _page.GetByLabel("Browse Root").FillAsync("Objects/OpcPlc/Harness");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Browse and Import Tags" }).ClickAsync();

        await ExpectExactVisibleWithDebugAsync("Tag Import");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Review and Finish" }).ClickAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Finish Setup" }).ClickAsync();
        await _page.WaitForURLAsync("**/");
        await ExpectVisibleWithDebugAsync("Operator-ready HMI starter shell");

        Assert.Equal(1, await QueryScalarAsync("select count(*) from LocalUsers where Username = 'scanner'"));
        Assert.True(await QueryScalarAsync("select count(*) from OpcUaNodeMappings") >= 1);
    }

    private async Task<long> QueryScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection($"Data Source={_host.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private async Task ExpectVisibleAsync(string text)
    {
        await _page.GetByText(text, new() { Exact = false }).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });
    }

    private async Task ExpectVisibleWithDebugAsync(string text)
    {
        try
        {
            await ExpectVisibleAsync(text);
        }
        catch (TimeoutException ex)
        {
            var mainText = await _page.Locator("main").InnerTextAsync();
            throw new Xunit.Sdk.XunitException(
                $"Timed out waiting for '{text}' on August 27, 2026. Current page text:{Environment.NewLine}{mainText}", ex);
        }
    }

    private async Task ExpectExactVisibleWithDebugAsync(string text)
    {
        try
        {
            await _page.GetByText(text, new() { Exact = true }).WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 30000
            });
        }
        catch (TimeoutException ex)
        {
            var mainText = await _page.Locator("main").InnerTextAsync();
            throw new Xunit.Sdk.XunitException(
                $"Timed out waiting for exact text '{text}' on August 27, 2026. Current page text:{Environment.NewLine}{mainText}", ex);
        }
    }

    private async Task WaitForBlazorReadyAsync()
    {
        await _page.WaitForFunctionAsync("() => typeof window.Blazor !== 'undefined'", null, new PageWaitForFunctionOptions
        {
            Timeout = 30000
        });
        await _page.WaitForTimeoutAsync(1000);
    }

    private static async Task EnsureOpcEndpointReachableAsync(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            throw new Xunit.Sdk.XunitException($"The configured live OPC endpoint '{endpoint}' is not a valid absolute URI.");
        }

        using var client = new System.Net.Sockets.TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await client.ConnectAsync(uri.Host, uri.Port, cts.Token);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"The live OPC endpoint '{endpoint}' was not reachable on August 27, 2026. Start the Docker PLC before running UI tests. {ex.Message}");
        }
    }
}
