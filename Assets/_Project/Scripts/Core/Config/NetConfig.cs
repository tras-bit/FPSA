using System;
using System.Collections.Generic;
using System.Globalization;
using Kordon.Core.Serialization;

namespace Kordon.Core.Config
{
    /// <summary>One deployable server region.</summary>
    [Serializable]
    public sealed class ServerRegion
    {
        public string Id = "eu-central";
        public string DisplayName = "EU Central (Frankfurt)";
        public string CountryCode = "DE";

        /// <summary>
        /// Host name or address of the dedicated server in this region.  Empty means the
        /// region is not deployed yet: the client then refuses to connect and tells the
        /// player to enter an address manually or use LAN discovery.  No address is ever
        /// hardcoded in gameplay code.
        /// </summary>
        public string Address = string.Empty;

        public int Port = 27015;

        /// <summary>Optional query/status endpoint used by the server browser.</summary>
        public string StatusUrl = string.Empty;

        public bool IsConfigured => !string.IsNullOrEmpty(Address);

        public ServerRegion Clone() => (ServerRegion)MemberwiseClone();

        public override string ToString() => $"{DisplayName} [{Id}] {Address}:{Port}";
    }

    /// <summary>What a client wants to join.</summary>
    [Serializable]
    public sealed class ClientConnectionRequest
    {
        public string Address = string.Empty;
        public int Port = 27015;
        public string PlayerName = string.Empty;
        public string RegionId = string.Empty;

