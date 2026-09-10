using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Xunit;

namespace IOTSnap.Hmi.Tests.Ui;

internal sealed class PlaywrightUiHost : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "iotsnap-ui-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _output = new();
    private Process? _process;

    public Uri BaseUri { get; private set; } = null!;
    public string DataDirectory => Path.Combine(_root, "data");
    public string DatabasePath => Path.Combine(_root, "ui-test.db");
    public string OpcEndpoint => Environment.GetEnvironmentVariable("IOTSNAP_LIVE_OPC_ENDPOINT") ?? "opc.tcp://127.0.0.1:50000";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(DataDirectory);

        var port = ReservePort();
        BaseUri = new Uri($"http://127.0.0.1:{port}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --no-build --project {Quote(GetAppProjectPath())} --urls {BaseUri}",
            WorkingDirectory = GetRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ConnectionStrings__Hmi"] = $"Data Source={DatabasePath}";
        startInfo.Environment["Hmi__DataDirectory"] = DataDirectory;

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        _process.OutputDataReceived += (_, args) => Capture(args.Data);
        _process.ErrorDataReceived += (_, args) => Capture(args.Data);

        if (!_process.Start())
        {
            throw new Xunit.Sdk.XunitException("The UI test host process did not start.");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        await WaitForHostAsync();
    }

    public async Task DisposeAsync()
    {
        if (_process is not null && !_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process?.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private async Task WaitForHostAsync()
    {
        using var client = new HttpClient();

        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (_process is not null && _process.HasExited)
            {
                throw new Xunit.Sdk.XunitException(
                    $"The UI test host exited before becoming ready. Output:{Environment.NewLine}{string.Join(Environment.NewLine, _output)}");
            }

            try
            {
                using var response = await client.GetAsync(new Uri(BaseUri, "/setup"));
                if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect or HttpStatusCode.Found)
                {
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(500);
        }

        throw new Xunit.Sdk.XunitException(
            $"The UI test host did not become ready at {BaseUri} on August 27, 2026. Output:{Environment.NewLine}{string.Join(Environment.NewLine, _output)}");
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private void Capture(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            lock (_output)
            {
                _output.Add(line);
                if (_output.Count > 200)
                {
                    _output.RemoveAt(0);
                }
            }
        }
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IOTSnap.Hmi.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the UI test output directory.");
    }

    private static string GetAppProjectPath()
        => Path.Combine(GetRepositoryRoot(), "src", "IOTSnap.Hmi", "IOTSnap.Hmi.csproj");

    private static string Quote(string value) => $"\"{value}\"";
}
