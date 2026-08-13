using System.Windows;
using System.Windows.Controls;

namespace EqlGearHelper.Wpf.Views;

public static class TreeSelection
{
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.RegisterAttached(
        "SelectedItem", typeof(object), typeof(TreeSelection), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    public static object? GetSelectedItem(DependencyObject element) => element.GetValue(SelectedItemProperty);
    public static void SetSelectedItem(DependencyObject element, object? value) => element.SetValue(SelectedItemProperty, value);

    private static void OnSelectedItemChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not TreeView tree) return;
        tree.SelectedItemChanged -= OnTreeSelectedItemChanged;
        tree.SelectedItemChanged += OnTreeSelectedItemChanged;
    }

    private static void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> args) => SetSelectedItem((TreeView)sender, args.NewValue);
}
