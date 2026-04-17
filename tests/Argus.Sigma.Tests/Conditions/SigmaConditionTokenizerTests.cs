using Argus.Sigma.Conditions;

namespace Argus.Sigma.Tests.Conditions;

public class SigmaConditionTokenizerTests
{
    [Fact]
    public void Tokenize_simple_selection_reference()
    {
        var tokens = SigmaConditionTokenizer.Tokenize("selection");
        tokens.Should().HaveCount(2);
        tokens[0].Kind.Should().Be(TokenKind.Identifier);
        tokens[0].Text.Should().Be("selection");
        tokens[1].Kind.Should().Be(TokenKind.End);
    }

    [Fact]
    public void Tokenize_and_or_not_keywords()
    {
        var tokens = SigmaConditionTokenizer.Tokenize("a and b or not c");
        tokens.Select(t => t.Kind).Should().ContainInOrder(
            TokenKind.Identifier, TokenKind.And, TokenKind.Identifier,
            TokenKind.Or, TokenKind.Not, TokenKind.Identifier, TokenKind.End);
    }

    [Fact]
    public void Tokenize_one_of_selection_wildcard()
    {
        var tokens = SigmaConditionTokenizer.Tokenize("1 of selection*");
        tokens.Select(t => t.Kind).Should().ContainInOrder(
            TokenKind.Number, TokenKind.Of, TokenKind.Identifier, TokenKind.Wildcard, TokenKind.End);
        tokens[2].Text.Should().Be("selection");
    }

    [Fact]
    public void Tokenize_parens()
    {
        var tokens = SigmaConditionTokenizer.Tokenize("(a or b) and c");
        tokens.Select(t => t.Kind).Should().ContainInOrder(
            TokenKind.LParen, TokenKind.Identifier, TokenKind.Or, TokenKind.Identifier,
            TokenKind.RParen, TokenKind.And, TokenKind.Identifier, TokenKind.End);
    }

    [Fact]
    public void Tokenize_rejects_unknown_character()
    {
        Action act = () => SigmaConditionTokenizer.Tokenize("a @ b");
        act.Should().Throw<FormatException>();
    }
}
