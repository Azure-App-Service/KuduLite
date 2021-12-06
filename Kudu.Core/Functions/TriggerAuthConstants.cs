using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Functions
{
    public static class TriggerAuthConstants
    {
        public const string TRIGGER_AUTH_REF_NAME_KEY = "name";

        public const string KAFKA_TRIGGER_PROTOCOL = "Protocol";
        public const string KAFKA_TRIGGER_AUTH_MODE = "AuthenticationMode";
        public const string KAFKA_TRIGGER_USERNAME = "Username";
        public const string KAFKA_TRIGGER_PASSWORD = "Password";
        public const string KAFKA_TRIGGER_SSL_CA_LOCATION = "SslCaLocation";
        public const string KAFKA_TRIGGER_SSL_CERT_LOCATION = "SslCertificateLocation";
        public const string KAFKA_TRIGGER_SSL_KEY_LOCATION = "SslKeyLocation";
        public const string KAFKA_TRIGGER_TLS = "Tls";

        public const string KAFKA_TRIGGER_AUTH_MODE_NOT_SET = "NotSet";
        public const string KAFKA_TRIGGER_PROTOCOL_NOT_SET = "NotSet";

        public const string KAFKA_KEDA_PARAM_AUTH_MODE = "sasl";
        public const string KAFKA_KEDA_PARAM_USERNAME = "username";
        public const string KAFKA_KEDA_PARAM_PASSWORD = "password";
        public const string KAFKA_KEDA_PARAM_CA_LOCATION = "ca";
        public const string KAFKA_KEDA_PARAM_CERT_LOCATION = "cert";
        public const string KAFKA_KEDA_PARAM_KEY_LOCATION = "key";
        public const string KAFKA_KEDA_PARAM_TLS = "tls";

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

    }
}
