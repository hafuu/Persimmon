﻿namespace Persimmon

open System
open System.Diagnostics

// Utility functions of TestResult<'T>
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TestResult =

  let private endMarkerTestBody (tc:TestCase<unit>) : Async<TestResult<unit>> = async {
    return InvalidOperationException() |> raise
  }
  let private endMarkerTestCase =
    new TestCase<unit>(Some "endMarker", [], [], endMarkerTestBody) :> TestCase
  let private endMarkerResult : AssertionResult<unit> = NotPassed (None, Skipped "endMarker")
  let private endMarkerResults = [endMarkerResult] |> NonEmptyList.ofSeq

  /// The marker represents the end of tests.
  /// The progress reporter needs the end marker in order to print new line at the end.
  let endMarker = Done (endMarkerTestCase, endMarkerResults, TimeSpan.Zero) :> TestResult

  let addAssertionResult x = function
    | Done (testCase, (Passed _, []), d) -> Done (testCase, NonEmptyList.singleton x, d)
    | Done (testCase, results, d) -> Done (testCase, NonEmptyList.cons x results, d)
    | Error (testCase, es, results, d) -> Error (testCase, es, (match x with Passed _ -> results | NotPassed(_, x) -> x::results), d)

  let addAssertionResults (xs: NonEmptyList<AssertionResult<_>>) = function
    | Done (testCase, (Passed _, []), d) -> Done (testCase, xs, d)
    | Done (testCase, results, d) ->
      Done (testCase, NonEmptyList.appendSeq xs (results |> NonEmptyList.toList |> AssertionResult.Seq.onlyNotPassed |> NotPassedCause.Seq.toAssertionResultList), d)
    | Error (testCase, es, results, d) ->
      Error (testCase, es, (xs |> NonEmptyList.toSeq |> AssertionResult.Seq.onlyNotPassed |> Seq.toList)@results, d)

  let addDuration x = function
    | Done (testCase, results, d) -> Done (testCase, results, d + x)
    | Error (testCase, es, results, ts) -> Error (testCase, es, results, ts + x)

  let addExceptions (es: ExceptionWrapper []) = function
    | Done (testCase, (Passed _, []), d) -> Error(testCase, es, [], d)
    | Done (testCase, results, d) ->
      let results =
        results
        |> NonEmptyList.toList
        |> AssertionResult.Seq.onlyNotPassed
        |> Seq.toList
      Error(testCase, es, results, d)
    | Error (testCase, es0, results, d) ->
      Error (testCase, Array.append es0 es, results, d)
