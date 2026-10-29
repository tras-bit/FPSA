using Kordon.Core.Config;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The dedicated server command line.  A server that silently falls back to a
    /// default port because a flag was mistyped is a server nobody can join, so the
    /// parser is explicit about what it understood.
    /// </summary>
    public class ServerLaunchTests
    {
        [Fact]
        public void NoFlags_MeansNotAServer()
        {
            ServerLaunchOptions options = ServerLaunchOptions.Parse(new[] { "Kordon.exe" });
            Assert.False(options.Enabled);
            Assert.Equal("0.0.0.0", options.BindAddress);
            Assert.Equal(0, options.Port);
            Assert.Equal("ffa_zavod7", options.MapId);
            Assert.False(options.HasUnknownArguments);
        }

        [Fact]
        public void ServerFlagAndArguments_AreParsed()
        {
            ServerLaunchOptions options = ServerLaunchOptions.Parse(new[]
            {
                "Kordon.exe", "-kordon-server", "-serverAddress", "10.0.0.7",
                "-serverPort", "27015", "-map", "ffa_zavod7", "-maxplayers", "20",
            });

            Assert.True(options.Enabled);
            Assert.Equal("10.0.0.7", options.BindAddress);
            Assert.Equal(27015, options.Port);
            Assert.Equal("ffa_zavod7", options.MapId);
            Assert.Equal(20, options.MaxPlayers);
            Assert.False(options.HasUnknownArguments);
        }

        [Fact]
        public void FlagsAreCaseInsensitive_BecauseOperatorsTypeThemByHand()
        {
            ServerLaunchOptions options = ServerLaunchOptions.Parse(new[]
            {
                "-KORDON-SERVER", "-ServerPort", "27016",
            });

            Assert.True(options.Enabled);
            Assert.Equal(27016, options.Port);
        }

        [Fact]
        public void InvalidPortValues_AreIgnoredInsteadOfStartingSomethingElse()
        {
            Assert.Equal(0, ServerLaunchOptions.Parse(new[] { "-kordon-server", "-serverPort", "70000" }).Port);
            Assert.Equal(0, ServerLaunchOptions.Parse(new[] { "-kordon-server", "-serverPort", "abc" }).Port);
            Assert.Equal(0, ServerLaunchOptions.Parse(new[] { "-kordon-server", "-serverPort" }).Port);
            Assert.Equal(0, ServerLaunchOptions.Parse(new[] { "-kordon-server", "-serverPort", "-3" }).Port);
        }

        [Fact]
        public void UnknownArguments_AreReportedNotSwallowed()
        {
            ServerLaunchOptions options = ServerLaunchOptions.Parse(new[]
            {
                "-kordon-server", "-port", "27015", "-verbose",
            });

            Assert.True(options.Enabled);
            Assert.Equal(0, options.Port);
            Assert.True(options.HasUnknownArguments);
            Assert.Contains("-port", options.UnknownArguments);
            Assert.Contains("-verbose", options.UnknownArguments);
        }

        [Fact]
        public void NullArguments_DoNotThrow()
        {
            ServerLaunchOptions options = ServerLaunchOptions.Parse(null);
            Assert.False(options.Enabled);
            Assert.Equal(0, options.UnknownArguments.Length);
        }
    }
}
