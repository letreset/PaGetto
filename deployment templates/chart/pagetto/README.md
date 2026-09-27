# pagetto

A Helm chart for [PaGetto](https://github.com/letreset/PaGetto), a lightweight NuGet and symbol server. It's built on the [bjw-s common library](https://github.com/bjw-s-labs/helm-charts/tree/main/charts/library/common).

## Install

The chart is published as an OCI artifact with every release:

```bash
helm install pagetto oci://ghcr.io/letreset/charts/pagetto --version <version>
```

The chart and app versions match the [release](https://github.com/letreset/PaGetto/releases) tag. By default the chart deploys the image `letreset/pagetto:<version>`.

## Values

See [`values.yaml`](values.yaml) for all options. Common ones:

| Key | Default | Description |
|-----|---------|-------------|
| `configMaps.pagetto-env.data` | SQLite + FileSystem on `/data` | Environment variables passed to PaGetto. See the [configuration docs](https://letreset.github.io/PaGetto/docs/configuration). Put secrets in a Secret instead. |
| `controllers.pagetto.containers.pagetto.env` | unset | Secret-backed environment variables, e.g. `Authentication__InitialAdmin__Password` from a Secret for the [first administrator](https://letreset.github.io/PaGetto/docs/authentication#the-first-administrator) in `Local` mode |
| `controllers.pagetto.containers.pagetto.image.repository` | `letreset/pagetto` | Image repository |
| `controllers.pagetto.containers.pagetto.image.tag` | release version | Image tag |
| `controllers.pagetto.containers.pagetto.probes` | `/livez` liveness, `/health` readiness | Health probes |
| `service.pagetto-srv.ports.http.port` | `8080` | Service port |
| `ingress.pagetto-ingress.enabled` | `false` | Enable ingress |
| `persistence.pagetto-data` | 10Gi RWO at `/data` | Packages, symbols, SQLite DB and Data Protection keys |
