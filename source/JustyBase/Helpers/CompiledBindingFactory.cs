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
            source => getter((TSource)source),
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
}
