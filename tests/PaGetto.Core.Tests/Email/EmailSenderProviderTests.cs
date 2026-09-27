using System;
using System.Collections.Generic;
using PaGetto.Core.Email;
using PaGetto.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace PaGetto.Core.Tests.Email;

public class EmailSenderProviderTests
{
    public class GetServiceFromProviders
    {
        [Fact]
        public void ReturnsSmtpSenderWhenTypeIsSmtp()
        {
            var services = BuildProvider(new Dictionary<string, string> { ["Email:Type"] = "Smtp" });

            var sender = DependencyInjectionExtensions.GetServiceFromProviders<IEmailSender>(services);

            Assert.IsType<SmtpEmailSender>(sender);
        }

        [Fact]
        public void ReturnsNullSenderWhenTypeIsNull()
        {
            var services = BuildProvider(new Dictionary<string, string> { ["Email:Type"] = "Null" });

            var sender = DependencyInjectionExtensions.GetServiceFromProviders<IEmailSender>(services);

            Assert.IsType<NullEmailSender>(sender);
        }

        [Fact]
        public void ReturnsNullSenderWhenUnconfigured()
        {
            var services = BuildProvider(new Dictionary<string, string>());

            var sender = DependencyInjectionExtensions.GetServiceFromProviders<IEmailSender>(services);

            Assert.IsType<NullEmailSender>(sender);
        }

        private static IServiceProvider BuildProvider(Dictionary<string, string> config)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(config)
                .Build();

            return new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                .AddOptions()
                .AddPaGettoApplication(_ => { })
                .Services
                .BuildServiceProvider();
        }
    }
}
