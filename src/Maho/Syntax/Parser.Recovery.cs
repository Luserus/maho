namespace Maho.Syntax;

internal sealed partial class Parser
{
    private static bool IsRecoveryBoundary(TokenKind kind) =>
        kind is TokenKind.EndToken or TokenKind.RightParen or TokenKind.RightBracket or TokenKind.RightBrace or TokenKind.Semicolon or TokenKind.Comma;

    private void SynchronizeTo(params TokenKind[] stopKinds)
    {
        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            if (Contains(stopKinds, CurrentToken.Kind))
                break;

            Consume();
        }
    }

    private bool CanStartExpression()
    {
        if (CurrentToken.Kind is TokenKind.LeftParen or TokenKind.LeftBrace or TokenKind.LeftBracket or TokenKind.Identifier)
            return true;

        if (IsLiteralTokenKind(CurrentToken.Kind))
            return true;

        var (kind, length) = GetCombinedOperatorData();
        return length > 0 && operatorTable.TryGetValue(kind, out var entry) && entry.IsPrefix;
    }

    private bool CanStartTopLevelConstruct() =>
        CurrentToken.Kind is TokenKind.EndToken or TokenKind.RightBrace or TokenKind.Semicolon ||
        CurrentToken.MatchingKind is MatchingKeywordKind.Namespace ||
        IsCurrentTokenModifier ||
        CanStartExpression();

    private bool CanStartMemberConstruct() =>
        CurrentToken.Kind is TokenKind.EndToken or TokenKind.RightBrace or TokenKind.Semicolon ||
        IsCurrentTokenModifier ||
        IsCurrentTokenTypeDeclarationStart ||
        CurrentToken.Kind is TokenKind.Identifier;

    private bool CanStartLocalConstruct() =>
        CurrentToken.Kind is TokenKind.EndToken or TokenKind.RightBrace or TokenKind.Semicolon ||
        IsCurrentTokenModifier ||
        CanStartExpression();

    private void SynchronizeConstruct(System.Func<bool> isRecoveryPoint)
    {
        if (CurrentToken.Kind is TokenKind.EndToken)
            return;

        Consume();

        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            if (CurrentToken.Kind is TokenKind.RightBrace)
                return;

            if (CurrentToken.Kind is TokenKind.Semicolon)
            {
                Consume();
                return;
            }

            if (isRecoveryPoint())
                return;

            Consume();
        }
    }

    private void SynchronizeTopLevel() => SynchronizeConstruct(CanStartTopLevelConstruct);
    private void SynchronizeMember() => SynchronizeConstruct(CanStartMemberConstruct);
    private void SynchronizeLocal() => SynchronizeConstruct(CanStartLocalConstruct);

    private void RecoverTopLevelIfStalled(int start)
    {
        if (current == start)
            SynchronizeTopLevel();
    }

    private void RecoverMemberIfStalled(int start)
    {
        if (current == start)
            SynchronizeMember();
    }

    private void RecoverLocalIfStalled(int start)
    {
        if (current == start)
            SynchronizeLocal();
    }
}