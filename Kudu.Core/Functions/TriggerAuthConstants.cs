using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Functions
{
    public static class TriggerAuthConstants
    {
        public const string TRIGGER_AUTH_REF_NAME_KEY = "name";

        public const string KAFKA_TRIGGER_PROTOCOL = "protocol";
        public const string KAFKA_TRIGGER_AUTH_MODE = "authenticationMode";
        public const string KAFKA_TRIGGER_USERNAME = "username";
        public const string KAFKA_TRIGGER_PASSWORD = "password";
        public const string KAFKA_TRIGGER_SSL_CA_LOCATION = "sslCaLocation";
        public const string KAFKA_TRIGGER_SSL_CERT_LOCATION = "sslCertificateLocation";
        public const string KAFKA_TRIGGER_SSL_KEY_LOCATION = "sslKeyLocation";
        public const string KAFKA_TRIGGER_TLS = "tls";

        public const string KAFKA_TRIGGER_AUTH_MODE_NOT_SET = "NotSet";
        public const string KAFKA_TRIGGER_PROTOCOL_NOT_SET = "NotSet";

        public const string KAFKA_KEDA_PARAM_AUTH_MODE = "sasl";
        public const string KAFKA_KEDA_PARAM_USERNAME = "username";
        public const string KAFKA_KEDA_PARAM_PASSWORD = "password";
        public const string KAFKA_KEDA_PARAM_CA_LOCATION = "ca";
        public const string KAFKA_KEDA_PARAM_CERT_LOCATION = "cert";
        public const string KAFKA_KEDA_PARAM_KEY_LOCATION = "key";
        public const string KAFKA_KEDA_PARAM_TLS = "tls";

        //Below protocol values must be same as BrokerProtocol values from kafka extension
        public const string KAFKA_TRIGGER_SASL_SSL_PROTOCOL = "SaslSsl";
        public const string KAFKA_TRIGGER_SSL_PROTOCOL = "Ssl";
        public const string KAFKA_TRIGGER_SASL_PLAINTEXT_PROTOCOL = "SaslPlaintext";
        public const string KAFKA_TRIGGER_PLAINTEXT_PROTOCOL = "Plaintext";

        public static readonly Dictionary<string, string> KafkaTriggerBindingToKedaProperty = new Dictionary<string, string>()
        {
            { KAFKA_TRIGGER_AUTH_MODE, KAFKA_KEDA_PARAM_AUTH_MODE },
            { KAFKA_TRIGGER_USERNAME, KAFKA_KEDA_PARAM_USERNAME },
            { KAFKA_TRIGGER_PASSWORD, KAFKA_KEDA_PARAM_PASSWORD },
            { KAFKA_TRIGGER_SSL_CA_LOCATION, KAFKA_KEDA_PARAM_CA_LOCATION },
            { KAFKA_KEDA_PARAM_CERT_LOCATION, KAFKA_KEDA_PARAM_CERT_LOCATION },
            { KAFKA_TRIGGER_SSL_KEY_LOCATION, KAFKA_KEDA_PARAM_KEY_LOCATION },
            { KAFKA_TRIGGER_TLS, KAFKA_KEDA_PARAM_TLS}
        };


         public static readonly Dictionary<string, string> KafkaTriggerAuthModeToKedaAuthModeProperty = new Dictionary<string, string>()
        {
            // mapping between possible values of authenticationMode from kafka trigger (comes from kafka extension)
            // to authenticationMode in KEDA
            
            { "NotSet", "none" },
            //{ "Gssapi", "none" }, // KEDA doesn't support Gssapi mechanism yet
            { "Plain", "plaintext" },
            { "ScramSha256", "scram_sha256" },
            { "ScramSha512", "scram_sha512" }
        };

    }
}
