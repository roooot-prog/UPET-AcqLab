using System.Windows;

namespace Spider8DAQ.App;

/// <summary>
/// Freezable bridge so DataGridColumn can bind to the window DataContext.
/// {x:Reference} to the Window from inside the Window is a XAML ProvideValue cycle.
/// </summary>
public sealed class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy));
}
