using System.Threading.Tasks;
using PaGetto.Core.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace PaGetto.Core.Tests.Email;

public class NullEmailSenderTests
{
    public class SendAsync
    {
        [Fact]
        public async Task DropsMessageWithoutThrowing()
        {
            var sender = new NullEmailSender(NullLogger<NullEmailSender>.Instance);

            await sender.SendAsync(new EmailMessage("to@example.com", "subject", "body"));
        }
    }
}
