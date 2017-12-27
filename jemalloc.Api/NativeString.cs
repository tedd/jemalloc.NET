﻿//B
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Buffers.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Primitives;
using System.Text.Utf8;

namespace jemalloc
{
    [DebuggerDisplay("{ToString()} utf8")]
    public readonly struct NativeString : IEquatable<NativeString>
    {
        #region Constructors
        public NativeString(ReadOnlySpan<byte> utf8Bytes) => buffer = new FixedBuffer<byte>(utf8Bytes);

        public NativeString(Utf8Span utf8Span) : this(utf8Span.Bytes) { }

        public NativeString(string utf16String)
        {
            if (utf16String == null)
            {
                throw new ArgumentNullException(nameof(utf16String));
            }

            if (utf16String == string.Empty)
            {
                buffer = new FixedBuffer<byte>();
            }
            else
            {
                buffer = new FixedBuffer<byte>(Encoding.UTF8.GetBytes(utf16String));
            }
        }

        private NativeString(byte[] utf8Bytes) => buffer = new FixedBuffer<byte>(utf8Bytes);
        
        #endregion

        #region Overidden members
        public override int GetHashCode() => Span.GetHashCode();

        public override string ToString() => Span.ToString();

        public override bool Equals(object obj)
        {
            if (obj is NativeString)
            {
                return Equals((NativeString)obj);
            }
            if (obj is string)
            {
                return Equals((string)obj);
            }

            return false;
        }
        #endregion

        #region Properties
        public static NativeString Empty => s_empty;

        public bool IsEmpty => Bytes.Length == 0;

        public ReadOnlySpan<byte> Bytes => buffer.WriteSpan;
        
        internal Utf8Span Span => new Utf8Span(Bytes);
        #endregion

        #region Operators
        public static bool operator ==(NativeString left, NativeString right) => left.Equals(right);
        public static bool operator !=(NativeString left, NativeString right) => !left.Equals(right);
        public static bool operator ==(NativeString left, Utf8Span right) => left.Equals(right);
        public static bool operator !=(NativeString left, Utf8Span right) => !left.Equals(right);
        public static bool operator ==(Utf8Span left, NativeString right) => right.Equals(left);
        public static bool operator !=(Utf8Span left, NativeString right) => !right.Equals(left);

        // TODO: do we like all these O(N) operators? 
        public static bool operator ==(NativeString left, string right) => left.Equals(right);
        public static bool operator !=(NativeString left, string right) => !left.Equals(right);
        public static bool operator ==(string left, NativeString right) => right.Equals(left);
        public static bool operator !=(string left, NativeString right) => !right.Equals(left);

        public static implicit operator ReadOnlySpan<byte>(NativeString utf8String) => utf8String.Bytes;

        public static implicit operator Utf8Span(NativeString utf8String) => utf8String.Span;

        public static explicit operator NativeString(string utf16String) => new NativeString(utf16String);

        public static explicit operator string(NativeString utf8String) => utf8String.ToString();
        #endregion

        #region Methods
        public bool Equals(NativeString other) => Bytes.SequenceEqual(other.Bytes);

        public bool Equals(Utf8Span other) => Bytes.SequenceEqual(other.Bytes);

        public bool Equals(string other) => Span.Equals(other);


        public Utf8CodePointEnumerator GetEnumerator() => new Utf8CodePointEnumerator(buffer.Span);

        public int CompareTo(NativeString other) => Span.CompareTo(other);

        public int CompareTo(string other) => Span.CompareTo(other);

        public int CompareTo(Utf8Span other) => Span.CompareTo(other);

        public bool StartsWith(uint codePoint) => Span.StartsWith(codePoint);

        public bool StartsWith(NativeString value) => Span.StartsWith(value.Span);

        public bool StartsWith(Utf8Span value) => Span.StartsWith(value);

        public bool EndsWith(NativeString value) => Span.EndsWith(value.Span);

        public bool EndsWith(Utf8Span value) => Span.EndsWith(value);

        public bool EndsWith(uint codePoint) => Span.EndsWith(codePoint);

        #region Slicing
        // TODO: should Utf8String slicing operations return Utf8Span? 
        // TODO: should we add slicing overloads that take char delimiters?
        // TODO: why do we even have Try versions? If the delimiter is not found, the result should be the original.
        public bool TrySubstringFrom(NativeString value, out NativeString result)
        {
            int idx = IndexOf(value);

            if (idx == StringNotFound)
            {
                result = default;
                return false;
            }

            result = Substring(idx);
            return true;
        }

        public bool TrySubstringFrom(uint codePoint, out NativeString result)
        {
            int idx = IndexOf(codePoint);

            if (idx == StringNotFound)
            {
                result = default;
                return false;
            }

            result = Substring(idx);
            return true;
        }

        public bool TrySubstringTo(NativeString value, out NativeString result)
        {
            int idx = IndexOf(value);

            if (idx == StringNotFound)
            {
                result = default;
                return false;
            }

            result = Substring(0, idx);
            return true;
        }

        public bool TrySubstringTo(uint codePoint, out NativeString result)
        {
            int idx = IndexOf(codePoint);

            if (idx == StringNotFound)
            {
                result = default;
                return false;
            }

            result = Substring(0, idx);
            return true;
        }
        #endregion

        #region Index-based operations
        // TODO: should we even have index based operations?
        // TODO: should we have search (e.g. IndexOf) overlaods that take char?

        public NativeString Substring(int index) => index == 0 ? this : Substring(index, Bytes.Length - index);

        public NativeString Substring(int index, int length)
        {
            if (length == 0)
            {
                return Empty;
            }
            if (index == 0 && length == Bytes.Length) return this;

            return new NativeString(buffer.Span.Slice(index, length));
        }

        public int IndexOf(NativeString value) => Bytes.IndexOf(value.Bytes);

        public int IndexOf(uint codePoint) => Span.IndexOf(codePoint);

        public int LastIndexOf(NativeString value) => Span.LastIndexOf(value.Span);

        public int LastIndexOf(uint codePoint) => Span.LastIndexOf(codePoint);

        public bool TryFormat(Span<byte> buffer, out int written, StandardFormat format = default, SymbolTable symbolTable = null)
        {
            if (!format.IsDefault) throw new ArgumentOutOfRangeException(nameof(format));
            if (symbolTable == SymbolTable.InvariantUtf8)
            {
                written = Bytes.Length;
                return Bytes.TryCopyTo(buffer);
            }

            return symbolTable.TryEncode(Bytes, buffer, out var consumed, out written);
        }
        #endregion

        /*
        // TODO: unless we change the type of Trim to Utf8Span, this double allocates.
        public FixedUtf8String Trim() => TrimStart().TrimEnd();

        // TODO: implement Utf8String.Trim(uint[])
        public FixedString Trim(uint[] codePoints) => throw new NotImplementedException();

        public FixedString TrimStart()
        {
            Utf8CodePointEnumerator it = GetEnumerator();
            while (it.MoveNext() &&Char.IsWhiteSpace(it.Current)) { }
            return Substring(it.PositionInCodeUnits);
        }

        public FixedUtf8String TrimStart(uint[] codePoints) {
            if (codePoints == null || codePoints.Length == 0) return TrimStart(); // Trim Whitespace

            Utf8CodePointEnumerator it = GetEnumerator();       
            while (it.MoveNext()) {
                if(Array.IndexOf(codePoints, it.Current) == -1){
                    break;
                }
            }

            return Substring(it.PositionInCodeUnits);
        }
        
        // TODO: do we even want this overload? System.String does not have an overload that takes string
        public FixedString TrimStart(FixedString characters)
        {
            if (characters == Empty)
            {
                // Trim Whitespace
                return TrimStart();
            }

            Utf8CodePointEnumerator it = GetEnumerator();
            Utf8CodePointEnumerator itPrefix = characters.GetEnumerator();

            while (it.MoveNext())
            {
                bool found = false;
                // Iterate over prefix set
                while (itPrefix.MoveNext())
                {
                    if (it.Current == itPrefix.Current)
                    {
                        // Character found, don't check further
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // Reached the end, char was not found
                    break;
                }

                itPrefix.Reset();
            }

            return Substring(it.PositionInCodeUnits);
        }

        public FixedString TrimEnd()
        {
            var it = new Utf8CodePointReverseEnumerator(Bytes);
            while (it.MoveNext() && Unicode.IsWhitespace(it.Current))
            {
            }

            return Substring(0, it.PositionInCodeUnits);
        }

        // TODO: implement Utf8String.TrimEnd(uint[])
        public FixedString TrimEnd(uint[] codePoints) => throw new NotImplementedException();

        // TODO: do we even want this overload? System.String does not have an overload that takes string
        public FixedString TrimEnd(FixedString characters)
        {
            if (characters == Empty)
            {
                // Trim Whitespace
                return TrimEnd();
            }

            var it = new Utf8CodePointReverseEnumerator(Bytes);
            Utf8CodePointEnumerator itPrefix = characters.GetEnumerator();

            while (it.MoveNext())
            {
                bool found = false;
                // Iterate over prefix set
                while (itPrefix.MoveNext())
                {
                    if (it.Current == itPrefix.Current)
                    {
                        // Character found, don't check further
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // Reached the end, char was not found
                    break;
                }

                itPrefix.Reset();
            }

            return Substring(0, it.PositionInCodeUnits);
        }
        */
        #endregion

        #region Fields
        //private readonly byte[] _buffer;
        private readonly FixedBuffer<byte> buffer;

        private const int StringNotFound = -1;

        static NativeString s_empty = new NativeString(string.Empty);
        #endregion

    }
}
