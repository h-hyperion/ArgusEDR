using Argus.Sigma.Models;

namespace Argus.Sigma.Tests.Models;

public class SigmaFieldSpecTests
{
    [Theory]
    [InlineData("CommandLine", "CommandLine", new string[0])]
    [InlineData("CommandLine|contains", "CommandLine", new[] { "contains" })]
    [InlineData("Image|endswith|all", "Image", new[] { "endswith", "all" })]
    public void Parse_splits_field_and_modifiers(string input, string expectedField, string[] expectedModifiers)
    {
        var spec = SigmaFieldSpec.Parse(input);

        spec.FieldName.Should().Be(expectedField);
        spec.Modifiers.Should().BeEquivalentTo(expectedModifiers);
    }

    [Fact]
    public void Parse_rejects_empty_field_name()
    {
        Action act = () => SigmaFieldSpec.Parse("|contains");
        act.Should().Throw<ArgumentException>();
    }
}
