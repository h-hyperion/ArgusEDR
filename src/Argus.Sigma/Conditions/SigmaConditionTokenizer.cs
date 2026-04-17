namespace Argus.Sigma.Conditions;

public enum TokenKind { Identifier, And, Or, Not, Of, LParen, RParen, Number, Wildcard, End }

public sealed record Token(TokenKind Kind, string Text, int Position);

public static class SigmaConditionTokenizer
{
    public static IReadOnlyList<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(') { tokens.Add(new Token(TokenKind.LParen, "(", i)); i++; continue; }
            if (c == ')') { tokens.Add(new Token(TokenKind.RParen, ")", i)); i++; continue; }
            if (c == '*') { tokens.Add(new Token(TokenKind.Wildcard, "*", i)); i++; continue; }
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < input.Length && char.IsDigit(input[i])) i++;
                tokens.Add(new Token(TokenKind.Number, input[start..i], start));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_')) i++;
                var word = input[start..i];
                var kind = word.ToLowerInvariant() switch
                {
                    "and" => TokenKind.And,
                    "or" => TokenKind.Or,
                    "not" => TokenKind.Not,
                    "of" => TokenKind.Of,
                    _ => TokenKind.Identifier
                };
                tokens.Add(new Token(kind, word, start));
                continue;
            }
            throw new FormatException($"Unexpected character '{c}' at position {i} in condition '{input}'");
        }
        tokens.Add(new Token(TokenKind.End, "", input.Length));
        return tokens;
    }
}
