using System;
using System.Globalization;

namespace Kordon.Core.Config
{
    /// <summary>
    /// Command line of the dedicated server build:
    ///
    ///   Kordon.exe -kordon-server -serverAddress 0.0.0.0 -serverPort 27015 -map ffa_zavod7
    ///
    /// Parsing is engine free (and unit tested) because the same flags are printed by
    /// the editor menu and documented in the README; a typo in a flag must be reported,
    /// not silently ignored into a default server that nobody can join.
    /// </summary>
    public sealed class ServerLaunchOptions
    {
        public const string ServerFlag = "-kordon-server";
        public const string AddressFlag = "-serveraddress";
        public const string PortFlag = "-serverport";
        public const string MapFlag = "-map";
        public const string MaxPlayersFlag = "-maxplayers";

        /// <summary>True when this process was started as a dedicated server.</summary>
        public bool Enabled;

        /// <summary>Interface to bind. The default binds every interface, never loopback.</summary>
        public string BindAddress = "0.0.0.0";

        /// <summary>0 means "use the port from net_config.json".</summary>
        public int Port;

        public string MapId = "ffa_zavod7";

        /// <summary>0 means "use MatchRulesConfig.MaxPlayers" (20).</summary>
        public int MaxPlayers;

        /// <summary>Anything that was not understood, in order, for the server log.</summary>
        public string[] UnknownArguments = Array.Empty<string>();

        public bool HasUnknownArguments => UnknownArguments.Length > 0;

        public static ServerLaunchOptions Parse(string[] arguments)
        {
            ServerLaunchOptions options = new ServerLaunchOptions();
            if (arguments == null)
            {
                return options;
            }

            int unknown = 0;
            string[] leftovers = new string[arguments.Length];
            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i] ?? string.Empty;
                switch (argument.ToLowerInvariant())
                {
                    case ServerFlag:
                        options.Enabled = true;
                        break;
                    case AddressFlag:
                        if (i + 1 < arguments.Length)
                        {
                            options.BindAddress = arguments[++i];
                        }

                        break;
                    case PortFlag:
                        if (i + 1 < arguments.Length &&
                            int.TryParse(arguments[++i], NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int port) &&
                            port > 0 && port <= 65535)
                        {
                            options.Port = port;
                        }

                        break;
                    case MapFlag:
                        if (i + 1 < arguments.Length)
                        {
                            options.MapId = arguments[++i];
                        }

                        break;
                    case MaxPlayersFlag:
                        if (i + 1 < arguments.Length &&
                            int.TryParse(arguments[++i], NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int players) &&
                            players > 0)
                        {
                            options.MaxPlayers = players;
                        }

                        break;
                }

                if (argument.StartsWith("-", StringComparison.Ordinal) &&
                    !IsKnownFlag(argument))
                {
                    leftovers[unknown++] = argument;
                }
            }

            if (unknown > 0)
            {
                string[] trimmed = new string[unknown];
                Array.Copy(leftovers, trimmed, unknown);
                options.UnknownArguments = trimmed;
            }

            return options;
        }

        private static bool IsKnownFlag(string argument)
        {
            switch (argument.ToLowerInvariant())
            {
                case ServerFlag:
                case AddressFlag:
                case PortFlag:
                case MapFlag:
                case MaxPlayersFlag:
                    return true;
                default:
                    return false;
            }
        }
    }
}
