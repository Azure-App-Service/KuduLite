using System;
using System.Collections.Generic;
using System.Text;
using Kudu.Services.Models;
using Xunit;

namespace Kudu.Tests.Services.Models
{
    public class EncryptedHostAssignmentContextTests
    {
        private const string _encryptionKey = "MDEyMzQ1Njc4OUFCQ0RFRjAxMjM0NTY3ODlBQkNERUY=";
        private const string _lastModifiedTime = "2019-01-15T15:53:00";
        private HostAssignmentContext _context = new HostAssignmentContext() {
            SiteId = 10,
            SiteName = "sitename",
            LastModifiedTime = DateTime.Parse(_lastModifiedTime),
            Environment = new Dictionary<string, string>()
            {
                {"APPSETTING_AzureWebJobsStorage", "<StorageString>"},
                {"APPSETTING_SCM_RUN_FROM_PACKAGE", "1"},
                {"APPSETTING_SCM_DO_BUILD_DURING_DEPLOYMENT", "true"},
                {"APPSETTING_SCM_NO_REPOSITORY", "1"},
                {"ENABLE_ORYX_BUILD", "true"},
                {"FRAMEWORK", "PYTHON"},
                {"FRAMEWORK_VERSION", "3.6"}
            }
        };

        [Fact]
        public void EncryptedContextShouldExistAfterCreate()
        {
            var result = EncryptedHostAssignmentContext.Create(_context, _encryptionKey);
            Assert.NotEmpty(result.EncryptedContext);
        }

        [Fact]
        public void DecryptedContextShouldMatchByEqual()
        {
            var encrypted = EncryptedHostAssignmentContext.Create(_context, _encryptionKey);
            var decrypted = encrypted.Decrypt(_encryptionKey);
            Assert.True(_context.Equals(decrypted));
        }

        [Fact]
        public void DecryptedContextShouldMatchEnvironments()
        {
            var encrypted = EncryptedHostAssignmentContext.Create(_context, _encryptionKey);
            var decrypted = encrypted.Decrypt(_encryptionKey);
            Assert.Equal(_context.Environment, decrypted.Environment);
        }
    }
}
