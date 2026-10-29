using Quantum.Mobile;
using Ui = Quantum.Plugin.Abstraction.Mobile;

namespace Quantum.Tests;

public sealed class MobilePagePublisherTests
{
    [Fact]
    public void IdenticalDeclarationsKeepTheRevisionAndExistingActionsValid()
    {
        var publisher = new MobilePagePublisher();
        publisher.Publish(Ui.Page("notes", "Notes", Ui.Text("text", "Same content")));
        var revision = publisher.Revision;
        publisher.Publish(Ui.Page("notes", "Notes", Ui.Text("text", "Same content")));
        Assert.Equal(revision, publisher.Revision);
        publisher.Publish(Ui.Page("notes", "Notes", Ui.Text("text", "Changed content")));
        Assert.True(publisher.Revision > revision);
    }
}
