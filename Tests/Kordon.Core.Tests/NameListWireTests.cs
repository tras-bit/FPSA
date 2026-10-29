using Kordon.Core.Serialization;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The scoreboard names travel as one length prefixed string because Netcode for
    /// GameObjects refuses a <c>string[]</c> RPC parameter and fails the build with
    /// "Don't know how to serialize System.String[]". Anything a player types has to
    /// survive the round trip, and a corrupt packet must never throw on a client.
    /// </summary>
    public sealed class NameListWireTests
    {
        [Fact]
        public void NamesRoundTripInOrder()
        {
            string payload = NameListWire.Encode(new[] { "Sergei", "Kovalenko", "R-3" });

            Assert.Equal(new[] { "Sergei", "Kovalenko", "R-3" }, NameListWire.Decode(payload, 3));
        }

        [Fact]
        public void SeparatorsInsideNamesSurvive()
        {
            string[] original = { "12:34", "a b", "x:y:z", "quote\"and'apostrophe" };
            string payload = NameListWire.Encode(original);

            Assert.Equal(original, NameListWire.Decode(payload, original.Length));
        }

        [Fact]
        public void EmptyAndNullNamesEncodeAsEmptyEntries()
        {
            string payload = NameListWire.Encode(new[] { "", null, "ok" });

            Assert.Equal(new[] { "", "", "ok" }, NameListWire.Decode(payload, 3));
        }

        [Fact]
        public void EncodingNothingGivesAnEmptyPayload()
        {
            Assert.Equal(string.Empty, NameListWire.Encode(new string[0]));
            Assert.Equal(string.Empty, NameListWire.Encode(null));
            Assert.Empty(NameListWire.Decode(string.Empty, 0));
        }

        [Fact]
        public void DecodingPadsToTheRequestedRowCount()
        {
            string payload = NameListWire.Encode(new[] { "first" });

            Assert.Equal(new[] { "first", "", "" }, NameListWire.Decode(payload, 3));
            Assert.Equal(new[] { "first" }, NameListWire.Decode(payload, 1));
        }

        [Fact]
        public void TruncatedPayloadKeepsWhatItCouldRead()
        {
            string payload = NameListWire.Encode(new[] { "alpha", "bravo" });

            string[] decoded = NameListWire.Decode(payload.Substring(0, payload.Length - 2), 2);

            Assert.Equal("alpha", decoded[0]);
            Assert.Equal(string.Empty, decoded[1]);
        }

        [Fact]
        public void GarbagePayloadDoesNotThrow()
        {
            Assert.Equal(new[] { "", "" }, NameListWire.Decode("not a frame", 2));
            Assert.Equal(new[] { "", "" }, NameListWire.Decode("-4:xxxx", 2));
            Assert.Equal(new[] { "", "" }, NameListWire.Decode("999:nothing-here", 2));
        }

        [Fact]
        public void NamesAreClippedAndCleaned()
        {
            Assert.Equal("Sergei Kovalenko", NameListWire.Clip("  Sergei   Kovalenko  "));
            Assert.Equal("no newline", NameListWire.Clip("no\nnewline"));
            Assert.Equal(string.Empty, NameListWire.Clip(null));

            string longName = new string('x', 200);
            Assert.Equal(NameListWire.MaxNameLength, NameListWire.Clip(longName).Length);
        }

        [Fact]
        public void EncodingCapsTheRowCount()
        {
            string[] many = new string[NameListWire.MaxNames + 10];
            for (int i = 0; i < many.Length; i++)
            {
                many[i] = "p" + i;
            }

            string[] decoded = NameListWire.Decode(NameListWire.Encode(many), many.Length);

            Assert.Equal("p0", decoded[0]);
            Assert.Equal(string.Empty, decoded[NameListWire.MaxNames]);
        }
    }
}
