using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace shttp.Tests.TestUtils
{
    class SingleByteContent : HttpContent
        {
            private readonly byte _content;

            public SingleByteContent(byte content)
            {
                _content = content;
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                stream.Write(new byte[] { _content }, 0, 1);
                return Task.CompletedTask;
            }

            protected override bool TryComputeLength(out long length)
            {
                length = 1;
                return true;
            }
        }
}