using System.Collections.Generic;
using Kordon.Core.Config;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// Networking is configuration driven: EU first regions, 20 slots, no loopback
    /// addresses baked into the client, and honest refusal when no server is deployed.
    /// </summary>
    public class NetConfigTests
    {
        private static NetConfig Shipped => TestData.Database.Net;

        [Fact]
        public void ShippedConfig_IsEuropeFirstWithTwentySlots()
        {
            Assert.Equal(20, Shipped.MaxPlayers);
            Assert.Equal(27015, Shipped.ServerPort);
            Assert.Equal(30, Shipped.ServerTickRate);
            Assert.Equal("eu-central", Shipped.DefaultRegionId);
            Assert.True(Shipped.Regions.Count >= 3);
            Assert.Null(Shipped.ValidateServer());
            Assert.False(Shipped.AllowLoopbackForDevelopment);
            Assert.True(Shipped.ReconnectGraceSeconds >= 90f);
            Assert.True(Shipped.MaxReconnectAttempts >= 1);

            foreach (ServerRegion region in Shipped.Regions)
            {
                Assert.StartsWith("eu-", region.Id);
                Assert.False(NetConfig.IsLoopback(region.Address));
                Assert.True(NetConfig.IsValidPort(region.Port));
            }
        }

        [Fact]
        public void DefaultConfig_MatchesTheShippedFile()
        {
            NetConfig defaults = NetConfig.Default();
            Assert.Equal(Shipped.MaxPlayers, defaults.MaxPlayers);
            Assert.Equal(Shipped.ServerPort, defaults.ServerPort);
            Assert.Equal(Shipped.DefaultRegionId, defaults.DefaultRegionId);
            Assert.Equal(Shipped.Regions.Count, defaults.Regions.Count);
            Assert.Null(defaults.ValidateServer());
            Assert.NotNull(defaults.DefaultRegion);
            Assert.Equal("eu-central", defaults.DefaultRegion.Id);
        }

        [Fact]
        public void UndeployedRegion_IsRefusedWithAnExplanation_NotSilently()
        {
            // No hosted server ships with the project: deploying one costs money and needs
            // an account with an infrastructure provider, so the region address is empty
            // and the client tells the player exactly what to do.
            ServerRegion region = Shipped.GetRegion("eu-central");
            Assert.False(region.IsConfigured);

            ConnectionValidation verdict = Shipped.ValidateClientRequest(new ClientConnectionRequest
            {
                PlayerName = "OPERATOR",
                RegionId = "eu-central"
            });

            Assert.False(verdict.Allowed);
            Assert.Contains("region-not-deployed", verdict.Reason);
            Assert.Contains("net_config.json", verdict.Reason);
        }

        [Fact]
        public void ClientRequests_AreValidatedBeforeAnySocketOpens()
        {
            NetConfig config = NetConfig.Default();

            Assert.Equal("empty-request", config.ValidateClientRequest(null).Reason);
            Assert.Equal("empty-player-name",
                config.ValidateClientRequest(new ClientConnectionRequest { Address = "play.example.com" }).Reason);
            Assert.Equal("unknown-region(mars-1)",
                config.ValidateClientRequest(new ClientConnectionRequest
                {
                    PlayerName = "OP",
                    RegionId = "mars-1"
                }).Reason);

            ConnectionValidation noAddress = config.ValidateClientRequest(new ClientConnectionRequest
            {
                PlayerName = "OP"
            });
            Assert.False(noAddress.Allowed);
            Assert.Contains("no-address", noAddress.Reason);
            Assert.Contains("LAN discovery", noAddress.Reason);

            Assert.Contains("invalid-address", config.ValidateClientRequest(new ClientConnectionRequest
            {
                PlayerName = "OP",
                Address = "not a host"
            }).Reason);

            Assert.Contains("invalid-port", config.ValidateClientRequest(new ClientConnectionRequest
            {
                PlayerName = "OP",
                Address = "play.example.com",
                Port = 0
            }).Reason);

            Assert.True(config.ValidateClientRequest(new ClientConnectionRequest
            {
                PlayerName = "OP",
                Address = "play.example.com",
                Port = 27015
            }).Allowed);
        }

        [Fact]
        public void LoopbackAddresses_AreRefusedByTheReleaseClient()
        {
            NetConfig config = NetConfig.Default();
            string[] loopback = { "localhost", "127.0.0.1", "127.9.9.9", "::1", "[::1]" };
            foreach (string address in loopback)
            {
                Assert.True(NetConfig.IsLoopback(address), $"{address} should be loopback");
                ConnectionValidation verdict = config.ValidateClientRequest(new ClientConnectionRequest
                {
                    PlayerName = "OP",
                    Address = address,
                    Port = 27015
                });
                Assert.False(verdict.Allowed);
                Assert.Contains("loopback-address-refused", verdict.Reason);
            }

            // Documented development escape hatch, off by default.
            config.AllowLoopbackForDevelopment = true;
            Assert.True(config.ValidateClientRequest(new ClientConnectionRequest
            {
                PlayerName = "OP",
                Address = "127.0.0.1",
                Port = 27015
            }).Allowed);

            Assert.False(NetConfig.IsLoopback(string.Empty));
            Assert.False(NetConfig.IsLoopback("play.kordon.example"));
            Assert.False(NetConfig.IsLoopback("10.0.0.5"));
        }

        [Fact]
        public void AConfiguredRegion_SuppliesAddressAndPort()
        {
            NetConfig config = NetConfig.Default();
            config.GetRegion("eu-central").Address = "frankfurt.kordon.example";
            config.GetRegion("eu-central").Port = 27020;

            ClientConnectionRequest request = new ClientConnectionRequest
            {
                PlayerName = "OPERATOR",
                RegionId = "eu-central"
            };

            Assert.True(config.ValidateClientRequest(request).Allowed);
            Assert.Equal("frankfurt.kordon.example", request.Address);
            Assert.Equal(27020, request.Port);
        }

        [Fact]
        public void AddressAndPortValidation_RejectsGarbage()
        {
            Assert.False(NetConfig.IsValidAddress(string.Empty));
            Assert.False(NetConfig.IsValidAddress("ab"));
            Assert.False(NetConfig.IsValidAddress("has space"));
            Assert.False(NetConfig.IsValidAddress("host/name"));
            Assert.False(NetConfig.IsValidAddress(new string('a', 254)));
            Assert.True(NetConfig.IsValidAddress("play.example.com"));
            Assert.True(NetConfig.IsValidAddress("10.0.0.5"));
            Assert.True(NetConfig.IsValidAddress("kordon-server_1.eu"));

            Assert.False(NetConfig.IsValidPort(0));
            Assert.False(NetConfig.IsValidPort(-1));
            Assert.False(NetConfig.IsValidPort(65536));
            Assert.True(NetConfig.IsValidPort(1));
            Assert.True(NetConfig.IsValidPort(65535));
        }

        [Fact]
        public void ServerConfiguration_IsCheckedBeforeOpeningSockets()
        {
            NetConfig config = NetConfig.Default();
            Assert.Null(config.ValidateServer());

            config.MaxPlayers = 19;
            Assert.Equal("ffa-server-must-advertise-20-slots", config.ValidateServer());
            config.MaxPlayers = 20;

            config.ServerTickRate = 5;
            Assert.Equal("server-tick-rate-out-of-range", config.ValidateServer());
            config.ServerTickRate = 200;
            Assert.Equal("server-tick-rate-out-of-range", config.ValidateServer());
            config.ServerTickRate = 30;

            config.ServerPort = 0;
            Assert.Equal("invalid-server-port", config.ValidateServer());
            config.ServerPort = 27015;

            config.ClientTimeoutSeconds = 0f;
            Assert.Equal("invalid-client-timeout", config.ValidateServer());
            config.ClientTimeoutSeconds = 20f;

            config.Regions.Add(new ServerRegion { Id = string.Empty, Address = "x.example" });
            Assert.Equal("region-without-id", config.ValidateServer());
            config.Regions.RemoveAt(config.Regions.Count - 1);

            config.Regions.Add(new ServerRegion { Id = "eu-test", Port = 99999 });
            Assert.Equal("invalid-port-for-region(eu-test)", config.ValidateServer());
        }

        [Fact]
        public void Configuration_RoundTripsThroughJson()
        {
            NetConfig config = NetConfig.Default();
            config.ServerPort = 27500;
            config.QueryPort = 27501;
            config.ServerTickRate = 64;
            config.ReconnectGraceSeconds = 120f;
            config.LanDiscoveryEnabled = false;
            config.ServerPasswordHash = "not-a-secret-hash";
            config.GetRegion("eu-west").Address = "ams.kordon.example";

            string json = config.ToJson();
            Assert.Contains("eu-west", json);
            Assert.DoesNotContain("localhost", json);

            NetConfig restored = NetConfig.FromJson(json);
            Assert.Equal(config.ServerPort, restored.ServerPort);
            Assert.Equal(config.QueryPort, restored.QueryPort);
            Assert.Equal(config.MaxPlayers, restored.MaxPlayers);
            Assert.Equal(config.ServerTickRate, restored.ServerTickRate);
            Assert.Equal(config.ClientTimeoutSeconds, restored.ClientTimeoutSeconds, 3);
            Assert.Equal(config.ReconnectGraceSeconds, restored.ReconnectGraceSeconds, 3);
            Assert.Equal(config.MaxReconnectAttempts, restored.MaxReconnectAttempts);
            Assert.Equal(config.DefaultRegionId, restored.DefaultRegionId);
            Assert.Equal(config.LanDiscoveryPort, restored.LanDiscoveryPort);
            Assert.Equal(config.LanDiscoveryEnabled, restored.LanDiscoveryEnabled);
            Assert.Equal(config.AllowLoopbackForDevelopment, restored.AllowLoopbackForDevelopment);
            Assert.Equal(config.ServerPasswordHash, restored.ServerPasswordHash);
            Assert.Equal(config.Regions.Count, restored.Regions.Count);
            Assert.Equal("ams.kordon.example", restored.GetRegion("eu-west").Address);
            Assert.True(restored.GetRegion("eu-west").IsConfigured);
            Assert.Null(restored.ValidateServer());
        }

        [Fact]
        public void ShippedConfig_DoesNotHardcodeAnyServerAddress()
        {
            // Every address comes from configuration; the game ships without one because
            // hosting costs money and requires an account with a provider.
            foreach (ServerRegion region in Shipped.Regions)
            {
                Assert.True(string.IsNullOrEmpty(region.Address) ||
                            NetConfig.IsValidAddress(region.Address));
            }

            Assert.True(Shipped.LanDiscoveryEnabled);
            Assert.True(Shipped.LanDiscoveryPort > 0);
            List<string> problems = TestData.Database.Validate();
            Assert.DoesNotContain(problems, p => p.Contains("net-config"));
        }
    }
}
