﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ICSharpCode.Decompiler.Tests.TestCases.Pretty
{
	public class Loops
	{
		#region foreach
		public class CustomClassEnumerator
		{
			public object Current {
				get {
					throw new NotImplementedException();
				}
			}

			public bool MoveNext()
			{
				throw new NotImplementedException();
			}

			public void Reset()
			{
				throw new NotImplementedException();
			}

			public CustomClassEnumerator GetEnumerator()
			{
				return this;
			}
		}

		[StructLayout(LayoutKind.Sequential, Size = 1)]
		public struct CustomStructEnumerator
		{
			public object Current {
				get {
					throw new NotImplementedException();
				}
			}

			public bool MoveNext()
			{
				throw new NotImplementedException();
			}

			public void Reset()
			{
				throw new NotImplementedException();
			}

			public CustomStructEnumerator GetEnumerator()
			{
				return this;
			}
		}

		public class CustomClassEnumerator<T>
		{
			public T Current {
				get {
					throw new NotImplementedException();
				}
			}

			public void Dispose()
			{
				throw new NotImplementedException();
			}

			public bool MoveNext()
			{
				throw new NotImplementedException();
			}

			public void Reset()
			{
				throw new NotImplementedException();
			}

			public CustomClassEnumerator<T> GetEnumerator()
			{
				return this;
			}
		}

		[StructLayout(LayoutKind.Sequential, Size = 1)]
		public struct CustomStructEnumerator<T>
		{
			public T Current {
				get {
					throw new NotImplementedException();
				}
			}

			public void Dispose()
			{
				throw new NotImplementedException();
			}

			public bool MoveNext()
			{
				throw new NotImplementedException();
			}

			public void Reset()
			{
				throw new NotImplementedException();
			}

			public CustomStructEnumerator<T> GetEnumerator()
			{
				return this;
			}
		}

		public class CustomClassEnumeratorWithIDisposable : IDisposable
		{
			public object Current {
				get {
					throw new NotImplementedException();
				}
			}

			public void Dispose()
			{
				throw new NotImplementedException();
			}

			public bool MoveNext()
			{
				throw new NotImplementedException();
			}

			public void Reset()
			{
				throw new NotImplementedException();
			}

			public CustomClassEnumeratorWithIDisposable GetEnumerator()
			{
				return this;
			}
		}

		[StructLayout(LayoutKind.Sequential, Size = 1)]
		public struct CustomStructEnumeratorWithIDisposable : IDisposable
		{
			public object Current {
				get {
					throw new NotImplementedException();
				}
			}

			public void Dispose()
			{
				throw new NotImplementedException();
			}

			public bool MoveNext()
			{
				throw new NotImplementedException();
			}

			public void Reset()
			{
				throw new NotImplementedException();
			}

			public CustomStructEnumeratorWithIDisposable GetEnumerator()
			{
				return this;
			}
		}

		public class CustomClassEnumeratorWithIDisposable<T> : IDisposable
		{
			public T Current {
				get {
					throw new NotImplementedException();
				}
			}

			public void Dispose()
			{
				throw new NotImplementedException();
			}

			public bool MoveNext()
			{
				throw new NotImplementedException();
			}

			public void Reset()
			{
				throw new NotImplementedException();
			}

			public CustomClassEnumeratorWithIDisposable<T> GetEnumerator()
			{
				return this;
			}
		}

		[StructLayout(LayoutKind.Sequential, Size = 1)]
		public struct CustomStructEnumeratorWithIDisposable<T> : IDisposable
		{
			public T Current {
				get {
					throw new NotImplementedException();
				}
			}

			public void Dispose()
			{
				throw new NotImplementedException();
			}

			public bool MoveNext()
			{
				throw new NotImplementedException();
			}

			public void Reset()
			{
				throw new NotImplementedException();
			}

			public CustomStructEnumeratorWithIDisposable<T> GetEnumerator()
			{
				return this;
			}
		}

		private static void Operation(ref int item)
		{
		}

		private static void Operation(Func<bool> f)
		{
		}

		public void ForEach(IEnumerable<string> enumerable)
		{
			foreach (string item in enumerable) {
				item.ToLower();
			}
		}

		public void ForEachOverList(List<string> list)
		{
			// List has a struct as enumerator, so produces quite different IL than foreach over the IEnumerable interface
			foreach (string item in list) {
				item.ToLower();
			}
		}

		public void ForEachOverNonGenericEnumerable(IEnumerable enumerable)
		{
			foreach (object item in enumerable) {
				item.ToString();
			}
		}

		public void ForEachOverNonGenericEnumerableWithAutomaticCastValueType(IEnumerable enumerable)
		{
			foreach (int item in enumerable) {
				item.ToString();
			}
		}

		public void ForEachOverNonGenericEnumerableWithAutomaticCastRefType(IEnumerable enumerable)
		{
			foreach (string item in enumerable) {
				Console.WriteLine(item);
			}
		}

		public void ForEachOnCustomClassEnumerator(CustomClassEnumerator e)
		{
			foreach (object item in e) {
				Console.WriteLine(item);
			}
		}

		// TODO : Needs additional pattern detection
		// CustomStructEnumerator does not implement IDisposable
		// No try-finally-Dispose is generated.
		//public void ForEachOnCustomStructEnumerator(CustomStructEnumerator e)
		//{
		//	foreach (object item in e) {
		//		Console.WriteLine(item);
		//	}
		//}

		public void ForEachOnGenericCustomClassEnumerator<T>(CustomClassEnumerator<T> e)
		{
			foreach (T item in e) {
				Console.WriteLine(item);
			}
		}

		// TODO : Needs additional pattern detection
		// CustomStructEnumerator does not implement IDisposable
		// No try-finally-Dispose is generated.
		//public void ForEachOnGenericCustomStructEnumerator<T>(CustomStructEnumerator<T> e)
		//{
		//	foreach (T item in e) {
		//		Console.WriteLine(item);
		//	}
		//}

		public void ForEachOnCustomClassEnumeratorWithIDisposable(CustomClassEnumeratorWithIDisposable e)
		{
			foreach (object item in e) {
				Console.WriteLine(item);
			}
		}

		public void ForEachOnCustomStructEnumeratorWithIDisposable(CustomStructEnumeratorWithIDisposable e)
		{
			foreach (object item in e) {
				Console.WriteLine(item);
			}
		}

		public void ForEachOnGenericCustomClassEnumeratorWithIDisposable<T>(CustomClassEnumeratorWithIDisposable<T> e)
		{
			foreach (T item in e) {
				Console.WriteLine(item);
			}
		}

		public void ForEachOnGenericCustomStructEnumeratorWithIDisposable<T>(CustomStructEnumeratorWithIDisposable<T> e)
		{
			foreach (T item in e) {
				Console.WriteLine(item);
			}
		}

		public static void NonGenericForeachWithReturnFallbackTest(IEnumerable e)
		{
			Console.WriteLine("NonGenericForeachWithReturnFallback:");
			IEnumerator enumerator = e.GetEnumerator();
			try {
				Console.WriteLine("MoveNext");
				if (enumerator.MoveNext()) {
					object current = enumerator.Current;
					Console.WriteLine("current: " + current);
				}
			} finally {
				IDisposable disposable = enumerator as IDisposable;
				if (disposable != null)
					disposable.Dispose();
			}
			Console.WriteLine("After finally!");
		}

		public static void ForeachWithRefUsage(List<int> items)
		{
			foreach (int item in items) {
				int current = item;
				Loops.Operation(ref current);
			}
		}

		public static void ForeachWithCapturedVariable(List<int> items)
		{
			foreach (int item in items) {
				int c = item;
				Loops.Operation((Func<bool>)(() => c == 5));
			}
		}

		public static T LastOrDefault<T>(IEnumerable<T> items)
		{
			T result = default(T);
			foreach (T item in items) {
				result = item;
			}
			return result;
		}
		#endregion
		//public void ForEachOverArray(string[] array)
		//{
		//	foreach (string text in array) {
		//		text.ToLower();
		//	}
		//}

		public void ForOverArray(string[] array)
		{
			for (int i = 0; i < array.Length; i++) {
				array[i].ToLower();
			}
		}

		public void NestedLoops()
		{
			for (int i = 0; i < 10; i++) {
				if (i % 2 == 0) {
					for (int j = 0; j < 5; j++) {
						Console.WriteLine("Y");
					}
				} else {
					Console.WriteLine("X");
				}
			}
		}

		//public int MultipleExits()
		//{
		//	int i = 0;
		//	while (true) {
		//		if (i % 4 == 0) { return 4; }
		//		if (i % 7 == 0) { break; }
		//		if (i % 9 == 0) { return 5; }
		//		if (i % 11 == 0) { break; }
		//		i++;
		//	}
		//	i = int.MinValue;
		//	return i;
		//}

		//public int InterestingLoop()
		//{
		//	int i = 0;
		//	if (i % 11 == 0) {
		//		while (true) {
		//			if (i % 4 == 0) {
		//				if (i % 7 == 0) {
		//					if (i % 11 == 0) {
		//						continue; // use a continue here to prevent moving the if (i%7) outside the loop
		//					}
		//					Console.WriteLine("7");
		//				} else {
		//					// this block is not part of the natural loop
		//					Console.WriteLine("!7");
		//				}
		//				break;
		//			}
		//			i++;
		//		}
		//		// This instruction is still dominated by the loop header
		//		i = int.MinValue;
		//	}
		//	return i;
		//}

		private bool Condition(string arg)
		{
			Console.WriteLine("Condition: " + arg);
			return false;
		}

		public void WhileLoop()
		{
			Console.WriteLine("Initial");
			if (this.Condition("if")) {
				while (this.Condition("while")) {
					Console.WriteLine("Loop Body");
					if (this.Condition("test")) {
						if (this.Condition("continue")) {
							continue;
						}
						if (!this.Condition("break"))
							break;
					}
					Console.WriteLine("End of loop body");
				}
				Console.WriteLine("After loop");
			}
			Console.WriteLine("End of method");
		}

		//public void WhileWithGoto()
		//{
		//	while (this.Condition("Main Loop")) {
		//		if (!this.Condition("Condition"))
		//			goto block2;
		//		block1:
		//		Console.WriteLine("Block1");
		//		if (this.Condition("Condition2"))
		//			continue;
		//		block2:
		//		Console.WriteLine("Block2");
		//		goto block1;
		//	}
		//}

		//public void DoWhileLoop()
		//{
		//	Console.WriteLine("Initial");
		//	if (this.Condition("if")) {
		//		do {
		//			Console.WriteLine("Loop Body");
		//			if (this.Condition("test")) {
		//				if (this.Condition("continue")) {
		//					continue;
		//				}
		//				if (!this.Condition("break"))
		//					break;
		//			}
		//			Console.WriteLine("End of loop body");
		//		} while (this.Condition("while"));
		//		Console.WriteLine("After loop");
		//	}
		//	Console.WriteLine("End of method");
		//}

		public void ForLoop()
		{
			Console.WriteLine("Initial");
			if (this.Condition("if")) {
				for (int i = 0; this.Condition("for"); i++) {
					Console.WriteLine("Loop Body");
					if (this.Condition("test")) {
						if (this.Condition("continue")) {
							continue;
						}
						if (!this.Condition("not-break"))
							break;
					}
					Console.WriteLine("End of loop body");
				}
				Console.WriteLine("After loop");
			}
			Console.WriteLine("End of method");
		}
	}
}
