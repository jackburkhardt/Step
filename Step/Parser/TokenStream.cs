﻿#region Copyright
// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TokenStream.cs" company="Ian Horswill">
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
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Step.Parser
{
    /// <summary>
    /// Transforms a stream of characters into a sequence of tokens (strings not containing whitespace)
    /// </summary>
    public class TokenStream
    {
        public TokenStream(TextReader input)
        {
            this.input = input;
        }

        #region Token buffer managment
        /// <summary>
        /// Buffer for accumulating characters into tokens
        /// </summary>
        private readonly StringBuilder token = new StringBuilder();

        /// <summary>
        /// True it token buffer non-empty
        /// </summary>
        private bool HaveToken => token.Length > 0;

        /// <summary>
        /// Add current stream character to token
        /// </summary>
        void AddCharToToken() => token.Append(Get());

        /// <summary>
        /// Return the accumulated characters as a token and clear the token buffer.
        /// </summary>
        string ConsumeToken()
        {
            var newToken = token.ToString();
            token.Length = 0;
            return newToken;
        }
        #endregion

        #region Stream interface
        /// <summary>
        /// The raw text stream
        /// </summary>
        private readonly TextReader input;

        /// <summary>
        /// True if we're at the end of the stream
        /// </summary>
        bool End => input.Peek() < 0;

        /// <summary>
        /// Return the current character, without advancing
        /// </summary>
        char Peek => (char) (input.Peek());

        /// <summary>
        /// Return the current character and advance to the next
        /// </summary>
        /// <returns></returns>
        private char Get() => (char) (input.Read());

        /// <summary>
        /// Synonym for Get().  Used to indicate the character is being deliberately thrown away.
        /// </summary>
        private void Skip() => Get();

        /// <summary>
        /// Skip over all whitespace chars, except newlines (they're considered tokens)
        /// </summary>
        void SkipWhitespace()
        {
            while (IsWhiteSpace) Skip();
        }
        #endregion

        #region Character classification
        /// <summary>
        /// Current character is non-newline whitespace
        /// </summary>
        bool IsWhiteSpace => char.IsWhiteSpace(Peek)  && Peek != '\n';

        /// <summary>
        /// Current character is some punctuation symbol other than '?'
        /// '?' is treated specially because it's allowed to start a variable-name token.
        /// </summary>
        private bool IsPunctuationNotQuestionMark => char.IsPunctuation(Peek) && Peek != '?';

        /// <summary>
        /// True if the current character can't be a continuation of a word token.
        /// </summary>
        private bool IsEndOfWord
        {
            get        
            {
                var c = Peek;
                return char.IsWhiteSpace(c) || char.IsPunctuation(c);
            }
        }
        #endregion

        /// <summary>
        /// The stream of tokens read from the stream.
        /// </summary>
        public IEnumerable<string> Tokens
        {
            get
            {
                while (!End)
                {
                    SkipWhitespace();
                    // Start of token
                    Debug.Assert(token.Length == 0);
                    // Handle any single-character tokens
                    while (IsPunctuationNotQuestionMark || Peek == '\n')
                        yield return Get().ToString();
                    // Allow ?'s at the start of word tokens
                    if (Peek == '?')
                        AddCharToToken();
                    // Now we should be at something like a word or number
                    while (!End && !IsEndOfWord)
                        AddCharToToken();
                    if (HaveToken)
                        yield return ConsumeToken();
                }
            }
        }
    }
}
