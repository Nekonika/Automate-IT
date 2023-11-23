using System.Text;

namespace Automate_IT
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Setup();
            Console.WriteLine("Hello, World!\r\n");
            Console.WriteLine();
            ValidationHelper.ValidateArgs(args);

            AitWorkload Workload = AitHelper.ParseAit(args.First());
            Console.WriteLine($"Runtime: {Workload.Runtime}");
            Console.WriteLine();
            Workload.Run();

            Console.ReadLine();
        }

        private static void Setup()
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(GetLongExceptionString(e.ExceptionObject as Exception));

            Console.ResetColor();

            if (e.IsTerminating)
            {
                Console.WriteLine("The Application will be terminated!");
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
            }
        }

        private static string GetLongExceptionString(Exception? ex)
        {
            if (ex == null) return string.Empty;

            StringBuilder SB = new();
            SB.AppendLine("Type:    " + ex.GetType().FullName);
            SB.AppendLine("Message: " + ex.Message);
            SB.AppendLine("Source:  " + ex.Source);
            SB.AppendLine("Stack-Trace:" + Environment.NewLine + ex.StackTrace);

            if (ex.InnerException != null)
            {
                SB.AppendLine();
                SB.AppendLine("Inner Exception:");
                SB.AppendLine(GetLongExceptionString(ex.InnerException));
            }

            return SB.ToString();
        }
    }
}