﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.ComponentModel.Composition
Imports System.Diagnostics.CodeAnalysis
Imports System.Threading
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Editor
Imports Microsoft.CodeAnalysis.Editor.Shared.Extensions
Imports Microsoft.CodeAnalysis.Editor.Shared.Utilities
Imports Microsoft.CodeAnalysis.Options
Imports Microsoft.CodeAnalysis.Shared.Extensions
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Extensions
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Microsoft.VisualStudio.Editor
Imports Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion
Imports Microsoft.VisualStudio.LanguageServices.Implementation.Snippets
Imports Microsoft.VisualStudio.Shell
Imports Microsoft.VisualStudio.Text
Imports Microsoft.VisualStudio.Text.Editor
Imports Microsoft.VisualStudio.TextManager.Interop
Imports Microsoft.VisualStudio.Utilities

Namespace Microsoft.VisualStudio.LanguageServices.VisualBasic.Snippets
    <Export(GetType(Commanding.ICommandHandler))>
    <ContentType(ContentTypeNames.VisualBasicContentType)>
    <Name("VB Snippets")>
    <Order(After:=PredefinedCompletionNames.CompletionCommandHandler)>
    <Order(After:=PredefinedCommandHandlerNames.SignatureHelpAfterCompletion)>
    <Order(Before:=PredefinedCommandHandlerNames.AutomaticLineEnder)>
    Friend NotInheritable Class SnippetCommandHandler
        Inherits AbstractSnippetCommandHandler

        Private ReadOnly _editorAdaptersFactoryService As IVsEditorAdaptersFactoryService
        Private ReadOnly _expansionClientFactory As Lazy(Of ISnippetExpansionClientFactory)

        <ImportingConstructor>
        <SuppressMessage("RoslynDiagnosticsReliability", "RS0033:Importing constructor should be [Obsolete]", Justification:="Used in test code: https://github.com/dotnet/roslyn/issues/42814")>
        Public Sub New(
            threadingContext As IThreadingContext,
            editorAdaptersFactoryService As IVsEditorAdaptersFactoryService,
            serviceProvider As SVsServiceProvider,
            editorOptionsService As EditorOptionsService,
            workspace As Lazy(Of VisualStudioWorkspace))
            MyBase.New(threadingContext, editorOptionsService, serviceProvider)
            _editorAdaptersFactoryService = editorAdaptersFactoryService
            _expansionClientFactory = New Lazy(Of ISnippetExpansionClientFactory)(
                Function()
                    Return workspace.Value.Services _
                        .GetLanguageServices(LanguageNames.VisualBasic) _
                        .GetRequiredService(Of ISnippetExpansionClientFactory)()
                End Function)
        End Sub

        Protected Overrides Function IsSnippetExpansionContext(document As Document, startPosition As Integer, cancellationToken As CancellationToken) As Boolean
            Dim syntaxTree = document.GetSyntaxTreeSynchronously(CancellationToken.None)

            Return Not syntaxTree.IsEntirelyWithinStringOrCharOrNumericLiteral(startPosition, cancellationToken) AndAlso
                Not syntaxTree.IsEntirelyWithinComment(startPosition, cancellationToken) AndAlso
                Not syntaxTree.FindTokenOnRightOfPosition(startPosition, cancellationToken).HasAncestor(Of XmlElementSyntax)()
        End Function

        Protected Overrides Function GetSnippetExpansionClientFactory() As ISnippetExpansionClientFactory
            Return _expansionClientFactory.Value
        End Function

        Protected Overrides Function TryInvokeInsertionUI(textView As ITextView, subjectBuffer As ITextBuffer, Optional surroundWith As Boolean = False) As Boolean
            Debug.Assert(Not surroundWith)

            Dim expansionManager As IVsExpansionManager = Nothing
            If Not TryGetExpansionManager(expansionManager) Then
                Return False
            End If

            expansionManager.InvokeInsertionUI(
                _editorAdaptersFactoryService.GetViewAdapter(textView),
                GetSnippetExpansionClientFactory().GetSnippetExpansionClient(textView, subjectBuffer),
                Guids.VisualBasicDebuggerLanguageId,
                bstrTypes:=Nothing,
                iCountTypes:=0,
                fIncludeNULLType:=1,
                bstrKinds:=Nothing,
                iCountKinds:=0,
                fIncludeNULLKind:=1,
                bstrPrefixText:=BasicVSResources.Insert_Snippet,
                bstrCompletionChar:=">"c)

            Return True

        End Function

        Protected Overrides Function TryInvokeSnippetPickerOnQuestionMark(textView As ITextView, subjectBuffer As ITextBuffer) As Boolean
            Dim text = subjectBuffer.AsTextContainer().CurrentText
            Dim caretPosition = textView.GetCaretPoint(subjectBuffer).Value.Position

            If (caretPosition > 1 AndAlso text(caretPosition - 1) = "?"c AndAlso Microsoft.CodeAnalysis.VisualBasic.SyntaxFacts.IsWhitespace(text(caretPosition - 2))) OrElse
                (caretPosition = 1 AndAlso text(0) = "?"c) Then

                DeleteQuestionMark(subjectBuffer, caretPosition)
                TryInvokeInsertionUI(textView, subjectBuffer)
                Return True
            End If

            Return False
        End Function

        Private Shared Sub DeleteQuestionMark(subjectBuffer As ITextBuffer, caretPosition As Integer)
            Dim currentSnapshot = subjectBuffer.CurrentSnapshot
            Dim document = currentSnapshot.GetOpenDocumentInCurrentContextWithChanges()
            If document IsNot Nothing Then
                subjectBuffer.ApplyChange(New TextChange(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(caretPosition - 1, caretPosition), String.Empty))
            End If
        End Sub
    End Class
End Namespace
