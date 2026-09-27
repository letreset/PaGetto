using System;
using Aliyun.OSS;
using PaGetto.Core;
using PaGetto.Core.Configuration;
using PaGetto.Core.Extensions;
using PaGetto.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace PaGetto.Aliyun;

public static class AliyunApplicationExtensions
{
    public static PaGettoApplication AddAliyunOssStorage(this PaGettoApplication app)
    {
        app.Services.AddPaGettoOptions<AliyunStorageOptions>(nameof(PaGettoOptions.Storage));

        app.Services.AddTransient<AliyunStorageService>();
        app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<AliyunStorageService>());

        app.Services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AliyunStorageOptions>>().Value;

            return new OssClient(options.Endpoint, options.AccessKey, options.AccessKeySecret);
        });

        app.Services.AddProvider<IStorageService>((provider, config) =>
        {
            if (!config.HasStorageType("AliyunOss")) return null;

            return provider.GetRequiredService<AliyunStorageService>();
        });

        return app;
    }

    public static PaGettoApplication AddAliyunOssStorage(
        this PaGettoApplication app,
        Action<AliyunStorageOptions> configure)
    {
        app.AddAliyunOssStorage();
        app.Services.Configure(configure);
        return app;
    }
}
