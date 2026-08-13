using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// Pins the lamp switch ledger: the always-on default for untracked (loose)
    /// lamps, the toggle, and the registration lifecycle.
    /// </summary>
    public class LampsTests
    {
        [Fact]
        public void UntrackedLampIsOn_TheProvenLooseServe()
        {
            var lamps = new Lamps();
            Assert.False(lamps.IsLamp(99));
            Assert.True(lamps.IsOn(99)); // the 1108 enabled=true a loose lamp always had
        }

        [Fact]
        public void FreshMountIsOn()
        {
            var lamps = new Lamps();
            Assert.True(lamps.Register(10, 1));
            Assert.True(lamps.IsOn(10));
        }

        [Fact]
        public void RestoreCanStartOff()
        {
            var lamps = new Lamps();
            lamps.Register(10, 1, on: false);
            Assert.False(lamps.IsOn(10));
        }

        [Fact]
        public void ToggleFlipsAndReturnsNewState()
        {
            var lamps = new Lamps();
            lamps.Register(10, 1);

            Assert.False(lamps.Toggle(10)); // on -> off
            Assert.False(lamps.IsOn(10));

            Assert.True(lamps.Toggle(10));  // off -> on
            Assert.True(lamps.IsOn(10));
        }

        [Fact]
        public void ToggleOnUnknownIdReturnsNull()
        {
            var lamps = new Lamps();
            Assert.Null(lamps.Toggle(99));
        }

        [Fact]
        public void ReRegistrationDoesNotResetPlayerSetState()
        {
            var lamps = new Lamps();
            lamps.Register(10, 1);
            lamps.Toggle(10); // player switched it off

            Assert.False(lamps.Register(10, 1));
            Assert.False(lamps.IsOn(10)); // the off survives
        }

        [Fact]
        public void UnregisterForgetsAndRemountStartsOn()
        {
            var lamps = new Lamps();
            lamps.Register(10, 1);
            lamps.Toggle(10); // off

            Assert.True(lamps.Unregister(10));
            Assert.True(lamps.IsOn(10)); // untracked again -> loose always-on default

            Assert.True(lamps.Register(10, 2));
            Assert.True(lamps.IsOn(10)); // fresh mount is on
        }
    }
}
