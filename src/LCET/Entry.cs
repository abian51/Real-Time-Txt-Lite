using System;
using System.Threading;
using System.Security.Principal;
using System.Windows;

namespace LCET
{
    public static class Entry
    {
        public static string Version { get { return "0.1.0"; } }

        public static void CheckThread()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("LCET requires an STA thread.");
        }

        public static void Run(string root, string edgePath)
        {
            CheckThread();
            string identity = WindowsIdentity.GetCurrent().User.Value;
            using (var mutex = new Mutex(false, "Local\\LCET-" + identity))
            using (var activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LCET-activate-" + identity))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) { activate.Set(); return; }
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    using (var window = new MainWindow(root, edgePath))
                    {
                        RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(activate,
                            (state, timedOut) => window.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                window.Show();
                                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                                window.Activate();
                            })), null, Timeout.Infinite, false);
                        try { app.Run(window); }
                        finally { registration.Unregister(null); }
                    }
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}