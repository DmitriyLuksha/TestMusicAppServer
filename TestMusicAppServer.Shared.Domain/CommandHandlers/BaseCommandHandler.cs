﻿using System.Threading;
using System.Threading.Tasks;
using MediatR;
using TestMusicAppServer.Shared.Domain.Commands;

namespace TestMusicAppServer.Shared.Domain.CommandHandlers
{
    public abstract class BaseCommandHandler<T> : AsyncRequestHandler<T>
        where T: ICommand
    {
        protected abstract override Task Handle(T command, CancellationToken cancellationToken);
    }
}
