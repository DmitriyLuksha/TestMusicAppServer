﻿using MediatR;
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestMusicAppServer.Common.Configurations;
using TestMusicAppServer.Shared.Infrastructure.ServiceBusMessageConverters;
using TestMusicAppServer.Track.Domain.Events;
using TestMusicAppServer.Track.Domain.Messages;

namespace TestMusicAppServer.Track.Infrastructure.Listeners
{
    public class AudioUploadingResultListener : IAudioUploadingResultListener
    {
        private readonly ServiceBusConfig _serviceBusConfig;
        private readonly ConnectionStringsConfig _connectionStringsConfig;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        private const string DeadLetterExceptionReason = "Can't process request due to exception";

        private IQueueClient _queueClient;

        public AudioUploadingResultListener(
            IOptions<ConnectionStringsConfig> connectionStringsConfig,
            IOptions<ServiceBusConfig> serviceBusConfig,
            IServiceScopeFactory scopeFactory,
            ILogger<AudioUploadingResultListener> logger
        )
        {
            this._connectionStringsConfig = connectionStringsConfig.Value;
            this._serviceBusConfig = serviceBusConfig.Value;
            this._scopeFactory = scopeFactory;
            this._logger = logger;
        }

        public void RegisterListener()
        {
            var serviceBusConnectionString = _connectionStringsConfig.TestMusicAppServiceBus;
            var queueName = _serviceBusConfig.AudioUploadingResultQueueName;

            _queueClient = new QueueClient(serviceBusConnectionString, queueName);

            var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
            {
                MaxConcurrentCalls = 1,
                AutoComplete = false
            };

            _queueClient.RegisterMessageHandler(ProcessMessagesAsync, messageHandlerOptions);
        }

        public void CloseListener()
        {
            _queueClient
                ?.CloseAsync()
                .GetAwaiter()
                .GetResult();
        }

        private async Task ProcessMessagesAsync(Message message, CancellationToken cancellationToken)
        {
            // Create scope manually because ProcessMessagesAsync executes outside
            // of the ASP.NET lifecycle
            using (var scope = _scopeFactory.CreateScope())
            {
                try
                {
                    var mediator = scope.ServiceProvider.GetService<IMediator>();

                    var messageJson = ServiceBusMessageConverter.MessageToString(message);
                    var deserializedMessage = JsonConvert.DeserializeObject<AudioUploadingResultMessage>(messageJson);
                    
                    var trackUploadFinishedEvent = new TrackUploadFinishedEvent
                    {
                        FileName = deserializedMessage.FileName,
                        IsSuccess = deserializedMessage.IsSuccess,
                        UserId = deserializedMessage.AdditionalData.UserId,
                        PlaylistId = deserializedMessage.AdditionalData.PlaylistId,
                        TrackName = deserializedMessage.AdditionalData.TrackName
                    };

                    await mediator.Publish(trackUploadFinishedEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Upload track");

                    var exceptionJson = JsonConvert.SerializeObject(ex);

                    await _queueClient.DeadLetterAsync(
                        message.SystemProperties.LockToken,
                        DeadLetterExceptionReason,
                        exceptionJson);

                    return;
                }

                await _queueClient.CompleteAsync(message.SystemProperties.LockToken);
            }
        }

        private Task ExceptionReceivedHandler(ExceptionReceivedEventArgs args)
        {
            _logger.LogError(args.Exception, "ExceptionReceivedHandler triggered");

            return Task.CompletedTask;
        }
    }
}
