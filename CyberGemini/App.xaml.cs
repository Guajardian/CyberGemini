using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace CyberGemini;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch any unhandled rendering / dispatcher exceptions so they don't silently kill the app
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Prevent the app from shutting down while the splash-to-main transition is in-flight
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            var splash = new SplashWindow();
            splash.Show();

            await Task.Delay(1200);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            // Now that a proper MainWindow is assigned, switch to normal shutdown behaviour
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            mainWindow.Show();
            splash.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "Unhandled error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}