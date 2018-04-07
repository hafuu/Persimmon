﻿namespace Persimmon.Internals

open System
open System.Reflection
open Microsoft.FSharp.Collections
open Persimmon

module internal TestCollectorImpl =

  let publicTypes (asm: Assembly) =
#if NETSTANDARD
    asm.ExportedTypes
    |> Seq.filter (fun typ ->
      let typ = typ.GetTypeInfo()
      typ.IsClass && not typ.IsGenericTypeDefinition
    )
#else
    asm.GetTypes()
    |> Seq.filter (fun typ -> typ.IsPublic && typ.IsClass && not typ.IsGenericTypeDefinition)
#endif

  let private publicNestedTypes (typ: Type) =
#if NETSTANDARD
    typ.GetTypeInfo().DeclaredNestedTypes
    |> Seq.choose (fun typ ->
      if typ.IsNestedPublic && typ.IsClass && not typ.IsGenericTypeDefinition then
        Some(typ.AsType())
      else None)
#else
    typ.GetNestedTypes()
    |> Seq.filter (fun typ -> typ.IsNestedPublic && typ.IsClass && not typ.IsGenericTypeDefinition)
#endif

  /// Traverse test instances recursive.
  /// <param name="partialSuggest">Nested sequence children is true: symbol naming is pseudo.</param>
  let rec private fixupAndCollectTests (testObject: obj, symbolName: string, index: int option) = seq {
    match testObject with

    /////////////////////////////////////////////////////
    // For test case:
    | :? TestCase as testCase ->
      // Set symbol name.
      match index with
      | Some i -> testCase.TrySetIndex i
      | None -> testCase.TrySetSymbolName symbolName
      yield testCase :> TestMetadata

    /////////////////////////////////////////////////////
    // For context:
    | :? Context as context ->
      // Set symbol name.
      match index with
      | Some i -> context.TrySetIndex i
      | None -> context.TrySetSymbolName symbolName
      yield context :> TestMetadata

    /////////////////////////////////////////////////////
    // For test objects (sequence, ex: array/list):
    // let tests = [                     --+
    //  test "success test(list)" {        |
    //    ...                              |
    //  }                                  | testObject
    //  test "failure test(list)" {        |
    //    ...                              |
    //  }                                  |
    // ]                                 --+
    | :? (TestMetadata seq) as tests ->
      // Nested children's symbol naming is pseudo.
      // "parentNamed[0]", "parentNamed[0][0]", ...
      let children =
        tests
        |> Seq.mapi (fun index child -> (child, index))
        |> Seq.collect (fun entry -> fixupAndCollectTests(fst entry, symbolName, Some (snd entry)))
      yield Context(symbolName, [], children) :> TestMetadata

    /////////////////////////////////////////////////////
    // Unknown type, ignored.
    | _ -> ()
  }

  let typedefis<'T>(typ: Type) =
#if NETSTANDARD
    typ.GetTypeInfo().IsGenericType
#else
    typ.IsGenericType
#endif
    && typ.GetGenericTypeDefinition() = typedefof<'T>

  let (|SubTypeOf|_|) (matching: Type) (typ: Type) =
#if NETSTANDARD
    if matching.GetTypeInfo().IsAssignableFrom(typ.GetTypeInfo()) then Some typ else None
#else
    if matching.IsAssignableFrom(typ) then Some typ else None
#endif
  let (|ArrayType|_|) (typ: Type) = if typ.IsArray then Some (typ.GetElementType()) else None
  let (|GenericType|_|) (typ: Type) =
#if NETSTANDARD
    let info = typ.GetTypeInfo()
    if info.IsGenericType then
      Some (typ.GetGenericTypeDefinition(), info.GenericTypeArguments)
#else
    if typ.IsGenericType then
      Some (typ.GetGenericTypeDefinition(), typ.GetGenericArguments())
