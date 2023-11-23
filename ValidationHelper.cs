namespace Automate_IT
{
    internal static class ValidationHelper
    {
        public static void ValidateArgs(string[] args)
        {
            if ((args?.Length ?? 0) != 1)
                throw new ArgumentException("You may specify exactly one path as command-line argument for this application.", nameof(args));

            string Arg0 = args![0];
            if (!File.Exists(Arg0))
                throw new FileNotFoundException("The specified file was not found.", Arg0);

            if (Path.GetExtension(Arg0).ToLower() != ".ait")
                throw new ArgumentException("Invalid file extension. The filename must end with \".ait\".", nameof(args));
        }
    }
}
