namespace Maho.Syntax;

/// <summary> Parser recovery heuristics for resynchronizing after a syntax error. </summary>
internal sealed partial class Parser
{
    /// <summary> Recognizes token kinds that can safely terminate a recovery scan. </summary>
    private static bool IsRecoveryBoundary(TokenKind kind) =>
        kind is TokenKind.EndToken or TokenKind.RightParen or TokenKind.RightBracket or TokenKind.RightBrace or TokenKind.Semicolon or TokenKind.Comma;

    /// <summary> Checks whether the current token can start an expression. </summary>
    private bool CanStartExpression()
    {
        if (CurrentToken.Kind is TokenKind.LeftParen or TokenKind.LeftBrace or TokenKind.LeftBracket or TokenKind.Identifier)
            return true;

        if (IsLiteralTokenKind(CurrentToken.Kind))
            return true;

        var (kind, length) = GetCombinedOperatorData();
        return length > 0 && operatorTable.TryGetValue(kind, out var entry) && entry.IsPrefix;
    }

    /// <summary> Checks whether the current token can begin a top-level construct. </summary>
    private bool CanStartTopLevelConstruct() =>
        CurrentToken.Kind is TokenKind.EndToken or TokenKind.RightBrace or TokenKind.Semicolon ||
        CurrentToken.MatchingKind is MatchingKeywordKind.Namespace ||
        IsCurrentTokenAttributeListStart ||
        IsCurrentTokenModifier ||
        CanStartExpression();

    /// <summary> Checks whether the current token can begin a member declaration or member statement. </summary>
    private bool CanStartMemberConstruct() =>
        CurrentToken.Kind is TokenKind.EndToken or TokenKind.RightBrace or TokenKind.Semicolon ||
        IsCurrentTokenAttributeListStart ||
        IsCurrentTokenModifier ||
        IsCurrentTokenTypeDeclarationStart ||
        CurrentToken.Kind is TokenKind.Identifier;

    /// <summary> Checks whether the current token can begin a local construct. </summary>
    private bool CanStartLocalConstruct() =>
        CurrentToken.Kind is TokenKind.EndToken or TokenKind.RightBrace or TokenKind.Semicolon ||
        IsCurrentTokenAttributeListStart ||
        IsCurrentTokenModifier ||
        CanStartExpression();

    /// <summary> Advances until the parser reaches a point that can plausibly resume parsing. </summary>
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

    /// <summary> Recovers a stalled top-level parse by scanning forward to the next safe boundary. </summary>
    private void SynchronizeTopLevel() => SynchronizeConstruct(CanStartTopLevelConstruct);
    /// <summary> Recovers a stalled member parse by scanning forward to the next safe boundary. </summary>
    private void SynchronizeMember() => SynchronizeConstruct(CanStartMemberConstruct);
    /// <summary> Recovers a stalled local parse by scanning forward to the next safe boundary. </summary>
    private void SynchronizeLocal() => SynchronizeConstruct(CanStartLocalConstruct);

    /// <summary> Forces top-level recovery when the parse cursor failed to advance. </summary>
    private void RecoverTopLevelIfStalled(int start)
    {
        if (current == start)
            SynchronizeTopLevel();
    }

    /// <summary> Forces member recovery when the parse cursor failed to advance. </summary>
    private void RecoverMemberIfStalled(int start)
    {
        if (current == start)
            SynchronizeMember();
    }

    /// <summary> Forces local recovery when the parse cursor failed to advance. </summary>
    private void RecoverLocalIfStalled(int start)
    {
        if (current == start)
            SynchronizeLocal();
    }
}
