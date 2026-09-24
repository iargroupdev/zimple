using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Zimple
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static Mutex mutex;

        [STAThread]
        public static void Main()
        {
            bool createdNew;
            mutex = new Mutex(true, "ZimpleTesting_AllInOne_SingleInstance", out createdNew);

            if (!createdNew)
            {
                MessageBox.Show("An instance of ZimpleTesting is already running.");
                return;
            }

            try
            {
                // Application isn't running yet, proceed to start
                var application = new App();
                application.DispatcherUnhandledException += Application_DispatcherUnhandledException;
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
                application.InitializeComponent();
                application.Run(new WindowAllinOne()); // Start MainWindow
            }
            finally
            {
                mutex.ReleaseMutex(); // Release the mutex when application closes
                mutex.Dispose();
            }
        }

        private static void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteCrashGuardLog("DispatcherUnhandledException", e.Exception);
            e.Handled = true;
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            WriteCrashGuardLog("UnhandledException", e.ExceptionObject as Exception);
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            WriteCrashGuardLog("UnobservedTaskException", e.Exception);
            e.SetObserved();
        }

        private static void WriteCrashGuardLog(string source, Exception exception)
        {
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ZimpleTesting",
                    "Logs");
                Directory.CreateDirectory(root);

                string path = Path.Combine(root, "CrashGuard.log");
                string message = exception == null
                    ? "No exception details available."
                    : exception.ToString();

                File.AppendAllText(
                    path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // Last-resort guard: logging must never crash the application.
            }
        }
    }
}
