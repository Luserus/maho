using System.Threading.Tasks;
using Maho.Diagnostics;
using Maho.Text;

namespace Maho.Tests;

public sealed class DiagnosticEngineTests
{
    [Fact]
    public void DiagnosticBuilder_ConstructsRichDiagnostic_WithLabelsNotesHelpAndSuggestions()
    {
        var source = new SourceText("var x = a + b;");
        var manager = new DiagnosticsManager(source);

        var opSpan = new TextSpan(10, 1);
        var lhsSpan = new TextSpan(8, 1);
        var rhsSpan = new TextSpan(12, 1);

        manager.BuildError("MH2001", "Cannot apply binary operator '+' to types 'String8' and 'Int32'", opSpan)
            .WithPrimaryLabel(opSpan, "cannot apply '+'")
            .WithSecondaryLabel(lhsSpan, "type is 'String8'")
            .WithSecondaryLabel(rhsSpan, "type is 'Int32'")
            .WithNote("an implementation of '+' exists for 'String8', but right operand must be 'String8'")
            .WithHelp("consider converting the integer to a string")
            .WithSuggestion("convert integer to string", new TextSpan(12, 1), "b.ToString()", applicability: SuggestionApplicability.MachineApplicable)
            .WithCustomData("lhsType", "String8")
            .WithCustomData("rhsType", "Int32")
            .Report();

        Assert.True(manager.HasErrors);
        Diagnostic diag = Assert.Single(manager.Diagnostics);

        Assert.Equal("MH2001", diag.DiagnosticCode);
        Assert.Equal(DiagnosticKind.Error, diag.Kind);
        Assert.Equal(opSpan, diag.Span);
        Assert.Equal(opSpan, diag.PrimarySpan);

        // Labels
        Assert.Equal(3, diag.Labels.Count);
        Assert.Equal(DiagnosticLabelStyle.Primary, diag.Labels[0].Style);
        Assert.Equal(opSpan, diag.Labels[0].Span);
        Assert.Equal("cannot apply '+'", diag.Labels[0].Message);

        Assert.Equal(DiagnosticLabelStyle.Secondary, diag.Labels[1].Style);
        Assert.Equal(lhsSpan, diag.Labels[1].Span);
        Assert.Equal("type is 'String8'", diag.Labels[1].Message);

        Assert.Equal(DiagnosticLabelStyle.Secondary, diag.Labels[2].Style);
        Assert.Equal(rhsSpan, diag.Labels[2].Span);
        Assert.Equal("type is 'Int32'", diag.Labels[2].Message);

        // Notes & Help
        DiagnosticNote note = Assert.Single(diag.Notes);
        Assert.Contains("right operand must be 'String8'", note.Message);

        DiagnosticHelp help = Assert.Single(diag.HelpMessages);
        Assert.Contains("converting the integer to a string", help.Message);

        // Suggestion
        DiagnosticSuggestion suggestion = Assert.Single(diag.Suggestions);
        Assert.Equal("convert integer to string", suggestion.Description);
        Assert.Equal(SuggestionApplicability.MachineApplicable, suggestion.Applicability);
        TextEdit edit = Assert.Single(suggestion.Edits);
        Assert.Equal(new TextSpan(12, 1), edit.Span);
        Assert.Equal("b.ToString()", edit.NewText);

        // Custom data
        Assert.Equal("String8", diag.CustomData["lhsType"]);
        Assert.Equal("Int32", diag.CustomData["rhsType"]);
    }

    [Fact]
    public void DiagnosticsManager_ReportTypeMismatch_EmitsExpectedDiagnosticStructure()
    {
        var source = new SourceText("1 + \"hello\"");
        var manager = new DiagnosticsManager(source);

        manager.ReportTypeMismatch(
            new TextSpan(2, 1), "+",
            new TextSpan(0, 1), "Int32",
            new TextSpan(4, 7), "String8",
            note: "no overload matches",
            help: "cast one operand");

        Diagnostic diag = Assert.Single(manager.Diagnostics);
        Assert.Equal("MH2001", diag.DiagnosticCode);
        Assert.Equal(3, diag.Labels.Count);
        Assert.Equal(DiagnosticLabelStyle.Primary, diag.Labels[0].Style);
        Assert.Equal(DiagnosticLabelStyle.Secondary, diag.Labels[1].Style);
        Assert.Equal(DiagnosticLabelStyle.Secondary, diag.Labels[2].Style);
        Assert.Equal("no overload matches", Assert.Single(diag.Notes).Message);
        Assert.Equal("cast one operand", Assert.Single(diag.HelpMessages).Message);
    }

