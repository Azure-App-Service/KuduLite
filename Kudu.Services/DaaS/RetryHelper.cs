using System;
using System.Threading.Tasks;

namespace Kudu.Services.Performance
{
    internal class RetryHelper
    {
        public static void RetryOnException(string actionInfo, Action operation, TimeSpan delay, int times = 3, bool throwAfterRetry = true)
        {
            var attempts = 0;
            do
            {
                try
                {
                    attempts++;
                    operation();
                    break; // Success! Lets exit the loop!
                }
                catch (Exception)
                {
                    if (attempts == times)
                    {
                        if (throwAfterRetry)
                        {
                            throw;
                        }
                    }
                    Task.Delay(delay).Wait();
                }
            } while (true);
        }
    }
}