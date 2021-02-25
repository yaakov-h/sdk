// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System;
using Microsoft.AspNetCore.Watch.Interop;

namespace Microsoft.DotNet.Watcher
{
    internal class PlatformException : Exception
    {
        public PlatformException(int errno)
        {
            HResult = errno;
        }

        public PlatformException() :
            this(LibC.errno)
        { }

        public static void Throw() => throw new PlatformException();
    }
}