    [Fact]
    public void DiagnosticsManager_ReportDuplicateDeclaration_EmitsPrimaryAndSecondaryLabels()
    {
        var file1 = new SourceText("public struct Foo;");
        var file2 = new SourceText("public struct Foo;");
        var manager = new DiagnosticsManager();

        var firstSpan = new TextSpan(14, 3);
        var secondSpan = new TextSpan(14, 3);

        manager.ReportDuplicateDeclaration("Type", "Foo", secondSpan, firstSpan, redeclSource: file2, firstSource: file1);

        Diagnostic diag = Assert.Single(manager.Diagnostics);
        Assert.Equal("MH1002", diag.DiagnosticCode);
        Assert.Equal(DiagnosticKind.Error, diag.Kind);
        Assert.Equal(2, diag.Labels.Count);

        // Primary on redeclaration
        Assert.Equal(DiagnosticLabelStyle.Primary, diag.Labels[0].Style);
        Assert.Equal(secondSpan, diag.Labels[0].Span);
        Assert.Same(file2, diag.Labels[0].Source);
        Assert.Contains("re-declared here", diag.Labels[0].Message);

        // Secondary on original declaration
        Assert.Equal(DiagnosticLabelStyle.Secondary, diag.Labels[1].Style);
        Assert.Equal(firstSpan, diag.Labels[1].Span);
        Assert.Same(file1, diag.Labels[1].Source);
        Assert.Contains("previous declaration", diag.Labels[1].Message);

        Assert.Single(diag.HelpMessages);
    }

    [Fact]
    public void DiagnosticsManager_ReportDuplicateTypeDeclaration_EmitsPrimaryAndSecondaryLabels()
    {
        var manager = new DiagnosticsManager();
        var firstSpan = new TextSpan(10, 5);
        var secondSpan = new TextSpan(30, 5);

        manager.ReportDuplicateTypeDeclaration("Person", secondSpan, firstSpan);

        Diagnostic diag = Assert.Single(manager.Diagnostics);
        Assert.Equal("MH1002", diag.DiagnosticCode);
        Assert.Equal(2, diag.Labels.Count);
        Assert.Equal(DiagnosticLabelStyle.Primary, diag.Labels[0].Style);
        Assert.Equal(DiagnosticLabelStyle.Secondary, diag.Labels[1].Style);
        Assert.Contains("Person", diag.Message);
        Assert.Single(diag.HelpMessages);
    }

    [Fact]
    public void DiagnosticsManager_ReportDuplicateFunctionDeclaration_EmitsPrimaryAndSecondaryLabels()
    {
        var manager = new DiagnosticsManager();
        var firstSpan = new TextSpan(5, 4);
        var secondSpan = new TextSpan(25, 4);

        manager.ReportDuplicateFunctionDeclaration("Calc", secondSpan, firstSpan);

        Diagnostic diag = Assert.Single(manager.Diagnostics);
        Assert.Equal("MH1003", diag.DiagnosticCode);
        Assert.Equal(2, diag.Labels.Count);
        Assert.Equal(DiagnosticLabelStyle.Primary, diag.Labels[0].Style);
        Assert.Equal(DiagnosticLabelStyle.Secondary, diag.Labels[1].Style);
        Assert.Contains("Calc", diag.Message);
        Assert.Single(diag.HelpMessages);
    }

