﻿// Copyright (c) 2019 AlphaSierraPapa for the SharpDevelop Team
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

namespace ICSharpCode.Decompiler.Solution
{
	/// <summary>
	/// A container class that holds platform and GUID information about a Visual Studio project.
	/// </summary>
	public class ProjectId
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="ProjectId"/> class.
		/// </summary>
		/// <param name="projectPlatform">The project platform.</param>
		/// <param name="projectGuid">The project GUID.</param>
		/// 
		/// <exception cref="ArgumentException">Thrown when <paramref name="projectPlatform"/> is null or empty.</exception>
		public ProjectId(string projectPlatform, Guid projectGuid)
		{
			if (string.IsNullOrWhiteSpace(projectPlatform)) {
				throw new ArgumentException("The platform cannot be null or empty.", nameof(projectPlatform));
			}

			Guid = projectGuid;
			PlatformName = projectPlatform;
		}

		/// <summary>
		/// Gets the GUID of this project.
		/// </summary>
		public Guid Guid { get; }

		/// <summary>
		/// Gets the platform name of this project. Only single platform per project is supported.
		/// </summary>
		public string PlatformName { get; }
	}
}
