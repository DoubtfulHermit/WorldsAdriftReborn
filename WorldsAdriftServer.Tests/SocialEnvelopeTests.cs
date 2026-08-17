using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Social;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The envelope rules, which are the ones that decide whether the client sees
    /// data or a modal about the network.
    /// </summary>
    public class SocialEnvelopeTests
    {
        [Fact]
        public void SuccessCarriesItsPayloadAtData()
        {
            JObject envelope = SocialEnvelope.Ok(new JObject { ["uid"] = "x" });

            Assert.True(envelope.Value<bool>("success"));
            Assert.Equal("x", envelope["data"]!.Value<string>("uid"));
        }

        /// <summary>
        /// Most list endpoints nest under data.items - the client iterates
        /// model.data["items"].
        /// </summary>
        [Fact]
        public void ListsNestUnderItems()
        {
            JObject envelope = SocialEnvelope.OkItems(new JArray("a", "b"));

            Assert.NotNull(envelope["data"]!["items"]);
            Assert.Equal(2, ((JArray)envelope["data"]!["items"]!).Count);
        }

        /// <summary>
        /// GET memberships/crew/{crewUid} is the exception: its client code
        /// iterates model.data DIRECTLY (CrewServerImpl.cs:60) while its immediate
        /// neighbour uses items (:76). Wrapping this one would make every crew
        /// render as empty, which is precisely the symptom this whole feature
        /// exists to remove - so it is worth a test of its own.
        /// </summary>
        [Fact]
        public void TheCrewMemberListIsABareArrayAtData()
        {
            JObject envelope = SocialEnvelope.OkBareList(new JArray("a", "b"));

            // An array, not an object with an items key. Asserting the TYPE is
            // the assertion: indexing a JArray by name throws rather than
            // returning null, so "no items key" cannot be checked directly.
            Assert.IsType<JArray>(envelope["data"]);
            Assert.Equal(2, ((JArray)envelope["data"]!).Count);
        }

        /// <summary>
        /// The body is always an object even when there is no payload. The client
        /// parses it and then assigns jToken["statusCode"], which throws on an
        /// array root and on an empty body.
        /// </summary>
        [Fact]
        public void EveryResponseHasAnObjectRoot()
        {
            Assert.IsType<JObject>(SocialEnvelope.OkNoData());
            Assert.IsType<JObject>(SocialEnvelope.Ok(null!));
            Assert.IsType<JObject>(SocialEnvelope.OkBareList(new JArray()));
            Assert.IsType<JObject>(SocialEnvelope.Error(SocialErrorCodes.AuthFailed));
        }

        /// <summary>
        /// statusCode and originalResponseData exist on the client's ResponseSchema
        /// but are written by the CLIENT after parsing (HttpHelper.cs:86,
        /// SocialRequest.cs:98). Emitting them would be inventing wire fields.
        /// </summary>
        [Fact]
        public void DoesNotEmitTheFieldsTheClientFillsInItself()
        {
            JObject envelope = SocialEnvelope.Ok(new JObject());

            Assert.Null(envelope["statusCode"]);
            Assert.Null(envelope["originalResponseData"]);
        }

        [Fact]
        public void ErrorsCarryTheCodeAndNotAnEmptyDataField()
        {
            JObject envelope = SocialEnvelope.Error(SocialErrorCodes.InviteNotFound);

            Assert.False(envelope.Value<bool>("success"));
            Assert.Equal("invite_not_found", envelope.Value<string>("errorCode"));
            Assert.Null(envelope["data"]);
        }
    }
}
