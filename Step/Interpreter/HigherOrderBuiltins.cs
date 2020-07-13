﻿#region Copyright
// --------------------------------------------------------------------------------------------------------------------
// <copyright file="HigherOrderBuiltins.cs" company="Ian Horswill">
// Copyright (C) 2020 Ian Horswill
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in the
// Software without restriction, including without limitation the rights to use, copy,
// modify, merge, publish, distribute, sublicense, and/or sell copies of the Software,
// and to permit persons to whom the Software is furnished to do so, subject to the
// following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using static Step.Interpreter.PrimitiveTask;

namespace Step.Interpreter
{
    /// <summary>
    /// Implementations of higher-order builtin primitives.
    /// </summary>
    internal static class HigherOrderBuiltins
    {
        internal static void DefineGlobals()
        {
            var g = Module.Global;

            g["DoAll"] = (DeterministicTextGeneratorMetaTask) DoAll;
            g["Once"] = (MetaTask) Once;
            g["ExactlyOnce"] = (MetaTask) ExactlyOnce;
            g["Max"] = (MetaTask) Max;
            g["Min"] = (MetaTask) Min;
        }

        private static IEnumerable<string> DoAll(object[] args, PartialOutput o, BindingEnvironment e)
        {
            return AllSolutionTextFromBody("DoAll", args, o, e).SelectMany(strings => strings);
        }

        private static bool Once(object[] args, PartialOutput o, BindingEnvironment e, Step.Continuation k)
        {
            try
            {
                StepChainFromBody("Once", args).Try(o, e, NonLocalExit.Throw);
            }
            catch (NonLocalExit x)
            {
                return k(x.Output, x.Bindings, x.DynamicState);
            }

            return false;
        }

        private static bool ExactlyOnce(object[] args, PartialOutput o, BindingEnvironment e, Step.Continuation k)
        {
            ArgumentCountException.Check("ExactlyOnce", 1, args);
            var chain = StepChainFromBody("Once", args);
            try
            {
                chain.Try(o, e, NonLocalExit.Throw);
            }
            catch (NonLocalExit x)
            {
                return k(x.Output, x.Bindings, x.DynamicState);
            }

            var failedCall = (Call) chain;
            throw new CallFailedException(failedCall.Task, e.ResolveList(failedCall.Arglist));
        }

        private static bool Max(object[] args, PartialOutput o, BindingEnvironment e, Step.Continuation k)
        {
            return MaxMinDriver("Max", args, 1, o, e, k);
        }

        private static bool Min(object[] args, PartialOutput o, BindingEnvironment e, Step.Continuation k)
        {
            return MaxMinDriver("Min", args, -1, o, e, k);
        }

        /// <summary>
        /// Core implementation of both Max and Min
        /// </summary>
        private static bool MaxMinDriver(string taskName, object[] args,
            int multiplier, PartialOutput o, BindingEnvironment e, Step.Continuation k)
        {
            var scoreVar = args[0] as LogicVariable;
            if (scoreVar == null)
                throw new ArgumentInstantiationException(taskName, e, args);

            var bestScore = multiplier * float.NegativeInfinity;
            CapturedState bestResult = new CapturedState();
            var gotOne = false;

            GenerateSolutionsFromBody(taskName, args.Skip(1).ToArray(), o, e,
                (output, u, d) =>
                {
                    gotOne = true;

                    var maybeScore = u.Lookup(scoreVar, scoreVar);
                    float score;
                    switch (maybeScore)
                    {
                        case int i:
                            score = i;
                            break;

                        case float f:
                            score = f;
                            break;

                        case double df:
                            score = (float) df;
                            break;

                        case LogicVariable _:
                            throw new ArgumentInstantiationException(taskName, new BindingEnvironment(e, u, d), args);

                        default:
                            throw new ArgumentTypeException(taskName, typeof(float), maybeScore, args);
                    }

                    if (multiplier * score > multiplier * bestScore)
                    {
                        bestScore = score;
                        bestResult = new CapturedState(o, output, u, d);
                    }

                    // Always ask for another solution
                    return false;
                });

            // When we get here, we've iterated through all solutions and kept the best one.
            // So pass it on to our continuation
            return gotOne
                   && k(o.Append(bestResult.Output), bestResult.Bindings, bestResult.DynamicState);
        }

        #region Data structures for recording execution state
        /// <summary>
        /// Used to record the results of a call so those results can be reapplied later.
        /// Used for all-solutions and maximization meta-predicates
        /// </summary>
        private struct CapturedState
        {
            public readonly string[] Output;
            public readonly BindingList<LogicVariable> Bindings;
            public readonly BindingList<GlobalVariableName> DynamicState;

