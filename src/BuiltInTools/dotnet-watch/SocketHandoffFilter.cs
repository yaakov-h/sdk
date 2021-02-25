// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Watcher.Tools;

namespace Microsoft.DotNet.Watcher
{
    internal class SocketHandoffFilter : IWatchFilter
    {
        public ValueTask ProcessAsync(DotNetWatchContext context, CancellationToken cancellationToken)
        {
            var pathToMiddleware = Path.Combine(AppContext.BaseDirectory, "middleware", "Microsoft.AspNetCore.Watch.ReloadIntegration.dll");

            const string dotnetStartHooksName = "DOTNET_STARTUP_HOOKS";
            context.ProcessSpec.EnvironmentVariables[dotnetStartHooksName] = AddOrAppend(context.ProcessSpec.EnvironmentVariables[dotnetStartHooksName], pathToMiddleware, Path.PathSeparator);

            const string hostingStartupAssembliesName = "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES";
            context.ProcessSpec.EnvironmentVariables[hostingStartupAssembliesName] = AddOrAppend(context.ProcessSpec.EnvironmentVariables[hostingStartupAssembliesName], "Microsoft.AspNetCore.Watch.ReloadIntegration", ';');


            string pipeName = default;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                pipeName = Guid.NewGuid().ToString();
                context.ProcessSpec.EnvironmentVariables["ZOCKET_PIPE_NAME"] = pipeName;

            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var duplicatedHttpSocket = context.HttpListenSocket.DuplicateSocketLinux();
                var duplicatedHttpsSocket = context.HttpsListenSocket.DuplicateSocketLinux();

                context.ProcessSpec.EnvironmentVariables["ZOCKET_LISTEN_HTTP_FD"] = duplicatedHttpSocket.DangerousGetHandle().ToInt32().ToString();
                context.ProcessSpec.EnvironmentVariables["ZOCKET_LISTEN_HTTPS_FD"] = duplicatedHttpsSocket.DangerousGetHandle().ToInt32().ToString();
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Task.Run(async () =>
                {
                    try
                    {
                        using var namedPipeServer = new NamedPipeServerStream(pipeName,
                            PipeDirection.InOut,
                            maxNumberOfServerInstances: 1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);
                        await namedPipeServer.WaitForConnectionAsync(cancellationToken);
                        var buffer = new byte[16]; // Only need enough for the length of a PID, 16 should be plenty
                        var length = await namedPipeServer.ReadAsync(buffer, cancellationToken);
                        var pid = BitConverter.ToInt32(new ReadOnlySpan<byte>(buffer).Slice(0, length));

                        // TODO how can we pass this info into other transports (QUIC) s.t. it duplicates rather than creates?

                        // Send http socket
                        var httpSocketInfo = context.HttpListenSocket.DuplicateSocketWindows(pid);
                        await namedPipeServer.WriteAsync(BitConverter.GetBytes(httpSocketInfo.ProtocolInformation.Length));
                        await namedPipeServer.WriteAsync(httpSocketInfo.ProtocolInformation, cancellationToken);

                        // Send https socket
                        var httpsSocketInfo = context.HttpsListenSocket.DuplicateSocketWindows(pid);
                        await namedPipeServer.WriteAsync(BitConverter.GetBytes(httpsSocketInfo.ProtocolInformation.Length));
                        await namedPipeServer.WriteAsync(httpsSocketInfo.ProtocolInformation, cancellationToken);
                    }

                    catch (Exception)
                    {
                        // Ignore exceptions for now.
                        // TODO enable a debug log mode.
                    }
                }, cancellationToken);
            }
            return ValueTask.CompletedTask;
        }


        private string AddOrAppend(string existing, string envVarValue, char separator)
        {
            if (!string.IsNullOrEmpty(existing))
            {
                return $"{existing}{separator}{envVarValue}";
            }
            else
            {
                return envVarValue;
            }
        }
    }
}
