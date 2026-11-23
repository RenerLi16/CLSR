using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CLSR.Offline.Tests
{
    public sealed class TestFailure : Exception
    {
        public TestFailure(string message) : base(message) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    public static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new TestFailure(message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new TestFailure($"{message}: expected <{expected}>, got <{actual}>");
        }

        public static void Null(object value, string message) => True(value == null, message + ": expected null, got " + value);
        public static void NotNull(object value, string message) => True(value != null, message + ": expected a value");
    }

    /// <summary>Dependency-free test runner: every [Test] static method in this assembly.</summary>
    public static class TestRunner
    {
        public static string RepoRoot { get; private set; }

        public static int Run(string root)
        {
            RepoRoot = root;
            var tests = typeof(TestRunner).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name).ToList();
            int failed = 0;
            var watch = Stopwatch.StartNew();
            foreach (MethodInfo test in tests)
            {
                string name = test.DeclaringType.Name + "." + test.Name;
                try
                {
                    test.Invoke(null, null);
                    Console.WriteLine("PASS  " + name);
                }
                catch (TargetInvocationException exception)
                {
                    failed++;
                    Console.WriteLine("FAIL  " + name + "\n      " + exception.InnerException?.Message);
                    if (!(exception.InnerException is TestFailure))
                        Console.WriteLine(exception.InnerException?.StackTrace);
                }
            }
            Console.WriteLine($"\n{tests.Count - failed} passed, {failed} failed ({tests.Count} tests, {watch.Elapsed.TotalSeconds:0.0}s)");
            return failed == 0 ? 0 : 1;
        }

        public static string Source(string relative) =>
            File.ReadAllText(Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

        /// <summary>Canonical text of every public field, for equality checks between views.</summary>
        public static string Dump(object value)
        {
            var text = new StringBuilder();
            DumpInto(value, text);
            return text.ToString();
        }

        private static void DumpInto(object value, StringBuilder text)
        {
            if (value == null) { text.Append("null"); return; }
            Type type = value.GetType();
            if (value is string || type.IsPrimitive || value is decimal || type.IsEnum)
            {
                text.Append(value);
                return;
            }
            if (value is System.Collections.IEnumerable list)
            {
                text.Append('[');
                foreach (object item in list) { DumpInto(item, text); text.Append(','); }
                text.Append(']');
                return;
            }
            if (Nullable.GetUnderlyingType(type) != null) { text.Append(value); return; }
            text.Append('{');
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name))
            {
                text.Append(field.Name).Append('=');
                DumpInto(field.GetValue(value), text);
                text.Append(';');
            }
            text.Append('}');
        }
    }
}
