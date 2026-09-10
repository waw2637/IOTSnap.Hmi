using System.Security.Claims;
using IOTSnap.Hmi.Components;
using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using IOTSnap.Hmi.Features.Designer;
using IOTSnap.Hmi.Runtime;
using IOTSnap.Hmi.Runtime.OpcUa;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

namespace IOTSnap.Hmi;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var dataDirectory = builder.Configuration["Hmi:DataDirectory"];
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");
        }

        Directory.CreateDirectory(dataDirectory);
        var keyDirectory = Path.Combine(dataDirectory, "keys");
        Directory.CreateDirectory(keyDirectory);
        var connectionString = (builder.Configuration.GetConnectionString("Hmi")
            ?? "Data Source={DataDirectory}/iotsnap-hmi.db")
            .Replace("{DataDirectory}", dataDirectory);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddMudServices();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        builder.Services.AddScoped(_ => new HttpClient
        {
            BaseAddress = new Uri(GetBaseAddress(builder))
        });
        builder.Services.AddScoped<IPasswordHasher<LocalUser>, PasswordHasher<LocalUser>>();
        builder.Services.AddScoped<RuntimeCommandService>();
        builder.Services.AddScoped<RuntimeAuditService>();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/login";
                options.Cookie.Name = "iotsnap_hmi_auth";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
            });
        builder.Services.AddAuthorization();
        builder.Services.AddHmiData(connectionString);
        builder.Services.AddHmiRuntime();

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/api/hmi/runtime", StringComparison.OrdinalIgnoreCase),
            branch => branch.Use(async (context, next) =>
            {
                var feature = context.Features.Get<IStatusCodePagesFeature>();
                if (feature is not null)
                {
                    feature.Enabled = false;
                }

                await next();
            }));
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();

        app.Use(async (context, next) =>
        {
            if (IsSetupBypassPath(context.Request.Path))
            {
                await next();
                return;
            }

            await using var scope = app.Services.CreateAsyncScope();
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HmiDbContext>>();
            await using var db = await dbContextFactory.CreateDbContextAsync(context.RequestAborted);
            var hasUsers = await db.LocalUsers.AnyAsync(context.RequestAborted);

            if (!hasUsers)
            {
                context.Response.Redirect("/setup");
                return;
            }

            await next();
        });

        app.UseAntiforgery();

        await EnsureDatabaseReadyAsync(app.Services, app.Logger);

        app.MapPost("/login/submit", async (
            HttpContext httpContext,
            IDbContextFactory<HmiDbContext> dbContextFactory,
            IPasswordHasher<LocalUser> passwordHasher,
            [FromForm] string username,
            [FromForm] string password,
            [FromForm] string? returnUrl) =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(httpContext.RequestAborted);
            var hasUsers = await db.LocalUsers.AnyAsync(httpContext.RequestAborted);

            if (!hasUsers)
            {
                return Results.LocalRedirect("/setup");
            }

            var user = await db.LocalUsers
                .FirstOrDefaultAsync(x => x.Username == username && x.IsEnabled, httpContext.RequestAborted);

            if (user is null)
            {
                return Results.LocalRedirect(BuildLoginRedirect(returnUrl, failed: true));
            }

            var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (verification == PasswordVerificationResult.Failed)
            {
                return Results.LocalRedirect(BuildLoginRedirect(returnUrl, failed: true));
            }

            user.LastLoginUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(httpContext.RequestAborted);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.GivenName, user.DisplayName),
                new(ClaimTypes.Role, user.Role)
            };

            var principal = new ClaimsPrincipal(
                new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal);

            return Results.LocalRedirect(NormalizeReturnUrl(returnUrl));
        }).DisableAntiforgery();

        app.MapPost("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("/login");
        }).DisableAntiforgery();

        app.MapGet("/favicon.ico", (IWebHostEnvironment environment) =>
            Results.File(
                Path.Combine(environment.WebRootPath, "favicon.svg"),
                "image/svg+xml"))
            .AllowAnonymous();

        app.MapGet("/setup/finalize", async (
            string? token,
            HttpContext httpContext,
            IDataProtectionProvider dataProtectionProvider,
            IDbContextFactory<HmiDbContext> dbContextFactory) =>
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return Results.LocalRedirect("/setup");
            }

            string payload;
            try
            {
                payload = dataProtectionProvider
                    .CreateProtector("IOTSnap.Hmi.SetupFinalize.v1")
                    .Unprotect(token);
            }
            catch
            {
                return Results.LocalRedirect("/setup");
            }

            var parts = payload.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !long.TryParse(parts[1], out var issuedUnixSeconds)
                || DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(issuedUnixSeconds) > TimeSpan.FromMinutes(10))
            {
                return Results.LocalRedirect("/setup");
            }

            await using var db = await dbContextFactory.CreateDbContextAsync(httpContext.RequestAborted);
            var user = await db.LocalUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == parts[0] && x.IsEnabled, httpContext.RequestAborted);

            if (user is null)
            {
                return Results.LocalRedirect("/setup");
            }

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                CreatePrincipal(user));

            return Results.LocalRedirect("/");
        }).AllowAnonymous();

        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/readyz", async (IDbContextFactory<HmiDbContext> dbContextFactory, IOpcUaRuntime runtime, CancellationToken cancellationToken) =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var userCount = await db.LocalUsers.CountAsync(cancellationToken);
            var profileCount = await db.OpcUaConnectionProfiles.CountAsync(cancellationToken);
            var runtimeStatus = await runtime.GetStatusAsync(cancellationToken);
            return Results.Json(
                new { status = runtimeStatus.IsConnected ? "ready" : "degraded", userCount, profileCount, runtime = runtimeStatus.Detail },
                statusCode: runtimeStatus.IsConnected || profileCount == 0 ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });

        app.MapDesignerApi();
        app.MapAgentControlApi();
        app.MapRuntimeApi();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }

    private static async Task EnsureDatabaseReadyAsync(
        IServiceProvider services,
        ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HmiDbContext>>();

        await using var db = await dbContextFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();

        if (!await db.AppSettings.AnyAsync())
        {
            db.AppSettings.AddRange(
                new AppSetting { Key = "ui.theme", Value = "dashboard-light", Category = "Ui" },
                new AppSetting { Key = "runtime.refreshMs", Value = "1000", Category = "Runtime" });
        }

        if (!await db.OpcUaConnectionProfiles.AnyAsync())
        {
            db.OpcUaConnectionProfiles.Add(new OpcUaConnectionProfile
            {
                Name = "Demo PLC",
                EndpointUrl = "opc.tcp://127.0.0.1:4840",
                Enabled = false,
                UseSecurity = false,
                SecurityPolicy = "None",
                SecurityMode = "None",
                AuthenticationMode = "Anonymous",
                PublishingIntervalMs = 1000,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
        }

        if (!await db.OpcUaNodeMappings.AnyAsync())
        {
            var profile = await db.OpcUaConnectionProfiles.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (profile is not null)
            {
                db.OpcUaNodeMappings.AddRange(
                    new OpcUaNodeMapping
                    {
                        OpcUaConnectionProfileId = profile.Id,
                        DisplayName = "Process Value",
                        NodeId = "ns=3;s=FastUInt1",
                        Area = "Process",
                        DataType = "Double",
                        SamplingIntervalMs = 1000,
                        IsWritable = false
                    },
                    new OpcUaNodeMapping
                    {
                        OpcUaConnectionProfileId = profile.Id,
                        DisplayName = "Start Command",
                        NodeId = "ns=3;s=Plant.Line.Start",
                        Area = "Commands",
                        DataType = "Boolean",
                        SamplingIntervalMs = 250,
                        IsWritable = true
                    });
            }
        }

        if (!await db.HmiScreens.AnyAsync())
        {
            var now = DateTimeOffset.UtcNow;
            var primaryNode = await db.OpcUaNodeMappings
                .AsNoTracking()
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();

            var primaryNodeId = primaryNode?.NodeId ?? "ns=3;s=FastUInt1";
            var commandNode = await db.OpcUaNodeMappings
                .AsNoTracking()
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync(x => x.IsWritable);
            var commandNodeId = commandNode?.NodeId ?? "ns=3;s=Plant.Line.Start";
            var displayName = string.IsNullOrWhiteSpace(primaryNode?.DisplayName)
                ? "Live Process Value"
                : primaryNode.DisplayName;

            var screen = new HmiScreen
            {
                Name = "Main Runtime",
                Slug = "main",
                Width = 1366,
                Height = 768,
                IsPublished = true,
                PublishedUtc = now,
                UpdatedUtc = now,
                Widgets =
                {
                    new HmiWidget
                    {
                        Key = "pv-main",
                        WidgetType = "Numeric",
                        Title = displayName,
                        X = 60,
                        Y = 60,
                        Width = 320,
                        Height = 160,
                        ZIndex = 1,
                        PropertiesJson = "{\"unit\":\"raw\",\"property\":\"Text\"}",
                        UpdatedUtc = now,
                        Bindings =
                        {
                            new HmiWidgetBinding
                            {
                                BindingRole = "Text",
                                SourceType = "OpcTag",
                                SourceKey = primaryNodeId,
                                MinRole = HmiRoles.Viewer,
                                UpdatedUtc = now
                            }
                        }
                    },
                    new HmiWidget
                    {
                        Key = "state-main",
                        WidgetType = "Numeric",
                        Title = "Line Status",
                        X = 420,
                        Y = 60,
                        Width = 240,
                        Height = 140,
                        ZIndex = 2,
                        PropertiesJson = "{\"property\":\"Color\"}",
                        UpdatedUtc = now,
                        Bindings =
                        {
                            new HmiWidgetBinding
                            {
                                BindingRole = "Color",
                                SourceType = "OpcTag",
                                SourceKey = primaryNodeId,
                                MinRole = HmiRoles.Viewer,
                                UpdatedUtc = now
                            }
                        }
                    },
                    new HmiWidget
                    {
                        Key = "cmd-start",
                        WidgetType = "CommandButton",
                        Title = "Start Line",
                        X = 700,
                        Y = 60,
                        Width = 220,
                        Height = 140,
                        ZIndex = 3,
                        PropertiesJson = "{\"property\":\"Action\",\"commandValue\":\"true\"}",
                        UpdatedUtc = now,
                        Bindings =
                        {
                            new HmiWidgetBinding
                            {
                                BindingRole = "Action",
                                SourceType = "OpcTag",
                                SourceKey = commandNodeId,
                                WriteRequiresConfirm = true,
                                MinRole = HmiRoles.Operator,
                                UpdatedUtc = now
                            }
                        }
                    }
                }
            };

            db.HmiScreens.Add(screen);
        }

#if DEV_SETUP_BYPASS
        var hostEnvironment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (hostEnvironment.IsDevelopment() && !await db.LocalUsers.AnyAsync())
        {
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<LocalUser>>();
            var adminUser = new LocalUser
            {
                Username = "admin",
                DisplayName = "Admin",
                Role = HmiRoles.Admin,
                IsEnabled = true,
                CreatedUtc = DateTimeOffset.UtcNow
            };

            adminUser.PasswordHash = passwordHasher.HashPassword(adminUser, "admin");
            db.LocalUsers.Add(adminUser);

            logger.LogWarning("DEV_SETUP_BYPASS enabled: seeded development admin account 'admin'/'admin'.");
        }
#endif

        await db.SaveChangesAsync();
        logger.LogInformation("HMI database ensured and migrated");
    }

    private static string GetBaseAddress(WebApplicationBuilder builder)
    {
        var configuredUrls = builder.Configuration["ASPNETCORE_URLS"];
        if (!string.IsNullOrWhiteSpace(configuredUrls))
        {
            var firstUrl = configuredUrls
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(firstUrl))
            {
                return firstUrl;
            }
        }

        return "http://localhost:5266";
    }

    private static bool IsSetupBypassPath(PathString path)
    {
        if (path.StartsWithSegments("/_framework")
            || path.StartsWithSegments("/_blazor")
            || path.StartsWithSegments("/_content")
            || path.StartsWithSegments("/favicon.ico")
            || path.StartsWithSegments("/app.css")
            || path.StartsWithSegments("/setup")
            || path.StartsWithSegments("/error")
            || path.StartsWithSegments("/not-found"))
        {
            return true;
        }

        return false;
    }

    private static string BuildLoginRedirect(string? returnUrl, bool failed)
    {
        var normalizedReturnUrl = Uri.EscapeDataString(NormalizeReturnUrl(returnUrl));
        return failed
            ? $"/login?error=1&returnUrl={normalizedReturnUrl}"
            : $"/login?returnUrl={normalizedReturnUrl}";
    }

    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/'))
        {
            return "/";
        }

        return returnUrl;
    }

    private static ClaimsPrincipal CreatePrincipal(LocalUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.DisplayName),
            new(ClaimTypes.Role, user.Role)
        };

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
