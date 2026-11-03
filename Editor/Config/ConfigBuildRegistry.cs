using System;
using System.Collections.Generic;

namespace Foundation.Content.Editor
{
    public sealed class ConfigBuildRegistry
    {
        private readonly List<IConfigExporter> _exporters = new();
        private readonly List<IConfigValidator> _validators = new();
        private readonly List<IConfigSourceProvider> _sourceProviders = new();
        private readonly List<IConfigBuildPostProcessor> _postProcessors = new();
        private readonly HashSet<Type> _exporterSourceTypes = new();
        private readonly HashSet<Type> _validatorTypes = new();
        private readonly HashSet<Type> _sourceProviderTypes = new();
        private readonly HashSet<Type> _postProcessorTypes = new();

        public IReadOnlyList<IConfigExporter> Exporters => _exporters;

        public IReadOnlyList<IConfigValidator> Validators => _validators;

        public IReadOnlyList<IConfigSourceProvider> SourceProviders => _sourceProviders;

        public IReadOnlyList<IConfigBuildPostProcessor> PostProcessors => _postProcessors;

        public void RegisterSourceProvider(IConfigSourceProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            var type = provider.GetType();
            if (!_sourceProviderTypes.Add(type))
                throw new InvalidOperationException(
                    $"A source provider of type '{type.Name}' is already registered.");

            _sourceProviders.Add(provider);
        }

        public void RegisterPostProcessor(IConfigBuildPostProcessor postProcessor)
        {
            if (postProcessor == null)
                throw new ArgumentNullException(nameof(postProcessor));

            var type = postProcessor.GetType();
            if (!_postProcessorTypes.Add(type))
                throw new InvalidOperationException(
                    $"A post processor of type '{type.Name}' is already registered.");

            _postProcessors.Add(postProcessor);
        }

        public void RegisterExporter(IConfigExporter exporter)
        {
            if (exporter == null)
                throw new ArgumentNullException(nameof(exporter));

            if (!_exporterSourceTypes.Add(exporter.SourceType))
                throw new InvalidOperationException(
                    $"An exporter for source type '{exporter.SourceType.Name}' is already registered.");

            _exporters.Add(exporter);
        }

        public void RegisterValidator(IConfigValidator validator)
        {
            if (validator == null)
                throw new ArgumentNullException(nameof(validator));

            var type = validator.GetType();
            if (!_validatorTypes.Add(type))
                throw new InvalidOperationException(
                    $"A validator of type '{type.Name}' is already registered.");

            _validators.Add(validator);
        }
    }
}
