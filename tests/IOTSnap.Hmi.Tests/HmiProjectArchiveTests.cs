using System.Collections.Generic;
using IOTSnap.Hmi.Features.Designer;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class HmiProjectArchiveTests
{
    [Fact]
    public void Archive_CanRoundTripManifestAndAssets()
    {
        var package = HmiProjectPackage.Create(new HmiProjectPackageScreen(
            "Archive Screen",
            "archive-screen",
            1024,
            600,
            false,
            new[]
            {
                new HmiProjectPackageWidget(
                    "widget-1",
                    "Numeric",
                    "Tank",
                    20,
                    20,
                    240,
                    120,
                    1,
                    "{}",
                    new[] { new HmiProjectPackageBinding("Text", "OpcTag", "ns=2;s=Plant.Tank", false, "Viewer") })
            }));

        var archiveBytes = HmiProjectPackageSerializer.CreateArchive(package, new Dictionary<string, byte[]>
        {
            ["logo.png"] = new byte[] { 1, 2, 3, 4 }
        });

        var restored = HmiProjectPackageSerializer.ReadArchive(archiveBytes);
        var assets = HmiProjectPackageSerializer.ReadAssets(archiveBytes);

        Assert.NotNull(restored);
        Assert.Equal("Archive Screen", restored!.Screen.Name);
        Assert.True(assets.ContainsKey("logo.png"));
        Assert.Equal(4, assets["logo.png"].Length);
    }
}
