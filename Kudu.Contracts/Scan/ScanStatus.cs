using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Contracts.Scan
{
    public enum ScanStatus
    {
        Starting,
        Executing,
        Failed,
        Success
    }
}
