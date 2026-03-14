using System;
using System.Threading.Tasks;
using DSharpPlus;
using Microsoft.Extensions.Logging;

namespace KoekoeBot
{
    public class KoekoeClientErrorHandler : IClientErrorHandler
    {
        private readonly ILogger<KoekoeClientErrorHandler> _logger;

        public KoekoeClientErrorHandler(ILogger<KoekoeClientErrorHandler> logger)
            => _logger = logger;

        public ValueTask HandleEventHandlerError(
            string name, Exception exception, Delegate handler, object sender, object eventArgs)
        {
            _logger.LogError(KoekoeController.BotEventId, exception,
                "Event handler exception in '{EventName}'", name);
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleGatewayError(Exception exception)
        {
            _logger.LogError(KoekoeController.BotEventId, exception, "Gateway exception occurred");
            return ValueTask.CompletedTask;
        }
    }
}