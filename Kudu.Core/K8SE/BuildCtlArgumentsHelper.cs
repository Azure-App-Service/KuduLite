using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.K8SE
{
    internal class BuildCtlArgumentsHelper
    {
        internal static void AddBuildCtlCommand(StringBuilder args, string verb)
        {
            args.AppendFormat("buildctl {0} ", verb);
        }

        internal static void AddAppNameArgument(StringBuilder args, string appName)
        {
            args.AppendFormat(" -appName {0}", appName);
        }

        internal static void AddAppPropertyArgument(StringBuilder args, string appProperty)
        {
            args.AppendFormat(" -appProperty {0}", appProperty);
        }

        internal static void AddAppPropertyValueArgument(StringBuilder args, string appPropertyValue)
        {
            args.AppendFormat(" -appPropertyValue {0}", appPropertyValue);
        }

        internal static void AddFunctionTriggersJsonToPatchValueArgument(StringBuilder args, string jsonToPatch)
        {
            args.AppendFormat(" -jsonToPatch {0}", jsonToPatch);
        }

        internal static void AddSecretData(StringBuilder args, string secretData)
        {
            args.AppendFormat(" -secretData {0}", secretData);
        }

        internal static void AddSecretName(StringBuilder args, string secretName)
        {
            args.AppendFormat(" -secretName {0}", secretName);
        }

        internal static void AddAuthRefSecretKeyToParamMap(StringBuilder args, string authRefSecretKeyToParamMap)
        {
            args.AppendFormat(" -secretKeyToKedaParamMap {0}", authRefSecretKeyToParamMap);
        }


    }
}
