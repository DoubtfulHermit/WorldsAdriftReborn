using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    public class AdminPageTests
    {
        [Fact]
        public void Dashboard_exposes_the_functional_sections_and_csrf_bound_operations()
        {
            string csrf = new string('a', 64);
            string html = AdminPage.Dashboard("{}", csrf);

            Assert.Contains("id=\"world\"", html);
            Assert.Contains("id=\"simulation\"", html);
            Assert.Contains("id=\"operations\"", html);
            Assert.Contains("data-command=\"resources-reset\"", html);
            Assert.Contains("data-command=\"ship-recall\"", html);
            Assert.Contains("data-command=\"ship-delete\"", html);
            Assert.Contains("name=\"csrf\" value=\"" + csrf + "\"", html);
            Assert.Contains("'X-Wareborn-CSRF':CSRF", html);
            Assert.Contains("Latest game-server completion", html);
            Assert.DoesNotContain("Worker A", html);
            Assert.DoesNotContain("migrate", html, StringComparison.OrdinalIgnoreCase);
        }
    }
}
