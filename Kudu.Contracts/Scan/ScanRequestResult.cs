using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Contracts.Scan
{
    public enum ScanRequestResult
    {
        RunningAynschronously,
        RanSynchronously,
        Pending,
        AsyncScanFailed
    }
}
