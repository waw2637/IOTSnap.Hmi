using Microsoft.Playwright;
using Xunit;

namespace IOTSnap.Hmi.Tests.Ui;

[CollectionDefinition(Name)]
public sealed class PlaywrightCollection : ICollectionFixture<PlaywrightBrowserFixture>
{
    public const string Name = "PlaywrightBrowser";
}

public sealed class PlaywrightBrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    public IBrowser? Browser { get; private set; }

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            ExecutablePath = "/usr/bin/google-chrome",
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }
}
