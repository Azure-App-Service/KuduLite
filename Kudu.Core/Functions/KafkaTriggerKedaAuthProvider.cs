using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

using System.Linq;
using Kudu.Core.K8SE;

namespace Kudu.Core.Functions
{
    public class KafkaTriggerKedaAuthProvider : IKedaAuthRefProvider
    {
        public static readonly Dictionary<string, string> TriggerBindingToKedaProperty = new Dictionary<string, string>()
        {
            { "AuthenticationMode", "sasl" },
            { "username", "username" },
            { "password", "password" },
            { "SslCaLocation", "ca" },
            { "SslCertificateLocation", "cert" },
            { "SslKeyLocation", "key" }
        };
    
        public IDictionary<string, string> PopulateAuthenticationRef(JToken t, string functionName) 
        {
            IDictionary<string, string> functionData = t.ToObject<Dictionary<string, JToken>>()
                .Where(i => i.Value.Type == JTokenType.String)
                .ToDictionary(k => k.Key, v => v.Value.ToString());

                IDictionary<string, string> secrets = new Dictionary<string, string>();
                IList<String> authSecretKeys = new List<String>();
                IDictionary<string, string> secretNameToKedaParam = new Dictionary<string, string>();
                //SSL, PLAINTEXT, SASL_PLAINTEXT, SASL_SSL
               if (functionData["Protocol"] == "SaslPlaintext" || functionData["Protocol"] == "Plaintext") {
                    //from this find all the data for secret
                    authSecretKeys.Add("AuthenticationMode");
                    authSecretKeys.Add("username");
                    authSecretKeys.Add("password");
                    //data:
                    // sasl: "plaintext"
                    // username: "admin"
                    // password: "admin"
                    // tls: "enable"
                    // ca: <your ca> SslCaLocation
                    // cert: <your cert> SslCertificateLocation
                    // key: <your key> SslKeyLocation
                    
                } else if (functionData["Protocol"] == "SaslSsl") {
                     authSecretKeys.Add("AuthenticationMode"); //:TODO check -AuthenticationMode is the binding name, but k4 will use secret name as parameter in trigger auth 
                     authSecretKeys.Add("username");
                     authSecretKeys.Add("password");
                     authSecretKeys.Add("tls");
                     authSecretKeys.Add("SslCaLocation");
                     authSecretKeys.Add("SslCertificateLocation");
                     authSecretKeys.Add("SslKeyLocation");
                } else if (functionData["Protocol"] == "SSL") {
                     authSecretKeys.Add("tls"); //this boolean flag 
                     authSecretKeys.Add("SslCaLocation");
                     authSecretKeys.Add("SslCertificateLocation");
                     authSecretKeys.Add("SslKeyLocation");
                }
                // :TODO remove this hard coding
                secrets.Add("sasl", "plaintext");
                secrets.Add("username", "admin");
                secrets.Add("password", "admin");
                //create  secret "functionname + triggerauth + secret".yaml
                //Runs build ctl command and creates secret
               // K8SEDeploymentHelper.CreateSecrets(functionName+"authRef-secrets", secrets);

                IDictionary<string, string> authRef = new Dictionary<string, string>();
                authRef.Add("name",functionName);
            //use secrets and generate authRef 

            // spec:
            // secretTargetRef:
            // - parameter: sasl
            //     name: keda-kafka-secrets
            //     key: sasl
            // - parameter: username
            //     name: keda-kafka-secrets
            //     key: username

            //generate authref name as "functionname + triggerauth".yaml
            //Runs build ctl command and creates TriggerAuthentication CRD

            //

            // functionName + "-secrets" is the filename for appsettings secrets
            K8SEDeploymentHelper.CreateTriggerAuthenticationRef(functionName + "-secrets", "username,password", functionName);
            return authRef;
        }
    }

}