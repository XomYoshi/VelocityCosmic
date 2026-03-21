using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace VelocityCosmic // Namespace simplified to match MainWindow.xaml
{
    public class EditorTabControl : TabControl
    {
        public event RoutedEventHandler AddTabClicked;

        public EditorTabControl()
        {
            if (DesignerProperties.GetIsInDesignMode(this)) return;
        }
    }
}
