using System.Net;
using API.IntegrationTests.Infrastructure;

namespace API.IntegrationTests.Auth;

/// <summary>
/// MH-SEC-051 documented baseline gaps that remain after Phase 8 auth cards.
/// Currently empty — MH-SEC-061 closed the last KnownGap in this suite.
/// Keep the class so future cards can reintroduce intentional reds without reshuffling traits.
/// </summary>
[Collection(AuthHttpCollection.Name)]
public sealed class AuthSessionKnownGapBaselineTests
{
    private readonly AuthWebApplicationFactory _factory;

    public AuthSessionKnownGapBaselineTests(AuthWebApplicationFactory factory) => _factory = factory;

    // Placeholder so the collection fixture still constructs; no KnownGap facts remain after MH-SEC-061.
    [Fact]
    public void Phase8_known_gap_suite_has_no_pending_reds()
        => Assert.NotNull(_factory);
}
