using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

using System.Linq;
using Kudu.Core.K8SE;
using Newtonsoft.Json;
using System.Text;

namespace Kudu.Core.Functions
{
    public class KafkaTriggerKedaAuthProvider : IKedaAuthRefProvider
    {
        public IDictionary<string, string> PopulateAuthenticationRef(JToken bindings, string functionName)
        {
            IDictionary<string, string> functionData = bindings.ToObject<Dictionary<string, JToken>>()
                .Where(i => i.Value.Type == JTokenType.String)
                .ToDictionary(k => k.Key, v => v.Value.ToString());

            IDictionary<string, string> secretKeyToKedaParam = new Dictionary<string, string>();

            //creates the map of secret keys to keda params required for trigger auth
            if (functionData["Protocol"] != "NotSet" && functionData["AuthenticationMode"] != "NotSet")
            {
                secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_AUTHENTICATION_MODE, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_AUTHENTICATION_MODE));
                secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_USERNAME, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_USERNAME));
                secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_PASSWORD, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_PASSWORD));
                
                if (functionData.ContainsKey(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CA_LOCATION))
                {
                    secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CA_LOCATION, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CA_LOCATION));
                }

                if (functionData.ContainsKey(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CERT_LOCATION))
                {
                    secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CERT_LOCATION, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CERT_LOCATION));
                    secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_TLS, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_TLS));
                }

                if (functionData.ContainsKey(TriggerAuthConstants.KAFKA_TRIGGER_SSL_KEY_LOCATION))
                {
                    secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_SSL_KEY_LOCATION, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_SSL_KEY_LOCATION));
                }
            }

            IDictionary<string, string> authRef = new Dictionary<string, string>();
            authRef.Add("name", functionName);

            string secretKeyToKedaParamMap = System.Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(JsonConvert.SerializeObject(secretKeyToKedaParam)));

            // functionName + "-secrets" is the filename for appsettings secrets
            K8SEDeploymentHelper.CreateTriggerAuthenticationRef(functionName + "-secrets", secretKeyToKedaParamMap, functionName);
            return authRef;
        }
    
        internal string getKedaProperty(string triggerBinding)
        {
            if (triggerBinding == null)
            {
                return null;
            }
            return TriggerAuthConstants.KafkaTriggerBindingToKedaProperty.GetValueOrDefault(triggerBinding);
        }
    }
}