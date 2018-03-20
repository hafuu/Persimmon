﻿namespace Persimmon.Tests

open Persimmon
open UseTestNameByReflection
open Helper

module AsyncTest =

  let asyncReturnValue = async {
    return 0
  }

  let ``should return value`` = test {
    let! v = asyncRun { it asyncReturnValue }
    do! assertEquals 0 v
  }

  exception MyException

  let asyncRaiseMyException = async {
    raise MyException
  }

  let ``should catch unhandled exception`` = test {
    let! e =
      match asyncRun { it asyncRaiseMyException } |> run with
      | Error(m, es, _, _) -> TestCase.makeDone m.Name m.Categories m.Parameters (Passed (es.[0]))
      | Done(m, xs, _) -> TestCase.makeDone m.Name m.Categories m.Parameters (NotPassed(None, Violated "expected throw exception, but was success"))
    do! assertEquals (typeof<MyException>.FullName) (e.FullTypeName)
  }
