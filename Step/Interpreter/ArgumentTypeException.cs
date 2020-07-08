﻿#region Copyright
// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ArgumentTypeException.cs" company="Ian Horswill">
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

namespace Step.Interpreter
{
    /// <summary>
    /// Signals a task was called with the wrong kind of argument
    /// </summary>
    public class ArgumentTypeException : ArgumentException
    {
        /// <inheritdoc />
        public ArgumentTypeException(object task, Type expected, object actual) 
            : base($"Wrong argument type in call to {task}, expected {expected.Name}, got {actual}")
        { }

        /// <summary>
        /// Check the specified argument value is of the right type.  If not, throw exception
        /// </summary>
        /// <param name="task">Name of task - used in error message if necessary</param>
        /// <param name="expected">Type expected</param>
        /// <param name="actual">Value provided</param>
        /// <exception cref="ArgumentTypeException">When value isn't of the expected type</exception>
        public static void Check(object task, Type expected, object actual)
        {
            if (!expected.IsInstanceOfType(actual))
                throw new ArgumentTypeException(task, expected, actual);
        }
    }
}