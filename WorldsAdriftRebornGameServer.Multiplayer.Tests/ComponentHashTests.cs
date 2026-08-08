using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The FNV hash that keys the component-update handler table.
    ///
    /// Registration hashes a handler's TBase type name; per-packet dispatch
    /// hashes the metaclass factory's type name; a handler runs only when the
    /// two values collide in a dictionary. There is no error path for a
    /// mismatch - handlers just silently never fire - so the algorithm is
    /// pinned here bit for bit. These vectors are the standard 64-bit FNV-1a
    /// results (xor-then-multiply, offset 14695981039346656037, prime
    /// 1099511628211), independently computable.
    /// </summary>
    public class ComponentHashTests
    {
        [Fact]
        public void The_empty_string_hashes_to_the_offset_basis()
        {
            Assert.Equal(14695981039346656037UL, ComponentHash.OfTypeFullName(""));
            Assert.Equal(0xcbf29ce484222325UL, ComponentHash.OfTypeFullName(""));
        }

        [Fact]
        public void A_single_character_matches_the_published_fnv1a_vector()
        {
            Assert.Equal(0xaf63dc4c8601ec8cUL, ComponentHash.OfTypeFullName("a"));
        }

        [Fact]
        public void A_type_name_shaped_input_is_stable()
        {
            // Not a magic value - just this algorithm's answer, pinned so a
            // future "cleanup" (e.g. to multiply-then-xor FNV-1, which the
            // original comment wrongly claimed this was) cannot silently
            // unregister every handler in the server.
            Assert.Equal(0x4d8eecd1d965145fUL, ComponentHash.OfTypeFullName("Improbable.TransformState"));
        }

        [Fact]
        public void Different_names_hash_differently()
        {
            Assert.NotEqual(
                ComponentHash.OfTypeFullName("Improbable.TransformState"),
                ComponentHash.OfTypeFullName("Improbable.TransformStat"));
        }
    }
}
