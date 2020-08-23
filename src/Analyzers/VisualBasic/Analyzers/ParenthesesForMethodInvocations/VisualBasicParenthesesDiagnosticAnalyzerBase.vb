﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports Microsoft.CodeAnalysis.CodeStyle
Imports Microsoft.CodeAnalysis.Diagnostics
Imports Microsoft.CodeAnalysis.VisualBasic.CodeStyle
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.ParenthesesForMethodInvocations
    Friend MustInherit Class VisualBasicParenthesesDiagnosticAnalyzerBase
        Inherits AbstractBuiltInCodeStyleDiagnosticAnalyzer

        Protected Sub New(diagnosticId As String, title As LocalizableString, message As LocalizableString)
            MyBase.New(
                diagnosticId, VisualBasicCodeStyleOptions.IncludeParenthesesForMethodInvocations,
                LanguageNames.VisualBasic, title, message)
        End Sub

        Public Overrides Function GetAnalyzerCategory() As DiagnosticAnalyzerCategory
            Return DiagnosticAnalyzerCategory.SemanticSpanAnalysis
        End Function

        Protected Overrides Sub InitializeWorker(context As AnalysisContext)
            context.RegisterOperationAction(AddressOf AnalyzeInvocation, OperationKind.Invocation)
        End Sub

        Private Sub AnalyzeInvocation(context As OperationAnalysisContext)
            Dim node = context.Operation.Syntax
            Dim invocationExpression = DirectCast(node, InvocationExpressionSyntax)
            Dim includeParentheses = context.Options.GetOption(VisualBasicCodeStyleOptions.IncludeParenthesesForMethodInvocations, node.SyntaxTree, context.CancellationToken).Value
            Dim descriptor = CreateDescriptorWithId(DescriptorId, _localizableTitle, _localizableMessageFormat)

            If IsViolatingPreference(includeParentheses, invocationExpression) Then
                context.ReportDiagnostic(DiagnosticHelper.Create(descriptor, node.GetLocation(), ReportDiagnostic.Hidden, ' Will the hidden severity here overwrite any user preference?
                    additionalLocations:={node.GetLocation()}, properties:=Nothing))
            End If
        End Sub

        Private Shared Function IsViolatingPreference(includeParenthesesPreference As Boolean, invocation As InvocationExpressionSyntax) As Boolean
            If includeParenthesesPreference Then
                ' User wants to include parentheses. Return True indicating violation if parentheses doesn't exist.
                Return invocation.ArgumentList Is Nothing
            End If

            ' User doesn't want to include parentheses. Return True indicating violation of parentheses exist for a method taking 0 arguments.
            Return invocation.ArgumentList IsNot Nothing AndAlso Not invocation.ArgumentList.Arguments.Any()
        End Function
    End Class
End Namespace