            //private static readonly string[] EmptyOutput = new string[0];

            private CapturedState(string[] output, BindingList<LogicVariable> bindings, BindingList<GlobalVariableName> dynamicState)
            {
                Output = output;
                Bindings = bindings;
                DynamicState = dynamicState;
            }

            public CapturedState(PartialOutput before, PartialOutput after, BindingList<LogicVariable> bindings,
                BindingList<GlobalVariableName> dynamicState)
                : this(PartialOutput.Difference(before, after), bindings, dynamicState)
            { }
        }

        /// <summary>
        /// Used to force control transfer to a surrounding call, preventing backtracking over the intervening calls.
        /// </summary>
        private class NonLocalExit : Exception
        {
            public readonly PartialOutput Output;
            public readonly BindingList<LogicVariable> Bindings;
            public readonly BindingList<GlobalVariableName> DynamicState;

            private NonLocalExit(PartialOutput output, BindingList<LogicVariable> bindings, BindingList<GlobalVariableName> dynamicState)
            {
                Output = output;
                Bindings = bindings;
                DynamicState = dynamicState;
            }

            public static bool Throw(PartialOutput output, BindingList<LogicVariable> environment,
                BindingList<GlobalVariableName> dynamicState)
            {
                throw new NonLocalExit(output, environment, dynamicState);
            }
        }
        #endregion

        #region Utilities for higher-order primitives
        /// <summary>
        /// Calls a task with the specified arguments and allows the user to provide their own continuation.
        /// The only (?) use case for this is when you want to forcibly generate multiple solutions
        /// </summary>
        internal static void GenerateSolutions(string taskName, object[] args, PartialOutput o, BindingEnvironment e, Step.Continuation k)
        {
            new Call(GlobalVariableName.Named(taskName), args, null).Try(o, e, k);
        }

        /// <summary>
        /// Find all solutions to the specified task and arguments.  Return a list of the arglists for each solution.
        /// </summary>
        internal static List<object[]> AllSolutions(string taskName, object[] args, PartialOutput o, BindingEnvironment e)
        {
            var results = new List<object[]>();
            GenerateSolutions(taskName, args, o, e, (output, b, d) =>
            {
                results.Add(new BindingEnvironment(e, b, d).ResolveList(args));
                return false;
            });
            return results;
        }

        /// <summary>
        /// Find all solutions to the specified task and arguments.  Return a list of the text outputs of each solution.
        /// </summary>
        internal static List<string[]> AllSolutionText(string taskName, object[] args, PartialOutput o, BindingEnvironment e)
        {
            var results = new List<string[]>();
            var initialLength = o.Length;
            GenerateSolutions(taskName, args, o, e, (output, b, d) =>
            {
                var chunk = new string[output.Length - initialLength];
                for (var i = initialLength; i < output.Length; i++)
                    chunk[i - initialLength] = o.Buffer[i];
                results.Add(chunk);
                return false;
            });
            return results;
        }

        /// <summary>
        /// Calls all the tasks in the body and allows the user to provide their own continuation.
        /// The only (?) use case for this is when you want to forcibly generate multiple solutions
        /// </summary>
        internal static void GenerateSolutionsFromBody(string callingTaskName, object[] body, PartialOutput o, BindingEnvironment e, Step.Continuation k)
        {
            StepChainFromBody(callingTaskName, body).Try(o, e, k);
        }

        internal static Step StepChainFromBody(string callingTaskName, object[] body)
        {
            Step chain = null;
            for (var i = body.Length - 1; i >= 0; i--)
            {
                if (body[i].Equals("\n"))
                    continue;
                var invocation = body[i] as object[];
                if (invocation == null  || invocation.Length == 0)
                    throw new ArgumentTypeException(callingTaskName, typeof(Call), body[i], body);
                var arglist = new object[invocation.Length - 1];
                Array.Copy(invocation, 1, arglist, 0, arglist.Length);
                chain = new Call(invocation[0], arglist, chain);
            }

            return chain;
        }

        /// <summary>
        /// Find all solutions to the specified sequence of calls.  Return a list of the text outputs of each solution.
        /// </summary>
        internal static List<string[]> AllSolutionTextFromBody(string callingTaskName, object[] body, PartialOutput o, BindingEnvironment e)
        {
            var results = new List<string[]>();
            var initialLength = o.Length;
            GenerateSolutionsFromBody(callingTaskName, body, o, e, (output, b, d) =>
            {
                var chunk = new string[output.Length - initialLength];
                for (var i = initialLength; i < output.Length; i++)
                    chunk[i - initialLength] = o.Buffer[i];
                results.Add(chunk);
                return false;
            });
            return results;
        }
        #endregion
    }
}
