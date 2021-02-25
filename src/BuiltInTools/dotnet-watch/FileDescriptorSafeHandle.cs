// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Watch.Interop;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.DotNet.Watcher
{
    internal class FileDescriptorSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public FileDescriptorSafeHandle(int fd) : this((IntPtr)fd) { }
        public FileDescriptorSafeHandle(IntPtr handle)
            : base(true)
        {
            SetHandle(handle);
        }
        protected override bool ReleaseHandle()
        {
            var rv = LibC.close(handle.ToInt32());
            if (rv == -1)
            {
                PlatformException.Throw();
            }
            return true;
        }
    }
}
