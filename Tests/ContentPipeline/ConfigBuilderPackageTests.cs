using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Foundation.Content.Authoring;
using Foundation.Serialization;
using NUnit.Framework;

namespace Foundation.Content.Editor.PackageTests
{
    public sealed class ConfigBuilderPackageTests
    {
        private string _outputDir;

        [SetUp]
        public void SetUp()
        {
            _outputDir = Path.Combine(Path.GetTempPath(), "foundation-content-tests-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_outputDir))
                Directory.Delete(_outputDir, true);
        }

        [Test]
        public void DuplicateOutputFiles_FailValidationBeforeAnyWrite()
        {
            var builder = new ConfigBuilder(
                new ConfigBuildSettings { OutputDirectory = _outputDir, Serializer = new TaggedSerializer() },
                new[] { new FakeExporter() }, Array.Empty<IConfigValidator>());
            var sources = new IConfigAuthoringSource[] { new FakeSource(), new FakeSource { Value = "other" } };

            var validation = builder.Validate(sources);
            var build = builder.Build(sources);

            Assert.That(validation.Success, Is.False);
            Assert.That(build.Success, Is.False);
            Assert.That(build.Errors, Has.Some.Matches<ConfigBuildError>(error => error.Kind == ConfigBuildErrorKind.DuplicateOutput));
            Assert.That(Directory.Exists(_outputDir), Is.False);
        }

        [TestCase(".")]
        [TestCase("bad:name.bytes")]
        [TestCase("CON.bytes")]
        public void Build_WithNonPortableFileNameFailsValidation(string fileName)
        {
            var builder = new ConfigBuilder(
                new ConfigBuildSettings { OutputDirectory = _outputDir },
                new[] { new FileNameExporter(fileName) },
                Array.Empty<IConfigValidator>());

            var report = builder.Build(new IConfigAuthoringSource[] { new FakeSource() });

            Assert.That(report.Success, Is.False);
            Assert.That(report.Errors, Has.Some.Matches<ConfigBuildError>(
                error => error.Kind == ConfigBuildErrorKind.InvalidFileName));
            Assert.That(Directory.Exists(_outputDir), Is.False);
        }

        [Test]
        public void Build_WhenSerializationFails_DoesNotReplaceAnyOutput()
        {
            Directory.CreateDirectory(_outputDir);
            var existingPath = Path.Combine(_outputDir, "first.bytes");
            File.WriteAllText(existingPath, "original");
            var builder = new ConfigBuilder(
                new ConfigBuildSettings { OutputDirectory = _outputDir, Serializer = new TaggedSerializer() },
                new[] { new SerializationFailureExporter() },
                Array.Empty<IConfigValidator>());

            Assert.Throws<InvalidOperationException>(() =>
                builder.Build(new IConfigAuthoringSource[] { new FakeSource() }));

            Assert.That(File.ReadAllText(existingPath), Is.EqualTo("original"));
        }

        [Test]
        public void Build_WhenPostProcessorFails_RestoresAllOutputFiles()
        {
            Directory.CreateDirectory(_outputDir);
            var firstPath = Path.Combine(_outputDir, "first.bytes");
            var secondPath = Path.Combine(_outputDir, "second.bytes");
            File.WriteAllText(firstPath, "original-first");
            File.WriteAllText(secondPath, "original-second");
            var builder = new ConfigBuilder(
                new ConfigBuildSettings { OutputDirectory = _outputDir, Serializer = new TaggedSerializer() },
                new[] { new TwoFileExporter() },
                Array.Empty<IConfigValidator>(),
                postProcessors: new[] { new ThrowingPostProcessor() });

            Assert.Throws<InvalidOperationException>(() =>
                builder.Build(new IConfigAuthoringSource[] { new FakeSource() }));

            Assert.That(File.ReadAllText(firstPath), Is.EqualTo("original-first"));
            Assert.That(File.ReadAllText(secondPath), Is.EqualTo("original-second"));
            Assert.That(Directory.GetDirectories(_outputDir, ".foundation-config-build-*"), Is.Empty);
        }

        private sealed class FakeSource : IConfigAuthoringSource
        {
            public string Value = "hello";
        }

        private sealed class FakeExporter : IConfigExporter
        {
            public Type SourceType => typeof(FakeSource);

            public IReadOnlyList<ConfigExportGroup> Export(IConfigAuthoringSource source)
            {
                var fake = (FakeSource)source;
                return new[]
                {
                    new ConfigExportGroup<string[]>(
                        "fake.bytes",
                        new[] { fake.Value },
                        new[] { fake.Value })
                };
            }
        }

        private sealed class FileNameExporter : IConfigExporter
        {
            private readonly string _fileName;

            public Type SourceType => typeof(FakeSource);

            public FileNameExporter(string fileName)
            {
                _fileName = fileName;
            }

            public IReadOnlyList<ConfigExportGroup> Export(IConfigAuthoringSource source)
                => new[] { new ConfigExportGroup<string[]>(_fileName, new[] { "value" }, new[] { "id" }) };
        }

        private sealed class SerializationFailureExporter : IConfigExporter
        {
            public Type SourceType => typeof(FakeSource);

            public IReadOnlyList<ConfigExportGroup> Export(IConfigAuthoringSource source)
                => new ConfigExportGroup[]
                {
                    new ConfigExportGroup<string[]>("first.bytes", new[] { "new" }, new[] { "first" }),
                    new ValidationOnlyGroup(),
                };
        }

        private sealed class TwoFileExporter : IConfigExporter
        {
            public Type SourceType => typeof(FakeSource);

            public IReadOnlyList<ConfigExportGroup> Export(IConfigAuthoringSource source)
                => new ConfigExportGroup[]
                {
                    new ConfigExportGroup<string[]>("first.bytes", new[] { "new-first" }, new[] { "first" }),
                    new ConfigExportGroup<string[]>("second.bytes", new[] { "new-second" }, new[] { "second" }),
                };
        }

        // A project's own binary format: the files hold whatever its serializer produced.
        private sealed class TaggedSerializer : ISerializer
        {
            public byte[] Serialize<T>(T value) => Encoding.UTF8.GetBytes("custom:" + string.Join("|", (string[])(object)value));

            public T Deserialize<T>(byte[] bytes) => throw new NotSupportedException();

            public object Deserialize(Type type, byte[] bytes) => throw new NotSupportedException();
        }

        [Test]
        public void Build_WritesTheFilesInTheFormatOfTheProjectsSerializer()
        {
            var builder = new ConfigBuilder(
                new ConfigBuildSettings { OutputDirectory = _outputDir, Serializer = new TaggedSerializer() },
                new[] { new FakeExporter() },
                Array.Empty<IConfigValidator>());

            var report = builder.Build(new IConfigAuthoringSource[] { new FakeSource() });

            Assert.That(report.Success, Is.True);
            Assert.That(File.ReadAllText(Path.Combine(_outputDir, "fake.bytes")), Is.EqualTo("custom:hello"));
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        public void WithoutAnExplicitSerializer_TheBuildNeedsExactlyOneInstalledAdapter(int installed, bool resolves)
        {
            var candidates = new Dictionary<string, Func<ISerializer>>();
            for (var index = 0; index < installed; index++)
                candidates["Adapter" + index] = () => new TaggedSerializer();

            var resolved = ConfigSerializers.TryResolve(null, candidates, out var serializer, out var problem);

            Assert.That(resolved, Is.EqualTo(resolves));
            Assert.That(serializer != null, Is.EqualTo(resolves));
            if (!resolves)
                Assert.That(problem, Does.Contain(nameof(ConfigBuildSettings.Serializer)), "the message says how to fix it");
        }

        [Test]
        public void AnExplicitSerializer_WinsOverTheInstalledAdapters()
        {
            var explicitSerializer = new TaggedSerializer();
            var candidates = new Dictionary<string, Func<ISerializer>>
            {
                ["A"] = () => new TaggedSerializer(),
                ["B"] = () => new TaggedSerializer(),
            };

            Assert.That(ConfigSerializers.TryResolve(explicitSerializer, candidates, out var serializer, out _), Is.True);
            Assert.That(serializer, Is.SameAs(explicitSerializer));
        }

        [Test]
        public void Build_WithNoExporterRegistered_FailsWithClearError()
        {
            var builder = new ConfigBuilder(
                new ConfigBuildSettings { OutputDirectory = _outputDir },
                Array.Empty<IConfigExporter>(),
                Array.Empty<IConfigValidator>());

            var report = builder.Build(new IConfigAuthoringSource[] { new FakeSource() });

            Assert.That(report.Success, Is.False);
            Assert.That(report.Errors, Has.Some.Matches<ConfigBuildError>(
                error => error.Kind == ConfigBuildErrorKind.NoExporter));
        }

        [Test]
        public void Build_WithDuplicateEntryId_FailsValidation()
        {
            var builder = new ConfigBuilder(
                new ConfigBuildSettings { OutputDirectory = _outputDir },
                new[] { new DuplicateIdExporter() },
                Array.Empty<IConfigValidator>());

            var report = builder.Build(new IConfigAuthoringSource[] { new FakeSource() });

            Assert.That(report.Success, Is.False);
            Assert.That(report.Errors, Has.Some.Matches<ConfigBuildError>(
                error => error.Kind == ConfigBuildErrorKind.DuplicateId));
        }

        [Test]
        public void Validate_DoesNotSerializeWriteOrRunPostProcessors()
        {
            var postProcessor = new CountingPostProcessor();
            var builder = new ConfigBuilder(
                new ConfigBuildSettings { OutputDirectory = _outputDir },
                new[] { new ValidationOnlyExporter() },
                Array.Empty<IConfigValidator>(),
                postProcessors: new[] { postProcessor });

            var report = builder.Validate(new IConfigAuthoringSource[] { new FakeSource() });

            Assert.That(report.Success, Is.True);
            Assert.That(report.WrittenFiles, Is.Empty);
            Assert.That(Directory.Exists(_outputDir), Is.False);
            Assert.That(postProcessor.Calls, Is.Zero);
        }

        [Test]
        public void Validate_UsesSameDuplicateIdRulesAsBuildWithoutWriting()
        {
            var builder = new ConfigBuilder(
                new ConfigBuildSettings { OutputDirectory = _outputDir },
                new[] { new DuplicateIdExporter() },
                Array.Empty<IConfigValidator>());
            var report = builder.Validate(new IConfigAuthoringSource[] { new FakeSource() });
            Assert.That(report.Success, Is.False);
            Assert.That(report.Errors.Single().Kind, Is.EqualTo(ConfigBuildErrorKind.DuplicateId));
            Assert.That(Directory.Exists(_outputDir), Is.False);
        }

        private sealed class CountingPostProcessor : IConfigBuildPostProcessor
        {
            public int Calls { get; private set; }

            public void OnBuildSucceeded(IReadOnlyList<IConfigAuthoringSource> sources)
            {
                Calls++;
            }
        }

        private sealed class ThrowingPostProcessor : IConfigBuildPostProcessor
        {
            public void OnBuildSucceeded(IReadOnlyList<IConfigAuthoringSource> sources)
                => throw new InvalidOperationException("Injected post-processor failure.");
        }

        private sealed class ValidationOnlyExporter : IConfigExporter
        {
            public Type SourceType => typeof(FakeSource);

            public IReadOnlyList<ConfigExportGroup> Export(IConfigAuthoringSource source) => new[] { new ValidationOnlyGroup() };
        }

        private sealed class ValidationOnlyGroup : ConfigExportGroup
        {
            public ValidationOnlyGroup() : base("validate.bytes") { }
            public override IReadOnlyList<string> EntryIds => new[] { "valid" };
            public override bool ContainsNullEntry() => false;
            public override byte[] Serialize(ISerializer serializer) => throw new InvalidOperationException("Validation must not serialize output.");
        }

        private sealed class DuplicateIdExporter : IConfigExporter
        {
            public Type SourceType => typeof(FakeSource);

            public IReadOnlyList<ConfigExportGroup> Export(IConfigAuthoringSource source)
            {
                return new[]
                {
                    new ConfigExportGroup<string[]>("dup.bytes", new[] { "a", "a" }, new[] { "same-id", "same-id" })
                };
            }
        }
    }
}
