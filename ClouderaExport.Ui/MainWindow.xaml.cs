using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ClouderaExport.Ui.ViewModels;
using TextBox = System.Windows.Controls.TextBox;

namespace ClouderaExport.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void LogConsoleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.ScrollToEnd();
        }
    }

    private void LogSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // Row 2 is the log panel in the 3-row layout
            var logRow = RootGrid.RowDefinitions[2];
            vm.UpdateLogPanelHeight(logRow.ActualHeight);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.FlushSave();
        }
        base.OnClosing(e);
    }
}