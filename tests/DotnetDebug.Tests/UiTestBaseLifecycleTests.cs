using EasyUse.Session.Contracts;
using EasyUse.TUnit.Core;
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

    private sealed class FakeUiFixture : UiTestBase<FakeUiSession, string>
    {
        public int LaunchSessionCalls { get; private set; }

        public int CreatePageCalls { get; private set; }

        protected override DesktopProjectLaunchOptions CreateLaunchOptions()
        {
            return new DesktopProjectLaunchOptions
            {
                ProjectRelativePath = "src/DotnetDebug.Avalonia/DotnetDebug.Avalonia.csproj",
                TargetFramework = "net9.0"
            };
        }

        protected override FakeUiSession LaunchSession(DesktopProjectLaunchOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
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
