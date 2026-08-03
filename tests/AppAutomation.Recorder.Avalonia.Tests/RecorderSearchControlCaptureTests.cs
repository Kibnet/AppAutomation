using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderSearchControlCaptureTests
{
    [Test]
    public async Task ManualInput_RecordsEnterSearch()
    {
        using var recorder = SearchControlCaptureFixture.Create();

        recorder.EnterText("orders");

        await AssertSingleCommand(
            recorder.Session,
            "Page.EnterSearch(static page => page.TableSearch, \"orders\");");
    }

    [Test]
    public async Task ManualClear_RecordsClearSearch()
    {
        using var recorder = SearchControlCaptureFixture.Create("orders");

        recorder.EnterText(string.Empty);

        await AssertSingleCommand(
            recorder.Session,
            "Page.ClearSearch(static page => page.TableSearch);");
    }

    [Test]
    public async Task HistoryClick_RecordsHistoryActionWithoutDuplicateTextEntry()
    {
        using var recorder = SearchControlCaptureFixture.Create();

        recorder.ApplyHistory("previous search");

        await AssertSingleCommand(
            recorder.Session,
            "Page.ApplySearchFromHistory(static page => page.TableSearch, \"previous search\");");
    }

    private static async Task AssertSingleCommand(RecorderSession session, string expectedCommand)
    {
        using (Assert.Multiple())
        {
            await Assert.That(session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(session.StepJournal[0].Preview).Contains(expectedCommand);
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("Page.EnterText");
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("Page.Click");
        }
    }
}
