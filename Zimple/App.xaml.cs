using System;
using System.Threading;
using System.Windows;

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
                application.InitializeComponent();
                application.Run(new WindowAllinOne()); // Start MainWindow
            }
            finally
            {
                mutex.ReleaseMutex(); // Release the mutex when application closes
                mutex.Dispose();
            }
        }
    }
}
