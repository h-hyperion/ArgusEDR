using Argus.Sigma.Conditions;

namespace Argus.Sigma.Tests.Conditions;

public class SigmaConditionParserTests
{
    private static readonly string[] TwoSelections = { "selection", "filter" };
    private static readonly string[] ThreeWildcardSelections = { "selection_a", "selection_b", "filter" };

    [Fact]
    public void Parses_single_selection()
    {
        var ast = SigmaConditionParser.Parse("selection", TwoSelections);
        ast.Should().BeOfType<SelectionRef>()
            .Which.Name.Should().Be("selection");
    }

    [Fact]
    public void Parses_and_of_two()
    {
        var ast = SigmaConditionParser.Parse("selection and filter", TwoSelections);
        ast.Should().BeOfType<AndNode>();
        ((AndNode)ast).Left.Should().BeOfType<SelectionRef>().Which.Name.Should().Be("selection");
        ((AndNode)ast).Right.Should().BeOfType<SelectionRef>().Which.Name.Should().Be("filter");
    }

    [Fact]
    public void Not_binds_tighter_than_and()
    {
        var ast = SigmaConditionParser.Parse("selection and not filter", TwoSelections);
        ast.Should().BeOfType<AndNode>();
        ((AndNode)ast).Right.Should().BeOfType<NotNode>();
    }

    [Fact]
    public void And_binds_tighter_than_or()
    {
        var ast = SigmaConditionParser.Parse("a or b and c", new[] { "a", "b", "c" });
        ast.Should().BeOfType<OrNode>();
        var or = (OrNode)ast;
        or.Right.Should().BeOfType<AndNode>();
    }

    [Fact]
    public void Parens_override_precedence()
    {
        var ast = SigmaConditionParser.Parse("(a or b) and c", new[] { "a", "b", "c" });
        ast.Should().BeOfType<AndNode>();
        ((AndNode)ast).Left.Should().BeOfType<OrNode>();
    }

    [Fact]
    public void One_of_wildcard_expands_to_matching_selections()
    {
        var ast = SigmaConditionParser.Parse("1 of selection*", ThreeWildcardSelections);
        ast.Should().BeOfType<QuantifierNode>();
        var q = (QuantifierNode)ast;
        q.Kind.Should().Be(Quantifier.OneOf);
        q.SelectionNames.Should().BeEquivalentTo(new[] { "selection_a", "selection_b" });
    }

    [Fact]
    public void All_of_them_expands_to_all_selections()
    {
        var ast = SigmaConditionParser.Parse("all of them", ThreeWildcardSelections);
        ast.Should().BeOfType<QuantifierNode>();
        var q = (QuantifierNode)ast;
        q.Kind.Should().Be(Quantifier.AllOf);
        q.SelectionNames.Should().BeEquivalentTo(ThreeWildcardSelections);
    }

    [Fact]
    public void Unknown_selection_throws()
    {
        Action act = () => SigmaConditionParser.Parse("missing", TwoSelections);
        act.Should().Throw<FormatException>()
           .WithMessage("*missing*");
    }
}
