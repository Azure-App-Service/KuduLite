using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Kudu.Core.Functions
{
    public interface IKedaAuthRefProvider 
    {
        public  IDictionary<string, string> PopulateAuthenticationRef(JToken functionBindings, string functionName);

    }
}