        /// <summary>Set when the player discovered the server on the local network.</summary>
        public bool FromLanDiscovery;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "{0}:{1} as '{2}' ({3})", Address, Port, PlayerName,
            string.IsNullOrEmpty(RegionId) ? "manual" : RegionId);
    }

    /// <summary>Result of validating a connection request.</summary>
    public struct ConnectionValidation
    {
        public bool Allowed;
        public string Reason;

        public static ConnectionValidation Ok() => new ConnectionValidation { Allowed = true };

        public static ConnectionValidation Deny(string reason) =>
            new ConnectionValidation { Allowed = false, Reason = reason };
    }

    /// <summary>
    /// Network configuration, loaded from Assets/StreamingAssets/net_config.json at
    /// runtime.  Region addresses, ports, tick rate and limits all live in that file,
    /// never in code: the shipped file contains the EU region entries with the address
    /// left blank for the server operator to fill in (see docs/04_NETWORKING.md).
    /// </summary>
    [Serializable]
    public sealed class NetConfig
    {
        public int ServerPort = 27015;
        public int QueryPort = 27016;
        public int MaxPlayers = 20;
        public int ServerTickRate = 30;
        public float ClientTimeoutSeconds = 20f;
        public float ReconnectGraceSeconds = 90f;
        public int MaxReconnectAttempts = 5;
        public float ReconnectRetryDelaySeconds = 2.5f;
        public string DefaultRegionId = "eu-central";
        public int LanDiscoveryPort = 47780;
        public bool LanDiscoveryEnabled = true;

        /// <summary>
        /// Development escape hatch.  When false (the shipped default) a client may not
        /// connect to a loopback address at all, which keeps "localhost" out of the
        /// player facing network path entirely.
        /// </summary>
        public bool AllowLoopbackForDevelopment = false;

        public string ServerPasswordHash = string.Empty;

        public List<ServerRegion> Regions = new List<ServerRegion>();

        public ServerRegion DefaultRegion => GetRegion(DefaultRegionId) ?? (Regions.Count > 0 ? Regions[0] : null);

        public ServerRegion GetRegion(string id)
        {
            foreach (ServerRegion region in Regions)
            {
                if (region.Id == id)
                {
                    return region;
                }
            }

            return null;
        }

        public static bool IsLoopback(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return false;
            }

            string normalized = address.Trim().ToLowerInvariant();
            if (normalized.StartsWith("[") && normalized.EndsWith("]"))
            {
                normalized = normalized.Substring(1, normalized.Length - 2);
            }

            return normalized == "localhost" || normalized == "127.0.0.1" || normalized == "::1" ||
                   normalized.StartsWith("127.") || normalized == "[::1]";
        }

        public static bool IsValidAddress(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return false;
            }

            string trimmed = address.Trim();
            if (trimmed.Length < 3 || trimmed.Length > 253)
            {
                return false;
            }

            foreach (char c in trimmed)
            {
                bool ok = char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == ':' || c == '_';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsValidPort(int port) => port > 0 && port <= 65535;

        /// <summary>Validate a client connection request against this configuration.</summary>
        public ConnectionValidation ValidateClientRequest(ClientConnectionRequest request)
        {
            if (request == null)
            {
                return ConnectionValidation.Deny("empty-request");
            }

            if (string.IsNullOrEmpty(request.PlayerName))
            {
                return ConnectionValidation.Deny("empty-player-name");
            }

            // A configured region may supply the address.
            if (string.IsNullOrEmpty(request.Address) && !string.IsNullOrEmpty(request.RegionId))
            {
                ServerRegion region = GetRegion(request.RegionId);
                if (region == null)
                {
                    return ConnectionValidation.Deny($"unknown-region({request.RegionId})");
                }

                if (!region.IsConfigured)
                {
                    return ConnectionValidation.Deny(
                        $"region-not-deployed({region.Id}): set its address in StreamingAssets/net_config.json " +
                        "or enter a server address manually");
                }

                request.Address = region.Address;
                // The region owns the endpoint: when it supplied the address it
                // supplies the port too, so a stale default cannot send the player
                // to the wrong port on that host.
                request.Port = region.Port;
            }

            if (string.IsNullOrEmpty(request.Address))
            {
                return ConnectionValidation.Deny("no-address: pick a configured region, enter an address, " +
                                                 "or use LAN discovery");
            }

            if (IsLoopback(request.Address) && !AllowLoopbackForDevelopment)
            {
                return ConnectionValidation.Deny(
                    "loopback-address-refused: the release client never connects to localhost " +
                    "(set allowLoopbackForDevelopment=true in net_config.json for a local test server)");
            }

            if (!IsValidAddress(request.Address))
            {
                return ConnectionValidation.Deny($"invalid-address({request.Address})");
            }

            if (!IsValidPort(request.Port))
            {
                return ConnectionValidation.Deny($"invalid-port({request.Port})");
            }

            if (IsLoopback(request.Address) && !AllowLoopbackForDevelopment)
            {
                return ConnectionValidation.Deny(
                    "loopback-address-refused: the release client never connects to localhost " +
                    "(set allowLoopbackForDevelopment=true in net_config.json for a local test server)");
            }

            return ConnectionValidation.Ok();
        }

        /// <summary>Validate the server side configuration before opening sockets.</summary>
        public string ValidateServer()
        {
            if (!IsValidPort(ServerPort))
            {
                return "invalid-server-port";
            }

            if (MaxPlayers != 20)
            {
                return "ffa-server-must-advertise-20-slots";
            }

            if (ServerTickRate < 10 || ServerTickRate > 128)
            {
                return "server-tick-rate-out-of-range";
            }

            if (ClientTimeoutSeconds <= 0f)
            {
                return "invalid-client-timeout";
            }

            foreach (ServerRegion region in Regions)
            {
                if (string.IsNullOrEmpty(region.Id))
                {
                    return "region-without-id";
                }

                if (region.Port != 0 && !IsValidPort(region.Port))
                {
                    return $"invalid-port-for-region({region.Id})";
                }
            }

            return null;
        }

        public static NetConfig FromJson(string json)
        {
            Json root = Json.Parse(json);
            NetConfig config = new NetConfig
            {
                ServerPort = root["serverPort"].AsInt(27015),
                QueryPort = root["queryPort"].AsInt(27016),
                MaxPlayers = root["maxPlayers"].AsInt(20),
                ServerTickRate = root["serverTickRate"].AsInt(30),
                ClientTimeoutSeconds = root["clientTimeoutSeconds"].AsFloat(20f),
                ReconnectGraceSeconds = root["reconnectGraceSeconds"].AsFloat(90f),
                MaxReconnectAttempts = root["maxReconnectAttempts"].AsInt(5),
                ReconnectRetryDelaySeconds = root["reconnectRetryDelaySeconds"].AsFloat(2.5f),
                DefaultRegionId = root["defaultRegionId"].AsString("eu-central"),
                LanDiscoveryPort = root["lanDiscoveryPort"].AsInt(47780),
                LanDiscoveryEnabled = root["lanDiscoveryEnabled"].AsBool(true),
                AllowLoopbackForDevelopment = root["allowLoopbackForDevelopment"].AsBool(false),
                ServerPasswordHash = root["serverPasswordHash"].AsString(string.Empty)
            };

            foreach (Json item in root["regions"].Items())
            {
                config.Regions.Add(new ServerRegion
                {
                    Id = item["id"].AsString(),
                    DisplayName = item["displayName"].AsString(item["id"].AsString()),
                    CountryCode = item["countryCode"].AsString(""),
                    Address = item["address"].AsString(string.Empty),
                    Port = item["port"].AsInt(config.ServerPort),
                    StatusUrl = item["statusUrl"].AsString(string.Empty)
                });
            }

            return config;
        }

        public string ToJson()
        {
            Dictionary<string, object> root = JsonObject.New()
                .With("serverPort", ServerPort)
                .With("queryPort", QueryPort)
                .With("maxPlayers", MaxPlayers)
                .With("serverTickRate", ServerTickRate)
                .With("clientTimeoutSeconds", ClientTimeoutSeconds)
                .With("reconnectGraceSeconds", ReconnectGraceSeconds)
                .With("maxReconnectAttempts", MaxReconnectAttempts)
                .With("reconnectRetryDelaySeconds", ReconnectRetryDelaySeconds)
                .With("defaultRegionId", DefaultRegionId)
                .With("lanDiscoveryPort", LanDiscoveryPort)
                .With("lanDiscoveryEnabled", LanDiscoveryEnabled)
                .With("allowLoopbackForDevelopment", AllowLoopbackForDevelopment)
                .With("serverPasswordHash", ServerPasswordHash);

            List<object> regions = new List<object>();
            foreach (ServerRegion region in Regions)
            {
                regions.Add(JsonObject.New()
                    .With("id", region.Id)
                    .With("displayName", region.DisplayName)
                    .With("countryCode", region.CountryCode)
                    .With("address", region.Address)
                    .With("port", region.Port)
                    .With("statusUrl", region.StatusUrl));
            }

            root["regions"] = regions;
            return MiniJson.Write(root, true);
        }

        /// <summary>Configuration shipped with the game.</summary>
        public static NetConfig Default()
        {
            NetConfig config = new NetConfig();
            config.Regions.Add(new ServerRegion
            {
                Id = "eu-central",
                DisplayName = "EU Central",
                CountryCode = "DE",
                Address = string.Empty,
                Port = 27015
            });
            config.Regions.Add(new ServerRegion
            {
                Id = "eu-west",
                DisplayName = "EU West",
                CountryCode = "NL",
                Address = string.Empty,
                Port = 27015
            });
            config.Regions.Add(new ServerRegion
            {
                Id = "eu-north",
                DisplayName = "EU North",
                CountryCode = "SE",
                Address = string.Empty,
                Port = 27015
            });
            return config;
        }
    }
}
