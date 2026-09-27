# Use Tencent Cloud COS

You can store packages to [Tencent Cloud  Object Storage Service](https://cloud.tencent.com/document/product/436/6222).

## Configure PaGetto

You can modify PaGetto's configurations by editing the `appsettings.json` file. For the full list of configurations, please refer to [PaGetto's configuration](../configuration.md) guide.

### Tencent Cloud Object Storage Service

Update the `appsettings.json` file:

```json
{
    ...

    "Storage": {
        "Type": "TencentCos",
        "AppId": "",
        "SecretId": "",
        "SecretKey": "",
        "BucketName": "pagetto",
        "Region": "ap-guangzhou"        
    },

    ...
}
```

`AppId`, `SecretId`, `SecretKey`, `BucketName` and `Region` are required. The bucket name is used together with the app id (`{BucketName}-{AppId}`). The optional `KeyDurationSecond` sets how long, in seconds, a signed request key stays valid (default `600`).
