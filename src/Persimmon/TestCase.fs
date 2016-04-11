﻿namespace Persimmon

open System
open System.Diagnostics

/// This DU represents the type of the test case.
/// If the test has some return values, then the type of the test case is HasValueTest.
/// If not, then it is NoValueTest.
type TestCaseType<'T> =
    /// The TestCase does not have any return values.
    /// It means that the TestCase is TestCase<unit>.
  | NoValueTest of TestCase<'T>
    /// The TestCase has some return values.
    /// It means that the TestCase is not TestCase<unit>.
  | HasValueTest of TestCase<'T>

module TestCase =
  let make name parameters x =
    new TestCase<_>(name, parameters, fun testCase -> Done (testCase, NonEmptyList.singleton x, TimeSpan.Zero))

  let makeError name parameters exn =
    new TestCase<_>(name, parameters, fun testCase -> Error (testCase, [exn], [], TimeSpan.Zero))

  let addNotPassed notPassedCause (x: TestCase<_>) =
    new TestCase<_>(x.Name, x.Parameters, fun testCase -> x.Run() |> TestResult.addAssertionResult (NotPassed notPassedCause))

  let private runNoValueTest (x: TestCase<'T>) (rest: 'T -> TestCase<'U>) =
    match x.Run() with
    | Done (testCase, (Passed unit, []), duration) ->
      let watch = Stopwatch.StartNew()
      try
        try (rest unit).Run() |> TestResult.addDuration duration
        finally watch.Stop()
      with e ->
        watch.Stop()
        Error (testCase, [e], [], duration + watch.Elapsed)
    | Done (testCase, assertionResults, duration) ->
      // If the TestCase does not have any values,
      // even if the assertion is not passed,
      // the test is continuable.
      // So, continue the test.
      let notPassed =
        assertionResults
        |> NonEmptyList.toList
        |> AssertionResult.List.onlyNotPassed
      let watch = Stopwatch.StartNew()
      try
        match notPassed with
        | [] -> failwith "oops!"
        | head::tail ->
          assert (typeof<'T> = typeof<unit>)
          // continue the test!
          let testRes = (rest Unchecked.defaultof<'T>).Run()
          watch.Stop()
          testRes
          |> TestResult.addAssertionResults (NonEmptyList.make (NotPassed head) (tail |> List.map NotPassed))
          |> TestResult.addDuration duration
      with e ->
        watch.Stop()
        Error (testCase, [e], notPassed, duration + watch.Elapsed)
    | Error (testCase, es, results, duration) ->
      // If the TestCase does not have any values,
      // even if the assertion is not passed,
      // the test is continuable.
      // So, continue the test.
      let watch = Stopwatch.StartNew()
      try
        assert (typeof<'T> = typeof<unit>)
        // continue th test!
        let testRes = (rest Unchecked.defaultof<'T>).Run()
        watch.Stop()
        match results with
        | [] -> testRes
        | head::tail ->
          testRes
          |> TestResult.addAssertionResults (NonEmptyList.make (NotPassed head) (tail |> List.map NotPassed))
          |> TestResult.addDuration duration
      with e ->
        watch.Stop()
        Error (testCase, e::es, results, duration + watch.Elapsed)

  let private runHasValueTest (x: TestCase<'T>) (rest: 'T -> TestCase<'U>) =
    match x.Run() with
    | Done (testCase, (Passed value, []), duration) ->
      let watch = Stopwatch.StartNew()
      try
        let result = (rest value).Run()
        watch.Stop()
        result
      with e ->
        watch.Stop()
        Error (testCase, [e], [], duration + watch.Elapsed)
    | Done (testCase, assertionResults, duration) ->
      // If the TestCase has some values,
      // the test is not continuable.
      let notPassed =
        assertionResults
        |> NonEmptyList.toList
        |> AssertionResult.List.onlyNotPassed
      match notPassed with
      | [] -> failwith "oops!"
      | head::tail -> Done (testCase, NonEmptyList.make (NotPassed head) (tail |> List.map NotPassed), duration)
    | Error (testCase, es, results, duration) ->
      // If the TestCase has some values,
      // the test is not continuable.
      Error (testCase, es, results, duration)

  let combine (x: TestCaseType<'T>) (rest: 'T -> TestCase<'U>) =
    match x with
    | NoValueTest x ->
      TestCase<_>(x.Name, x.Parameters, fun _ -> runNoValueTest x rest)
    | HasValueTest x ->
      TestCase<_>(x.Name, x.Parameters, fun _ -> runHasValueTest x rest)