#endif
    else
      None

  let collectPersimmonTests (f: unit -> obj) (typ: Type) name =
    let testMetadataType = typeof<TestMetadata>
    match typ with
    | SubTypeOf testMetadataType _ ->
      fixupAndCollectTests (f (), name, None)
    | ArrayType elemType when typedefis<TestCase<_>>(elemType) || elemType = testMetadataType ->
      fixupAndCollectTests (f (), name, None)
    | GenericType (genTypeDef, _) when genTypeDef = typedefof<TestCase<_>> ->
      fixupAndCollectTests (f (), name, None)
    | GenericType (genTypeDef, [| elemType |]) when genTypeDef = typedefof<_ seq> && (typedefis<TestCase<_>>(elemType) || elemType = testMetadataType) ->
      fixupAndCollectTests (f (), name, None)
    | GenericType (genTypeDef, [| elemType |]) when genTypeDef = typedefof<_ list> && (typedefis<TestCase<_>>(elemType) || elemType = testMetadataType) ->
      fixupAndCollectTests (f (), name, None)
    | _ -> Seq.empty

  /// Retreive test object via target property, and traverse.
  let private collectTestsFromProperty (p: PropertyInfo) =
    collectPersimmonTests (fun () -> p.GetValue(null, null)) p.PropertyType p.Name

  /// Retreive test object via target method, and traverse.
  let private collectTestsFromMethod (m: MethodInfo) =
    collectPersimmonTests (fun () -> m.Invoke(null, [||])) m.ReturnType m.Name

  let private collectCategories (typ: Type) =
#if NETSTANDARD
    let info = typ.GetTypeInfo()
    info.GetCustomAttributes(typeof<CategoryAttribute>, true)
#else
    typ.GetCustomAttributes(typeof<CategoryAttribute>, true)
#endif
    |> Seq.collect (fun attr -> (attr :?> CategoryAttribute).Categories)
    |> Seq.toArray

  /// Retreive test object via target type, and traverse.
  let rec collectTests (typ: Type) =
    seq {
      // For properties (value binding):
      yield!
        typ
#if NETSTANDARD
          .GetTypeInfo().DeclaredProperties
        |> Seq.filter (fun p ->
          let m = p.GetMethod
          (m <> null) && m.IsStatic && m.IsPublic
            // Ignore setter only property / indexers
            && p.CanRead && (p.GetIndexParameters() |> Array.isEmpty)
        )
#else
          .GetProperties(BindingFlags.Static ||| BindingFlags.Public)
        // Ignore setter only property / indexers
        |> Seq.filter (fun p -> p.CanRead && (p.GetGetMethod() <> null) && (p.GetIndexParameters() |> Array.isEmpty))
#endif
        |> Seq.collect collectTestsFromProperty
      // For methods (function binding):
      yield!
        typ
#if NETSTANDARD
          .GetTypeInfo().DeclaredMethods
        // Ignore getter methods / open generic methods / method has parameters
        |> Seq.filter (fun m ->
          m.IsStatic && m.IsPublic &&
          not m.IsSpecialName && not m.IsGenericMethodDefinition && (m.GetParameters() |> Array.isEmpty)
        )
#else
          .GetMethods(BindingFlags.Static ||| BindingFlags.Public)
        // Ignore getter methods / open generic methods / method has parameters
        |> Seq.filter (fun m -> not m.IsSpecialName && not m.IsGenericMethodDefinition && (m.GetParameters() |> Array.isEmpty))
#endif
        |> Seq.collect collectTestsFromMethod
      // For nested modules:
#if NETSTANDARD
#else
      for nestedType in publicNestedTypes typ do
        match collectTestsAsContext nestedType with
        | Some t -> yield t
        | None -> ()
#endif
    }
  and private collectTestsAsContextImpl name (typ: Type) =
    let tests = collectTests typ |> Seq.toArray
    if Array.isEmpty tests then
      None
    else
      let categories = collectCategories typ
      Some (Context(name, categories, tests) :> TestMetadata)
  and collectTestsAsContext (typ: Type) =
    collectTestsAsContextImpl typ.Name typ

  /// Collect test cases from assembly
  let collect targetAssembly =
    targetAssembly
    |> publicTypes
    |> Seq.choose (fun t -> collectTestsAsContextImpl t.FullName t)

  /// Remove contexts and flatten structured test objects.
  let rec flattenTestCase (testMetadata: TestMetadata) = seq {
    match testMetadata with
    | :? Context as context ->
      for child in context.Children do
        yield! flattenTestCase child
    | :? TestCase as testCase -> yield testCase
    | _ -> ()
  }

[<Sealed>]
type TestCollector() =
  /// Collect tests with basic procedure.
  member __.Collect targetAssembly =
    TestCollectorImpl.collect targetAssembly |> Seq.toArray

  /// Collect test cases.
  member __.CollectOnlyTestCases targetAssembly =
    TestCollectorImpl.collect targetAssembly
    |> Seq.collect TestCollectorImpl.flattenTestCase
    |> Seq.toArray

  /// CollectAndCallback collect test cases and callback. (Internal use only)
  member __.CollectAndCallback(targetAssembly, callback: Action<obj>) =
    TestCollectorImpl.collect targetAssembly
    |> Seq.collect TestCollectorImpl.flattenTestCase
    |> Seq.iter callback.Invoke
