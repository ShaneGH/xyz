using Microsoft.FSharp.Control;
using System.Threading.Tasks;

namespace ShinyHttpCache.Tests.TestUtils
{
    public static class FSharpUtils
    {
        public static Task<T> ToTask<T>(this FSharpAsync<T> task)
        {
            return FSharpAsync.StartAsTask(task, null, null);
        }
    }
}