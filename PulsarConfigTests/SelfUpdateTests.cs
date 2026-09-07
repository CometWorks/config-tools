#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CometWorks.ConfigTools;
using Xunit;

namespace PulsarConfigTests;

public sealed class SelfUpdateTests
{
    [Fact]
    public void SelectsHighestStableReleaseForOnlyThisToolAndPlatform()
    {
        object Release(string tag, string asset, bool prerelease = false) =>
            new
            {
                tag_name = tag,
                draft = false,
                prerelease,
                assets = new[]
                {
                    new
                    {
                        name = asset,
                        digest = "sha256:" + new string('a', 64),
                        browser_download_url = "https://github.com/CometWorks/config-tools/releases/download/"
                            + tag
                            + "/"
                            + asset,
                    },
                },
            };
        using var releases = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new[]
                {
                    Release("magnetarconfig-v99.0.0", "MagnetarConfig-linux-x64.bin"),
                    Release("pulsarconfig-v1.2.0", "PulsarConfig-linux-x64.bin"),
                    Release("pulsarconfig-v3.0.0", "PulsarConfig-linux-x64.bin", true),
                    Release("pulsarconfig-v2.0.0", "PulsarConfig-linux-x64.bin"),
                    Release("pulsarconfig-v1.9.0", "PulsarConfig-linux-x64.bin"),
                    Release("pulsarconfig-v4.0.0", "PulsarConfig-win-x64.exe"),
                }
            )
        );
        var release = SelfUpdate.Select(
            releases.RootElement,
            "PulsarConfig",
            "PulsarConfig-linux-x64.bin"
        );
        Assert.Equal("pulsarconfig-v2.0.0", release!.Tag);
        Assert.Null(
            SelfUpdate.Select(releases.RootElement, "MagnetarConfig", "MagnetarConfig-win-x64.exe")
        );
    }

    [Fact]
    public void ReplacementVerifiesBeforeWritingAndKeepsRecoverableBackup()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "tool-update-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            string staged = Path.Combine(root, "new"),
                target = Path.Combine(root, "tool");
            File.WriteAllText(staged, "new executable");
            File.WriteAllText(target, "old executable");
            Assert.Throws<IOException>(() =>
                SelfUpdate.Replace(staged, target, new string('0', 64))
            );
            Assert.Equal("old executable", File.ReadAllText(target));
            Assert.False(File.Exists(target + ".previous"));
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(staged)));
            SelfUpdate.Replace(staged, target, hash);
            Assert.Equal("new executable", File.ReadAllText(target));
            Assert.Equal("old executable", File.ReadAllText(target + ".previous"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(null, false, true, true)]
    [InlineData(null, true, false, true)]
    [InlineData("wayland", true, false, false)]
    [InlineData("wayland,x11", true, false, true)]
    [InlineData("x11", false, true, false)]
    [InlineData("offscreen", false, false, true)]
    public void PrerequisitesRespectEitherDisplayBackend(
        string? driver,
        bool x11,
        bool wayland,
        bool expected
    ) => Assert.Equal(expected, Prerequisites.DisplayAvailable(driver, x11, wayland));

    [Theory]
    [InlineData(
        "Microsoft.NETCore.App 10.0.1 [/usr/share/dotnet/shared/Microsoft.NETCore.App]",
        true
    )]
    [InlineData(
        "Microsoft.AspNetCore.App 10.0.1 [/runtime]\nMicrosoft.NETCore.App 9.0.2 [/runtime]",
        false
    )]
    [InlineData("Microsoft.NETCore.App 10.0.0-preview.1 [/runtime]", false)]
    public void PrerequisitesRequireStableNet10Runtime(string output, bool expected) =>
        Assert.Equal(expected, Prerequisites.HasNet10(output));
}
