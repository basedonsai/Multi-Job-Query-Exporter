using System.Windows;
using System.Windows.Controls;

namespace ClouderaExport.Ui.Helpers;

public static class PasswordBoxHelper
{
    public static readonly DependencyProperty BoundPassword =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(string.Empty, OnBoundPasswordChanged));

    public static readonly DependencyProperty BindPassword =
        DependencyProperty.RegisterAttached(
            "BindPassword",
            typeof(bool),
            typeof(PasswordBoxHelper),
            new PropertyMetadata(false, OnBindPasswordChanged));

    private static readonly DependencyProperty UpdatingPassword =
        DependencyProperty.RegisterAttached(
            "UpdatingPassword",
            typeof(bool),
            typeof(PasswordBoxHelper),
            new PropertyMetadata(false));

    public static string GetBoundPassword(DependencyObject d) => (string)d.GetValue(BoundPassword);
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPassword, value);

    public static bool GetBindPassword(DependencyObject d) => (bool)d.GetValue(BindPassword);
    public static void SetBindPassword(DependencyObject d, bool value) => d.SetValue(BindPassword, value);

    private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox box)
        {
            if ((bool)e.NewValue)
            {
                box.PasswordChanged += HandlePasswordChanged;
            }
            else
            {
                box.PasswordChanged -= HandlePasswordChanged;
            }
        }
    }

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox box)
        {
            if (!GetUpdatingPassword(box))
            {
                box.Password = (string)e.NewValue ?? string.Empty;
            }
        }
    }

    private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            SetUpdatingPassword(box, true);
            SetBoundPassword(box, box.Password);
            SetUpdatingPassword(box, false);
        }
    }

    private static bool GetUpdatingPassword(DependencyObject d) => (bool)d.GetValue(UpdatingPassword);
    private static void SetUpdatingPassword(DependencyObject d, bool value) => d.SetValue(UpdatingPassword, value);
}
