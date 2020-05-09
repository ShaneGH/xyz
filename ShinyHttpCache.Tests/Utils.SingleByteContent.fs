module ShinyHttpCache.Tests.Utils.SingleByteContent

open System.Net.Http
open System.Runtime.InteropServices
open System.Threading.Tasks

type SingleByteContent (content: byte) =
    inherit HttpContent()
    
    override __.SerializeToStreamAsync(stream, context) =
        stream.Write([|content|], 0, 1);
        Task.CompletedTask
    
    override __.TryComputeLength([<Out>] length) =
        length <- 1L
        true