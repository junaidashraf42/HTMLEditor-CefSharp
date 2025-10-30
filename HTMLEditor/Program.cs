using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using CefSharp;

namespace HTMLEditor
{
    internal static class Program
    {
        private static bool cefInitialized;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            //Cef.EnableHighDPISupport();

            try
            {
                InitializeCefRuntime();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to initialise the Chromium runtime. {ex.Message}", "HTML Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        private static void InitializeCefRuntime()
        {
            //if (cefInitialized || Cef.IsInitialized)
            //{
            //    cefInitialized = true;
            //    return;
            //}

            TrySubscribeAnyCpuSupport();

            CefSharpSettings.SubprocessExitIfParentProcessClosed = true;

            var basePath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase ?? AppContext.BaseDirectory;
            var appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HTMLEditor", "CefSharp");
            var cachePath = Path.Combine(appDataRoot, "Cache");
            var rootCachePath = Path.Combine(appDataRoot, "RootCache");
            var logDirectory = Path.Combine(appDataRoot, "Logs");

            Directory.CreateDirectory(cachePath);
            Directory.CreateDirectory(rootCachePath);
            Directory.CreateDirectory(logDirectory);

            var logFile = Path.Combine(logDirectory, "cef.log");
            //var settings = new CefSettings
            //{
            //    BrowserSubprocessPath = Path.Combine(basePath, "CefSharp.BrowserSubprocess.exe"),
            //    CachePath = cachePath,
            //    RootCachePath = rootCachePath,
            //    LogFile = logFile,
            //    LogSeverity = LogSeverity.Info
            //};

            //if (!File.Exists(settings.BrowserSubprocessPath))
            //{
            //    throw new FileNotFoundException($"Browser subprocess not found at {settings.BrowserSubprocessPath}. Ensure CefSharp is restored correctly.");
            //}

            //if (!Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null))
            //{
            //    throw new InvalidOperationException($"Cef.Initialize returned false. Check the log at {logFile} for details.");
            //}

            //Application.ApplicationExit += (_, __) =>
            //{
            //    if (Cef.IsInitialized)
            //    {
            //        Cef.Shutdown();
            //    }
            //};

            cefInitialized = true;
        }

        private static void TrySubscribeAnyCpuSupport()
        {
            var cefRuntimeType = typeof(Cef).Assembly.GetType("CefSharp.CefRuntime");
            if (cefRuntimeType == null)
            {
                return;
            }

            var method = cefRuntimeType.GetMethod("SubscribeAnyCpuAssemblyResolver", BindingFlags.Public | BindingFlags.Static)
                         ?? cefRuntimeType.GetMethod("SubscribeAnyCpuAppDomainResolver", BindingFlags.Public | BindingFlags.Static)
                         ?? cefRuntimeType.GetMethod("SubscribeAnyCpuSupport", BindingFlags.Public | BindingFlags.Static);

            method?.Invoke(null, null);
        }
    }
}
