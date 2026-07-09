using System;
using System.IO;
using Microsoft.Extensions.Options;
using VNO.Core.Models;
using VNO.Server.Services;
using Xunit;

namespace VNO.Server.Tests;

/// <summary>
/// Proves moderation bans survive a server process restart
/// </summary>
public sealed class BanRegistryPersistenceTests
{
    [Fact]
    public void Added_and_removed_bans_are_persisted_atomically()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vno-bans-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = Options.Create(new ServerSettings { DataDirectory = directory });
            var first = new BanRegistry(options);
            first.Add(new BanEntry
            {
                Kind = BanKind.IpAddress,
                Target = "203.0.113.8",
                Reason = "test",
                PlacedBy = "operator",
            });

            var reloaded = new BanRegistry(options);
            Assert.True(reloaded.IsAddressBanned("203.0.113.8"));
            Assert.True(reloaded.Remove(BanKind.IpAddress, "203.0.113.8"));

            Assert.False(new BanRegistry(options).IsAddressBanned("203.0.113.8"));
            Assert.False(File.Exists(Path.Combine(directory, "bans.json.tmp")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
