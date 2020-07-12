﻿#region Copyright
// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ExpressionStream.cs" company="Ian Horswill">
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

using System.Collections.Generic;
using System.IO;

namespace Step.Parser
{
    /// <summary>
    /// Transforms a sequence of tokens into a sequence of expressions
    /// An expression is either a token, or an array of tokens generated by a bracketed expression.
    /// </summary>
    public class ExpressionStream
    {
        /// <summary>
        /// Make an object that reads a stream of nested expressions from a file
        /// </summary>
        public ExpressionStream(TextReader stream, string filePath) : this(new TokenStream(stream, filePath))
        { }

        /// <summary>
        /// Make an object that reads a stream of nested expressions from a TokenStream
        /// </summary>
        public ExpressionStream(TokenStream tokens)
        {
            tokenStream = tokens;
            this.tokens = tokens.Tokens.GetEnumerator();
            MoveNext();
        }

        private readonly TokenStream tokenStream;

        /// <summary>
        /// File from which this data is being read, if any
        /// </summary>
        public string FilePath => tokenStream.FilePath;

        /// <summary>
        /// Line number in file from which we are currently reading.
        /// </summary>
        public int LineNumber => tokenStream.LineNumber;

        #region TokenStream interface
        /// <summary>
        /// Sequence of tokens read from the original stream
        /// </summary>
        private readonly IEnumerator<string> tokens;
        /// <summary>
        /// True if we've hit the end of the stream
        /// </summary>
        private bool end;

        /// <summary>
        /// Move to the next token in the stream
        /// </summary>
        private void MoveNext()
        {
            end = !tokens.MoveNext();
        }

        /// <summary>
        /// Return the current token and move to the next
        /// </summary>
        private string Get()
        {
            var tok = tokens.Current;
            MoveNext();
            return tok;
        }

        /// <summary>
        /// Returns the current token without advancing to the next token.
        /// </summary>
        private string Peek => tokens.Current;
        #endregion

        /// <summary>
        /// Sequence of tokens, with bracketed groups of expressions replace with a single array.
        /// </summary>
        public IEnumerable<object> Expressions
        {
            get
            {
                List<object> buffer = new List<object>();
                while (!end)
                {
                    while (!end && Peek != "[")
                    {
                        var token = Get();
                        if (token == "]")
                            throw new SyntaxError("Stray close bracket found without matching open bracket");
                        yield return token;
                    }
                    if (!end)
                    {
                        // We're at the start of a bracketed expression
                        Get(); // Swallow [
                        buffer.Clear();
                        while (!end && Peek != "]")
                        {
                            var token = Get();
                            if (token == "[")
                                buffer.Add(ReadSubExpression());
                            else
                                buffer.Add(token);
                        }
                        if (end)
                            throw new SyntaxError(
                                "Incomplete expression: open bracket without a matching close bracket");
                        Get(); // Swallow ]
                        yield return buffer.ToArray();
                    }
                }
            }
        }

        private object[] ReadSubExpression()
        {
            var buffer = new List<object>();

            while (!end && Peek != "]")
            {
                var token = Get();
                if (token == "[")
                    buffer.Add(ReadSubExpression());
                else
                    buffer.Add(token);
            }
            if (end)
                throw new SyntaxError(
                    "Incomplete expression: open bracket without a matching close bracket");
            Get(); // Swallow ]
            return buffer.ToArray();
        }
    }
}
