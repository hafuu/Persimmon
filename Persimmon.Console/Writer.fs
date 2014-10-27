﻿namespace global

open System
open System.IO

type Writer(path: FileInfo option, defaultWriter: TextWriter) =
  let writer =
    match path with
    | Some path -> new StreamWriter(path.FullName, false, Text.Encoding.UTF8) :> TextWriter
    | None -> defaultWriter

  member __.WriteLine(str: string) =
    writer.WriteLine(str)

  interface IDisposable with
    member __.Dispose () =
      match path with
      | Some _ -> writer.Dispose()
      | None -> ()

