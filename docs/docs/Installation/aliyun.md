# Use Alibaba Cloud (Aliyun) OSS

You can store packages to [Alibaba Cloud (Aliyun) Object Storage Service](https://www.alibabacloud.com/product/object-storage-service).

## Configure PaGetto

You can modify PaGetto's configurations by editing the `appsettings.json` file. For the full list of configurations, please refer to [PaGetto's configuration](../configuration.md) guide.

### Alibaba Cloud Object Storage Service (OSS)

Update the `appsettings.json` file:

```json
{
    ...

    "Storage": {
        "Type": "AliyunOss",
        "Endpoint": "oss-us-west-1.aliyuncs.com",
        "Bucket": "foo",
        "AccessKey": "",
        "AccessKeySecret": "",
        "Prefix": "lib/pagetto" // optional
    },

    ...
}
```
