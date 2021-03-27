﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis
{
    internal sealed class SourceGeneratedDocumentState : DocumentState
    {
        public string HintName { get; }
        public ISourceGenerator SourceGenerator { get; }

        public static SourceGeneratedDocumentState Create(
            string hintName,
            SourceText generatedSourceText,
            SyntaxTree generatedSyntaxTree,
            DocumentId documentId,
            ISourceGenerator sourceGenerator,
            HostLanguageServices languageServices,
            SolutionServices solutionServices,
            CancellationToken cancellationToken)
        {
            var options = generatedSyntaxTree.Options;
            var filePath = generatedSyntaxTree.FilePath;

            var textAndVersion = TextAndVersion.Create(generatedSourceText, VersionStamp.Create());
            ValueSource<TextAndVersion> textSource = new ConstantValueSource<TextAndVersion>(textAndVersion);

            var root = generatedSyntaxTree.GetRoot(cancellationToken);
            Contract.ThrowIfNull(languageServices.SyntaxTreeFactory, "We should not have a generated syntax tree for a language that doesn't support trees.");

            if (languageServices.SyntaxTreeFactory.CanCreateRecoverableTree(root))
            {
                // We will only create recoverable text if we can create a recoverable tree; if we created a
                // recoverable text but not a new tree, it would mean tree.GetText() could still potentially return
                // the non-recoverable text, but asking the document directly for it's text would give a recoverable
                // text with a different object identity.
                textSource = CreateRecoverableText(textAndVersion, solutionServices);

                generatedSyntaxTree = languageServices.SyntaxTreeFactory.CreateRecoverableTree(
                    documentId.ProjectId,
                    filePath: generatedSyntaxTree.FilePath,
                    options,
                    textSource,
                    generatedSourceText.Encoding,
                    root);
            }

            var treeAndVersion = TreeAndVersion.Create(generatedSyntaxTree, textAndVersion.Version);

            return new SourceGeneratedDocumentState(
                languageServices,
                solutionServices,
                documentServiceProvider: null,
                new DocumentInfo.DocumentAttributes(
                    documentId,
                    name: hintName,
                    folders: SpecializedCollections.EmptyReadOnlyList<string>(),
                    options.Kind,
                    filePath: filePath,
                    isGenerated: true,
                    designTimeOnly: false),
                options,
                sourceText: null, // don't strongly hold the text
                textSource,
                treeAndVersion,
                sourceGenerator,
                hintName);
        }

        private SourceGeneratedDocumentState(
            HostLanguageServices languageServices,
            SolutionServices solutionServices,
            IDocumentServiceProvider? documentServiceProvider,
            DocumentInfo.DocumentAttributes attributes,
            ParseOptions? options,
            SourceText? sourceText,
            ValueSource<TextAndVersion> textSource,
            TreeAndVersion treeAndVersion,
            ISourceGenerator sourceGenerator,
            string hintName)
            : base(languageServices, solutionServices, documentServiceProvider, attributes, options, sourceText, textSource, new ConstantValueSource<TreeAndVersion>(treeAndVersion))
        {
            SourceGenerator = sourceGenerator;
            HintName = hintName;
        }

        /// <summary>
        /// Equivalent to calling <see cref="DocumentState.GetSyntaxTree(CancellationToken)"/>, but avoids the implicit requirement of a cancellation token since
        /// we can always get the tree right away.
        /// </summary>
        /// <remarks>
        /// We won't expose this through <see cref="SourceGeneratedDocument"/> in case the implementation changes.
        /// </remarks>
        public SyntaxTree SyntaxTree
        {
            get
            {
                // We are always holding onto the SyntaxTree object with a ConstantValueSource, so we can just fetch this
                // without any extra work. Unlike normal documents where we don't even have a tree object until we've fetched text and
                // the tree the first time, the generated case we start with a tree and text and then wrap it.
                return GetSyntaxTree(CancellationToken.None);
            }
        }

        protected override TextDocumentState UpdateText(ValueSource<TextAndVersion> newTextSource, PreservationMode mode, bool incremental)
        {
            throw new NotSupportedException(WorkspacesResources.The_contents_of_a_SourceGeneratedDocument_may_not_be_changed);
        }

        public SourceGeneratedDocumentState WithUpdatedGeneratedContent(SourceText sourceText, SyntaxTree lazySyntaxTree, ParseOptions parseOptions, CancellationToken cancellationToken)
        {
            if (TryGetText(out var existingText) &&
                Checksum.From(existingText.GetChecksum()) == Checksum.From(sourceText.GetChecksum()) &&
                SyntaxTree.Options.Equals(parseOptions))
            {
                // We can reuse this instance directly
                return this;
            }

            return SourceGeneratedDocumentState.Create(
                this.HintName,
                sourceText,
                lazySyntaxTree,
                this.Id,
                this.SourceGenerator,
                this.LanguageServices,
                this.solutionServices,
                cancellationToken);
        }
    }
}