    [Fact]
    public void DiagnosticsManager_ReportDuplicateVariableDeclaration_EmitsPrimaryAndSecondaryLabels()
    {
        var manager = new DiagnosticsManager();
        var firstSpan = new TextSpan(2, 3);
        var secondSpan = new TextSpan(20, 3);

        manager.ReportDuplicateVariableDeclaration("val", secondSpan, firstSpan);

        Diagnostic diag = Assert.Single(manager.Diagnostics);
        Assert.Equal("MH1005", diag.DiagnosticCode);
        Assert.Equal(2, diag.Labels.Count);
        Assert.Equal(DiagnosticLabelStyle.Primary, diag.Labels[0].Style);
        Assert.Equal(DiagnosticLabelStyle.Secondary, diag.Labels[1].Style);
        Assert.Contains("val", diag.Message);
        Assert.Single(diag.HelpMessages);
    }

    [Fact]
    public void DiagnosticsManager_ReportDuplicatePropertyDeclaration_EmitsPrimaryAndSecondaryLabels()
    {
        var manager = new DiagnosticsManager();
        var firstSpan = new TextSpan(4, 4);
        var secondSpan = new TextSpan(24, 4);

        manager.ReportDuplicatePropertyDeclaration("Size", secondSpan, firstSpan);

        Diagnostic diag = Assert.Single(manager.Diagnostics);
        Assert.Equal("MH1006", diag.DiagnosticCode);
        Assert.Equal(2, diag.Labels.Count);
        Assert.Equal(DiagnosticLabelStyle.Primary, diag.Labels[0].Style);
        Assert.Equal(DiagnosticLabelStyle.Secondary, diag.Labels[1].Style);
        Assert.Contains("Size", diag.Message);
        Assert.Single(diag.HelpMessages);
    }

    [Fact]
    public void DiagnosticsManager_ReportCyclicTypeHierarchy_EmitsPrimaryLabelAndHelp()
    {
        var manager = new DiagnosticsManager();
        var span = new TextSpan(10, 4);

        manager.ReportCyclicTypeHierarchy(span, "Node");

        Diagnostic diag = Assert.Single(manager.Diagnostics);
        Assert.Equal("MH1004", diag.DiagnosticCode);
        Assert.Single(diag.Labels);
        Assert.Equal(DiagnosticLabelStyle.Primary, diag.Labels[0].Style);
        Assert.Contains("cycle", diag.Message);
        Assert.Single(diag.HelpMessages);
    }

    [Fact]
    public void DiagnosticsManager_ReportAmbiguousTypeReference_EmitsPrimaryLabelAndHelp()
    {
        var manager = new DiagnosticsManager();
        var span = new TextSpan(5, 3);

        manager.ReportAmbiguousTypeReference(span, "Foo");

        Diagnostic diag = Assert.Single(manager.Diagnostics);
        Assert.Equal("MH1001", diag.DiagnosticCode);
        Assert.Single(diag.Labels);
        Assert.Equal(DiagnosticLabelStyle.Primary, diag.Labels[0].Style);
        Assert.Contains("Foo", diag.Message);
        Assert.Single(diag.HelpMessages);
    }

    [Fact]
    public void DiagnosticsManager_ThreadSafety_AllowsConcurrentReports()
    {
        var manager = new DiagnosticsManager();

        Parallel.For(0, 1000, i =>
        {
            manager.ReportError($"MH{i:D4}", $"Error {i}", new TextSpan(i, 1));
        });

        Assert.Equal(1000, manager.Diagnostics.Count);
        Assert.True(manager.HasErrors);
    }

    [Fact]
    public void DiagnosticSuggestion_SupportsMultiEditWorkspaceChanges()
    {
        var file1 = new SourceText("var a = 1;");
        var file2 = new SourceText("var b = a;");

        var edits = new[]
        {
            new TextEdit(new TextSpan(4, 1), "renamedA", file1),
            new TextEdit(new TextSpan(8, 1), "renamedA", file2)
        };

        var suggestion = new DiagnosticSuggestion("Rename 'a' to 'renamedA' across files", edits, SuggestionApplicability.MachineApplicable);

        Assert.Equal(2, suggestion.Edits.Count);
        Assert.Same(file1, suggestion.Edits[0].Source);
        Assert.Same(file2, suggestion.Edits[1].Source);
        Assert.Equal("renamedA", suggestion.Edits[0].NewText);
        Assert.Equal("renamedA", suggestion.Edits[1].NewText);
    }
}
