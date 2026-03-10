using AppAutomation.TUnit;
using TUnit.Assertions;
using TUnit.Core;

public class UiTestBaseLifecycleTests
{
    [Test]
    public async Task SetupAndCleanup_CreateAndDisposeSession()
    {
        var fixture = new FakeUiFixture();

        fixture.SetupUiSession();

        using (Assert.Multiple())
        {
            await Assert.That(fixture.LaunchSessionCalls).IsEqualTo(1);
            await Assert.That(fixture.CreatePageCalls).IsEqualTo(1);
            await Assert.That(fixture.AccessPage()).IsEqualTo("page-ready");
            await Assert.That(fixture.AccessSession().Disposed).IsEqualTo(false);
        }

        var createdSession = fixture.AccessSession();
        fixture.CleanupUiSession();

        using (Assert.Multiple())
        {
            await Assert.That(createdSession.Disposed).IsEqualTo(true);
            await Assert.That(() => fixture.AccessSession()).Throws<InvalidOperationException>();
            await Assert.That(() => fixture.AccessPage()).Throws<InvalidOperationException>();
        }
    }

    [Test]
    public async Task WaitUntil_ReturnsWhenConditionEventuallyMatches()
    {
        var fixture = new FakeUiFixture();
        var attempts = 0;

        var result = fixture.RunWaitUntil(
            () => ++attempts,
            static value => value >= 3,
            timeout: TimeSpan.FromSeconds(1));

        using (Assert.Multiple())
        {
            await Assert.That(result).IsEqualTo(3);
            await Assert.That(attempts >= 3).IsEqualTo(true);
        }
    }

    [Test]
    public async Task RetryUntil_RepeatsUntilAttemptSucceeds()
    {
        var fixture = new FakeUiFixture();
        var attempts = 0;

        fixture.RunRetryUntil(
            () => ++attempts >= 3,
            timeout: TimeSpan.FromSeconds(1));

        await Assert.That(attempts >= 3).IsEqualTo(true);
    }

    private sealed class FakeUiFixture : UiTestBase<FakeUiSession, string>
    {
        public int LaunchSessionCalls { get; private set; }

        public int CreatePageCalls { get; private set; }

        protected override FakeUiSession LaunchSession()
        {
            LaunchSessionCalls++;
            return new FakeUiSession();
        }

        protected override string CreatePage(FakeUiSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            CreatePageCalls++;
            return "page-ready";
        }

        public FakeUiSession AccessSession() => Session;

        public string AccessPage() => Page;

        public int RunWaitUntil(Func<int> valueFactory, Predicate<int> condition, TimeSpan timeout) =>
            WaitUntil(valueFactory, condition, timeout: timeout);

        public void RunRetryUntil(Func<bool> attempt, TimeSpan timeout) =>
            RetryUntil(attempt, timeout: timeout);
    }

    private sealed class FakeUiSession : IUiTestSession
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
