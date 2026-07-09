using System;
using System.Windows;
using System.Windows.Controls;

namespace SampleApp.Wpf;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty TitleTextProperty =
        DependencyProperty.Register("TitleText", typeof(string), typeof(MainWindow));

    public void Show(UserControl control)
    {
        Console.WriteLine(control.ToString());
    }
}
