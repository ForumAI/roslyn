﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.SymbolSearch
{
    /// <summary>
    /// Base type for services that we want to delay running until certain criteria is met.
    /// For example, we don't want to run the <see cref="VisualStudioSymbolSearchService"/> core codepath
    /// if the user has not enabled the features that need it.  That helps us avoid loading
    /// dlls unnecessarily and bloating the VS memory space.
    /// </summary>
    internal abstract class AbstractDelayStartedService : ForegroundThreadAffinitizedObject
    {
        private readonly IGlobalOptionService _globalOptions;
        private readonly ConcurrentSet<string> _registeredLanguages = new();

        /// <summary>
        /// Option that controls if this service is enabled or not (regardless of language).
        /// </summary>
        private readonly Option2<bool> _featureEnabledOption;

        /// <summary>
        /// Options that control if this service is enabled or not for a particular language.
        /// </summary>
        private readonly ImmutableArray<PerLanguageOption2<bool>> _perLanguageOptions;

        protected CancellationToken DisposalToken => ThreadingContext.DisposalToken;

        private readonly AsyncBatchingWorkQueue _optionChangedWorkQueue;

        private bool _enabled = false;

        protected AbstractDelayStartedService(
            IGlobalOptionService globalOptions,
            IAsynchronousOperationListenerProvider listenerProvider,
            IThreadingContext threadingContext,
            Option2<bool> featureEnabledOption,
            ImmutableArray<PerLanguageOption2<bool>> perLanguageOptions)
            : base(threadingContext)
        {
            _globalOptions = globalOptions;
            _featureEnabledOption = featureEnabledOption;
            _perLanguageOptions = perLanguageOptions;
            _optionChangedWorkQueue = new AsyncBatchingWorkQueue(
                TimeSpan.FromMilliseconds(500),
                ProcessOptionChangesAsync,
                listenerProvider.GetListener(FeatureAttribute.Workspace),
                this.DisposalToken);

            _globalOptions.OptionChanged += OnOptionChanged;
        }

        protected abstract Task EnableServiceAsync(CancellationToken cancellationToken);

        public void RegisterLanguage(string language)
        {
            _registeredLanguages.Add(language);
            _optionChangedWorkQueue.AddWork();
        }

        private void OnOptionChanged(object sender, OptionChangedEventArgs e)
            => _optionChangedWorkQueue.AddWork();

        private async ValueTask ProcessOptionChangesAsync(CancellationToken arg)
        {
            // If we're already enabled, nothing to do.
            if (_enabled)
                return;

            // If feature is totally disabled.  Do nothing.
            if (!_globalOptions.GetOption(_featureEnabledOption))
                return;

            // If feature isn't enabled for any registered language, do nothing.
            if (!_registeredLanguages.Any(lang => !_perLanguageOptions.Any(option => _globalOptions.GetOption(option, lang))))
                return;

            // We were enabled for some language.  Kick off the work for this service now.
            _enabled = true;
            _globalOptions.OptionChanged -= OnOptionChanged;

            await this.EnableServiceAsync(this.DisposalToken).ConfigureAwait(false);
        }
    }
}
