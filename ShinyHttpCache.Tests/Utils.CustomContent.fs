module ShinyHttpCache.Tests.Utils.CustomContent

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

type NoContent () =
    inherit HttpContent()
    
    override __.SerializeToStreamAsync(_, _) = Task.CompletedTask
    
    override __.TryComputeLength([<Out>] length) =
        length <- 0L
        true