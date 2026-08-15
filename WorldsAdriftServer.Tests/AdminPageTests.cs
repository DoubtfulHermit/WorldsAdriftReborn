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

        [Fact]
        public void Dashboard_uses_one_responsive_console_system_and_compact_safe_controls()
        {
            string html = AdminPage.Dashboard("{}", new string('b', 64));

            Assert.Contains("class=\"selectors\"", html);
            Assert.Contains("class=\"nudge-pad\"", html);
            Assert.Contains("aria-label=\"Nudge ship north one metre\"", html);
            Assert.Contains("class=\"tool danger-zone\"", html);
            Assert.Contains("class=\"danger-button\"", html);
            Assert.Contains("class=\"receipt\"", html);
            Assert.Contains("@media(max-width:760px)", html);
            Assert.Contains("prefers-reduced-motion", html);
            Assert.Contains("button:focus-visible", html);
            Assert.Contains("--accent:#74c9cf", html);
            Assert.DoesNotContain("--timber", html);
            Assert.DoesNotContain("#eebd8e", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cdn", html, StringComparison.OrdinalIgnoreCase);
        }
    }
}
