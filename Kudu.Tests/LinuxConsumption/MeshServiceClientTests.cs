using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Core.LinuxConsumption;
using Moq;
using Moq.Protected;
using Xunit;

namespace Kudu.Tests.LinuxConsumption
{
    [Collection("MockedEnvironmentVariablesCollection")]
    public class MeshServiceClientTests
    {
        private const string MeshInitUri = "http://local:6756/";
        private const string ConnectionString = "DefaultEndpointsProtocol=https;AccountName=storage-account;AccountKey=AAAABBBBBAAAABBBBBAAAABBBBBAAAABBBBBAAAABBBBBAAAABBBBBAAAABBBBBAAAABBBBBAAAABBBBBCCCCC==";

        private readonly Mock<HttpMessageHandler> _handlerMock;
        private readonly MeshServiceClient _meshServiceClient;

        public MeshServiceClientTests()
        {
            var environmentVariables = new Dictionary<string, string>
            {
                [Constants.ContainerName] = "container-name",
                [Constants.MeshInitURI] = MeshInitUri
            };

            var systemEnvironment = new TestSystemEnvironment(environmentVariables);

            _handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            _meshServiceClient = new MeshServiceClient(systemEnvironment, new HttpClient(_handlerMock.Object));
        }

        [Fact]
        public async Task DoesNotThrowWhenSuccessful()
        {
            _handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK
            });

            await _meshServiceClient.MountCifs(ConnectionString, "share-name", "/target-path");

            _handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => string.Equals(MeshInitUri, r.RequestUri.AbsoluteUri)),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task ThrowsExceptionOnFailure()
        {
            _handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError
            });

            try
            {
                await _meshServiceClient.MountCifs(ConnectionString, "share-name", "/target-path");
            }
            catch (HttpRequestException)
            {
                // Exception is expected
                return;
            }

            // Shouldn't reach here
            Assert.False(true);
        }
    }
}
