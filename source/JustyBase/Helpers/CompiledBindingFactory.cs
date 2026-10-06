using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;

namespace JustyBase.Helpers;

internal static class CompiledBindingFactory
{
    public static CompiledBinding OneWay<TSource, TValue>(
        string propertyName,
        Func<TSource, TValue> getter,
        IValueConverter? converter = null)
    {
        var propertyInfo = new ClrPropertyInfo(
            propertyName,
            source => source is TSource typed ? getter(typed) : default(TValue),
            (_, _) => { },
            typeof(TValue));

        var path = new CompiledBindingPathBuilder()
            .Property(propertyInfo, PropertyInfoAccessorFactory.CreateInpcPropertyAccessor)
            .Build();

        return new CompiledBinding(path)
        {
            Mode = BindingMode.OneWay,
            Converter = converter
        };
    }

    /// <summary>
    /// AOT-friendly replacement for <c>new Binding("Fields[i]")</c> indexer
    /// paths. A string binding path requires the expression parser and
    /// runtime reflection (IL3050 under Native AOT); this variant carries a
    /// pre-compiled delegate instead, with no dynamic code involved.
    /// One-way only: grid rows are immutable snapshots, so no change
    /// subscription is needed.
    /// </summary>
    public static CompiledBinding OneWayIndexer<TSource>(
        int index,
        Func<TSource, object?> getter,
        IValueConverter? converter = null)
    {
        return OneWay<TSource, object?>($"Item[{index}]", getter, converter);
    }
}
