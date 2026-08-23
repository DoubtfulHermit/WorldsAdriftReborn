using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    public sealed class FlightShadowStatsProjectionTests
    {
        [Fact]
        public void Reader_allowlists_and_bounds_shadow_observation()
        {
            JObject root = JObject.Parse(@"{
              'schemaVersion': 19,
              'runtime': { 'shipDomains': [{
                'domainId':'ship:1','hullEntityId':1,
                'flight': { 'present':true, 'shadow': {
                  'present':true,'enabled':true,'vectorAvailable':true,
                  'reason':'vector-live','forceX':12.5,'forceY':-3,'forceZ':999,
                  'rawTorqueX':1,'rawTorqueY':2,'rawTorqueZ':3,
                  'retailTorqueX':0,'retailTorqueY':4,'retailTorqueZ':5,
                  'acceptedParts':999999,'rejectedParts':-5,
                  'massApproximation':true,'terrainAvailable':false,
                  'collisionCandidates':7,'collisionContacts':2,
                  'collisionHardRejected':false,'unknown':'discard-me'
                }}
              }]}
            }");
            GameStatsSnapshot parsed = GameStatsSnapshot.Parse(root);
            JObject shadow = (JObject)Assert.Single(parsed.ShipDomains).Json["flight"]!["shadow"]!;
            Assert.True((bool)shadow["vectorAvailable"]!);
            Assert.Equal(999, (double)shadow["forceZ"]!);
            Assert.Equal(4096, (int)shadow["acceptedParts"]!);
            Assert.Equal(0, (int)shadow["rejectedParts"]!);
            Assert.Null(shadow["unknown"]);
            Assert.False((bool)shadow["terrainAvailable"]!);
        }

        [Fact]
        public void Missing_shadow_is_explicitly_absent()
        {
            JObject root = JObject.Parse("{'runtime':{'shipDomains':[{'domainId':'ship:1','hullEntityId':1,'flight':{'present':true}}]}}");
            JObject shadow = (JObject)Assert.Single(GameStatsSnapshot.Parse(root).ShipDomains)
                .Json["flight"]!["shadow"]!;
            Assert.False((bool)shadow["present"]!);
            Assert.False((bool)shadow["enabled"]!);
        }
    }
}
