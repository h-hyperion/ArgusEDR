namespace Argus.Sigma.Conditions;

public static class SigmaConditionParser
{
    public static SigmaCondition Parse(string input, IReadOnlyCollection<string> availableSelections)
    {
        var tokens = SigmaConditionTokenizer.Tokenize(input);
        var cursor = new Cursor(tokens, availableSelections);
        var ast = cursor.ParseOr();
        cursor.Expect(TokenKind.End);
        return ast;
    }

    private sealed class Cursor
    {
        private readonly IReadOnlyList<Token> _tokens;
        private readonly IReadOnlyCollection<string> _selections;
        private int _pos;

        public Cursor(IReadOnlyList<Token> tokens, IReadOnlyCollection<string> selections)
        {
            _tokens = tokens;
            _selections = selections;
        }

        private Token Peek() => _tokens[_pos];
        private Token Consume() => _tokens[_pos++];

        public void Expect(TokenKind kind)
        {
            if (Peek().Kind != kind)
                throw new FormatException($"Expected {kind} but got {Peek().Kind} at position {Peek().Position}");
            Consume();
        }

        public SigmaCondition ParseOr()
        {
            var left = ParseAnd();
            while (Peek().Kind == TokenKind.Or)
            {
                Consume();
                var right = ParseAnd();
                left = new OrNode(left, right);
            }
            return left;
        }

        private SigmaCondition ParseAnd()
        {
            var left = ParseNot();
            while (Peek().Kind == TokenKind.And)
            {
                Consume();
                var right = ParseNot();
                left = new AndNode(left, right);
            }
            return left;
        }

        private SigmaCondition ParseNot()
        {
            if (Peek().Kind == TokenKind.Not)
            {
                Consume();
                return new NotNode(ParseNot());
            }
            return ParseAtom();
        }

        private SigmaCondition ParseAtom()
        {
            var t = Peek();
            if (t.Kind == TokenKind.LParen)
            {
                Consume();
                var inner = ParseOr();
                Expect(TokenKind.RParen);
                return inner;
            }
            if (t.Kind == TokenKind.Number)
            {
                if (t.Text != "1")
                    throw new FormatException($"Only '1 of' / 'all of' quantifiers are supported; got '{t.Text} of ...' at {t.Position}");
                Consume();
                Expect(TokenKind.Of);
                return ParseQuantifierTarget(Quantifier.OneOf);
            }
            if (t.Kind == TokenKind.Identifier && t.Text.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                Consume();
                Expect(TokenKind.Of);
                return ParseQuantifierTarget(Quantifier.AllOf);
            }
            if (t.Kind == TokenKind.Identifier)
            {
                Consume();
                if (!_selections.Contains(t.Text))
                    throw new FormatException($"Condition references unknown selection '{t.Text}' at position {t.Position}");
                return new SelectionRef(t.Text);
            }
            throw new FormatException($"Unexpected token {t.Kind} ('{t.Text}') at position {t.Position}");
        }

        private SigmaCondition ParseQuantifierTarget(Quantifier quant)
        {
            var t = Peek();
            if (t.Kind != TokenKind.Identifier)
                throw new FormatException($"Expected selection name or 'them' after quantifier; got {t.Kind} at {t.Position}");
            Consume();

            IReadOnlyList<string> resolved;
            if (t.Text.Equals("them", StringComparison.OrdinalIgnoreCase))
            {
                resolved = _selections.ToArray();
            }
            else if (Peek().Kind == TokenKind.Wildcard)
            {
                Consume();
                resolved = _selections.Where(s => s.StartsWith(t.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (resolved.Count == 0)
                    throw new FormatException($"Wildcard '{t.Text}*' matched no selections at position {t.Position}");
            }
            else
            {
                if (!_selections.Contains(t.Text))
                    throw new FormatException($"Quantifier references unknown selection '{t.Text}' at position {t.Position}");
                resolved = new[] { t.Text };
            }
            return new QuantifierNode(quant, resolved);
        }
    }
}
