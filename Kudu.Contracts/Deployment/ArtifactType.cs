using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Contracts.Deployment
{
    public enum ArtifactType
    {
        Unknown,
        War,
        Jar,
        Ear,
        Lib,
        Static,
        Startup,
        Script,
        Zip,
    }
}
