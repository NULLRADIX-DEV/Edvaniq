using Edvaniq.Client.Core;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.ArchitectureTests;

// #278: the client sends the version Content wants and words a missing tick as Content does. The client can't reference
// Content, so a change on one side turns this red.
public class MaterialConfirmationValuesTests
{
    [Fact]
    public void Version_IsContents()
    {
        Assert.Equal(MaterialConfirmation.Version, MaterialConfirmationValues.Version);
        Assert.Null(MaterialConfirmation.Check(MaterialConfirmationValues.Version));
    }

    [Fact]
    public void Message_IsContents() =>
        Assert.Equal(MaterialConfirmationValues.Missing, MaterialConfirmation.Check(null));
}