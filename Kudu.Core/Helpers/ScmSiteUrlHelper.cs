using System.Text.RegularExpressions;

namespace Kudu.Core.Helpers
{
    public class ScmSiteUrlHelper
    {
        private static Regex malformedScmHostnameRx = new Regex(@"://~\d+");

        /// <summary>
        /// Remove the ~[number] in http url
        /// </summary>
        /// <param name="scmUrl">An scm site url (e.g. http://~1linuxfunctiondev-funnystamp-func/)</param>
        /// <returns>A url without ~1, (e.g. http://linuxfunctiondev-funnystamp-func/) </returns>
        public static string SanitizeUrl(string scmUrl)
        {
            if (string.IsNullOrEmpty(scmUrl))
            {
                return scmUrl;
            }

            return malformedScmHostnameRx.Replace(scmUrl, @"://", 1);
        }
    }
}