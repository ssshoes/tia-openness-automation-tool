using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TIA_Project_v11
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // AssemblyResolve helper - load PublicAPI DLLs from standard TIA path if not found
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    var name = new AssemblyName(args.Name).Name + ".dll";
                    var candidate = Path.Combine(@"C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19", name);
                    if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
                }
                catch { }
                return null;
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SimpleMainForm());
        }
    }
}
