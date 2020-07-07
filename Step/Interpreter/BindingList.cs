﻿#region Copyright
// --------------------------------------------------------------------------------------------------------------------
// <copyright file="BindingList.cs" company="Ian Horswill">
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

namespace Step.Interpreter
{
    /// <summary>
    /// Represents values of variables of different types.
    /// In the case of LocalVariables, which can be unified, this might be another variable,
    /// in which case the bound variable has whatever value the other variable has.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class BindingList<T>
    {
        public readonly T Variable;
        public readonly object Value;
        public readonly BindingList<T> Next;

        public BindingList(T variable, object value, BindingList<T> next)
        {
            Variable = variable;
            Value = value;
            Next = next;
        }

        public static bool TryLookup(BindingList<T> bindingList, T variable, out object value)
        {
            for (var cell = bindingList; cell != null; cell = cell.Next)
                if (ReferenceEquals(cell.Variable, variable))
                {
                    value = cell.Value;
                    return true;
                }

            value = null;
            return false;
        }

        public static object Lookup(BindingList<T> bindingList, T v, object defaultValue)
            => bindingList == null ? defaultValue : bindingList.Lookup(v, defaultValue);

        public object Lookup(T v, object defaultValue)
        {
            for (var cell = this; cell != null; cell = cell.Next)
                if (ReferenceEquals(cell.Variable, v))
                    return cell.Value;
            return defaultValue;
        }


        /// <summary>
        /// Make a new binding list with specified additional binding
        /// </summary>
        public BindingList<T> Bind(T variable, object value)
        {
            return new BindingList<T>(variable, value, this);
        }
    }
}