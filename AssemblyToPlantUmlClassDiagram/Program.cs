using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace AssemblyToPlantUmlClassDiagram
{
    static class Program
    {
        enum TypeClassifier
        {
            Interface,
            AbstractClass,
            Object,
            Enum,
            Attribute,
            StaticClass,
            Primitive,
            ValueType,
            Delegate,
        }

        static void Main(string[] args)
        {
            AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("System.Collections.Immutable, PublicKeyToken=b03f5f7f11d50a3a"));
            AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("System.Numerics, PublicKeyToken=b77a5c561934e089"));
            AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("System, PublicKeyToken=b77a5c561934e089"));
            AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("System.Memory, PublicKeyToken=cc7b13ffcd2ddd51"));
            AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("System.ObjectModel, PublicKeyToken=b03f5f7f11d50a3a"));

            Console.WriteLine("@startuml");
            Console.WriteLine("hide empty members");
            Console.WriteLine("left to right direction");

            var assemblies = AssemblyLoadContext.All.SelectMany(ctx => ctx.Assemblies).Where(asm => asm != Assembly.GetExecutingAssembly());
            foreach (var asm in assemblies)
            {
                //Console.WriteLine($"package {asm.FullName.AsSpan().Chop(',').ToString()} {{");
                foreach (var nsg in asm.GetExportedTypes().Where(TypeFilter).GroupBy(t => t.Namespace).OrderBy(g => g.Key))
                {
                    if (string.IsNullOrEmpty(nsg.Key) || !NamespaceFilter(nsg.Key))
                        continue;

                    Console.WriteLine($"namespace {nsg.Key} {{");

                    var typeClasses = nsg.OrderBy(t => t.Name).GroupBy(t => GetTypeClassifier(t), EqualityComparer<TypeClassifier>.Default);

                    foreach (var typeInfo in typeClasses.OrderBy(a => a.Key))
                    {
                        if (typeInfo.Key == TypeClassifier.Interface)
                        {
                            //Console.WriteLine("together {");
                        }

                        foreach (var t in typeInfo)
                        {
                            WriteTypeInfo(typeInfo.Key, t);
                        }

                        if (typeInfo.Key == TypeClassifier.Interface)
                        {
                            //Console.WriteLine("}");
                        }
                        Console.WriteLine();
                    }
                    Console.WriteLine("}"); // namespace
                }
                //Console.WriteLine($"}}");
            }

            //Console.WriteLine("hide System.Collections.Generic.IEnumerable<out T>");
            Console.WriteLine("hide System.IEquatable<T>");
            Console.WriteLine("hide System.IDisposable");
            Console.WriteLine("hide System.ICloneable");
            Console.WriteLine("hide System.IComparable");
            Console.WriteLine("hide System.Runtime.Serialization.ISerializable");
            Console.WriteLine("hide System.Runtime.Serialization.IDeserializationCallback");
            Console.WriteLine("@enduml");
        }
        static ReadOnlySpan<char> Chop(this ReadOnlySpan<char> str, char c)
        {
            var i = str.IndexOf(c);
            return i != -1 ? str.Slice(0, i) : str;
        }
        static bool TypeFilter(Type t) =>
            t != null &&
            !t.IsPrimitive &&
            t.GetCustomAttribute(typeof(ObsoleteAttribute)) == null &&
            t != typeof(object) && t != typeof(ValueType) && t != typeof(Enum) && t != typeof(void) &&
            !t.IsNested &&
            !typeof(Attribute).IsAssignableFrom(t) &&
            !typeof(Exception).IsAssignableFrom(t) &&
            !typeof(Delegate).IsAssignableFrom(t);

        static bool NamespaceFilter(string ns) =>
            !ns.StartsWith("Internal") &&
            !ns.StartsWith("System.Runtime.Intrinsics") &&
            !ns.StartsWith("System.Reflection.") &&
            !ns.StartsWith("System.Runtime.InteropServices.ComTypes") &&
            !ns.StartsWith("System.Runtime.InteropServices.WindowsRuntime") &&
            !ns.StartsWith("System.Security") &&
            ns != "System.Collections";

        static TypeClassifier GetTypeClassifier(Type t) =>
            t.IsInterface ? TypeClassifier.Interface :
            t.IsEnum ? TypeClassifier.Enum :
            t.IsAbstract && !t.IsSealed ? TypeClassifier.AbstractClass :
            typeof(Attribute).IsAssignableFrom(t) ? TypeClassifier.Attribute :
            t.IsAbstract && t.IsSealed ? TypeClassifier.StaticClass :
            t.IsValueType && !t.IsEnum ? TypeClassifier.ValueType :
            TypeClassifier.Object;

        static void WriteTypeInfo(TypeClassifier tc, Type t)
        {
            var classifier =
                tc == TypeClassifier.Interface ? "interface" :
                tc == TypeClassifier.Enum ? "enum" :
                tc == TypeClassifier.AbstractClass ? "abstract class" :
                tc == TypeClassifier.Attribute ? "annotation" :
                "class";

            var isDisposable = typeof(IDisposable).IsAssignableFrom(t);
            var isAsyncDisposable = typeof(IAsyncDisposable).IsAssignableFrom(t);
            var isEnumerable = typeof(System.Collections.IEnumerable).IsAssignableFrom(t) || !t.IsByRefLike && typeof(IEnumerable<>).MakeGenericType(t).IsAssignableFrom(t);
            var isEquatable = !t.IsByRefLike && typeof(IEquatable<>).MakeGenericType(t).IsAssignableFrom(t);

            var stereotype =
                tc == TypeClassifier.ValueType ? $"<< (V,orchid) struct >>" :
                tc == TypeClassifier.Delegate ? $"<< (D,#FF7700) delegate >>" :
                tc == TypeClassifier.StaticClass ? $"<<static>>" :
                "";
            var stereotypes = new StringBuilder(stereotype);
            stereotypes.
                Append(isDisposable ? "<< Disposable >>" : "").
                Append(isAsyncDisposable ? "<< AsyncDisposable >>" : "").
                Append(isEnumerable ? "<< Enumerable >>" : "").
                Append(isEquatable ? "<< Equatable >>" : "");

            Console.WriteLine($"  {classifier} \"{t.GetFriendlyName()}\" {stereotypes} {GetDocUrl(t)}");

            if (TypeFilter(t.BaseType) && NamespaceFilter(t.Namespace))
            {
                var baseType = t.BaseType.IsGenericType ? t.BaseType.GetGenericTypeDefinition() : t.BaseType;
                Console.WriteLine($"  \"{baseType.Namespace}.{baseType.GetFriendlyName()}\" <|-- \"{t.GetFriendlyName()}\"");
            }

            if (!t.IsEnum)
            {
                var interfaces = t.GetInterfaces();
                foreach (var i in interfaces)
                {
                    var ii = i.IsGenericType ? i.GetGenericTypeDefinition() : i;
                    if (IgnoreInterfaces.Contains(ii))
                        continue;
#if false
                    var baseType = t.BaseType;
                    var isImplimentedByBase = false;
                    while (baseType != null)
                    {
                        if (i.IsAssignableFrom(baseType))
                        {
                            isImplimentedByBase = true;
                            break;
                        }
                        baseType = baseType.BaseType;
                    }

                    if (isImplimentedByBase)
                        continue;
#endif
                    if (interfaces.Any(t => i != t && i.IsAssignableFrom(t)))
                        continue;

                    if (i.IsPublic && NamespaceFilter(i.Namespace))
                    {
                        Console.WriteLine($"  \"{ii.Namespace}.{ii.GetFriendlyName()}\" <|.. \"{t.GetFriendlyName()}\"");
                    }
                }
            }
        }

        static string GetDocUrl(Type t)
        {
            return "[[https://docs.microsoft.com/dotnet/api/" + t.FullName.Replace('`', '-') + "]]";
        }

        static readonly ISet<Type> IgnoreInterfaces = new HashSet<Type>
        {
            typeof(ICloneable),
            typeof(IAsyncDisposable),
            typeof(IDisposable),
            typeof(IEquatable<>),
            typeof(IComparable),
            typeof(IComparable<>),
            typeof(System.Collections.IEnumerable),
            typeof(IEnumerable<>),
        };
    }

    public static class TypeExtensions
    {
        [ThreadStatic]
        static StringBuilder stringBuilderCache = new StringBuilder();

        public static string GetFriendlyName(this Type type, bool showGenericArguments = true, bool showDeclaringType = true, bool quoted = false)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            stringBuilderCache.Clear();
            if (quoted)
                stringBuilderCache.Append('"');
            BuildFriendlyName(stringBuilderCache, type, showGenericArguments, showDeclaringType);
            if (quoted)
                stringBuilderCache.Append('"');
            return stringBuilderCache.ToString();
        }

        private static void BuildFriendlyName(StringBuilder builder, Type type, bool showGenericArguments, bool showDeclaringType)
        {
            bool isBasic = true;
            if (showDeclaringType && type.IsNested && !type.IsGenericParameter)
            {
                BuildFriendlyName(builder, type.DeclaringType, showGenericArguments, showDeclaringType);
                builder.Append('+');
            }

            if (type.IsPointer)
            {
                isBasic = false;
                BuildFriendlyName(builder, type.GetElementType(), showGenericArguments, showDeclaringType);
                builder.Append('*');
            }

            if (type.IsGenericParameter)
            {
                isBasic = false;
                if ((type.GenericParameterAttributes & GenericParameterAttributes.Covariant) != 0)
                {
                    builder.Append("out ");
                }
                else if ((type.GenericParameterAttributes & GenericParameterAttributes.Contravariant) != 0)
                {
                    builder.Append("in ");
                }
                builder.Append(type.Name);
            }

            if (type.IsGenericType)
            {
                isBasic = false;
                string name = type.Name;
#if true
                int index = name.IndexOf('`');
                if (index > 0)
                    name = name.Substring(0, name.IndexOf('`'));
#endif
                var args = type.GetGenericArguments();
                if (type.IsGenericTypeDefinition)
                {
                    builder.Append(name).Append('<');
                    if (showGenericArguments)
                    {
                        for (int i = 0; i < args.Length; ++i)
                        {
                            if (i > 0)
                                builder.Append(", ");
                            BuildFriendlyName(builder, args[i], showGenericArguments, showDeclaringType);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < args.Length - 1; ++i)
                            builder.Append(',');
                    }
                }
                else
                {
                    builder.Append(name).Append('<');
                    for (int i = 0; i < args.Length; ++i)
                    {
                        if (i > 0)
                            builder.Append(", ");
                        BuildFriendlyName(builder, args[i], showGenericArguments, showDeclaringType);
                    }
                }
                builder.Append('>');
            }

            if (isBasic)
            {
                builder.Append(type.Name);
            }
        }


    }

}
