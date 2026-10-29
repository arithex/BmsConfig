using System.Windows;
using System.Windows.Threading;

namespace BmsConfig
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Dark.xaml is loaded statically (so the designer has a palette); replace it with the user's choice.
            Resources.MergedDictionaries.RemoveAt(0);
            ThemeManager.Initialize();

            DispatcherUnhandledException += OnUnhandledException;
            base.OnStartup(e);
        }

        void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.ToString(), "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
