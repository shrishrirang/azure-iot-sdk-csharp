﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client.Exceptions;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;

namespace Microsoft.Azure.Devices.Client
{
    /// <summary>
    /// Contains methods that a convention based device/ module can use to send telemetry to the service,
    /// respond to commands and perform operations on its properties.
    /// </summary>
    internal partial class InternalClient
    {
        internal PayloadConvention PayloadConvention => _clientOptions.PayloadConvention ?? DefaultPayloadConvention.Instance;

        internal Task SendTelemetryAsync(TelemetryMessage telemetryMessage, CancellationToken cancellationToken)
        {
            if (telemetryMessage == null)
            {
                throw new ArgumentNullException(nameof(telemetryMessage));
            }

            if (telemetryMessage.Telemetry != null)
            {
                telemetryMessage.Telemetry.Convention = PayloadConvention;
                telemetryMessage.PayloadContentEncoding = PayloadConvention.PayloadEncoder.ContentEncoding.WebName;
                telemetryMessage.PayloadContentType = PayloadConvention.PayloadSerializer.ContentType;
            }

            return SendEventAsync(telemetryMessage, cancellationToken);
        }

        internal Task SubscribeToCommandsAsync(Func<CommandRequest, object, Task<CommandResponse>> callback, object userContext, CancellationToken cancellationToken)
        {
            // Subscribe to methods default handler internally and use the callback received internally to invoke the user supplied command callback.
            var methodDefaultCallback = new MethodCallback(async (methodRequest, userContext) =>
            {
                CommandRequest commandRequest;
                if (methodRequest.Name != null
                    && methodRequest.Name.Contains(ConventionBasedConstants.ComponentLevelCommandSeparator))
                {
                    string[] split = methodRequest.Name.Split(ConventionBasedConstants.ComponentLevelCommandSeparator);
                    string componentName = split[0];
                    string commandName = split[1];
                    commandRequest = new CommandRequest(PayloadConvention, commandName, componentName, methodRequest.Data);
                }
                else
                {
                    commandRequest = new CommandRequest(payloadConvention: PayloadConvention, commandName: methodRequest.Name, data: methodRequest.Data);
                }

                CommandResponse commandResponse = await callback.Invoke(commandRequest, userContext).ConfigureAwait(false);
                commandResponse.PayloadConvention = PayloadConvention;
                return commandResponse.ResultAsBytes != null
                    ? new MethodResponse(commandResponse.ResultAsBytes, commandResponse.Status)
                    : new MethodResponse(commandResponse.Status);
            });

            return SetMethodDefaultHandlerAsync(methodDefaultCallback, userContext, cancellationToken);
        }

        internal async Task<ClientProperties> GetClientTwinPropertiesAsync(CancellationToken cancellationToken)
        {
            try
            {
                ClientPropertiesAsDictionary clientPropertiesDictionary = await InnerHandler
                    .GetClientTwinPropertiesAsync<ClientPropertiesAsDictionary>(cancellationToken)
                    .ConfigureAwait(false);

                return clientPropertiesDictionary.ToClientProperties(PayloadConvention);
            }
            catch (IotHubCommunicationException ex) when (ex.InnerException is OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
        }

        internal async Task<ClientPropertiesUpdateResponse> UpdateClientPropertiesAsync(ClientPropertyCollection clientProperties, CancellationToken cancellationToken)
        {
            if (clientProperties == null)
            {
                throw new ArgumentNullException(nameof(clientProperties));
            }

            try
            {
                clientProperties.Convention = PayloadConvention;
                byte[] body = clientProperties.GetPayloadObjectBytes();
                using Stream bodyStream = new MemoryStream(body);

                return await InnerHandler.SendClientTwinPropertyPatchAsync(bodyStream, cancellationToken).ConfigureAwait(false);
            }
            catch (IotHubCommunicationException ex) when (ex.InnerException is OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
        }

        internal Task SubscribeToWritablePropertyUpdateRequestsAsync(Func<ClientPropertyCollection, object, Task> callback, object userContext, CancellationToken cancellationToken)
        {
            // Subscribe to DesiredPropertyUpdateCallback internally and use the callback received internally to invoke the user supplied Property callback.
            var desiredPropertyUpdateCallback = new DesiredPropertyUpdateCallback((twinCollection, userContext) =>
            {
                // convert a TwinCollection to PropertyCollection
                var propertyCollection = ClientPropertyCollection.WritablePropertyUpdateRequestsFromTwinCollection(twinCollection, PayloadConvention);
                callback.Invoke(propertyCollection, userContext);

                return TaskHelpers.CompletedTask;
            });

            return SetDesiredPropertyUpdateCallbackAsync(desiredPropertyUpdateCallback, userContext, cancellationToken);
        }
    }
}