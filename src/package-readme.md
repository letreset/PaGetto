# PaGetto

This package is part of [PaGetto](https://github.com/letreset/PaGetto), a lightweight, self-hosted NuGet and symbol server with multiple feeds, per-feed permissions and Microsoft Entra ID sign-in.

Most people run PaGetto as a server: see the [documentation](https://letreset.github.io/PaGetto/) for Docker, Kubernetes and IIS. The libraries are published so you can host PaGetto inside your own ASP.NET Core app; the [sample](https://github.com/letreset/PaGetto/tree/main/samples/PaGettoWebApplication) shows how.

To talk to a NuGet server from your own code, use [`PaGetto.Protocol`](https://www.nuget.org/packages/PaGetto.Protocol).
