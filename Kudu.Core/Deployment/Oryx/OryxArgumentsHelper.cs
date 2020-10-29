﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Deployment.Oryx
{
    internal static class OryxArgumentsHelper
    {
        internal static void AddOryxBuildCommand(StringBuilder args, string source, string destination)
        {
            args.AppendFormat("oryx build {0} -o {1}", source, destination);
        }

        internal static void AddLanguage(StringBuilder args, string language)
        {
            args.AppendFormat(" --platform {0}", language);
        }

        internal static void AddLanguageVersion(StringBuilder args, string languageVer)
        {
            args.AppendFormat(" --platform-version {0}", languageVer);
        }

        internal static void AddTempDirectoryOption(StringBuilder args, string tempDir)
        {
            args.AppendFormat(" -i {0}", tempDir);
        }

        internal static void AddNodeCompressOption(StringBuilder args, string format)
        {
            args.AppendFormat(" -p compress_node_modules={0}", format);
        }

        internal static void AddPythonCompressOption(StringBuilder args)
        {
            args.Append(" --compress-destination-dir");
        }

        internal static void AddPythonVirtualEnv(StringBuilder args, string virtualEnv)
        {
            args.AppendFormat(" -p virtualenv_name={0}", virtualEnv);
        }

        internal static void AddPythonPackageDir(StringBuilder args, string packageDir)
        {
            args.AppendFormat(" -p packagedir={0}", packageDir);
        }

        internal static void AddPublishedOutputPath(StringBuilder args, string path)
        {
            args.AppendFormat(" -publishedOutputPath {0}", path);
        }

        internal static void AddDebugLog(StringBuilder args)
        {
            args.AppendFormat(" --log-file /tmp/build-debug.log ");
        }
    }
}
