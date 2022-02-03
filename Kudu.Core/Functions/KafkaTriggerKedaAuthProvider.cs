using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

using System.Linq;
using Kudu.Core.K8SE;
using Newtonsoft.Json;
using System.Text;
using k8s;

namespace Kudu.Core.Functions
{
    public class KafkaTriggerKedaAuthProvider : IKedaAuthRefProvider
    {
        public IDictionary<string, string> PopulateAuthenticationRef(JToken bindings, string functionName)
        {
            IDictionary<string, string> functionData = bindings.ToObject<Dictionary<string, JToken>>()
                .Where(i => i.Value.Type == JTokenType.String)
                .ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            if (!IsTriggerAuthRequired(functionData, functionName)) {
                return null;
            }

            //map of secret keys to keda params required for trigger auth
            IDictionary<string, string> secretKeyToKedaParam = new Dictionary<string, string>();
            IDictionary<string, string> secretsForAppSettings = new Dictionary<string, string>();

            if ((functionData[TriggerAuthConstants.KAFKA_TRIGGER_PROTOCOL].Equals(TriggerAuthConstants.KAFKA_TRIGGER_SASL_SSL_PROTOCOL, StringComparison.OrdinalIgnoreCase) 
                    || functionData[TriggerAuthConstants.KAFKA_TRIGGER_PROTOCOL].Equals(TriggerAuthConstants.KAFKA_TRIGGER_SASL_PLAINTEXT_PROTOCOL, StringComparison.OrdinalIgnoreCase)))
            {
                secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_AUTH_MODE, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_AUTH_MODE));
                secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_USERNAME, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_USERNAME));
                secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_PASSWORD, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_PASSWORD));

                secretsForAppSettings.Add(TriggerAuthConstants.KAFKA_TRIGGER_AUTH_MODE, TriggerAuthConstants.KafkaTriggerAuthModeToKedaAuthModeProperty.GetValueOrDefault(functionData[TriggerAuthConstants.KAFKA_TRIGGER_AUTH_MODE]));
                secretsForAppSettings.Add(TriggerAuthConstants.KAFKA_TRIGGER_USERNAME, functionData[TriggerAuthConstants.KAFKA_TRIGGER_USERNAME]);
                secretsForAppSettings.Add(TriggerAuthConstants.KAFKA_TRIGGER_PASSWORD, functionData[TriggerAuthConstants.KAFKA_TRIGGER_PASSWORD]);               
            }

            if (functionData.ContainsKey(TriggerAuthConstants.KAFKA_TRIGGER_PROTOCOL) 
                    && (functionData[TriggerAuthConstants.KAFKA_TRIGGER_PROTOCOL].Equals(TriggerAuthConstants.KAFKA_TRIGGER_SASL_SSL_PROTOCOL, StringComparison.OrdinalIgnoreCase)
                        || functionData[TriggerAuthConstants.KAFKA_TRIGGER_PROTOCOL].Equals(TriggerAuthConstants.KAFKA_TRIGGER_SSL_PROTOCOL, StringComparison.OrdinalIgnoreCase))) {
                secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_TLS, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_TLS));
                secretsForAppSettings.Add(TriggerAuthConstants.KAFKA_TRIGGER_TLS, "enable");
            }

            if (functionData.ContainsKey(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CA_LOCATION))
            {
                secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CA_LOCATION, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CA_LOCATION));
                secretsForAppSettings.Add(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CA_LOCATION, functionData[TriggerAuthConstants.KAFKA_TRIGGER_SSL_CA_LOCATION]);
            }

            if (functionData.ContainsKey(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CERT_LOCATION))
            {
                secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CERT_LOCATION, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CERT_LOCATION));
                secretsForAppSettings.Add(TriggerAuthConstants.KAFKA_TRIGGER_SSL_CERT_LOCATION, functionData[TriggerAuthConstants.KAFKA_TRIGGER_SSL_CERT_LOCATION]);
            }

            if (functionData.ContainsKey(TriggerAuthConstants.KAFKA_TRIGGER_SSL_KEY_LOCATION))
            {
                secretKeyToKedaParam.Add(TriggerAuthConstants.KAFKA_TRIGGER_SSL_KEY_LOCATION, getKedaProperty(TriggerAuthConstants.KAFKA_TRIGGER_SSL_KEY_LOCATION));
                secretsForAppSettings.Add(TriggerAuthConstants.KAFKA_TRIGGER_SSL_KEY_LOCATION, functionData[TriggerAuthConstants.KAFKA_TRIGGER_SSL_KEY_LOCATION]);
            }

            //step 1: add the required trigger auth data as secrets in appsetting secrets file
            try 
            {
                AddTriggerAuthAppSettingsSecrets(secretsForAppSettings, functionName);

            } catch (Exception ex)
            {
                //logging and continuing as keda handles if secret expected is not found
                Console.WriteLine("Error while adding secrets required for trigger auth ", ex.ToString());
            }
            
           //step 2: Create the trigger auth CRD
            IDictionary<string, string> authRef = new Dictionary<string, string>();
            authRef.Add(TriggerAuthConstants.TRIGGER_AUTH_REF_NAME_KEY, functionName);
            try
            {
                CreateTriggerAuthenticationRef(secretKeyToKedaParam, functionName); 
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while creating Trigger Authentication Ref, function name : {0} ", functionName, ex.ToString());
                Console.WriteLine("KEDA might not to able to scale the app {0} due to missing Trigger Authentication details", functionName, ex.ToString());
                return null;
            }
            return authRef;
        }

        internal virtual void CreateTriggerAuthenticationRef(IDictionary<string, string> secretKeyToKedaParam, string functionName)
        {
            string secretKeyToKedaParamMap = System.Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(JsonConvert.SerializeObject(secretKeyToKedaParam)));
            // functionName + "-secrets" is the filename for appsettings secrets
            K8SEDeploymentHelper.CreateTriggerAuthenticationRef(functionName + "-secrets", secretKeyToKedaParamMap, functionName);
        }

         internal virtual void AddTriggerAuthAppSettingsSecrets(IDictionary<string, string> secretsForAppSettings, string functionName)
        {
            string appNamespace = System.Environment.GetEnvironmentVariable("K8SE_APPS_NAMESPACE");
            K8SEDeploymentHelper.UpdateKubernetesSecrets(secretsForAppSettings, functionName + "-secrets", appNamespace);
        }

        internal string getKedaProperty(string triggerBinding)
        {
            if (triggerBinding == null)
            {
                return null;
            }
            return TriggerAuthConstants.KafkaTriggerBindingToKedaProperty.GetValueOrDefault(triggerBinding);
        }

        internal Boolean IsTriggerAuthRequired(IDictionary<string, string> functionBindings, string functionName) {

            if (!functionBindings.ContainsKey(TriggerAuthConstants.KAFKA_TRIGGER_PROTOCOL) || !functionBindings.ContainsKey(TriggerAuthConstants.KAFKA_TRIGGER_AUTH_MODE)) {
                return false;
            }

            if (functionBindings[TriggerAuthConstants.KAFKA_TRIGGER_AUTH_MODE].Equals(TriggerAuthConstants.KAFKA_TRIGGER_AUTH_MODE_NOT_SET, StringComparison.OrdinalIgnoreCase) 
                    || functionBindings[TriggerAuthConstants.KAFKA_TRIGGER_AUTH_MODE].Equals(TriggerAuthConstants.KAFKA_TRIGGER_PROTOCOL_NOT_SET, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            if (functionBindings[TriggerAuthConstants.KAFKA_TRIGGER_AUTH_MODE].Equals("Gssapi", StringComparison.OrdinalIgnoreCase)) {
                Console.WriteLine("Gssapi as Authentication Mode is not supported in Keda, function app {0} might not be able to scale", functionName);
            }
            return true;
        }
    }
}
