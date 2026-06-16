using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace NozzleScheduleExtractor
{
    internal static class GuiProgram
    {
        [STAThread]
        public static void Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveSolidWorksInterop;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static Assembly ResolveSolidWorksInterop(object sender, ResolveEventArgs args)
        {
            string simpleName = new AssemblyName(args.Name).Name + ".dll";
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, simpleName);
            if (File.Exists(local))
                return Assembly.LoadFrom(local);

            string swPath = Path.Combine(@"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS", simpleName);
            if (File.Exists(swPath))
                return Assembly.LoadFrom(swPath);

            return null;
        }
    }
}
