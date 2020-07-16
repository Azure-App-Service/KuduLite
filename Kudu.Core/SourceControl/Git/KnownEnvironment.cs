
namespace Kudu.Core.SourceControl.Git
{
    /// <summary>
    /// Environment variables used for the post receive hook
    /// </summary>
    internal static class KnownEnvironment
    {
        public const string EXEPATH = "KUDU_EXE";
        public const string APPPATH = "KUDU_APPPATH";
        public const string MSBUILD = "KUDU_MSBUILD";
        public const string DEPLOYER = "KUDU_DEPLOYER";

        // Command to launch the post receive hook
        // CORE NOTE modified the script to run "dotnet," assuming EXEPATH points
        // to a framework-dependent Core app.
        public static string KUDUCOMMAND = "benv dotnet=2.2 dotnet \"$" + EXEPATH + "\" " +
                                           "\"$" + APPPATH + "\" " +
                                           "\"$" + MSBUILD + "\" " +
                                           "\"$" + DEPLOYER + "\"";

        // Command to launch the post receive hook for dynamic install
        // CORE NOTE modified the script to run "dotnet," assuming EXEPATH points
        // to a framework-dependent Core app.
        public static string KUDUCOMMAND_DYNAMICINSTALL = "dotnet \"$" + EXEPATH + "\" " +
                                           "\"$" + APPPATH + "\" " +
                                           "\"$" + MSBUILD + "\" " +
                                           "\"$" + DEPLOYER + "\"";
    }
}
