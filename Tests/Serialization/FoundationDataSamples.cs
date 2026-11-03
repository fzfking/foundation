using System;
using System.Reflection;
using Foundation.Localization;
using Foundation.Tutorial;
using Foundation.Tutorial.Flow;
using NUnit.Framework;

namespace Foundation.Tests.Serialization
{
    // Adapters teach their serializer the Foundation data types by hand, so a field added to a type but forgotten
    // in a formatter would be silently dropped; every adapter round-trips these samples (every public field set to
    // a non-default value) to catch that.
    public static class FoundationDataSamples
    {
        public static readonly Type[] DataTypes =
        {
            typeof(TutorialConfig),
            typeof(TutorialChapterSnapshot),
            typeof(TutorialSnapshot),
            typeof(TutorialScriptConfig),
            typeof(TutorialStepScript),
            typeof(TutorialNode),
            typeof(TutorialNamedArgument),
            typeof(LocalizationTableData),
        };

        // Nested types are filled two levels deep: enough to exercise arrays of objects and the recursive
        // TutorialNode children, without an endless tree.
        private const int MaxDepth = 2;

        public static object Create(Type type) => Fill(type, 0);

        public static int PublicFieldCount(Type type) => type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length;

        public static void AssertSame(Type type, object expected, object actual, string path = null)
        {
            path ??= type.Name;
            if (expected == null || actual == null)
            {
                Assert.That(actual, Is.EqualTo(expected), path);
                return;
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var expectedValue = field.GetValue(expected);
                var actualValue = field.GetValue(actual);
                var fieldPath = path + "." + field.Name;

                if (field.FieldType.IsArray && field.FieldType.GetElementType() != typeof(string))
                    AssertSameArray(field.FieldType.GetElementType(), (Array)expectedValue, (Array)actualValue, fieldPath);
                else
                    Assert.That(actualValue, Is.EqualTo(expectedValue), fieldPath);
            }
        }

        private static void AssertSameArray(Type elementType, Array expected, Array actual, string path)
        {
            if (expected == null || actual == null)
            {
                Assert.That(actual, Is.EqualTo(expected), path);
                return;
            }

            Assert.That(actual.Length, Is.EqualTo(expected.Length), path + ".Length");
            for (var index = 0; index < expected.Length; index++)
                AssertSame(elementType, expected.GetValue(index), actual.GetValue(index), path + "[" + index + "]");
        }

        private static object Fill(Type type, int depth)
        {
            var instance = Activator.CreateInstance(type);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                field.SetValue(instance, FillValue(field.FieldType, field.Name, depth));
            return instance;
        }

        private static object FillValue(Type type, string name, int depth)
        {
            if (type == typeof(string))
                return name + "-" + depth;
            if (type == typeof(int))
                return 7 + depth;
            if (type == typeof(bool))
                return true;
            if (type.IsEnum)
                return Enum.GetValues(type).GetValue(1);

            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                if (elementType != typeof(string) && depth >= MaxDepth)
                    return null;

                var array = Array.CreateInstance(elementType, 2);
                for (var index = 0; index < array.Length; index++)
                {
                    array.SetValue(
                        elementType == typeof(string) ? name + "-" + index : Fill(elementType, depth + 1),
                        index);
                }

                return array;
            }

            throw new NotSupportedException($"{type.Name} field '{name}' needs a sample value.");
        }
    }
}
