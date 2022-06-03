﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.ReplaceConditionalWithStatements;

internal class AbstractReplaceConditionalWithStatementsCodeRefactoringProvider<
    TExpressionSyntax,
    TConditionalExpressionSyntax,
    TStatementSyntax,
    TReturnStatementSyntax,
    TExpressionStatementSyntax,
    TLocalDeclarationStatementSyntax,
    TVariableDeclaratorSyntax>
    : CodeRefactoringProvider
    where TExpressionSyntax : SyntaxNode
    where TConditionalExpressionSyntax : TExpressionSyntax
    where TStatementSyntax : SyntaxNode
    where TReturnStatementSyntax : TStatementSyntax
    where TExpressionStatementSyntax : TStatementSyntax
    where TLocalDeclarationStatementSyntax : TStatementSyntax
    where TVariableDeclaratorSyntax : SyntaxNode
{
    public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
    {
        var conditionalExpression = await context.TryGetRelevantNodeAsync<TConditionalExpressionSyntax>().ConfigureAwait(false);
        if (conditionalExpression is null)
            return;

        var (document, _, cancellationToken) = context;

        // supports:
        // 1. a = x ? y : z;
        // 2. var a = x ? y : z;
        // 3. return x ? y : z;

        var syntaxFacts = document.GetRequiredLanguageService<ISyntaxFactsService>();
        var topExpression = (TExpressionSyntax)syntaxFacts.WalkUpParentheses(conditionalExpression);

        if (topExpression.GetAncestor<TStatementSyntax>() is { } assignmentStatement &&
            syntaxFacts.IsSimpleAssignmentStatement(assignmentStatement))
        {
            syntaxFacts.GetPartsOfAssignmentStatement(assignmentStatement, out _, out var right);
            if (right == topExpression)
            {
                context.RegisterRefactoring(CodeAction.Create(
                    FeaturesResources.Replace_conditional_expression_with_statements,
                    c => ReplaceConditionalExpressionInAssignmentStatement(document, conditionalExpression, topExpression, assignmentStatement, c)));
                return;
            }
        }

        if (topExpression.Parent is TVariableDeclaratorSyntax variableDeclarator &&
            conditionalExpression.GetAncestor<TLocalDeclarationStatementSyntax>() is { } localDeclarationStatement &&
            syntaxFacts.IsDeclaratorOfLocalDeclarationStatement(topExpression.Parent, localDeclarationStatement))
        {
            context.RegisterRefactoring(CodeAction.Create(
                FeaturesResources.Replace_conditional_expression_with_statements,
                c => ReplaceConditionalExpressionInLocalDeclarationStatement(
                    document, conditionalExpression, topExpression, variableDeclarator, localDeclarationStatement, c)));
            return;
        }

        if (topExpression.Parent is TReturnStatementSyntax returnStatement)
        {
            context.RegisterRefactoring(CodeAction.Create(
                FeaturesResources.Replace_conditional_expression_with_statements,
                c => ReplaceConditionalExpressionInReturnStatement(document, conditionalExpression, topExpression, returnStatement, c)));
            return;
        }
    }

    private Task<Document> ReplaceConditionalExpressionInAssignmentStatement(
        Document document,
        TConditionalExpressionSyntax conditionalExpression,
        TExpressionSyntax topExpression,
        TStatementSyntax assignmentStatement,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    private Task<Document> ReplaceConditionalExpressionInLocalDeclarationStatement(
        Document document,
        TConditionalExpressionSyntax conditionalExpression,
        TExpressionSyntax topExpression,
        TVariableDeclaratorSyntax variableDeclarator,
        TLocalDeclarationStatementSyntax localDeclarationStatement,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    private async Task<Document> ReplaceConditionalExpressionInReturnStatement(
        Document document,
        TConditionalExpressionSyntax conditionalExpression,
        TExpressionSyntax topExpression,
        TReturnStatementSyntax returnStatement,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var root = await document.GetRequiredSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        var generator = SyntaxGenerator.GetGenerator(document);
        var syntaxFacts = document.GetRequiredLanguageService<ISyntaxFactsService>();

        // When we have `return x ? y : z`, then the type of 'y' and 'z' can influence each other.
        // If we convert this into:
        //
        // if (x)
        //   return y;
        // else
        //   return z;
        //
        // Then we need to preserve that meaning so that 'y' and 'z' have the same type/value, even after the
        // transformation.
        var conditionalType = semanticModel.GetTypeInfo(conditionalExpression, cancellationToken).Type;

        syntaxFacts.GetPartsOfConditionalExpression(conditionalExpression, out var condition, out var whenTrue, out var whenFalse);
        var ifStatement = generator.IfStatement(
            condition,
            new[] { generator.ReturnStatement(TryCast(generator, whenTrue, conditionalType)) },
            new[] { generator.ReturnStatement(TryCast(generator, whenFalse, conditionalType)) }).WithTriviaFrom(returnStatement);

        var editor = new SyntaxEditor(root, generator);
        editor.ReplaceNode(
            returnStatement,
            ifStatement);
    }

    private static SyntaxNode TryCast(SyntaxGenerator generator, SyntaxNode whenTrue, ITypeSymbol? conditionalType)
        => conditionalType is null or IErrorTypeSymbol
            ? whenTrue
            : generator.CastExpression(conditionalType, whenTrue);
}
