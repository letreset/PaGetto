---
sidebar_label: Introduction
---

# PaGetto

PaGetto (pronounced "pa getto") is a lightweight, self-hosted **NuGet and symbol server** for .NET teams. It implements the NuGet v3 protocol, so `dotnet`, NuGet, Visual Studio and Rider work with it out of the box. It is [open source](https://github.com/letreset/PaGetto), cross-platform and cloud ready.

![The package list of the Internal feed](./assets/overview.png)

## Features

PaGetto offers:

- **[Multiple feeds](feeds.md)**: each feed has its own packages, settings, retention and read-through mirror of nuget.org or any other NuGet v3 feed.
- **[Users and permissions](authentication.md)**: local accounts, Microsoft Entra ID sign-in, groups, per-feed pull/push/delete permissions, and personal access tokens with expiry email reminders.
- **A [web UI](web-ui.md)** with per-feed search filters, package management (unlist, relist, delete) and admin pages for feeds, accounts and groups.
- **Data Protection keys in storage**, so sign-in cookies survive restarts and work across replicas.
- **HTTP hardening**: security headers, optional HSTS, configurable CORS and response compression.

Coming from BaGetter? PaGetto takes over an existing BaGetter database and package storage in place; see [Migrating from BaGetter](migrating-from-bagetter.md).

## Backends

| | Supported |
|---|---|
| Database | SQLite, SQL Server, PostgreSQL, MySQL |
| Package storage | File system, Azure Blob Storage, AWS S3, Google Cloud Storage, Alibaba Cloud (Aliyun) OSS, Tencent Cloud COS |
| Hosting | Docker (`linux/amd64`, `linux/arm64`), Kubernetes (Helm), Windows/IIS, any machine with the ASP.NET Core 10 runtime |

## Run PaGetto

- [Docker](Installation/docker.md)
- [Kubernetes (Helm)](Installation/kubernetes.md)
- [On your computer](Installation/local.md)
- [Azure](Installation/azure.md), [AWS](Installation/aws.md), [Google Cloud](Installation/gcp.md), [Alibaba Cloud](Installation/aliyun.md), [Tencent Cloud](Installation/tencent.md)
- [Behind Windows IIS](Installation/iis-proxy.md)

## PaGetto SDK

You can use the [`PaGetto.Protocol`](https://www.nuget.org/packages/PaGetto.Protocol) package (`dotnet add package PaGetto.Protocol`) to interact with a NuGet server. See the [PaGetto SDK](Advanced/sdk.md) guide.
